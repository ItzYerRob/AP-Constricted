using UnityEngine;
using Unity.Netcode;

//Deprecated
public class ServerPushProxy : NetworkBehaviour
{
    [Header("Proxy sizing")]
    public float radiusShrink = 0.05f;   //margin to reduce snagging
    public float heightShrink = 0.08f;

    [Header("Layers")]
    public string proxyLayerName = "PlayerProxy";

    Rigidbody proxyRb;
    Transform proxy;
    CapsuleCollider proxyCol;

    //Cache last target for optional velocity calc
    Vector3 lastTargetPos;
    Quaternion lastTargetRot;
    bool inited;

    Rigidbody sourceRb;
    CapsuleCollider sourceCapsule;

    void Awake() {
        sourceRb = GetComponent<Rigidbody>();
        sourceCapsule      = GetComponent<CapsuleCollider>();
    }

    public override void OnNetworkSpawn() {
        if (!IsServer) return; //proxy is server-only

        CreateProxy();
    }

    void CreateProxy()
    {
        proxy = new GameObject($"PushProxy_{OwnerClientId}").transform;
        proxy.gameObject.layer = LayerMask.NameToLayer(proxyLayerName);
        proxy.SetPositionAndRotation(transform.position, transform.rotation);

        proxyRb = proxy.gameObject.AddComponent<Rigidbody>();
        proxyRb.isKinematic = true;
        proxyRb.interpolation = RigidbodyInterpolation.Interpolate;
        proxyRb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        proxyCol = proxy.gameObject.AddComponent<CapsuleCollider>();

        //Prevent the proxy from colliding with its own source body
        if (sourceCapsule != null) Physics.IgnoreCollision(proxyCol, sourceCapsule, true);

        //Copy collider dimensions (slightly smaller to avoid scraping/sticking)
        if (sourceCapsule != null)
        {
            proxyCol.center = sourceCapsule.center;
            proxyCol.direction = sourceCapsule.direction;
            proxyCol.radius = Mathf.Max(0f, sourceCapsule.radius - radiusShrink);
            proxyCol.height = Mathf.Max(proxyCol.radius * 2f + 0.01f, sourceCapsule.height - heightShrink);
        }
        else
        {
            proxyCol.center = Vector3.up * 0.9f;
            proxyCol.radius = 0.45f;
            proxyCol.height = 1.8f;
        }

        lastTargetPos = transform.position;
        lastTargetRot = transform.rotation;
        inited = true;
    }

    void FixedUpdate()
    {
        if (!IsServer || !inited) return;

        //Use the player Transform
        Vector3 targetPos = transform.position;
        Quaternion targetRot = transform.rotation;

        //Clamp large teleports to avoid tunneling pushables.
        const float maxStep = 2.0f; //meters per fixed step
        Vector3 delta = targetPos - proxyRb.position;
        if (delta.sqrMagnitude > maxStep * maxStep)
        {
            proxyRb.position = targetPos;
            proxyRb.rotation = targetRot;
        }
        else
        {
            //Kinematic sweep produces contact impulses for pushables
            proxyRb.MovePosition(targetPos);
            proxyRb.MoveRotation(targetRot);
        }

        lastTargetPos = targetPos;
        lastTargetRot = targetRot;
    }

    public override void OnNetworkDespawn() {
        if (IsServer && proxy != null)
            Destroy(proxy.gameObject);
    }
}
