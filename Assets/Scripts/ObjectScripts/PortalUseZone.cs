using UnityEngine;

[RequireComponent(typeof(Collider))]
public class PortalUseZone : MonoBehaviour, IInteractable
{
    Portal portal;
    Transform selfPoint;
    Transform oppositePoint;
    //Label override per-portal-end
    [SerializeField] string labelOverride = "Use";

    bool initialized;

    //Called by Portal.Awake to tie this zone to the portal and the opposite endpoint.
    public void Initialize(Portal portal, Transform selfPoint, Transform oppositePoint) {
        this.portal        = portal;
        this.selfPoint     = selfPoint;
        this.oppositePoint = oppositePoint;
        initialized        = true;

        var col = GetComponent<Collider>();
        col.isTrigger = true;
    }

    void Reset() {
        //Make sure any collider we add in the editor is trigger by default
        var col = GetComponent<Collider>();
        if (col) col.isTrigger = true;
    }

    public bool TryGetHint(GameObject interactor, out InteractionHint hint) {
        if (!initialized || !portal || !oppositePoint) {
            hint = new InteractionHint("Portal (not configured)", false, "Missing endpoint.");
            return true;
        }

        if (!portal.CanUse(interactor, out var reason)) {
            hint = new InteractionHint(
                labelOverride,
                available: false,
                reason: reason
            );
            return true;
        }

        //Here we could also check lock state if we want to implement IInteractible
        hint = new InteractionHint(labelOverride, available: true);
        return true;
    }

    public void Interact(GameObject interactor)
    {
        if (!initialized || !portal || !oppositePoint) return;

        if (!portal.CanUse(interactor, out _)) return;

        portal.Teleport(interactor, oppositePoint);
    }
}