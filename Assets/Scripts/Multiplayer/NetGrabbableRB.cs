using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Components;
using System.Collections;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Rigidbody))]
public class NetGrabbableRB : NetworkBehaviour {
    [Header("Validation")]
    [SerializeField] float maxGrabDistance = 1f;
    [SerializeField] float maxReleaseSpeed = 40f;
    [SerializeField] float maxAngularSpeed = 40f;

    Rigidbody _rb;
    Collider _srcCol;
    bool _held;
    ulong _holderId = ulong.MaxValue;

    //Last target from owner (server side)
    Vector3 _srvTargetPos;
    Quaternion _srvTargetRot = Quaternion.identity;

    void Awake() {
        _rb = GetComponent<Rigidbody>();
        _srcCol = GetComponent<Collider>();
    }

    public override void OnNetworkSpawn() {
        if (IsServer) {
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
        }
    }

    //Grab / Release handshake

    [ServerRpc(RequireOwnership = false)]
    public void TryGrabServerRpc(Vector3 reportedEye, Vector3 reportedForward, ServerRpcParams rpc = default)
    {
        var sender = rpc.Receive.SenderClientId;

        _held      = true;
        _holderId  = sender;

        Debug.Log($"[NetGrabbableRB] Grab accepted by client {sender} for {name}");

        //Transfer ownership: owner client’s AuthoritativeNetworkRB now simulates
        NetworkObject.ChangeOwnership(_holderId);

        var netTransform = GetComponent<NetworkTransform>();
        if (netTransform != null)
        {
            netTransform.enabled = false;
        }

        SetHeldClientRpc(true, _holderId);
    }

    private IEnumerator ReenableNetworkTransformDelayed(NetworkTransform netTransform)
    {
        yield return new WaitForSeconds(1f); //Small delay
        netTransform.enabled = true;
    }

    [ServerRpc(RequireOwnership = false)]
    public void ReleaseServerRpc(
        Vector3 pos, Quaternion rot, Vector3 linVel, Vector3 angVel, ServerRpcParams rpc = default)
    {
        if (!_held) return;
        if (rpc.Receive.SenderClientId != _holderId) return;

        //Clamp speeds (validation)
        linVel = Vector3.ClampMagnitude(linVel, maxReleaseSpeed);
        if (angVel.magnitude > maxAngularSpeed)
            angVel = angVel.normalized * maxAngularSpeed;

        //Snap once on server
        _rb.position       = pos;
        _rb.rotation       = rot;
        _rb.linearVelocity       = linVel;
        _rb.angularVelocity = angVel;

        _held = false;
        ulong releasingClient = _holderId;
        _holderId = ulong.MaxValue;

        //Let ownership fall back to server after short delay
        StartCoroutine(TransferOwnershipDelayed(releasingClient));

        Debug.Log($"[NetGrabbableRB] Released by client {rpc.Receive.SenderClientId}");
        SetHeldClientRpc(false, ulong.MaxValue);
    }

    private IEnumerator TransferOwnershipDelayed(ulong previousOwner) {
        yield return new WaitForSeconds(2f);

        var netTransform = GetComponent<NetworkTransform>();
        if (netTransform != null)
            netTransform.enabled = false;

        NetworkObject.ChangeOwnership(NetworkManager.ServerClientId);
    }

    [ClientRpc]
    void SetHeldClientRpc(bool held, ulong holder) {
        //UI/FX hooks for non-owners (e.g. outline, tooltip, highlight who holds it)
    }
}
