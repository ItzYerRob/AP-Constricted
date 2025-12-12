using UnityEngine;

public class PlayerDisabledState : IPlayerState
{
    private readonly PlayerController player;
    private Rigidbody rb;
    private float yaw; //Accumulated yaw in degrees

    public PlayerDisabledState(PlayerController player) {
        this.player = player;
        rb = player.GetComponent<Rigidbody>();
    }

    public void Enter() {
        //Disable objects when the player is "frozen"
        if (player.enabledObject) player.enabledObject.SetActive(false);
        if (player.lockerObject) player.lockerObject.SetActive(false);
        if (player.disabledObject) player.disabledObject.SetActive(true);

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

    public void Update() {
        //Rotate player based on mouse input, use raw mouse delta to avoid frame-based smoothing
        float mouseX = Input.GetAxisRaw("Mouse X");
        //Do NOT multiply by deltaTime for mouse deltas;
        yaw += mouseX * player.charStats.rotationSpeed;
    }

    public void FixedUpdate() {
        //Apply the rotation at the physics rate
        rb.MoveRotation(Quaternion.Euler(0f, yaw, 0f));
    }

    public void Exit() { }
}