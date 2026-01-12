using Unity.Netcode;
using UnityEngine;

public partial class EnemyAI : NetworkBehaviour
{
    [Header("Adaptive Noise Learning")]

    [Tooltip("How quickly trust falls as badNoise grows. Larger = distrust noise sooner.")]
    [Range(0.01f, 1f)] public float trustDecay = 0.2f;

    [Tooltip("Lower bound on trust so noise is never ignored completely.")]
    [Range(0f, 1f)] public float minNoiseTrust = 0.15f;

    [Tooltip("How much to increase badNoise after a clearly unhelpful investigation.")]
    public float badNoiseIncrement = 1f;

    [Tooltip("How much to decrease badNoise after a helpful outcome.")]
    public float goodNoiseDecrement = 0.75f;

    [Tooltip("Distance margin used to decide whether we got meaningfully closer/further.")]
    public float progressDistanceThreshold = 0.75f;

    //Evaluation snapshot (what we knew when we committed to investigate)
    private bool _hasEvaluationSnapshot;
    private Vector3 _snapshotTargetPos;
    private float _startDistanceToSnapshot;
    
    private float BadNoise {
        get => GameManager.Instance.BadNoise;
        set => GameManager.Instance.BadNoise = Mathf.Max(0f, value);
    }

    //Converts accumulated noise unreliability into a trust multiplier. badNoise=0 -> trust~=1. As badNoise grows, trust decays exponentially until min trust.
    private float NoiseTrust {
        get {
            float unclamped = Mathf.Exp(-BadNoise * trustDecay);
            return Mathf.Max(minNoiseTrust, unclamped);
        }
    }

    private bool TryGetClosestPlayerPosition(out Vector3 pos) {
        pos = default;

        float bestSqrDist = float.PositiveInfinity;
        Transform best = null;

        foreach (Transform t in PlayerTarget.AllPlayers) {
            if (!t) continue;

            float sqr = (transform.position - t.position).sqrMagnitude;
            if (sqr < bestSqrDist) {
                bestSqrDist = sqr;
                best = t;
            }
        }

        if (!best) return false;
        pos = best.position;
        return true;
    }

    //Callable by the state so we can score the result later.
    public void BeginNoiseInvestigationEvaluation() {
        _hasEvaluationSnapshot = TryGetClosestPlayerPosition(out _snapshotTargetPos);
        if (_hasEvaluationSnapshot) _startDistanceToSnapshot = Vector3.Distance(transform.position, _snapshotTargetPos);
    }

    //Called when the investigate run ends. Updates BadNoise based on whether the run was useful.
    public void FinishNoiseEvaluation(bool acquiredTarget) {
        if (!IsServer) return;

        //We actually acquired a target -> noise was good.
        if (acquiredTarget) {
            BadNoise -= goodNoiseDecrement;
            return;
        }

        //If we couldn't form a snapshot, we can't score progress reliably.
        if (!_hasEvaluationSnapshot) return;

        float endDist = Vector3.Distance(transform.position, _snapshotTargetPos);
        float distanceImprovement = _startDistanceToSnapshot - endDist;
        //DistanceImprovement > 0 means we moved closer to snapshot target.

        if (distanceImprovement < -progressDistanceThreshold) { BadNoise += badNoiseIncrement; }
        else if (distanceImprovement > progressDistanceThreshold) { BadNoise -= goodNoiseDecrement * 0.5f; }
    }

    public void NotifyHeardNoise(Vector3 position, float suspicion, float bias) {
        if (_currentState == _pursue) return;

        //Prevent negatives and bias=0 edge cases.
        float clampedSuspicion = Mathf.Max(0f, suspicion);
        float clampedBias = Mathf.Max(0.001f, bias);

        float rawScore = clampedSuspicion * clampedBias;

        //Apply learned trust.
        float score = rawScore * NoiseTrust;

        //Only replace the current investigation candidate if this is better.
        if (hasNoiseToInvestigate && score <= noiseScore) return;

        hasNoiseToInvestigate = true;
        noisePosition = position;
        noiseHeardTime = Time.time;
        noiseSuspicion = suspicion;
        noiseScore = score;

        if (_currentState == _patrol) SwitchState(_investigate);
    }
}
