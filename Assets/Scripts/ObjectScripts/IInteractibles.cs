using UnityEngine;

public readonly struct InteractionHint
{
    public readonly string label;
    public readonly bool available; //Is this action currently allowed? (ex: blocked by lock, cooldown, permissions, etc)
    public readonly string reasonIfUnavailable; //Optional details (ex: Need Lockpick, etc)

    public InteractionHint(string label, bool available = true, string reason = null) {
        this.label = label;
        this.available = available;
        this.reasonIfUnavailable = reason;
    }
}

public interface IInteractable {
    //What would happen if I pressed the key now?
    bool TryGetHint(GameObject interactor, out InteractionHint hint);

    //Actually do the thing.
    void Interact(GameObject interactor);
}

public interface ILockable {
    bool Locked { get; set; }

    //Called by QTE on success
    void OnUnlockSucceeded();

    //Called by QTE on fail
    void OnUnlockFailed();
}
