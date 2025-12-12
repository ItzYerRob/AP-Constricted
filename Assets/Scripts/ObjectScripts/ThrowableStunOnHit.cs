using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(Rigidbody), typeof(Collider))]
public class ThrowableStunOnHit : NetworkBehaviour
{
    [Header("Filters")]
    public LayerMask enemyLayers = ~0;
    public bool useTriggersToo = false; //If colliders are triggers

    [Header("Gating")]
    public float minRelativeSpeed = 2.5f; //Require at least this impact speed
    public float reuseCooldown = 0.15f; //Cooldown before this object can stun again
    public bool oneShot = false; //If true, disables after first successful stun

    [Header("Stun Payload")]
    public float stunDuration = 0.6f;
    public float investigateDistance = 9f; //Only used if enemy had no target on stun start

    [Header("Direction")]
    public bool useObjectVelocityAsDirection = true; //Prefer incoming velocity
    public bool flattenY = true; //Ignore vertical component

    private Rigidbody _rb;
    private float _nextAllowedTime = 0f;
    private bool _disabled;

    void Awake() { _rb = GetComponent<Rigidbody>(); }

    void OnCollisionEnter(Collision c) {
        if (useTriggersToo) return;
        TryStunFromCollision(c.collider, c.relativeVelocity, c.GetContact(0).point);
    }

    //Trigger cols? Unsure if needed
    void OnTriggerEnter(Collider other) {
        if (!useTriggersToo) return;

        //Approximate relative speed for triggers: object linear speed
        Vector3 relVel = _rb ? _rb.linearVelocity : Vector3.zero;
        Vector3 hitPoint = other.ClosestPoint(transform.position);
        TryStunFromCollision(other, relVel, hitPoint);
    }

    private void TryStunFromCollision(Collider other, Vector3 relativeVelocity, Vector3 hitPoint) {
        if (_disabled) return;
        if (!IsServer) return; //Stun must be driven by server authority
        if (Time.time < _nextAllowedTime) return;

        //Layer filter
        if (((1 << other.gameObject.layer) & enemyLayers) == 0) return;

        //Speed gate
        float speed = relativeVelocity.magnitude;
        if (speed < minRelativeSpeed) return;

        //Find EnemyAI on the hit object (parent-safe)
        EnemyAI enemy = other.GetComponentInParent<EnemyAI>();
        if (!enemy) return;

        //Derive stun direction in world space
        Vector3 dir;

        if (useObjectVelocityAsDirection && _rb && _rb.linearVelocity.sqrMagnitude > 1e-4f) {
            dir = _rb.linearVelocity.normalized;
        }
        else {
            //Fallback: from object towards enemy center, or from contact point into enemy
            Vector3 toEnemy = (enemy.transform.position - transform.position);
            if (toEnemy.sqrMagnitude < 1e-4f) toEnemy = (enemy.transform.position - hitPoint);
            dir = toEnemy.sqrMagnitude > 1e-6f ? toEnemy.normalized : enemy.transform.forward;
        }

        if (flattenY) dir.y = 0f;
        if (dir.sqrMagnitude < 1e-6f) return;

        //Apply stun directly (we are on the server)
        enemy.ApplyStunWorld(dir, stunDuration, investigateDistance);

        //Arm cooldown / optional one-shot
        _nextAllowedTime = Time.time + reuseCooldown;
        if (oneShot) _disabled = true;
    }
}