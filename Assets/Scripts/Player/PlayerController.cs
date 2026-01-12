using Unity.Netcode;
using Unity.VisualScripting;
using UnityEngine;

public class PlayerController : NetworkBehaviour
{
    public CharacterStats charStats;

    public Transform cam;
    public float interactDistance = 5f;
    public LayerMask interactMask;
    public GameObject flashLight;

    [DoNotSerialize]public float horizontalInput;
    [DoNotSerialize]public float verticalInput;
    [DoNotSerialize]public float inputMag;

    [Header("Movement")]
    public float airAccel    = 0.2f;
    public float groundFriction = 1.0f;
    public Transform groundCheck;
    public LayerMask groundMask;
    public float maxStepHeight = 0.4f;
    public float stepCheckDistance = 0.4f;   //How far in front we look for steps
    public float stepSmooth = 10f;           //How fast we lerp upward
    [Range(0f, 89f)]
    public float maxSlopeAngle = 45f;
    public bool isGrounded, isWalking;
    private float groundedCoyoteTime = 0.1f;
    private float groundedTimer;
    public bool IsGroundedOrJustLeft => groundedTimer > 0f;
    
    [Header("State-related Objects")]
    public GameObject enabledObject;
    public GameObject lockerObject;
    public GameObject disabledObject;
    public Camera mainCamera;

    [HideInInspector] public float verticalVelocity;

    public IPlayerState currentState;

    //States
    [HideInInspector] public PlayerActiveState activeState;
    [HideInInspector] public PlayerLockerState lockerState;
    [HideInInspector] public PlayerDisabledState disabledState;

    public GameObject interactPrompt;
    private IInteractable currentInteractable;
    private InteractionHint currentHint;

    [Header("Visuals")]
    [SerializeField] private Animator bodyAnimator;   //Assign the child Animator here in Inspector

    public enum AnimState : byte {
        IdleHurt,
        WalkHurt,
        Jump
    }

    public NetworkVariable<AnimState> CurrentAnimState = new NetworkVariable<AnimState>(
            AnimState.IdleHurt,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

    private void Awake() {
        activeState = new PlayerActiveState(this);
        lockerState = new PlayerLockerState(this);
        disabledState = new PlayerDisabledState(this);
    }

    private void Start() {
        SwitchState(activeState);
    }

    private void Update() {
        if (!IsOwner) return; //Hard gate

        isGrounded = Physics.CheckSphere(groundCheck.position, 0.2f, groundMask);

        if (isGrounded) groundedTimer = groundedCoyoteTime;
        else groundedTimer -= Time.deltaTime;

        horizontalInput = Input.GetAxis("Horizontal");
        verticalInput = Input.GetAxis("Vertical");
        //Raw input magnitude for intent gating (before normalization)
        inputMag = Mathf.Clamp01(new Vector2(horizontalInput, verticalInput).magnitude);
        isWalking = (isGrounded && inputMag > 0.1f);

        if (Input.GetKeyDown(KeyCode.I))
        {
            if (currentState == activeState) SwitchState(disabledState);
            else if (currentState == disabledState) SwitchState(activeState);
        }

        DetectInteractable();

        if (Input.GetKeyDown(KeyCode.E) && currentInteractable != null && currentHint.available) { currentInteractable.Interact(gameObject); }

        currentState?.Update();
    }

    private void FixedUpdate() {
        if (!IsOwner) return; //Hard gate
        currentState?.FixedUpdate();
    }

    public void SwitchState(IPlayerState newState) {
        currentState?.Exit();
        currentState = newState;
        currentState.Enter();
    }

    public override void OnNetworkSpawn() {
        Debug.Log($"{name}  NetObjId={NetworkObjectId}  Owner={OwnerClientId}  Local={NetworkManager.LocalClientId}  IsOwner={IsOwner}  IsServer={IsServer}  IsHost={IsHost}");

        //Local-only visuals/input
        if (!IsOwner) {
            if (mainCamera) mainCamera.enabled = false;
            if (flashLight) flashLight.SetActive(false);
        }

        if (IsClient) { //We only care about visuals on clients
            CurrentAnimState.OnValueChanged += HandleAnimStateChanged;
            HandleAnimStateChanged(CurrentAnimState.Value, CurrentAnimState.Value); //Force initial state
        }
    }
    
    private void DetectInteractable() {
        Ray ray = new Ray(cam.position, cam.forward);
        if (Physics.Raycast(ray, out var hit, interactDistance, interactMask, QueryTriggerInteraction.Collide)) {
            var interactable = hit.collider.GetComponentInParent<IInteractable>();
            if (interactable != null && interactable.TryGetHint(gameObject, out var hint)) {
                //Check if anything about the hint changed, not just the interactable or label
                if (currentInteractable != interactable || 
                    !hint.label.Equals(currentHint.label) ||
                    hint.available != currentHint.available ||
                    hint.reasonIfUnavailable != currentHint.reasonIfUnavailable)
                {
                    currentInteractable = interactable;
                    currentHint = hint;
                    UIManager.Instance.interactPrompt.SetActive(true);
                    UIManager.Instance.SetInteractLabel(hint.label, hint.available, hint.reasonIfUnavailable);
                }
                return;
            }
        }

        if (currentInteractable != null) {
            currentInteractable = null;
            UIManager.Instance.interactPrompt.SetActive(false);
        }
    }

    private void OnDestroy() {
        if (IsClient) {
            CurrentAnimState.OnValueChanged -= HandleAnimStateChanged;
        }
    }

    private void HandleAnimStateChanged(AnimState oldState, AnimState newState) {
        if (!bodyAnimator) return;

        switch (newState) {
            case AnimState.IdleHurt:
                bodyAnimator.CrossFade("Idle_Hurt", 0.1f);
                break;

            case AnimState.WalkHurt:
                bodyAnimator.CrossFade("Walk_Hurt", 0.1f);
                break;

            case AnimState.Jump:
                bodyAnimator.CrossFade("Jump", 0.05f);
                break;
        }
    }

}