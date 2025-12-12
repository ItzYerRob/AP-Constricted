using Unity.Netcode;
using UnityEngine;

public struct RigidbodyState : INetworkSerializable
{
    public Vector3 Position;
    public Quaternion Rotation;
    public Vector3 Velocity;
    public double ServerTime; //NGO network time

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Position);
        serializer.SerializeValue(ref Rotation);
        serializer.SerializeValue(ref Velocity);
        serializer.SerializeValue(ref ServerTime);
    }
}

public class AuthoritativeNetworkRB : NetworkBehaviour
{
    [SerializeField] private Rigidbody rb;
    [SerializeField] private float correctionDuration = 0.15f;
    [SerializeField] private float snapDistance = 3.0f;

    private NetworkVariable<RigidbodyState> _state =
        new NetworkVariable<RigidbodyState>(
            writePerm: NetworkVariableWritePermission.Owner);

    private RigidbodyState _lastState;
    private bool _hasState;

    private Vector3 _targetPos;
    private Quaternion _targetRot;
    private Vector3 _targetVel;
    private float _correctionTimer;

    private void Awake() {
        if (!rb) rb = GetComponent<Rigidbody>();
    }

    public override void OnNetworkSpawn() {
        base.OnNetworkSpawn();

        if (IsOwner) { EnableOwnerSimulation(); }
        else { EnableFollowerMode(); }

        _state.OnValueChanged += OnStateChanged;
    }
    private void EnableOwnerSimulation() { 
        rb.isKinematic = false;   //Owner simulates physics
    }

    private void EnableFollowerMode() {
        rb.isKinematic = true;    //Follower only interpolates, no local forces
    }
    public override void OnGainedOwnership() {
        EnableOwnerSimulation();
    }

    public override void OnLostOwnership() {
        EnableFollowerMode();
    }



    private void OnDestroy() {
        if (_state != null) _state.OnValueChanged -= OnStateChanged;
    }

    private void FixedUpdate() {
        if (!IsSpawned) return;

        if (IsOwner) {
            //This instance is authoritative right now (server or client)
            var s = new RigidbodyState {
                Position   = rb.position,
                Rotation   = rb.rotation,
                Velocity   = rb.linearVelocity,
                ServerTime = NetworkManager.ServerTime.Time
            };
            _state.Value = s;
        }
        else {
            //Follower mode: apply interpolation to the latest _state
            if (!_hasState) return;

            float dist = Vector3.Distance(rb.position, _targetPos);
            if (dist > snapDistance) {
                rb.position       = _targetPos;
                rb.rotation       = _targetRot;
                rb.linearVelocity       = _targetVel;
                _correctionTimer  = 0f;
                return;
            }

            if (_correctionTimer < correctionDuration) {
                float t = _correctionTimer / correctionDuration;

                var newPos = Vector3.Lerp(rb.position, _targetPos, t);
                var newRot = Quaternion.Slerp(rb.rotation, _targetRot, t);
                var newVel = Vector3.Lerp(rb.linearVelocity, _targetVel, t);

                rb.MovePosition(newPos);
                rb.MoveRotation(newRot);
                rb.linearVelocity = newVel;

                _correctionTimer += Time.fixedDeltaTime;
            }
        }
    }

    private void OnStateChanged(RigidbodyState oldState, RigidbodyState newState) {
        if (IsOwner) return; //We are the writer; Don't treat our own state as remote

        _lastState      = newState;
        _hasState       = true;

        _targetPos      = newState.Position;
        _targetRot      = newState.Rotation;
        _targetVel      = newState.Velocity;
        _correctionTimer = 0f;
    }

    [Rpc(SendTo.Server, RequireOwnership = false)]
    private void RequestTemporaryAuthorityRpc(float duration, RpcParams rpcParams = default) {
        ulong senderClientId = rpcParams.Receive.SenderClientId; //Who sent this?

        if (!IsServer) return;

        StartCoroutine(GrantTemporaryAuthorityCoroutine(senderClientId, duration));
    }

    public void RequestTemporaryAuthority(float duration) {
        if (!IsOwner) { RequestTemporaryAuthorityRpc(duration); }
    }

    private System.Collections.IEnumerator GrantTemporaryAuthorityCoroutine(ulong clientId, float duration) {
        if (!IsServer) yield break;

        ulong originalOwner = OwnerClientId;

        NetworkObject.ChangeOwnership(clientId); //Give ownership to the client

        yield return new WaitForSeconds(duration);

        //Only revert if the object is still owned by that client
        if (OwnerClientId == clientId) {
            if (originalOwner == NetworkManager.ServerClientId) {
                //Return to server authority
                NetworkObject.RemoveOwnership(); //NGO: removes owner, server becomes owner
            }
            else {
                //Restore previous non-server owner if needed
                NetworkObject.ChangeOwnership(originalOwner);
            }
        }
    }
}
