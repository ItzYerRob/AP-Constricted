using UnityEngine;

public class HideSpot : MonoBehaviour, IInteractable, ILockable
{
    public bool used = false;
    public bool open = false;
    public bool locked = false;
    public Camera mainCamera;

    [Header("Hiding Position")]
    public Transform hidePoint;

    public bool Locked { get => locked; set => locked = value; }

    void Awake()
    {
        if (hidePoint == null)
        {
            hidePoint = transform.Find("hideCenterPoint");
            if (hidePoint == null)
                Debug.LogError($"[HideSpot] No child named 'hideCenterPoint' found on {name}.");
        }
    }

    public bool TryGetHint(GameObject interactor, out InteractionHint hint)
    {
        if (locked)
        {
            hint = new InteractionHint("Unlock [E]");
            return true;
        }

        var player = interactor.GetComponent<PlayerController>();
        if (player != null && player.currentState == player.lockerState)
        {
            hint = new InteractionHint("Exit [E]");
            return true;
        }

        hint = new InteractionHint("Hide [E]");
        return true;
    }

    public void Interact(GameObject interactor)
    {
        var player = interactor.GetComponent<PlayerController>();
        if (player == null) return;

        if (!locked)
        {
            if (player.currentState == player.activeState)
            {
                player.lockerState.SetLocker(this);
                player.SwitchState(player.lockerState);
            }
            else if (player.currentState == player.lockerState)
            {
                player.SwitchState(player.activeState);
            }
        }
        else
        {
            if (!QTEManager.Instance.RequestQTE(this))
                Debug.Log("[HideSpot] QTE busy; ignoring interaction.");
        }
    }

    public void OnUnlockSucceeded()
    {
        Debug.Log("[HideSpot] Unlock succeeded.");
    }

    public void OnUnlockFailed()
    {
        Debug.Log("[HideSpot] Unlock failed.");
    }
}