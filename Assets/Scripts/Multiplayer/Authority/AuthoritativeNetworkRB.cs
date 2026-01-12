using Unity.Netcode;
using UnityEngine;

public struct RigidbodyState : INetworkSerializable
{
    public Vector3 Position;
    public Quaternion Rotation;
    public Vector3 Velocity;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Position);
        serializer.SerializeValue(ref Rotation);
        serializer.SerializeValue(ref Velocity);
    }
}

public class AuthoritativeNetworkRB : NetworkBehaviour
{
    [SerializeField] private Rigidbody rb;
    [SerializeField] private float correctionDuration = 0.15f;
    [SerializeField] private float snapDistance = 3.0f;

    private NetworkVariable<RigidbodyState> _state = new NetworkVariable<RigidbodyState>(writePerm: NetworkVariableWritePermission.Owner);

    private bool _hasState;

    private Vector3 _targetPos;
    private Quaternion _targetRot;
    private Vector3 _targetVel;
    private float _correctionTimer;

    #region Throttling
    [Header("Send Throttling")]
    [SerializeField] private float sendRateHz = 20f; //20 Updates/sec
    [SerializeField] private float posThreshold = 0.02f; //Meters
    [SerializeField] private float rotThresholdDeg = 1.5f; //Degrees
    [SerializeField] private float velThreshold = 0.05f; //m/s
    [SerializeField] private float maxSilence = 0.25f; //Interval for forced update (0.25 = 4 forced updates per second)
    private float _nextSendTime;
    private float _lastSendTime;
    private RigidbodyState _lastSent;
    private bool _hasLastSent;
    #endregion Throttling

    private void Awake() {
        if (!rb) rb = GetComponent<Rigidbody>();
    }

    public override void OnNetworkSpawn() {
        if (IsOwner) EnableOwnerSimulation();
        else EnableFollowerMode();

        _state.OnValueChanged += OnStateChanged;
    }
    public override void OnNetworkDespawn() {
        _state.OnValueChanged -= OnStateChanged;
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

    private void FixedUpdate() {
        if (!IsSpawned) return;

        if (IsOwner) {
            //This instance is authoritative right now (server or client)
            float now = Time.time;
            float interval = (sendRateHz <= 0f) ? 0f : (1f / sendRateHz);

            // Build current state
            var s = new RigidbodyState {
                Position = rb.position,
                Rotation = rb.rotation,
                Velocity = rb.linearVelocity,
            };

            //Throttling
            bool due = now >= _nextSendTime;
            bool force = !_hasLastSent || (now - _lastSendTime) >= maxSilence;

            bool movedEnough = !_hasLastSent ||
                (s.Position - _lastSent.Position).sqrMagnitude >= posThreshold * posThreshold ||
                Quaternion.Angle(s.Rotation, _lastSent.Rotation) >= rotThresholdDeg ||
                (s.Velocity - _lastSent.Velocity).sqrMagnitude >= velThreshold * velThreshold;

            if ((due && movedEnough) || force)
            {
                _state.Value = s;
                _lastSent = s;
                _hasLastSent = true;
                _lastSendTime = now;
                _nextSendTime = now + interval;
            }

            return;
        }
        else {
            //Follower mode: apply interpolation to the latest _state
            if (!_hasState) return;

            float snapDistSq = snapDistance * snapDistance;
            if ((rb.position - _targetPos).sqrMagnitude > snapDistSq) {
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

        _hasState       = true;

        _targetPos      = newState.Position;
        _targetRot      = newState.Rotation;
        _targetVel      = newState.Velocity;
        _correctionTimer = 0f;
    }
}
