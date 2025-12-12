using UnityEngine;

public class LevelPickup : MonoBehaviour, IInteractable
{
    [Tooltip("How many levels to grant on pickup.")]
    [Min(1)] public int levelsGranted = 1;

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
        GameManager.Instance.AddLevels(Mathf.Max(1, levelsGranted));

        //We can put VFX/SFX here before destroy, like trhis
        // Instantiate(pickupVfx, transform.position, Quaternion.identity);

        Destroy(gameObject);
    }
}