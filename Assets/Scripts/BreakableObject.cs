using UnityEngine;

public class BreakableObject : MonoBehaviour
{
    [Header("Broken version prefab")]
    public GameObject brokenVersion;

    [Header("Impact settings")]
    public float breakForce = 5f; // Minimum impact velocity to break

    private bool hasBroken = false;

    void OnCollisionEnter(Collision collision)
    {
        // Only break once
        if (hasBroken) return;

        // Check impact strength
        if (collision.relativeVelocity.magnitude > breakForce)
        {
            Break();
        }
    }

    void Break()
    {
        hasBroken = true;

        // Spawn broken version
        if (brokenVersion != null)
        {
            GameObject shattered = Instantiate(
                brokenVersion,
                transform.position,
                transform.rotation
            );

            // Optional: copy velocity to shards
            Rigidbody[] shards = shattered.GetComponentsInChildren<Rigidbody>();
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                foreach (var shard in shards)
                {
                    shard.linearVelocity = rb.linearVelocity;
                }
            }
        }

        // Destroy the intact object
        Destroy(gameObject);
    }
}
