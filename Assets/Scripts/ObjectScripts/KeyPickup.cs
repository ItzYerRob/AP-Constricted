using UnityEngine;

public class KeyPickup : MonoBehaviour, IInteractable
{
    [Tooltip("Optional: restrict interaction to objects with this component.")]
    public bool requirePlayerController = true;
    private bool _consumed; //Prevent double activation if multiple hits/frames call Interact.

    public bool TryGetHint(GameObject interactor, out InteractionHint hint)
    {

        hint = new InteractionHint("Collect [E]");
        return true;
    }

    public void Interact(GameObject interactor)
    {
        if (_consumed) return;

        //Validate interactor is the player
        var player = interactor.GetComponent<PlayerController>();
        if (requirePlayerController && player == null) return;

        _consumed = true;
        GameManager.Instance.PickedUpKey();

        //We can put VFX/SFX here before destroy, like trhis
        // Instantiate(pickupVfx, transform.position, Quaternion.identity);

        Destroy(gameObject);
    }
}