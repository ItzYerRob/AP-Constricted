using System.Collections;
using UnityEngine;
using Unity.Netcode;

public class MoveWhenApproach : NetworkBehaviour
{
    [Header("References (children)")]
    [SerializeField] private Transform movingObj;
    [SerializeField] private Transform movEnd;

    [Header("Triggering")]
    [SerializeField] private float triggerRadius = 0f; //0 only trigger collider; >0 also allow radius check
    [SerializeField] private bool triggerOnce = true;

    [Header("Motion")]
    [SerializeField] private float speed = 8f;
    [SerializeField] private bool stopWhenReached = false;
    [SerializeField] private float reachEpsilon = 0.02f;

    [Header("Lifetime")]
    [SerializeField] private float destroyAfterSeconds = 2.5f;
    [SerializeField] private bool destroyWholeObject = true; //true=destroy parent, false=destroy movingObj only

    private bool triggered;
    private Coroutine moveRoutine;

    private void Reset() {
        //Auto-wire children by name if present
        if (movingObj == null) {
            var t = transform.Find("MovingObj");
            if (t != null) movingObj = t;
        }
        if (movEnd == null) {
            var t = transform.Find("MovEnd");
            if (t != null) movEnd = t;
        }
    }

    private void Awake() {
        if (movingObj == null || movEnd == null) {
            Debug.LogError($"{nameof(MoveWhenApproach)} on {name} is missing references.", this);
        }
    }

    private void Update() {
        //Allow proximity-based trigger without relying purely on trigger collider.
        if (triggerRadius > 0f && !triggered) {
            //Only server should decide state changes that affect everyone.
            if (IsSpawned && !IsServer) return;

            foreach (var p in PlayerTarget.AllPlayers) {
                if (p == null) continue;
                float d = Vector3.Distance(p.position, transform.position);
                if (d <= triggerRadius) {
                    Trigger();
                    break;
                }
            }
        }
    }

    private void OnTriggerEnter(Collider other) {
        if (triggered) return;

        if (other.GetComponentInParent<PlayerTarget>() == null) return;

        //Only server should trigger if destruction/visibility is networked.
        if (IsSpawned && !IsServer) return;

        Trigger();
    }

    private void Trigger() {
        if (triggered && triggerOnce) return;
        triggered = true;

        if (movingObj == null || movEnd == null) return;

        //If this object is a NetworkObject, propagate to clients so they animate too.
        if (IsSpawned) {
            TriggerClientRpc();
        }

        //Server does the authoritative movement & destruction.
        StartMoveAndDestroy();
    }

    [ClientRpc]
    private void TriggerClientRpc() {
        //Clients start visual motion too
        if (IsServer) return; //Server already started in Trigger()

        StartMoveAndDestroy();
    }

    private void StartMoveAndDestroy() {
        if (moveRoutine != null) StopCoroutine(moveRoutine);
        moveRoutine = StartCoroutine(MoveAndDestroyRoutine());
    }

    private IEnumerator MoveAndDestroyRoutine() {
        float t = 0f;

        //Move for lifetime (or until reached if stopWhenReached)
        while (t < destroyAfterSeconds) {
            t += Time.deltaTime;

            if (movingObj != null && movEnd != null) {
                movingObj.position = Vector3.MoveTowards(
                    movingObj.position,
                    movEnd.position,
                    speed * Time.deltaTime
                );

                if (stopWhenReached) {
                    if ((movingObj.position - movEnd.position).sqrMagnitude <= reachEpsilon * reachEpsilon) break;
                }
            }

            yield return null;
        }

        //Server authoritative destruction if networked
        if (IsSpawned) {
            if (!IsServer) yield break;

            if (destroyWholeObject) {
                var no = GetComponent<NetworkObject>();
                if (no != null) no.Despawn(true);
                else Destroy(gameObject);
            }
            else {
                var noChild = movingObj != null ? movingObj.GetComponent<NetworkObject>() : null;
                if (noChild != null) noChild.Despawn(true);
                else if (movingObj != null) Destroy(movingObj.gameObject);
            }
        }
        else {
            //Non-networked fallback
            if (destroyWholeObject) Destroy(gameObject);
            else if (movingObj != null) Destroy(movingObj.gameObject);
        }
    }
}
