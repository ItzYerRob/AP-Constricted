using UnityEngine;

public class PlayerActiveState : IPlayerState
{
    private readonly PlayerController player;
    private CharacterStats charStats;
    private Rigidbody rb;
    private float yaw; //Accumulated yaw in degrees
    
    //public Animator camAnim;
    private Vector3 moveDirection;
    private int jumpsRemaining;
    private float mass;
    private Vector3 groundNormal = Vector3.up;
    private bool IsGrounded;
    private bool wasGrounded;
    public PlayerActiveState(PlayerController player) {
        this.player = player;
        rb = player.GetComponent<Rigidbody>();
        charStats = player.GetComponent<CharacterStats>();
    }

    public void Enter() {
        if (player.enabledObject) player.enabledObject.SetActive(true);
        if (player.lockerObject) player.lockerObject.SetActive(false);
        if (player.disabledObject) player.disabledObject.SetActive(false);

        //Sync yaw with current pose
        yaw = player.transform.eulerAngles.y;

        //Reset vertical velocity
        Vector3 v = rb.linearVelocity;
        v.y = 0f;
        rb.linearVelocity = v;

        //Make physics write smoothly to the Transform
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
    }

    public void Update()
    {
        //camAnim.SetBool("isWalking", isWalking);

        IsGrounded = player.isGrounded;

        if (IsGrounded && !wasGrounded) {
            //Just landed
            jumpsRemaining = charStats.maxJumpCount;
        }

        moveDirection = new Vector3(player.horizontalInput, 0f, player.verticalInput).normalized;
        if (moveDirection.sqrMagnitude > 1e-5f) moveDirection.Normalize();
        //Convert move direction to world space
        moveDirection = player.transform.TransformDirection(moveDirection);

        //Multi-Jump Logic
        if (Input.GetButtonDown("Jump") && jumpsRemaining > 0) {
            rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z); //Reset vertical velocity
            rb.AddForce(Vector3.up * charStats.jumpForce, ForceMode.Impulse);
            jumpsRemaining--;
        }

        wasGrounded = IsGrounded;

        //Rotate player based on mouse input
        //Use raw mouse delta to avoid frame-based smoothing
        float mouseX = Input.GetAxisRaw("Mouse X");
        //Do not multiply by deltaTime for mouse deltas;
        yaw += mouseX * player.charStats.rotationSpeed;

        //Anim State Decision (owner-only, because Update is already gated by IsOwner)
        PlayerController.AnimState newAnimState;

        if (!IsGrounded) {
            //In air, Jump animation
            newAnimState = PlayerController.AnimState.Jump;
        }
        else if (player.isWalking) {
            //Grounded and actually moving, Walk_Hurt
            newAnimState = PlayerController.AnimState.WalkHurt;
        }
        else {
            //Grounded and not moving, Idle_Hurt
            newAnimState = PlayerController.AnimState.IdleHurt;
        }

        if (player.CurrentAnimState.Value != newAnimState) {
            //Only send when state changes to avoid unnecessary network traffic
            player.CurrentAnimState.Value = newAnimState;
        }

    }

    public void FixedUpdate() {
        //Apply the rotation at the physics rate
        rb.MoveRotation(Quaternion.Euler(0f, yaw, 0f));

        if (IsGrounded) {
            UpdateGroundNormal();
        }
        else {
            groundNormal = Vector3.up;
            rb.AddForce(Vector3.down * charStats.gravityForce, ForceMode.Acceleration);
        }

        float accelScale = IsGrounded ? 1f : player.airAccel;

        //Ground-Aligned Movement
        if (moveDirection != Vector3.zero) {
            Vector3 desiredDir = moveDirection;

            if (IsGrounded) {
                //Project onto slope
                desiredDir = Vector3.ProjectOnPlane(desiredDir, groundNormal);
                if (desiredDir.sqrMagnitude > 0.0001f)
                    desiredDir.Normalize();
            }

            Vector3 dv = desiredDir * (charStats.moveSpeed * accelScale) * Time.fixedDeltaTime;
            rb.AddForce(dv, ForceMode.VelocityChange);
        }

        //Existing friction / speed clamp
        if (IsGrounded) {
            Vector3 v = rb.linearVelocity;
            Vector3 vHoriz = new Vector3(v.x, 0f, v.z);

            float frictionStep = player.groundFriction * Time.fixedDeltaTime;
            Vector3 vHorizAfter = Vector3.MoveTowards(vHoriz, Vector3.zero, frictionStep);

            rb.linearVelocity = new Vector3(vHorizAfter.x, v.y, vHorizAfter.z);
        }

        float maxSpeed = charStats.moveSpeed;
        {
            Vector3 v = rb.linearVelocity;
            Vector3 vHoriz = new Vector3(v.x, 0f, v.z);
            float sq = vHoriz.sqrMagnitude;
            float maxSq = maxSpeed * maxSpeed;
            if (sq > maxSq)
            {
                Vector3 vClamped = vHoriz.normalized * maxSpeed;
                rb.linearVelocity = new Vector3(vClamped.x, v.y, vClamped.z);
            }
        }

        if (IsGrounded) { HandleStepUp(); }
    }

    private void UpdateGroundNormal() {
        Vector3 origin = player.groundCheck.position + Vector3.up * 0.4f;
        float castDistance = 0.5f;

        if (Physics.SphereCast(origin, 0.2f, Vector3.down,
                            out RaycastHit hit,
                            castDistance,
                            player.groundMask,
                            QueryTriggerInteraction.Ignore))
        {
            float angle = Vector3.Angle(hit.normal, Vector3.up);

            //Respect slope limit
            if (angle <= player.maxSlopeAngle) {
                groundNormal = hit.normal;
            }
            else {
                //Too steep: treat as flat
                groundNormal = Vector3.up;
            }

            Debug.DrawRay(hit.point, hit.normal, Color.green);
        }
        else {
            groundNormal = Vector3.up;
        }
    }
    
    private void HandleStepUp() {
        //Use input / intention direction, not velocity
        Vector3 moveDir = moveDirection;
        moveDir.y = 0f;
        if (moveDir.sqrMagnitude < 0.0001f) return;
        moveDir.Normalize();

        float maxStepHeight     = player.maxStepHeight;
        float stepCheckDistance = player.stepCheckDistance;
        float stepSmooth        = player.stepSmooth;
        LayerMask mask          = player.groundMask;

        Vector3 footPos = player.groundCheck.position;

        //Offset from rb center to feet, assumed roughly constant
        float centerToFoot = rb.position.y - footPos.y;

        //Debug
        Debug.DrawLine(rb.position, footPos, Color.cyan);
        Debug.DrawRay(footPos, Vector3.up * maxStepHeight, Color.gray);
        Debug.DrawRay(footPos, moveDir * stepCheckDistance, Color.white);

        //Low Ray: Detect blocking surface in front of feet
        Vector3 originLow = footPos + Vector3.up * 0.02f; //Very close to feet
        if (!Physics.Raycast(originLow, moveDir, out RaycastHit hitLow, stepCheckDistance, mask, QueryTriggerInteraction.Ignore))
            return;

        //Ignore perfectly flat tops; otherwise keep it simple
        float lowAngle = Vector3.Angle(hitLow.normal, Vector3.up);
        if (lowAngle < 5f) return;

        //High Ray: Ensure we have space to step at maxStepHeight
        Vector3 originHigh = footPos + Vector3.up * (maxStepHeight + 0.05f);
        if (Physics.Raycast(originHigh, moveDir, stepCheckDistance, mask, QueryTriggerInteraction.Ignore))
            return; //Blocked at step height

        //Down Ray: Find top of step
        //Slightly past the front face, above maxStepHeight
        Vector3 originDown = footPos + moveDir * (hitLow.distance + 0.05f) + Vector3.up * (maxStepHeight + 0.1f);

        if (!Physics.Raycast(originDown, Vector3.down, out RaycastHit hitTop, maxStepHeight + 0.2f, mask, QueryTriggerInteraction.Ignore)) return;

        float minStepHeight = 0.05f;
        float heightDiff = hitTop.point.y - footPos.y;
        if (heightDiff < minStepHeight || heightDiff > maxStepHeight) return;

        //Move up + slightly forward
        Vector3 currentPos = rb.position;

        float forwardOffset = Mathf.Min(hitLow.distance + 0.05f, stepCheckDistance);
        float targetCenterY = hitTop.point.y + centerToFoot;

        Vector3 targetPos = currentPos
                        + moveDir * forwardOffset
                        + Vector3.up * (targetCenterY - currentPos.y);

        Vector3 newPos = Vector3.Lerp(currentPos, targetPos, stepSmooth * Time.fixedDeltaTime);
        rb.MovePosition(newPos);
    }

    public void Exit() { }
}