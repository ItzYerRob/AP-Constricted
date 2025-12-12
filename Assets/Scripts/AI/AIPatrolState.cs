using UnityEngine;

public sealed class PatrolState : IEnemyState
{
    private readonly EnemyAI enemy;

    public PatrolState(EnemyAI enemy) => this.enemy = enemy;    

    public void Enter()
    {
        enemy.OnEnterPatrol();
        enemy.motor.followPatrolPoints = true;
        enemy.motor.ClearTarget();
    }

    public void Update()
    {
        //Transition if target comes into range/LOS
        if (enemy.CanAggroTarget())
        {
            enemy.SwitchState(enemy.PursueState);
            return;
        }

        //Eventually add idle scan/looking around here
    }

    public void FixedUpdate() { }

    public void Exit() { }
}
