using UnityEngine;

[DisallowMultipleComponent]
public class Portal : MonoBehaviour
{
    [Header("Child names (prefix match allowed)")]
    [Tooltip("Will match children that START with this name (e.g. EntryPoint, EntryPoint (2), EntryPointCustom).")]
    public string entryName = "EntryPoint";

    [Tooltip("Will match children that START with this name (e.g. ExitPoint, ExitPoint (2), ExitPointCustom).")]
    public string exitName = "ExitPoint";

    [Header("Destination")]
    [Tooltip("If true, align player rotation to destination child's forward.")]
    public bool alignRotation = true;

    [Tooltip("Extra offset applied at the destination (in world space).")]
    public Vector3 exitOffset = Vector3.zero;

    [Header("Safety")]
    [Tooltip("Min delay between teleports to avoid bounce.")]
    public float cooldown = 0.25f;

    [Tooltip("Shift forward from the destination point to avoid clipping/collisions.")]
    public float destinationClearance = 0.5f;

    Transform entry;
    Transform exit;

    float lastUseTime = -999f;

    public Transform Entry => entry;
    public Transform Exit  => exit;

    void Awake() {
        entry = FindChildStartingWith(entryName);
        exit  = FindChildStartingWith(exitName);

        if (!entry || !exit) {
            Debug.LogError(
                $"Portal '{name}': could not find child starting with '{entryName}' and/or '{exitName}'.\n" +
                "Create child transforms with names starting with these strings. (Positions/orientations matter.)",
                this
            );
            return;
        }

        //Auto-attach PortalUseZone to the children if missing
        EnsureUseZone(entry, exit);
        EnsureUseZone(exit, entry);
    }

    Transform FindChildStartingWith(string prefix) {
        foreach (Transform child in transform) {
            if (child.name.StartsWith(prefix))
                return child;
        }
        return null;
    }

    void EnsureUseZone(Transform self, Transform opposite)
    {
        var zone = self.GetComponent<PortalUseZone>();
        if (!zone)
            zone = self.gameObject.AddComponent<PortalUseZone>();

        zone.Initialize(this, self, opposite);

        //Ensure we have a trigger collider for the interaction system to hit
        var col = self.GetComponent<Collider>();
        if (!col)
            col = self.gameObject.AddComponent<SphereCollider>();
        col.isTrigger = true;
    }

    //Check if the portal can be used right now (cooldown, etc).
    public bool CanUse(GameObject interactor, out string reasonIfUnavailable) {
        if (Time.time < lastUseTime + cooldown) {
            reasonIfUnavailable = "";
            return false;
        }

        reasonIfUnavailable = null;
        return true;
    }

    //Teleport the given interactor from one side to the other.
    public void Teleport(GameObject interactor, Transform destination) {
        if (!interactor) return;

        //Cooldown
        lastUseTime = Time.time;

        var t = interactor.transform;

        //Compute target pose
        Vector3 dstPos = destination.position
                         + destination.forward * destinationClearance
                         + exitOffset;

        Quaternion dstRot = alignRotation
            ? Quaternion.LookRotation(destination.forward, Vector3.up)
            : t.rotation;

        //Move based on controller type
        if (interactor.TryGetComponent(out CharacterController characterController) && characterController.enabled) {
            characterController.enabled = false;
            t.SetPositionAndRotation(dstPos, dstRot);
            characterController.enabled = true;
        }
        else if (interactor.TryGetComponent(out Rigidbody rb)) {
            rb.position = dstPos;
            rb.rotation = dstRot;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        else {
            t.SetPositionAndRotation(dstPos, dstRot);
        }
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected() {
        var e = FindChildStartingWith(entryName);
        var x = FindChildStartingWith(exitName);

        if (e) {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(e.position, 0.5f);
        }
        if (x) {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(x.position, 0.5f);
        }
        if (e && x) {
            Gizmos.color = Color.white;
            Gizmos.DrawLine(e.position, x.position);
        }
    }
#endif
}