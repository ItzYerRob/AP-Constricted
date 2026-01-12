using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(Collider))]
public class MedkitPickup : NetworkBehaviour, IInteractable
{
    [Tooltip("Restrict interaction to objects with this component.")]
    public bool requirePlayerController = true;
    private bool _consumed; //Prevent double activation if multiple hits/frames call Interact.
    
    [Tooltip("If true, only CharacterStats with AreWeAPlayer = true will be healed.")]
    public bool onlyHealPlayers = true;

    [Tooltip("If true, this medkit can only be used once.")]
    public bool singleUse = true;

    private bool consumed;
    public GameObject EnemySoundGO;

    public bool TryGetHint(GameObject interactor, out InteractionHint hint) {
        hint = new InteractionHint("Heal [E]");
        return true;
    }

    public void Interact(GameObject interactor) {
        if (_consumed) return;

        //Validate interactor is the player
        var player = interactor.GetComponent<PlayerController>();
        if (requirePlayerController && player == null) return;
        
        //Try to find CharacterStats on the entering object or one of its parents
        var stats = interactor.GetComponentInParent<CharacterStats>();
        if (stats == null) return;

        if (onlyHealPlayers && !stats.AreWeAPlayer) return;

        //Already at full health? ignore
        if (stats.currentHealth.Value >= stats.maxHealth) return;

        //Heal to full on the server
        stats.Heal(100f);

        EnemySoundGO.SetActive(true);

        _consumed = true;

        //We can put VFX/SFX here before destroy, like trhis
        // Instantiate(pickupVfx, transform.position, Quaternion.identity);

        //Destroy / despawn medkit so nobody else can use it
        if (singleUse)
        {
            Destroy(gameObject);
            
            //In case we want to destroy this for all players? Dunno
            // var netObj = GetComponent<NetworkObject>();
            // if (netObj != null && netObj.IsSpawned)
            // {
            //     //Despawn across the network
            //     netObj.Despawn();
            // }
            // else
            // {
            //     //Fallback for non-networked testing
            //     Destroy(gameObject);
            // }
        }
    }

    [ClientRpc]
    private void PlayPickupEffectsClientRpc()
    {
        //Local-only visuals here, ex:
        // AudioSource.PlayClipAtPoint(pickupSound, transform.position);
        // Instantiate(pickupVfxPrefab, transform.position, Quaternion.identity);
    }
}