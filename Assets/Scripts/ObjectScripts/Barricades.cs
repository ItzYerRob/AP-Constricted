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
        else
        {
            //Player level too low
            hint = new InteractionHint($"Level {requiredLevel} required to break");
        }

        return true;
    }

    public void Interact(GameObject interactor)
    {
        if (_consumed) return;

        //Validate interactor is the player
        var player = interactor.GetComponent<PlayerController>();
        if (requirePlayerController && player == null) return;

        //Extra safety: enforce level requirement on the logic side as well.
        if (GameManager.Instance == null || GameManager.Instance.PlayerLevel < requiredLevel)
        {
            //Player not allowed to break yet.
            return;
        }

        _consumed = true;

        //We can put VFX/SFX here before destroy, like trhis:
        // Instantiate(pickupVfx, transform.position, Quaternion.identity);

        var netObj = GetComponent<NetworkObject>();
        if (netObj != null && netObj.IsSpawned)
        {
            //Despawn across the network
            netObj.Despawn();
        }
        else
        {
            //Fallback for non-networked testing
            Destroy(gameObject);
        }
    }

    [ClientRpc]
    private void PlayPickupEffectsClientRpc()
    {
        //Put local-only visuals here, ex::
        // AudioSource.PlayClipAtPoint(pickupSound, transform.position);
        // Instantiate(pickupVfxPrefab, transform.position, Quaternion.identity);
    }
}
