using UnityEngine;

public sealed class InvestigateNoiseState : IEnemyState
{
    private readonly EnemyAI enemy;
    private bool _acquiredTarget;
    private bool _finishedEvaluation;

    public InvestigateNoiseState(EnemyAI enemy) => this.enemy = enemy;

    public void Enter() {
        _acquiredTarget = false;
        _finishedEvaluation = false;

        enemy.OnEnterInvestigate();

        //Stop patrol path following; walk directly to the noise point.
        enemy.motor.followPatrolPoints = false;

        //Commit point for learning.
        enemy.BeginNoiseInvestigationEvaluation();

        if (enemy.hasNoiseToInvestigate) enemy.motor.SetDestination(enemy.noisePosition);
    }

    public void Update() {
        //Seeing a target while investigating -> success case.
        if (enemy.CanAggroTarget()) {
            _acquiredTarget = true;
            enemy.SwitchState(enemy.PursueState);
            return;
        }

        //If noise got cleared externally -> treat as failure and leave.
        if (!enemy.hasNoiseToInvestigate) {
            enemy.SwitchState(enemy.PatrolState);
            return;
        }

        //Time out -> clear noise and leave.
        if (Time.time - enemy.noiseHeardTime > enemy.investigateDuration) {
            enemy.hasNoiseToInvestigate = false;
            enemy.SwitchState(enemy.PatrolState);
            return;
        }

        //Reached the point -> clear noise and leave.
        float sqrDist = (enemy.transform.position - enemy.noisePosition).sqrMagnitude;
        float reachSqr = enemy.investigateReachRadius * enemy.investigateReachRadius;

        if (sqrDist <= reachSqr) {
            enemy.hasNoiseToInvestigate = false;
            enemy.SwitchState(enemy.PatrolState);
            return;
        }

        //Keep moving. If NotifyHeardNoise updates enemy.noisePosition mid-run, naturally steer toward the newest/best noise without restarting evaluation.
        enemy.motor.SetDestination(enemy.noisePosition);
    }

    public void FixedUpdate() { }

    public void Exit() {
        enemy.motor.ClearTarget();

        // Finish exactly once.
        if (_finishedEvaluation) return;
        _finishedEvaluation = true;

        enemy.FinishNoiseEvaluation(_acquiredTarget);
    }
}
