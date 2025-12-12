using UnityEngine;

public class GameOver : MonoBehaviour, IInteractable
{
    public GameObject GameOverUI;

    [Tooltip("Optional: restrict interaction to objects with this component.")]
    public bool requirePlayerController = true;
    private bool _consumed; //Prevent double activation if multiple hits/frames call Interact.

    public bool TryGetHint(GameObject interactor, out InteractionHint hint)
    {

        hint = new InteractionHint("End the Game [E]");
        return true;
    }

    public void Interact(GameObject interactor)
    {
        if (_consumed) return;

        //Validate interactor is the player
        var player = interactor.GetComponent<PlayerController>();
        if (requirePlayerController && player == null) return;

        _consumed = true;
        GameOverUI.SetActive(true);

        //We can put VFX/SFX here before destroy, like trhis
        // Instantiate(pickupVfx, transform.position, Quaternion.identity);
    }
}
