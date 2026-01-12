using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Components;
using System.Collections;

public struct HeldState : INetworkSerializable
{
    public bool Held;
    public ulong HolderId;
    public uint Token;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Held);
        serializer.SerializeValue(ref HolderId);
        serializer.SerializeValue(ref Token);
    }
}

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Rigidbody))]
public class NetGrabbableRB : NetworkBehaviour {
    [Header("Validation")]
    private readonly NetworkVariable<HeldState> _heldState =
        new(writePerm: NetworkVariableWritePermission.Server);

    bool _held;
    ulong _holderId = ulong.MaxValue;
    uint _currentToken;
    private Coroutine _returnOwnershipCo;
    private uint _releaseGen; //increases each release
    public override void OnNetworkSpawn() {
        if (IsServer) {
            //Ensure initial value is consistent
            _heldState.Value = new HeldState { Held = false, HolderId = ulong.MaxValue, Token = 0 };
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void TryGrabServerRpc(uint token, Vector3 reportedEye, Vector3 reportedForward, ServerRpcParams rpc = default) {
        var sender = rpc.Receive.SenderClientId;

        //If someone else holds it, reject
        if (_held && _holderId != sender) return;

        //If we have a pending ownership return from a previous release, cancel it.
        if (_returnOwnershipCo != null) {
            StopCoroutine(_returnOwnershipCo);
            _returnOwnershipCo = null;
        }

        //Ensure holder and not same holder
        if (_held && _holderId == sender && token <= _currentToken) {
            EnsureOwnership(sender);
            PublishHeldState(sender, _currentToken);
            return;
        }

        _held = true;
        _holderId = sender;
        _currentToken = token;

        EnsureOwnership(sender);
        PublishHeldState(sender, token);
    }

    private void EnsureOwnership(ulong sender) {
        if (NetworkObject.OwnerClientId != sender) NetworkObject.ChangeOwnership(sender);
    }

    private void PublishHeldState(ulong sender, uint token) {
        _heldState.Value = new HeldState { Held = true, HolderId = sender, Token = token };
    }

    [ServerRpc(RequireOwnership = false)]
    public void ReleaseServerRpc(uint token, Vector3 pos, Quaternion rot, Vector3 linVel, Vector3 angVel, ServerRpcParams rpc = default)
    {
        var sender = rpc.Receive.SenderClientId;
        if (!_held || sender != _holderId) return;
        if (token != _currentToken) return;

        //Validation and snap

        _held = false;
        _holderId = ulong.MaxValue;

        _heldState.Value = new HeldState { Held = false, HolderId = ulong.MaxValue, Token = _currentToken };

        //Start a guarded return
        _releaseGen++;
        uint myGen = _releaseGen;

        if (_returnOwnershipCo != null) StopCoroutine(_returnOwnershipCo);
        _returnOwnershipCo = StartCoroutine(TransferOwnershipDelayed(sender, myGen));
    }

    public HeldState Held => _heldState.Value;

    private IEnumerator TransferOwnershipDelayed(ulong previousOwner, uint releaseGen)
    {
        yield return new WaitForSeconds(2f);

        //If a new grab happened, or it is held again, do nothing.
        if (releaseGen != _releaseGen) yield break;
        if (_held) yield break;

        //if ownership already changed for some other reason, do nothing.
        if (NetworkObject.OwnerClientId != previousOwner) yield break;

        NetworkObject.ChangeOwnership(NetworkManager.ServerClientId);
        _returnOwnershipCo = null;
    }
}
