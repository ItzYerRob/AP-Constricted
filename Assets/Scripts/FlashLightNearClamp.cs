using UnityEngine;

[RequireComponent(typeof(Light))]
public class FlashlightNearClamp : MonoBehaviour
{
    public Camera cam;

    [Header("Intensity")]
    public float maxIntensity = 200f;
    public float minIntensity = 5f;
    public float intensityLerpSpeed = 5f; //How fast we move toward target intensity

    [Header("Clamp & Probe")]
    public float maxClampDistance = 2f;
    public float probeDistance = 5f;
    public LayerMask hitMask = ~0;
    public AnimationCurve clampCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Cone Sampling")]
    public float coneAngle = 10f;      //Degrees, full angle of cone we sample around forward
    public int coneRaysPerAxis = 2;    //Number of steps in + / - yaw & pitch; total rays ~= (2n+1)^2

    private Light L;
    private float _targetIntensity;

    void Awake() {
        L = GetComponent<Light>();
        if (!cam) cam = Camera.main;

        L.type = LightType.Spot;
        //Start at max so we don't "fade in" from zero on the first frame
        _targetIntensity = maxIntensity;
        L.intensity = _targetIntensity;
    }

    void LateUpdate() {
        //Determine the clamp factor t based on the nearest hit inside a cone
        float t = ComputeClampFactorFromCone();

        //Convert t into a target intensity
        _targetIntensity = Mathf.Lerp(minIntensity, maxIntensity, t);

        //Smoothly move current intensity toward target intensity
        L.intensity = Mathf.MoveTowards(
            L.intensity,
            _targetIntensity,
            intensityLerpSpeed * Time.deltaTime
        );
    }

    //Casts multiple rays in a cone around camera forward and returns a clamp factor t in [0,1], based on the closest hit within that cone.
    private float ComputeClampFactorFromCone() {
        if (!cam) return 1f; //Fail-safe: no camera, no clamp

        Vector3 origin = cam.transform.position;
        Vector3 forward = cam.transform.forward;

        float closestHit = probeDistance;
        bool hitSomething = false;

        //Center ray first, saves performance if it hits
        if (Physics.Raycast(origin, forward, out var centerHit, probeDistance, hitMask, QueryTriggerInteraction.Ignore)) {
            hitSomething = true;
            closestHit = centerHit.distance;
        }

        //If coneRaysPerAxis == 0, we only do the center ray
        if (coneRaysPerAxis > 0)
        {
            float halfAngle = coneAngle * 0.5f;

            //Loop yaw / pitch offsets in a grid
            for (int y = -coneRaysPerAxis; y <= coneRaysPerAxis; y++) {
                for (int x = -coneRaysPerAxis; x <= coneRaysPerAxis; x++) {
                    //Skip center, we already did it
                    if (x == 0 && y == 0) continue;

                    float yaw   = (x / (float)coneRaysPerAxis) * halfAngle;   //left/right
                    float pitch = (y / (float)coneRaysPerAxis) * halfAngle;   //up/down

                    //Yaw around camera up, then pitch around camera right
                    Quaternion rot =
                        Quaternion.AngleAxis(yaw,   cam.transform.up) *
                        Quaternion.AngleAxis(-pitch, cam.transform.right);

                    Vector3 dir = rot * forward;

                    if (Physics.Raycast(origin, dir, out var hit, probeDistance, hitMask, QueryTriggerInteraction.Ignore)) {
                        hitSomething = true;
                        if (hit.distance < closestHit)
                            closestHit = hit.distance;
                    }
                }
            }
        }

        //No hits in cone: no clamp
        if (!hitSomething) return 1f;

        //Map closest hit distance to [0,1] then through curve
        float d = Mathf.Clamp(closestHit, 0f, maxClampDistance);
        float normalized = d / maxClampDistance;    //0 at contact, 1 at maxClampDistance
        return clampCurve.Evaluate(normalized);
    }
}