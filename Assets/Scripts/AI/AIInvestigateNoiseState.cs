using UnityEngine;

public sealed class InvestigateNoiseState : IEnemyState
{
    private readonly EnemyAI enemy;

    public InvestigateNoiseState(EnemyAI enemy) => this.enemy = enemy;

    public void Enter()
    {
        enemy.OnEnterInvestigate();
        //Stop following patrol and walk directly to noise point.
        enemy.motor.followPatrolPoints = false;

        if (enemy.hasNoiseToInvestigate) {
            enemy.motor.SetDestination(enemy.noisePosition);
        }
    }

    public void Update()
    {
        //If we see a target while moving to noise, then enter pursuit.
        if (enemy.CanAggroTarget()) {
            enemy.SwitchState(enemy.PursueState);
            return;
        }

        //If hearing has been cleared externally, go back to patrol.
        if (!enemy.hasNoiseToInvestigate)
        {
            enemy.SwitchState(enemy.PatrolState);
            return;
        }

        //Give up after N seconds if nothing is found.
        if (Time.time - enemy.noiseHeardTime > enemy.investigateDuration)
        {
            enemy.hasNoiseToInvestigate = false;
            enemy.SwitchState(enemy.PatrolState);
            return;
        }

        //Check if we’re close enough to consider the point searched.
        float sqrDist = (enemy.transform.position - enemy.noisePosition).sqrMagnitude;
        if (sqrDist <= enemy.investigateReachRadius * enemy.investigateReachRadius)
        {
            enemy.hasNoiseToInvestigate = false;
            enemy.SwitchState(enemy.PatrolState);
            return;
        }

        //Keep walking toward the noise. If a new noise is heard, enemy.noisePosition will be updated by NotifyHeardNoise().
        enemy.motor.SetDestination(enemy.noisePosition);
    }

    public void FixedUpdate() { }

    public void Exit() {
        //Clear explicit target; PatrolState.Enter will re-enable patrol pathing.
        enemy.motor.ClearTarget();
    }
}