using UnityEngine;
using System.Collections;
using Unity.Netcode;

public class Door : NetworkBehaviour, IInteractable, ILockable
{
    //These are private, as it's good practice, other scripts should interact via the Locked property or Interact(), not by directly changing the network variable.
    private NetworkVariable<bool> networkOpen = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<bool> networkLocked = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    [Header("Initial State")]
    [SerializeField]
    private bool startsLocked = false;
    [SerializeField]
    private bool startsOpen = false;
    
    [Header("Rotation")]
    public float openAngleOffset = 90f;
    public float rotationSpeedDegPerSec = 180f;
    public RotationAxis openAxis = RotationAxis.Z;
    public enum RotationAxis {X, Y,Z}

    private Quaternion closedRot;
    
    //Local flag on each client and the server to prevent spamming animations and to update the hint.
    //The server will have its own 'isAnimating' flag to prevent networked state, changes from being spammed while its animating.
    private bool isAnimating;

    public bool Locked { 
        get => networkLocked.Value; 
        set => SetLockedServerRpc(value); 
    }

    private Rigidbody rb;

    void Awake() {
        closedRot = transform.localRotation;
        rb = GetComponent<Rigidbody>(); 
        if (rb == null) Debug.LogError("Door is missing a Rigidbody component");
    }

    //OnNetworkSpawn is called when the object is initialized on the network, this is the best place to subscribe to NetworkVariable changes.
    public override void OnNetworkSpawn() {
        //The Server is responsible for setting the initial state.
        if (IsServer) {
            networkOpen.Value = startsOpen;
            networkLocked.Value = startsLocked;
        }

        //Subscribe to changes
        networkOpen.OnValueChanged += OnOpenStateChanged;
        networkLocked.OnValueChanged += OnLockStateChanged;

        //Immediately set the door to its *current* networked state when spawned, this ensures clients who join late see the door in its correct state.
        AnimateToState(networkOpen.Value, skipAnimation: true);
    }

    //Always unsubscribe from events when the object is despawned.
    public override void OnNetworkDespawn() {
        networkOpen.OnValueChanged -= OnOpenStateChanged;
        networkLocked.OnValueChanged -= OnLockStateChanged;
    }

    //Called on all clients (and the server) when networkOpen.Value changes.
    private void OnOpenStateChanged(bool previousValue, bool newValue) { AnimateToState(newValue); }
    
    //Reaction to a state change from the server.
    private void OnLockStateChanged(bool previousValue, bool newValue) {
        //We could play a locked sound here
        Debug.Log($"[Door] Lock state changed to: {newValue}");
    }

    //Helper function to run the animation.
    private void AnimateToState(bool shouldBeOpen, bool skipAnimation = false) {
        
        if (isAnimating) { StopAllCoroutines();}

        //Build the Euler offset depending on which axis we picked
        Vector3 eulerOffset = Vector3.zero;
        if (shouldBeOpen) {
            switch (openAxis) {
                case RotationAxis.X:
                    eulerOffset.x = openAngleOffset;
                    break;
                case RotationAxis.Y:
                    eulerOffset.y = openAngleOffset;
                    break;
                case RotationAxis.Z:
                    eulerOffset.z = openAngleOffset;
                    break;
            }
        }

        Quaternion target = closedRot * Quaternion.Euler(eulerOffset);

        if (skipAnimation) {
            transform.localRotation = target;
            return;
        }

        StartCoroutine(RotateTo(target));
    }

    public bool TryGetHint(GameObject interactor, out InteractionHint hint) {
        //Read from the NetworkVariables to provide the correct hint.
        if (networkLocked.Value) {
            hint = new InteractionHint("Unlock [E]", available: true);
            return true;
        }

        hint = new InteractionHint(networkOpen.Value ? "Close [E]" : "Open [E]", 
                                    available: !isAnimating,
                                    reason: isAnimating ? "Busy" : null);
        return true;
    }

    public void Interact(GameObject interactor) {
        var player = interactor.GetComponent<PlayerController>();
        if (player == null) return;

        //The client just tells the server they interacted here.
        InteractServerRpc();
    }

    //[ServerRpc] attribute means this function is called by a client but executes on the server, we set RequireOwnership = false so any client can interact, not just the owner.
    [ServerRpc(RequireOwnership = false)]
    private void InteractServerRpc(ServerRpcParams rpcParams = default) {
        if (isAnimating) { //Only runs on the server.
            Debug.Log("[Door Server] Animation busy; ignoring interaction.");
            return;
        }

        if (networkLocked.Value) {
            //Get the Client ID of the player who sent this RPC
            ulong clientId = rpcParams.Receive.SenderClientId;

            //Create parameters to send an RPC *only* to that client
            var clientRpcParams = new ClientRpcParams {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
            };

            //Send the ClientRpc to tell that client to start their QTE
            RequestQTEOnClientRpc(clientRpcParams);
        }
        else
        {
            //If not locked, just toggle the door (server-side)
            TryToggleOpen();
        }
    }

    public void TryToggleOpen() {
        //Guard to run only on server.
        if (!IsServer) return;

        //The server checks its own isAnimating flag and the lock state.
        if (networkLocked.Value || isAnimating) return;

        //The server doesn't animate. It just changes the networked state.
        networkOpen.Value = !networkOpen.Value;

        //Because networkOpen.Value changed, OnOpenStateChanged will now be triggered on the server and all clients, causing all of them to run the animation.
    }

    private IEnumerator RotateTo(Quaternion target) {
        isAnimating = true;

        if (rb != null) rb.isKinematic = true;

        float angle = Quaternion.Angle(transform.localRotation, target);
        float duration = Mathf.Approximately(rotationSpeedDegPerSec, 0f) ? 0f : angle / rotationSpeedDegPerSec;

        Quaternion start = transform.localRotation;
        float t = 0f;

        if (duration <= 0f) {
            transform.localRotation = target;
            isAnimating = false;
            yield break;
        }

        while (t < 1f) {
            t += Time.deltaTime / duration;
            transform.localRotation = Quaternion.Slerp(start, target, Mathf.SmoothStep(0f, 1f, t));
            yield return null;
        }

        transform.localRotation = target;
        isAnimating = false;
    }

    //This ServerRpc is called when the Locked property's setter is used.
    [ServerRpc(RequireOwnership = false)]
    private void SetLockedServerRpc(bool newLockState) { networkLocked.Value = newLockState; }

    //Called by the local QTEManager on the client
    public void OnUnlockSucceeded() {
        //Don't do anything locally. Tell the server we succeeded.
        ReportQTEResultServerRpc(true);
    }

    //Called by the local QTEManager on the client
    public void OnUnlockFailed() {
        //Don't do anything locally. Tell the server we failed.
        ReportQTEResultServerRpc(false);
    }

    //Called by the client's QTE.
    [ServerRpc(RequireOwnership = false)]
    private void ReportQTEResultServerRpc(bool success)
    {
        //Runs on the server
        if (success) {
            Debug.Log("[Door Server] QTE Unlock succeeded; opening.");
            networkLocked.Value = false;
            
            //Call the server-side helper to open the door
            TryToggleOpen();
        }
        else {
            Debug.Log("[Door Server] QTE Unlock failed.");
            //Maybe send another rpc to the client to warn of failure?
        }
    }

    //Called by the server
    [ClientRpc]
    private void RequestQTEOnClientRpc(ClientRpcParams clientRpcParams = default) {
        //Runs on the client that interacted, it finds its local QTEManager instance and starts the QTE
        if (!QTEManager.Instance.RequestQTE(this)) {
            //QTE was busy *on the client's screen*, which is fine.
            Debug.Log("[Door Client] My local QTEManager was busy.");
        }
    }

}