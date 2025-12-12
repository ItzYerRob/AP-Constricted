using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//Maybe Deprecated?
/// <summary>
/// Table vault logic isolated from PlayerActiveState:
/// - Spherecasts forward to find a "table" on a given LayerMask
/// - Validates table X/Y rotation in [-3, 3] degrees
/// - Temporarily ignores collisions with that table
/// - Applies a velocity-changing impulse in the aim direction + upward
/// </summary>
[DefaultExecutionOrder(1000)] // Run AFTER typical gameplay scripts
public class TableVault : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Rigidbody rb;
    [SerializeField] private Transform aimTransform;
    [SerializeField] private LayerMask tableMask;

    [Header("Detection")]
    [SerializeField] private float maxVaultDistance = 1.8f;
    [SerializeField] private float castRadius = 0.35f;
    [SerializeField] private float castHeight = 1.0f;
    [SerializeField] private float castVerticalBias = 0.25f;
    [SerializeField] private float castDistStart = -1.0f;

    [Header("Vault Impulse")]
    [SerializeField] private float horizontalSpeed = 6.5f;
    [SerializeField] private float upwardSpeed = 3.5f;

    [Header("Misc")]
    [SerializeField] private float maxXYAngle = 3f;
    [SerializeField] private float ignoreCollisionTime = 0.6f;
    [SerializeField] private float cooldown = 0.25f;
    [SerializeField] private bool debugGizmos = true;

    private float _lastVaultTime = -999f;
    private readonly List<(Collider a, Collider b)> _ignoredPairs = new List<(Collider, Collider)>();
    private Collider[] _playerColliders;
    private Transform _lastTableRoot;

    private void Reset()
    {
        rb = GetComponent<Rigidbody>();
        if (!aimTransform)
        {
            var cam = GetComponentInChildren<Camera>();
            if (cam) aimTransform = cam.transform;
        }

        //If a layer named "Table" exists, use it by default
        int tableLayer = LayerMask.NameToLayer("Table");
        if (tableLayer >= 0)
        {
            tableMask = (1 << tableLayer);
        }
    }

    private void Awake()
    {
        if (!rb) rb = GetComponent<Rigidbody>();
        _playerColliders = GetComponentsInChildren<Collider>(includeInactive: true);
    }

    private void Update()
    {
        if (!Input.GetButtonDown("Jump")) return;
        if (Time.time < _lastVaultTime + cooldown) return;

        if (TryFindValidTable(out var hit, out var tableRoot))
        {
            //Compute vault direction from aim (XZ only)
            Vector3 fwd = (aimTransform ? aimTransform.forward : transform.forward);
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = transform.forward;
            fwd.Normalize();

            Vector3 desiredVelocity = fwd * horizontalSpeed + Vector3.up * upwardSpeed;

            //Takeoff vector
            Vector3 currentV = rb.linearVelocity;
            Vector3 deltaV = desiredVelocity - currentV;
            rb.AddForce(deltaV, ForceMode.VelocityChange);

            //Temporarily ignore collisions between player and the table
            BeginIgnoreTable(tableRoot);

            _lastVaultTime = Time.time;
        }
    }

    private bool TryFindValidTable(out RaycastHit hit, out Transform tableRoot)
    {
        hit = default;
        tableRoot = null;

        //Build spherecast from chest height
        Vector3 dir = (aimTransform ? aimTransform.forward : transform.forward);
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-4f) dir = transform.forward;
        dir.Normalize();

        //Start offset must be along dir, not world forward
        Vector3 origin = transform.position + Vector3.up * castHeight + dir * castDistStart;

        Vector3 end = origin + dir * maxVaultDistance + Vector3.up * castVerticalBias;
        Vector3 castDir = end - origin;
        float castLen = castDir.magnitude;
        if (castLen < 1e-4f) return false;
        castDir /= castLen;

        if (!Physics.SphereCast(origin, castRadius, castDir, out hit, castLen, tableMask, QueryTriggerInteraction.Ignore))
            return false;

        tableRoot = ResolveTableRoot(hit.collider);

        //X and Y rot of object in [-maxXYAngle, +maxXYAngle]
        Vector3 e = tableRoot.rotation.eulerAngles;
        float x = Mathf.DeltaAngle(0f, e.x);
        float y = Mathf.DeltaAngle(0f, e.y);

        if (Mathf.Abs(x) > maxXYAngle) return false;
        if (Mathf.Abs(y) > maxXYAngle) return false;

        return true;
    }

    private static Transform ResolveTableRoot(Collider c)
    {
        if (c.attachedRigidbody) return c.attachedRigidbody.transform;
        return c.transform.root;
    }

    private void BeginIgnoreTable(Transform tableRoot)
    {
        _lastTableRoot = tableRoot;
        _ignoredPairs.Clear();

        var tableCols = tableRoot.GetComponentsInChildren<Collider>(includeInactive: true);
        for (int i = 0; i < _playerColliders.Length; i++)
        {
            var pc = _playerColliders[i];
            if (!pc || !pc.enabled || pc.isTrigger) continue;

            for (int j = 0; j < tableCols.Length; j++)
            {
                var tc = tableCols[j];
                if (!tc || !tc.enabled || tc.isTrigger) continue;

                Physics.IgnoreCollision(pc, tc, true);
                _ignoredPairs.Add((pc, tc));
            }
        }

        //Ensure we re-enable even if player never separates
        StartCoroutine(ReenableCollisionsAfter(ignoreCollisionTime));
    }

    private IEnumerator ReenableCollisionsAfter(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        for (int i = 0; i < _ignoredPairs.Count; i++)
        {
            var (a, b) = _ignoredPairs[i];
            if (a && b) Physics.IgnoreCollision(a, b, false);
        }
        _ignoredPairs.Clear();
        _lastTableRoot = null;
    }

    private void OnDrawGizmosSelected()
    {
        if (!debugGizmos) return;

        Vector3 dir = (aimTransform ? aimTransform.forward : transform.forward);
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-4f) dir = transform.forward;
        dir.Normalize();

        Vector3 origin = transform.position + Vector3.up * castHeight + dir * castDistStart;
        Vector3 end = origin + dir * maxVaultDistance + Vector3.up * castVerticalBias;

        Gizmos.DrawWireSphere(origin, castRadius);
        Gizmos.DrawWireSphere(end, castRadius);
        Gizmos.DrawLine(origin, end);

        if (Application.isPlaying)
        {
            Vector3 fwd = (aimTransform ? aimTransform.forward : transform.forward);
            fwd.y = 0f; fwd.Normalize();
            Vector3 desired = fwd * horizontalSpeed + Vector3.up * upwardSpeed;
            Gizmos.DrawRay(transform.position + Vector3.up * 0.5f, desired * 0.2f);
        }
    }
}