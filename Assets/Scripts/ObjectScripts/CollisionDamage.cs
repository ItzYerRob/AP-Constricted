using UnityEngine;

public class CollisionDamage : MonoBehaviour
{
    public float forceMultiplier = 0.001f;
    public float minDamageThreshold = 10000; //Avoid micro-collisions dealing damage
    private Rigidbody rb;
    private CharacterStats stats;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        stats = GetComponent<CharacterStats>();
    }

    void OnCollisionEnter(Collision collision)
    {
        if (stats == null) return;

        //Only consider meaningful collisions
        float impactForce = collision.impulse.magnitude / Time.fixedDeltaTime;
        impactForce*=forceMultiplier;

        if (impactForce > minDamageThreshold)
        {
            float damage = impactForce;
            stats.ApplyDamage(damage);
            Debug.Log("Object took " + damage + "damage");
        }
    }
}