using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(Collider))]
public class Barricades : NetworkBehaviour, IInteractable
{
    [Tooltip("Restrict interaction to objects with this component.")]
    public bool requirePlayerController = true;

    [Tooltip("Minimum player level required to break this barricade.")]
    [SerializeField] private int requiredLevel = 2;

    private bool _consumed; //Prevent double activation if multiple hits/frames call Interact.

    public bool TryGetHint(GameObject interactor, out InteractionHint hint) {
        int playerLevel = 0; //Default if no GM

        if (GameManager.Instance != null) {
            playerLevel = GameManager.Instance.PlayerLevel;
        }

        if (playerLevel >= requiredLevel) {
            //Player is high enough level
            hint = new InteractionHint("Break [E]");
        }
        else {
            //Player level too low
            hint = new InteractionHint($"Level {requiredLevel} required to break");
        }

        return true;
    }

    public void Interact(GameObject interactor) {
        if (_consumed) return;

        //Sanity check on caller side
        var player = interactor.GetComponent<PlayerController>();
        if (requirePlayerController && player == null) return;

        //Gate by the local client's level. (Host will also run this locally, which is fine.)
        int localLevel = GetLocalPlayerLevel();
        if (localLevel < requiredLevel)
            return;

        _consumed = true; //prevent multiple interact prompts somehow.

        //If we're the server/host, we can immediately despawn.
        if (IsServer) {
            DespawnOnServer();
            return;
        }

        //Otherwise ask the server to despawn. We only need some identifier for who requested; the server doesn't need level.
        var interactorNetObj = interactor.GetComponent<NetworkObject>();
        if (interactorNetObj == null) return;

        RequestBreakServerRpc(interactorNetObj.NetworkObjectId);
    }

    private int GetLocalPlayerLevel() {
        //Use local GameManager state only.
        return (GameManager.Instance != null) ? GameManager.Instance.PlayerLevel : 0;
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestBreakServerRpc(ulong interactorNetworkObjectId, ServerRpcParams rpcParams = default) {
        if (_consumed) return;

        //Server-side validation: ensure the requester object exists
        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(interactorNetworkObjectId, out var interactorNetObj)) return;

        if (requirePlayerController && interactorNetObj.GetComponent<PlayerController>() == null) return;

        _consumed = true;

        PlayPickupEffectsClientRpc();
        DespawnOnServer();
    }

    private void DespawnOnServer() {
        //Server-side despawn
        var netObj = GetComponent<NetworkObject>();
        if (netObj != null && netObj.IsSpawned) netObj.Despawn(true);
        else Destroy(gameObject);
    }

    [ClientRpc]
    private void PlayPickupEffectsClientRpc() {
        //visuals/audio on all clients
    }

}
