using UnityEngine;

public sealed class PursueTargetState : IEnemyState
{
    private readonly EnemyAI enemy;

    public PursueTargetState(EnemyAI enemy) => this.enemy = enemy;

    public void Enter() {
        enemy.OnEnterPursue();
        enemy.motor.followPatrolPoints = false;
        if (enemy.target) enemy.motor.SetTarget(enemy.target, overridePatrol: true);
    }


    public void Update()
    {
        //Keep resettingg the target (motor handles the rest)
        if (enemy.target) {
            enemy.motor.SetTarget(enemy.target);
        }

        //Transition back if we lost the target
        if (enemy.ShouldDeaggro()) {
            enemy.SwitchState(enemy.PatrolState);
            return;
        }
    }

    public void FixedUpdate() { }

    public void Exit()
    {
        //Clear pursuit steering to avoid any residual velocity shennanigans
        enemy.motor.ClearTarget();
    }
}
