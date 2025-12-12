using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(NetworkObject))]
public class NoiseOnImpact : NetworkBehaviour
{
    public float minSpeedForNoise = 1f;     //Below this, ignore
    public float maxSpeedForMaxNoise = 20f; //Clamp relative velocity into [min, max]
    public float baseRadius = 10f;          //Radius at minSpeed
    public float maxRadius = 30f;          //Radius at maxSpeed
    public float cooldown = 0.25f;         //Avoid spamming on bounces

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
        if (Time.time - _lastEmitTime < cooldown)
            return;

        float speed = collision.relativeVelocity.magnitude;
        if (speed < minSpeedForNoise)
            return;

        //Map speed to [0,1] loudness
        float t = Mathf.InverseLerp(minSpeedForNoise, maxSpeedForMaxNoise, speed);
        float radius = Mathf.Lerp(baseRadius, maxRadius, t);

        var no = GetComponent<NetworkObject>();

        NoiseEvent e = new NoiseEvent {
            Position = collision.contacts.Length > 0 ? collision.contacts[0].point : transform.position,
            Radius = radius,
            Loudness = t,
            Source = no
        };

        if (NoiseSystem.Instance != null) { NoiseSystem.Instance.EmitNoise(e);
            // Debug.Log($"[Noise] Emitted by {gameObject.name}. Loudness: {t:F2}, Radius: {radius:F2}, Position: {e.Position}");
        }


        //Here is where we should put code callbacks to play actual sound clips

        _lastEmitTime = Time.time;
    }
}
