using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(Rigidbody))]
public class DamageOnContact : NetworkBehaviour
{
    [Header("Damage Tuning")]
    [Tooltip("Minimum relative speed before any damage is dealt.")]
    public float minRelativeSpeed = 2f;

    [Tooltip("Scales how much damage is done per unit of 'impact energy'.")]
    public float damagePerKineticUnit = 0.01f;

    [Tooltip("Optional hard cap on DPS to keep things sane.")]
    public float maxDamagePerSecond = 50f;

    [Tooltip("Layers that can be damaged by this object.")]
    public LayerMask damageableLayers = ~0;

    private Rigidbody rb;

    private void Awake() {
        rb = GetComponent<Rigidbody>();
    }

    //Continuous damage while colliding
    private void OnCollisionStay(Collision collision)
    {
        if (!IsServer) return; //Only the server should apply damage
        if ((damageableLayers.value & (1 << collision.gameObject.layer)) == 0) return; //Layer Filter

        var targetStats = collision.gameObject.GetComponentInParent<CharacterStats>();
        if (targetStats == null) return;

        if (targetStats.isImmune) return;

        //Impact strength calculation
        //How fast we are moving relative to the other body
        float relativeSpeed = collision.relativeVelocity.magnitude;

        //Ignore very soft contacts
        if (relativeSpeed < minRelativeSpeed) return;

        //Approximate kinetic energy of THIS damaging body
        float kinetic = rb.mass * relativeSpeed * relativeSpeed;

        //Convert kinetic energy to damage per second
        float damagePerSecond = kinetic * damagePerKineticUnit;

        //Clamp to avoid insane spikes
        if (maxDamagePerSecond > 0f) damagePerSecond = Mathf.Min(damagePerSecond, maxDamagePerSecond);

        //We are in a physics callback, damage should be scaled by fixedDeltaTime
        float damageThisStep = damagePerSecond * Time.fixedDeltaTime;

        if (damageThisStep <= 0f) return;

        targetStats.ApplyDamage(damageThisStep);
    }

    //Burst dmg on first impact
    private void OnCollisionEnter(Collision collision)
    {
        if (!IsServer) return;

        var targetStats = collision.gameObject.GetComponentInParent<CharacterStats>();
        if (targetStats == null || targetStats.isImmune) return;

        float relativeSpeed = collision.relativeVelocity.magnitude;
        if (relativeSpeed < minRelativeSpeed) return;

        //Impulse-based burst damage on first hit
        float impulseMagnitude = collision.impulse.magnitude;
        float burstDamage = impulseMagnitude * 0.1f;

        if (burstDamage > 0f)
            targetStats.ApplyDamage(burstDamage);
    }
}
