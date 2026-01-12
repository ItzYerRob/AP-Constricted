using Unity.Netcode;
using UnityEngine;

public class RBManipulator : NetworkBehaviour
{
    [Header("Setup")]
    public Camera cam;
    public LayerMask grabbableMask = ~0;

    [Header("Grab Tuning")]
    public float maxGrabDistance = 1f;
    public float grabRadius = 0.35f;     //World-space radius for SphereCast
    public bool useScreenSpaceAim = false;
    public float screenAimRadiusPx = 32f;
    public int screenAimSamples = 13;    //Number of rays inside aim circle

    [Header("Hold/Throw")]
    public float baseHoldDistance = 3f;
    public float holdHeightOffset = -0.5f;
    public float springStrength = 500f;
    public float dampingStrength = 50f;
    public float throwForce = 800f;

    [Header("Debug")]
    public bool debugLogs = true;
    public bool debugDraw = true;

    private float objectSize;
    private static readonly RaycastHit[] _hitBuffer = new RaycastHit[32]; //Reuse buffers

    //Net-hold state
    NetGrabbableRB heldNet;
    Rigidbody heldRB;

    uint _grabToken;
    bool _pendingGrab;
    float _pendingSince;
    float _retryAccumulator;

    [Header("Grab Retry")]
    public float grabRetryInterval = 0.2f;
    public float grabTimeout = 2.0f;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        //Only the owning client runs this manipulator
        if (!IsOwner || !IsClient) {
            enabled = false;
            return;
        }

        if (!cam) cam = Camera.main;
    }

    void Update()
    {
        if (!IsOwner || !IsClient) return;

        if (GameManager.Instance != null && GameManager.Instance.PlayerLevel < 2) {
            if (heldRB) Release(false); return;
        }

        if (Input.GetMouseButtonDown(1)) TryGrab();

        if (Input.GetMouseButtonUp(1)) Release(true);
    }
    
    void FixedUpdate()
    {
        if (!IsOwner || !IsClient) return;

        //If we grabbed but it wasnt acknowledged, retry
        if (_pendingGrab && heldNet)
        {
            bool ack =
                heldNet.Held.Held &&
                heldNet.Held.HolderId == NetworkManager.Singleton.LocalClientId &&
                heldNet.Held.Token == _grabToken;

            if (!ack)
            {
                //Timeout: give up and revert local optimistic state
                if (Time.time - _pendingSince > grabTimeout)
                {
                    FailPendingGrab();
                    return;
                }

                //Periodic resend (don't spam every tick)
                _retryAccumulator += Time.fixedDeltaTime; //Accumulate time since last FixedUpdate
                if (_retryAccumulator >= grabRetryInterval) {
                    //Reset the accumulator
                    _retryAccumulator = 0f;

                    //Resend the RPC
                    heldNet.TryGrabServerRpc(_grabToken, cam.transform.position, cam.transform.forward);
                }
                return; //Don’t apply hold forces until acked
            }

            _pendingGrab = false;

            //Now it is safe to apply forces / movement
        }

        if (heldRB) MoveHeldObject();
    }

    void TryGrab()
    {
        if (!TryGetBestHit(out RaycastHit bestHit)) return;

        var rb = bestHit.rigidbody;
        var net = rb ? rb.GetComponent<NetGrabbableRB>() : null;
        if (!rb || !net) return;

        //Start a new request token
        _grabToken++;
        _pendingGrab = true;
        _pendingSince = Time.time;
        _retryAccumulator = 0f;

        //Ask server
        net.TryGrabServerRpc(_grabToken, cam.transform.position, cam.transform.forward);

        //Optimistic local hold
        heldRB = rb;
        heldNet = net;
        heldRB.useGravity = false;
        heldRB.isKinematic = false;

        objectSize = bestHit.collider.bounds.extents.magnitude;
    }

    void FailPendingGrab()
    {
        //Revert local optimistic changes
        if (heldRB)
        {
            heldRB.useGravity = true;
        }
        heldRB = null;
        heldNet = null;
        _pendingGrab = false;
    }
    void Release(bool throwIt)
    {
        if (!heldRB) return;

        //If we never got ack, just local-drop (don’t send release)
        if (_pendingGrab)
        {
            FailPendingGrab();
            return;
        }

        //Normal release
        var v = heldRB.linearVelocity;
        var w = heldRB.angularVelocity;

        if (throwIt)
        {
            heldRB.AddForce(cam.transform.forward * throwForce, ForceMode.Impulse);
            v = heldRB.linearVelocity;
        }

        heldNet.ReleaseServerRpc(_grabToken, heldRB.position, heldRB.rotation, v, w);

        heldRB.useGravity = true;
        heldRB = null;
        heldNet = null;
    }

    void MoveHeldObject() {
        Vector3 targetPos =
            cam.transform.position +
            cam.transform.forward * (baseHoldDistance + objectSize) +
            Vector3.up * holdHeightOffset;

        Vector3 displacement = targetPos - heldRB.position;
        Vector3 springForce = displacement * springStrength;
        Vector3 dampingForce = -heldRB.linearVelocity * dampingStrength;

        heldRB.AddForce(springForce + dampingForce, ForceMode.Force);
        heldRB.AddTorque(-heldRB.angularVelocity * 0.5f, ForceMode.Force);

        if (debugDraw) Debug.DrawLine(heldRB.position, targetPos, Color.magenta, Time.fixedDeltaTime);
    }

    bool TryGetBestHit(out RaycastHit bestHit) {
        return useScreenSpaceAim
            ? TryGetBestHitScreenSpace(out bestHit)
            : TryGetBestHitSphereCast(out bestHit);
    }

    //World-space thick ray
    bool TryGetBestHitSphereCast(out RaycastHit bestHit) {
        bestHit = default;
        float bestDistance = float.MaxValue;

        var ray = new Ray(cam.transform.position, cam.transform.forward);
        if (debugDraw) Debug.DrawRay(ray.origin, ray.direction * maxGrabDistance, Color.cyan, 0.25f);

        int count = Physics.SphereCastNonAlloc(
            ray, grabRadius, _hitBuffer, maxGrabDistance,
            grabbableMask, QueryTriggerInteraction.Ignore);

        if (debugLogs) Debug.Log($"SphereCast hits={count} radius={grabRadius} mask={grabbableMask.value}");

        for (int i = 0; i < count; i++) {
            var hit = _hitBuffer[i];
            if (debugLogs) {
                var hasRB = hit.rigidbody ? "RB" : "noRB";
                Debug.DrawLine(ray.origin, hit.point, Color.yellow, 0.25f);
                Debug.Log($" -[{i}] {hit.collider.name} dist={hit.distance:F2} {hasRB}");
            }

            if (!hit.rigidbody) continue;

            var mani = hit.collider.GetComponentInParent<NetGrabbableRB>();
            if (!mani) continue;

            if (hit.distance < bestDistance) {
                bestDistance = hit.distance;
                bestHit = hit;
            }
        }

        if (bestDistance < float.MaxValue) {
            Debug.Log($"Best hit: {bestHit.collider.name} dist={bestDistance:F2}");
            return true;
        }

        return false;
    }

    //Screen-space aim circle
    bool TryGetBestHitScreenSpace(out RaycastHit bestHit) {
        bestHit = default;
        float bestDistance = float.MaxValue;

        Vector2 center = new Vector2(0.5f, 0.5f);
        float rx = screenAimRadiusPx / Screen.width;
        float ry = screenAimRadiusPx / Screen.height;

        Vector2[] samples = GetSampleOffsets(screenAimSamples);

        int rays = 0, hits = 0;
        foreach (var s in samples) {
            rays++;
            Vector2 vp = new Vector2(center.x + s.x * rx, center.y + s.y * ry);
            Ray ray = cam.ViewportPointToRay(new Vector3(vp.x, vp.y, 0f));
            if (debugDraw) Debug.DrawRay(ray.origin, ray.direction * maxGrabDistance, Color.cyan, 0.25f);

            if (Physics.Raycast(ray, out RaycastHit hit, maxGrabDistance, grabbableMask, QueryTriggerInteraction.Ignore)) {
                hits++;
                if (debugLogs) Debug.Log($"Ray[{rays}] → hit {hit.collider.name} dist={hit.distance:F2} hasRB={hit.rigidbody!=null}");
                if (!hit.rigidbody) continue;

                var mani = hit.collider.GetComponentInParent<NetGrabbableRB>();
                if (!mani) continue;

                if (hit.distance < bestDistance) {
                    bestDistance = hit.distance;
                    bestHit = hit;
                }
            }
        }

        if (debugLogs) Debug.Log($"ScreenSpace rays={rays} hits={hits}");
        if (bestDistance < float.MaxValue) {
            Debug.Log($"Best hit: {bestHit.collider.name} dist={bestDistance:F2}");
            return true;
        }
        return false;
    }

    static Vector2[] GetSampleOffsets(int n) {
        if (n < 1) n = 1;
        if (n == 1) return new[] { Vector2.zero };

        int ring = n - 1;
        Vector2[] res = new Vector2[n];
        res[0] = Vector2.zero;
        for (int i = 0; i < ring; i++)
        {
            float t = (i / (float)ring) * Mathf.PI * 2f;
            float r = 1f;
            res[i + 1] = new Vector2(Mathf.Cos(t), Mathf.Sin(t)) * r;
        }
        return res;
    }
}
