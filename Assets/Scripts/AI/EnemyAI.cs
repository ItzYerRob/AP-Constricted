using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public partial class EnemyAI : NetworkBehaviour
{
    [Header("References")]
    public EnemyNavmeshMotor motor;
    public Transform target;
    public Transform eye;

    [Header("Perception (Distance/FOV)")]
    [Tooltip("If false, uses simple aggro/deaggro ranges. If true, also requires target to be within a view cone.")]
    public bool useViewCone = true;

    [Min(0f)] public float aggroRange = 12f;
    [Min(0f)] public float deaggroRange = 16f;

    [Tooltip("Max distance for initial detection when using view cone.")]
    [Min(0f)] public float viewRange = 12f;

    [Tooltip("Half-angle (degrees) of the forward view cone for initial aggro.")]
    [Range(0f, 179.9f)] public float viewHalfAngle = 50f;

    [Tooltip("Half-angle (degrees) for keeping aggro; usually >= viewHalfAngle to add angular hysteresis.")]
    [Range(0f, 179.9f)] public float loseHalfAngle = 65f;

    [Tooltip("If true, only consider the horizontal plane for FOv.")]
    public bool horizontalOnlyFOV = true;

    [Header("Line of Sight")]
    public bool requireLineOfSight = true;
    public LayerMask losObstacles = ~0;
    public float eyeHeight = 1.6f;
    [Min(0f)] public float lostConfirmSeconds = 7.5f;

    [Header("Investigation")]
    [Tooltip("How long the enemy will search around the noise before giving up.")]
    public float investigateDuration = 4f;

    [Tooltip("How close to the noise point counts as arrived.")]
    public float investigateReachRadius = 1.5f;

    [HideInInspector] public bool   hasNoiseToInvestigate;
    [HideInInspector] public Vector3 noisePosition;
    [HideInInspector] public float  noiseHeardTime;
    [HideInInspector] public float noiseSuspicion;
    
    [Tooltip("Multiplier applied to the 'go investigate throw direction' hint produced by StunState.")]
    public float stunDirectionBias = 10f;
    [HideInInspector] public float noiseScore;  //suspicion * bias

    [Header("State-toggled objects")]
    public GameObject patrolStateObj;
    public GameObject pursueStateObj;
    public GameObject investigateStateObj;
    public GameObject stunStateObj;

    public IEnemyState _currentState;
    PatrolState _patrol;
    PursueTargetState _pursue;
    InvestigateNoiseState _investigate;
    StunState _stun; 

    float _lastSeenTime;

    //stun pending data (filled right before switching to StunState)
    [HideInInspector] public Vector3 PendingStunDirectionWorld;
    [HideInInspector] public float PendingStunDuration;
    [HideInInspector] public float PendingStunInvestigateDistance;
    [HideInInspector] public bool PendingStunHadTarget;



    void Awake() {
        if (!motor) motor = GetComponent<EnemyNavmeshMotor>();
        _patrol  = new PatrolState(this);
        _pursue = new PursueTargetState(this);
        _investigate = new InvestigateNoiseState(this);
        _stun = new StunState(this);
    }

    void OnEnable()  => SwitchState(_patrol);
    void OnDisable() { _currentState?.Exit(); _currentState = null; }

    void Update()      => _currentState?.Update();
    void FixedUpdate() => _currentState?.FixedUpdate();

    public void SwitchState(IEnemyState newState) {
        if (newState == _currentState) return;
        
        string oldStateName = _currentState?.GetType().Name ?? "null";
        string newStateName = newState?.GetType().Name ?? "null";
        
        _currentState?.Exit();
        _currentState = newState;
        _currentState?.Enter();

        Debug.Log($"[AI State] {gameObject.name} switched from {oldStateName} to {newStateName}");
    }

    public bool CanAggroTarget() {
        Transform closestTarget = null;
        float closestDistSqr = Mathf.Infinity;

        //Iterate over static list of all players
        foreach (Transform playerTransform in PlayerTarget.AllPlayers)
        {
            if (playerTransform == null) continue;

            //Start of checks
            float dist = Vector3.Distance(transform.position, playerTransform.position);
            float initialRange = useViewCone ? viewRange : aggroRange;

            if (dist > initialRange) continue; //Distance
            if (useViewCone && !IsInViewCone(playerTransform, viewHalfAngle)) continue;  //FOV
            if (requireLineOfSight && !HasLineOfSightTo(playerTransform)) continue; //LOS

            //Passed all checks
            //Find the closest valid target
            float distSqr = (transform.position - playerTransform.position).sqrMagnitude;
            if (distSqr < closestDistSqr) {
                closestDistSqr = distSqr;
                closestTarget = playerTransform;
            }
        }

        if (closestTarget != null) {
            //We found a target! Set it and return true.
            this.target = closestTarget; 
            return true;
        }

        //No target found, ensure ours is null
        this.target = null;
        return false;
    }

    public bool ShouldDeaggro()
    {
        if (!target) return true;

        float dist = Vector3.Distance(transform.position, target.position);
        bool inRange = dist <= deaggroRange;

        bool inAngle = true;
        if (useViewCone) inAngle = IsInViewCone(target, loseHalfAngle);

        bool hasLOS = true;
        if (requireLineOfSight) hasLOS = HasLineOfSightTo(target);

        //Aggro is valid if all required conditions are satisfied
        bool hasValidAggro = inRange && inAngle && hasLOS;

        if (hasValidAggro)
        {
            //Refresh "last time conditions were good"
            _lastSeenTime = Time.time;
            return false;
        }

        //Conditions are *currently* bad, but we keep chasing for a short grace period
        return (Time.time - _lastSeenTime) >= lostConfirmSeconds;
    }

    bool IsInViewCone(Transform t, float halfAngleDeg) {
        Transform eyeTx = eye ? eye : transform;

        Vector3 origin = eyeTx.position + Vector3.up * (eye ? 0f : eyeHeight);
        Vector3 toT    = (t.position + Vector3.up * eyeHeight) - origin;

        if (horizontalOnlyFOV) {
            toT.y = 0f;
        }

        float sqrDist = toT.sqrMagnitude;
        if (sqrDist < 1e-6f) return true; //on top of us

        Vector3 fwd = eyeTx.forward;
        if (horizontalOnlyFOV) fwd.y = 0f;

        //Dumb fix for when forward degenerates to zero (horizontal-only in vertical eye)
        if (fwd.sqrMagnitude < 1e-6f) fwd = eyeTx.forward;

        fwd.Normalize();
        toT.Normalize();

        float cosTheta     = Vector3.Dot(fwd, toT);
        float cosHalfAngle = Mathf.Cos(halfAngleDeg * Mathf.Deg2Rad);

        return cosTheta >= cosHalfAngle;
    }

    bool HasLineOfSightTo(Transform t) {
        Transform eyeTx = eye ? eye : transform;

        Vector3 origin = eyeTx.position + Vector3.up * (eye ? 0f : eyeHeight);
        Vector3 dest = t.position + Vector3.up * eyeHeight;
        Vector3 dir = dest - origin;
        float dist = dir.magnitude;
        if (dist <= 0.001f) return true;

        //Ignore own collider and get target
        if (Physics.Raycast(origin, dir.normalized, out RaycastHit hit, dist, losObstacles, QueryTriggerInteraction.Ignore))
        {
            return hit.transform.IsChildOf(t);
        }
        return true;
    }
    
    public void ApplyStunWorld(Vector3 hitDirWorld, float duration, float investigateDistance) {
        if (!IsServer) return;

        //Flatten & normalize (no vertical bias)
        hitDirWorld.y = 0f;
        Vector3 dir = hitDirWorld.sqrMagnitude > 1e-6f ? hitDirWorld.normalized : transform.forward;

        PendingStunDirectionWorld      = dir;
        PendingStunDuration            = Mathf.Max(0.01f, duration);
        PendingStunInvestigateDistance = Mathf.Max(0f, investigateDistance);
        PendingStunHadTarget           = target != null;

        SwitchState(_stun);
    }

    //accepts relative direction (local space XY ≡ right/forward).
    public void ApplyStunLocal(Vector2 localRightForward, float duration, float investigateDistance) {
        Vector3 local = new Vector3(localRightForward.x, 0f, localRightForward.y);
        Vector3 world = transform.TransformDirection(local);
        ApplyStunWorld(world, duration, investigateDistance);
    }

    //State Enters
    public void OnEnterPatrol() {
        patrolStateObj.SetActive(true); pursueStateObj.SetActive(false); investigateStateObj.SetActive(false);

        this.target = null;
    }
    public void OnEnterPursue() {
        pursueStateObj.SetActive(true); patrolStateObj.SetActive(false); investigateStateObj.SetActive(false);

        _lastSeenTime = Time.time;  //Needed so it doesn't immediately time out
    }
    public void OnEnterInvestigate() {
        investigateStateObj.SetActive(true); pursueStateObj.SetActive(false); patrolStateObj.SetActive(false);
    }

    //Expose states to allow transitions
    public IEnemyState PatrolState  => _patrol;
    public IEnemyState PursueState  => _pursue;
    public IEnemyState InvestigateState => _investigate;
    public IEnemyState StunState => _stun;

#if UNITY_EDITOR
    void OnDrawGizmosSelected() {
        if (hasNoiseToInvestigate) {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(noisePosition, investigateReachRadius);
            Gizmos.DrawLine(transform.position, noisePosition);
        }

        if (!useViewCone) return;

        //Gizmo to visualize the FOV
        Transform eyeTx = eye ? eye : transform;
        Vector3 origin = eyeTx.position + Vector3.up * (eye ? 0f : eyeHeight);

        UnityEditor.Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;

        DrawFOVArc(origin, eyeTx.forward, viewRange, viewHalfAngle, new Color(0f, 1f, 0f, 0.15f));
        DrawFOVArc(origin, eyeTx.forward, deaggroRange, loseHalfAngle, new Color(1f, 0.5f, 0f, 0.1f));
    }

    static void DrawFOVArc(Vector3 origin, Vector3 forward, float range, float halfAngle, Color color) {
        using (new UnityEditor.Handles.DrawingScope(color))
        {
            forward.y = 0f; forward.Normalize();
            UnityEditor.Handles.DrawSolidArc(origin, Vector3.up,
                Quaternion.Euler(0f, -halfAngle, 0f) * forward,
                halfAngle * 2f, range);
        }
    }
    
#endif
}