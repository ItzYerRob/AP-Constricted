using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(EnemyAI))]
public class EnemyHearing : NetworkBehaviour, INoiseListener
{
    [Header("Hearing")]
    public float hearingMultiplier = 1f;
    public float maxHearingRadius = 30f;

    [Tooltip("Minimum suspicion (0-1) required to interrupt patrol.")]
    [Range(0f, 1f)]
    public float minSuspicionToInvestigate = 0.25f;

    private EnemyAI _ai;

    public override void OnNetworkSpawn() {
        if (!IsServer) return; //AI authority on server

        _ai = GetComponent<EnemyAI>();
        if (NoiseSystem.Instance != null) NoiseSystem.Instance.Register(this);
    }

    void OnDestroy() {
        if (!IsServer) return;
        if (NoiseSystem.Instance != null) NoiseSystem.Instance.Unregister(this);
    }

    public void OnNoiseHeard(in NoiseEvent e) {
        if (!IsServer || _ai == null) return;

        float effectiveRadius = Mathf.Min(e.Radius * hearingMultiplier, maxHearingRadius);
        float dist = Vector3.Distance(transform.position, e.Position);
        if (dist > effectiveRadius) { return; }

        //Suspicion [0,1]: louder + closer = higher
        float distanceFactor = 1f - Mathf.Clamp01(dist / effectiveRadius);
        float suspicion = e.Loudness * distanceFactor;

        if (suspicion < minSuspicionToInvestigate) { Debug.Log($"[Hearing] Noise rejected: suspicion too low ({suspicion:F2} < {minSuspicionToInvestigate:F2})"); return; }

        //Don’t let noise pull us off an active chase.
        if (_ai._currentState == _ai.PursueState) { return; }

        _ai.NotifyHeardNoise(e.Position, suspicion, 1f);
    }
}