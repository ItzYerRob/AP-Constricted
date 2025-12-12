using UnityEngine;


//DEPRECATED
public class PlayerLockerState : IPlayerState
{
    private readonly PlayerController player;
    private CharacterStats charStats;
    private Rigidbody rb;
    private Collider playerCollider;
    private Collider lockerCollider;
    private Transform hidePoint;
    private float enterTime;

    //Accumulated yaw like in PlayerActiveState
    private float yaw;

    public PlayerLockerState(PlayerController player)
    {
        this.player = player;
        rb = player.GetComponent<Rigidbody>();
        charStats = player.GetComponent<CharacterStats>();
        playerCollider = player.GetComponent<Collider>();
    }

    public void SetLocker(HideSpot locker)
    {
        hidePoint = locker.hidePoint;
        lockerCollider = locker.GetComponent<Collider>();
    }

    public void Enter()
    {
        enterTime = Time.time;

        //local-only flashlight usage is already handled in OnNetworkSpawn, but this is still fine because only the owner calls states
        if (player.flashLight) player.flashLight.SetActive(false);

        if (player.enabledObject) player.enabledObject.SetActive(false);
        if (player.lockerObject)  player.lockerObject.SetActive(true);
        if (player.disabledObject) player.disabledObject.SetActive(false);

        //Stop vertical motion
        Vector3 v = rb.linearVelocity;
        v.y = 0f;
        rb.linearVelocity = v;

        //Move player inside locker and match orientation
        if (hidePoint != null)
        {
            rb.position = hidePoint.position;

            //Sync yaw with the locker’s facing
            yaw = hidePoint.eulerAngles.y;
            rb.MoveRotation(Quaternion.Euler(0f, yaw, 0f));
        }
        else
        {
            //Fallback: sync yaw with current pose
            yaw = player.transform.eulerAngles.y;
        }

        //Disable collisions between player and locker on the local physics world (authoritative side)
        if (lockerCollider != null && playerCollider != null)
        {
            Physics.IgnoreCollision(playerCollider, lockerCollider, true);
        }

        //Freeze movement to prevent sliding; we still rotate via MoveRotation.
        rb.isKinematic = true;

        //Drive anim state over the network
        if (player.CurrentAnimState.Value != PlayerController.AnimState.IdleHurt) {
            player.CurrentAnimState.Value = PlayerController.AnimState.IdleHurt;
        }
    }

    public void Update()
    {
        //PlayerController.Update already has `if (!IsOwner) return, so this runs owner-only, same as PlayerActiveState.Update.

        //Rotate player based on mouse input (raw, no deltaTime)
        float mouseX = Input.GetAxisRaw("Mouse X");
        yaw += mouseX * charStats.rotationSpeed;

        //Ignore input for a short grace period
        if (Time.time - enterTime < 0.2f) return;

        //Exit locker
        if (Input.GetKeyDown(KeyCode.E)) {
            player.SwitchState(player.activeState);
            Debug.Log("Exited");
        }
    }

    public void FixedUpdate()
    {
        //Apply rotation at the physics rate so NetworkTransform sees it
        rb.MoveRotation(Quaternion.Euler(0f, yaw, 0f));
    }

    public void Exit()
    {
        //Re-enable collisions and movement on the local physics authority
        if (lockerCollider != null && playerCollider != null)
        {
            Physics.IgnoreCollision(playerCollider, lockerCollider, false);
        }

        rb.isKinematic = false;

        if (player.flashLight) player.flashLight.SetActive(true);

        //When exiting, PlayerActiveState.Enter or PlayerDisabledState.Enter, will immediately set a new anim state; nothing needed here.
    }
}