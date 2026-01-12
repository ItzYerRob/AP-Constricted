using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent), typeof(Rigidbody))]
public class EnemyNavmeshMotor : MonoBehaviour
{
    private bool _inited;
    [Header("Movement Settings")]
    [Min(0f)] public float moveForce = 20f;
    [Min(0f)] public float maxSpeed = 5f;
    [Min(0f)] public float slopeClimbForce = 10f;
    public float rotationSpeedDeg = 720f;
    public float drag = 2f;

    [Header("Gravity Settings")]
    public float gravityAcceleration = -9.81f;   //world-space Y
    public float extraGroundGravity = 0f;        //Extra "stick to ground"

    [Header("Slope Detection")]
    [Range(0f, 89f)] public float maxSlopeAngle = 45f;
    public LayerMask groundMask = ~0;
    public float groundCheckDistance = 0.5f;
    public float groundCheckRadius = 0.3f;
    
    [Header("Follow Settings")]
    public Transform target;
    [Min(0f)] public float stoppingDistance = 1f;
    [Min(0f)] public float updateRate = 0.2f;
    public float heightSyncThreshold = 1f; //Snap to agent if too far apart

    [Header("Patrol Settings")]
    public bool followPatrolPoints = false;
    public Transform[] patrolPoints;
    [Min(0f)] public float patrolStopTime = 1.5f;
    [Min(0f)] public float patrolSpeedMultiplier = 1.0f;

    private NavMeshAgent agent;
    private Rigidbody rb;
    private float nextUpdateTime;

    //Patrol state
    private int patrolIndex;
    private bool isWaitingAtPoint;
    private float waitEndTime;

    //Ground detection
    private RaycastHit groundHit;
    private bool isGrounded;
    private bool MotorEnabled = true;

    void Awake() => InitIfNeeded();

    void InitIfNeeded() {
        if (_inited) return;
        agent = GetComponent<NavMeshAgent>();
        rb    = GetComponent<Rigidbody>();
        agent.updatePosition = false;
        agent.updateRotation = false;
        agent.stoppingDistance = stoppingDistance;

        rb.useGravity = false;
        rb.linearDamping = drag;
        rb.angularDamping = 5f;
        rb.constraints = RigidbodyConstraints.FreezeRotation;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        _inited = true;
    }

    void Update() {
        //Keep agent synced with actual position for pathfinding
        agent.nextPosition = transform.position;

        //If locked, pause nav updates (but still keep nextPosition in sync)
        if (IsMovementLocked) {
            agent.ResetPath();
            return;
        }

        //Patrol logic
        if (followPatrolPoints && HasPatrolPoints()) {
            Transform waypoint = patrolPoints[patrolIndex];
            if (target != waypoint) target = waypoint;

            float dist = Vector3.Distance(transform.position, waypoint.position);
            if (!isWaitingAtPoint && dist <= stoppingDistance) { BeginWait(); return; }

            if (isWaitingAtPoint) {
                if (Time.time >= waitEndTime) { EndWaitAndAdvance(); }
                else { agent.ResetPath(); return; }
            }
        }

        if (!target) { agent.ResetPath(); return; }

        //Throttled destination update
        if (Time.time >= nextUpdateTime) {
            agent.SetDestination(target.position);
            nextUpdateTime = Time.time + updateRate;

            //After agent.SetDestination
            if (!agent.hasPath) Debug.LogWarning("Agent has no path");
            if (agent.pathPending) Debug.Log("Path pending...");
            if (agent.isOnNavMesh == false) Debug.LogError("Agent off NavMesh!");
        }
    }

    void FixedUpdate() {
        if(!MotorEnabled) return;

        InitIfNeeded();

        CheckGround();
        
        //If locked, apply braking and skip steering/rotation
        if (IsMovementLocked) {
            Vector3 horizontal = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
            rb.AddForce(-horizontal * drag, ForceMode.Force);
            ApplyGravity(false);
            return;
        }

        //Sync height if agent gets too far ahead (climbed stairs while RB didn't)
        float heightDiff = agent.nextPosition.y - transform.position.y;
        if (Mathf.Abs(heightDiff) > heightSyncThreshold && agent.hasPath) {
            //Gradually pull rigidbody toward agent's height
            float pullForce = heightDiff * 5f;
            rb.AddForce(Vector3.up * pullForce, ForceMode.Force);
        }

        //Early return if waiting
        if (isWaitingAtPoint) {
            //Apply braking force
            Vector3 horizontalVel = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
            rb.AddForce(-horizontalVel * drag, ForceMode.Force);
            return;
        }

        //Calculate desired movement direction from NavMesh
        float speedMul = (followPatrolPoints && HasPatrolPoints()) ?  Mathf.Max(0f, patrolSpeedMultiplier) : 1f;
        
        Vector3 desiredVelocity = agent.desiredVelocity * speedMul;
        Vector3 desiredDirection = new Vector3(desiredVelocity.x, 0f, desiredVelocity.z);

        //Project movement direction onto slope
        Vector3 moveDirection = desiredDirection.normalized;
        bool isClimbingSlope = false;

        if (desiredDirection.magnitude > 0.01f) {
            
            if (isGrounded) { moveDirection = ProjectOntoSlope(moveDirection, out isClimbingSlope); }

            //Calculate current horizontal speed
            Vector3 currentHorizontalVel = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
            float currentSpeed = currentHorizontalVel.magnitude;

            //Apply force if under max speed
            if (currentSpeed < maxSpeed * speedMul) {
                Vector3 force = moveDirection * moveForce;
                rb.AddForce(force, ForceMode.Force);
            }

            //Apply upward force when climbing slopes/stairs
            if (isClimbingSlope && isGrounded) {
                float slopeAngle = Vector3.Angle(Vector3.up, groundHit.normal);
                float climbFactor = Mathf.InverseLerp(5f, maxSlopeAngle, slopeAngle);
                rb.AddForce(Vector3.up * slopeClimbForce * climbFactor, ForceMode.Force);
            }

            //Clamp horizontal velocity
            Vector3 clampedHorizontal = Vector3.ClampMagnitude(
                new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z), 
                maxSpeed * speedMul
            );
            rb.linearVelocity = new Vector3(clampedHorizontal.x, rb.linearVelocity.y, clampedHorizontal.z);

            //Rotate to face movement direction
            Vector3 faceDirection = new Vector3(desiredVelocity.x, 0f, desiredVelocity.z);
            if (faceDirection.sqrMagnitude > 0.0001f) {
                Quaternion targetRot = Quaternion.LookRotation(faceDirection);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, 
                    targetRot, 
                    rotationSpeedDeg * Time.fixedDeltaTime
                );
            }
        }

        ApplyGravity(isClimbingSlope);
    }

    private void ApplyGravity(bool isClimbingSlope) {
        Vector3 worldGravity = new Vector3(0f, gravityAcceleration, 0f);

        if (!isGrounded) {
            rb.AddForce(worldGravity, ForceMode.Acceleration);
        }
        else {
            //On ground, only disable gravity while actively climbing
            if (!isClimbingSlope) {
                Vector3 gravityAlongNormal = Vector3.Project(worldGravity, -groundHit.normal);
                rb.AddForce(gravityAlongNormal, ForceMode.Acceleration);
            }
        }
    }

    private void CheckGround() {
        Vector3 origin = transform.position + Vector3.up * 0.5f;
        isGrounded = Physics.SphereCast(
            origin, 
            groundCheckRadius,
            Vector3.down, 
            out groundHit, 
            groundCheckDistance, 
            groundMask, 
            QueryTriggerInteraction.Ignore
        );
    }

    private Vector3 ProjectOntoSlope(Vector3 direction, out bool isClimbing) {
        isClimbing = false;
        
        if (!isGrounded) return direction;

        float slopeAngle = Vector3.Angle(Vector3.up, groundHit.normal);

        if (slopeAngle > maxSlopeAngle) {
            //keep horizontal movement but don't allow upward component
            Vector3 flat = new Vector3(direction.x, 0f, direction.z).normalized;
            return flat;
        }

        //Project the movement direction onto the slope
        Vector3 projected = Vector3.ProjectOnPlane(direction, groundHit.normal).normalized;
        
        //Check if we're moving upward on the slope
        if (slopeAngle > 5f && projected.y > 0.01f) {
            isClimbing = true;
        }

        return projected;
    }

    public void SetTarget(Transform newTarget, bool overridePatrol = true)
    {
        if (overridePatrol) followPatrolPoints = false;

        //Early return if target is uh, target. Resetting Path is bad mkay?
        if (target == newTarget) return;

        isWaitingAtPoint = false;
        target = newTarget;

        agent.ResetPath();
        nextUpdateTime = 0f; //Force a quick destination push
    }

    private Transform destinationWaypoint;
    public void SetDestination(Vector3 destination, bool overridePatrol = true)
    {
        InitIfNeeded();

        if (destinationWaypoint == null) {
            GameObject go = new GameObject($"_Waypoint_{gameObject.name}");
            destinationWaypoint = go.transform;
        }

        destinationWaypoint.position = destination;

        SetTarget(destinationWaypoint, overridePatrol);
    }

    public void ClearTarget() {
        InitIfNeeded();
        target = null;
        isWaitingAtPoint = false;
        agent.ResetPath();
    }

    private bool HasPatrolPoints() {
        return patrolPoints != null && patrolPoints.Length > 0;
    }

    private void BeginWait()  {
        isWaitingAtPoint = true;
        waitEndTime = Time.time + patrolStopTime;
        agent.ResetPath();
    }

    private void EndWaitAndAdvance() {
        isWaitingAtPoint = false;
        patrolIndex = (patrolIndex + 1) % patrolPoints.Length;
        target = patrolPoints[patrolIndex];
        nextUpdateTime = 0f;
    }

    public void ForceExitPatrolWait() {
        isWaitingAtPoint = false;
        agent.ResetPath();
        nextUpdateTime = 0f;
    }


    private float movementLockUntil = -1f;
    public void LockMovementUntil(float unlockTime) {
        movementLockUntil = Mathf.Max(movementLockUntil, unlockTime);
        agent.ResetPath();
    }

    public bool IsMovementLocked => Time.time < movementLockUntil;

    public void ZeroHorizontalVelocity() { rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f); }

    public void AddVelocityChange(Vector3 deltaVel) { rb.AddForce(deltaVel, ForceMode.VelocityChange); }

#if UNITY_EDITOR
    void OnDrawGizmosSelected() {
        if (isGrounded) {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(transform.position, groundHit.point);
            Gizmos.DrawWireSphere(groundHit.point, 0.1f);
            
            //Draw slope normal
            Gizmos.color = Color.blue;
            Gizmos.DrawRay(groundHit.point, groundHit.normal);
        }

        //Draw agent position difference
        if (agent != null && Application.isPlaying) {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(transform.position, agent.nextPosition);
            Gizmos.DrawWireSphere(agent.nextPosition, 0.2f);
        }
    }

    void OnValidate() {
        if (agent != null) agent.stoppingDistance = stoppingDistance;
    }
#endif
}