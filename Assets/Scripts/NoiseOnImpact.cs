using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(NetworkObject))]
public class NoiseOnImpact : NetworkBehaviour
{
    public float minSpeedForNoise = 1f; //Below this, ignore
    public float maxSpeedForMaxNoise = 20f; //Clamp relative velocity into [min, max]
    public float baseRadius = 10f; //Radius at minSpeed
    public float maxRadius = 30f; //Radius at maxSpeed
    public float cooldown = 0.25f; //Avoid spamming on bounces

    [Header("Noise Projection")]
    public LayerMask floorMask = ~0;
    public float rayUp = 0.15f; //Start above the contact
    public float maxDrop = 20f; //how far down we search for a floor

    private Rigidbody _rb;
    private float _lastEmitTime = -999f;

    void Awake() {
        _rb = GetComponent<Rigidbody>();
    }

    void OnCollisionEnter(Collision collision)
    {
        //Only server should decide AI behaviour.
        if (!IsServer) return;

        //Simple cooldown to avoid multiple noise events from the same bounce cascade.
        if (Time.time - _lastEmitTime < cooldown) return;

        float speed = collision.relativeVelocity.magnitude;
        if (speed < minSpeedForNoise) return;

        //Map speed to [0,1] loudness
        float t = Mathf.InverseLerp(minSpeedForNoise, maxSpeedForMaxNoise, speed);
        float radius = Mathf.Lerp(baseRadius, maxRadius, t);

        //If has collision data, rawPos at contact point, else at current obj position
        Vector3 rawPos = (collision.contacts.Length > 0)
            ? collision.contacts[0].point
            : transform.position;

        // Project the noise down to the floor beneath the impact point.
        Vector3 resolvedPos = ResolveToFloor(rawPos);
        
        NoiseEvent e = new NoiseEvent {
            Position = resolvedPos,
            Radius   = radius,
            Loudness = t,
            Source   = GetComponent<NetworkObject>()
        };

        if (NoiseSystem.Instance != null) NoiseSystem.Instance.EmitNoise(e);

        _lastEmitTime = Time.time;
    }

    private Vector3 ResolveToFloor(Vector3 p)
    {
        Vector3 origin = p + Vector3.up * rayUp;
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, maxDrop, floorMask, QueryTriggerInteraction.Ignore))
        {
            return hit.point;
        }
        return p; //Fallback if nothing below
    }

}
