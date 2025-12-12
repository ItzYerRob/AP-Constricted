// StunState.cs
using UnityEngine;

public sealed class StunState : IEnemyState
{
    private readonly EnemyAI enemy;
    private float   _stunEndTime;
    private Vector3 _dirWorld;
    private float   _investigateDistance;
    private bool    _hadTargetOnEnter;
    private bool    _knockbackApplied;
    
    private const float KnockbackImpulse = 4.0f;

    public StunState(EnemyAI enemy) => this.enemy = enemy;

    public void Enter() {
        enemy.motor.followPatrolPoints = false;
        enemy.motor.ClearTarget();
        enemy.OnEnterInvestigate();

        _dirWorld            = enemy.PendingStunDirectionWorld;
        _investigateDistance = enemy.PendingStunInvestigateDistance;
        _hadTargetOnEnter    = enemy.PendingStunHadTarget;

        _stunEndTime         = Time.time + enemy.PendingStunDuration;
        _knockbackApplied    = false;

        //Hard-lock movement for the stun duration
        enemy.motor.LockMovementUntil(_stunEndTime);

        enemy.motor.ZeroHorizontalVelocity();
    }

    public void Update()
    {
        //Apply knockback once on first Update after Enter so Rigidbody is initialized
        if (!_knockbackApplied) {
            _knockbackApplied = true;
            enemy.motor.AddVelocityChange(_dirWorld * KnockbackImpulse);
        }

        //Do not aggro while stunned
        if (Time.time >= _stunEndTime) {
            OnStunFinished();
        }
    }

    private void OnStunFinished() {
        //If we had a target when the stun began, try to resume the chase.
        if (_hadTargetOnEnter) {
            if (enemy.target != null && !enemy.ShouldDeaggro()) {
                enemy.SwitchState(enemy.PursueState);
                return;
            }
            if (enemy.CanAggroTarget()) {
                enemy.SwitchState(enemy.PursueState);
                return;
            }
            enemy.SwitchState(enemy.PatrolState);
            return;
        }

        //If no target on enter, Investigate along stun direction
        if (_investigateDistance > 0.01f && _dirWorld.sqrMagnitude > 1e-6f) {
            Vector3 investigatePoint = enemy.transform.position -_dirWorld.normalized * _investigateDistance;

            //Use a high bias so this wins over ordinary bounce/fall noises
            enemy.NotifyHeardNoise(investigatePoint, suspicion: 1f, bias: enemy.stunDirectionBias);

            enemy.SwitchState(enemy.InvestigateState);
            return;
        }

        enemy.SwitchState(enemy.PatrolState);
    }

    public void FixedUpdate() { }

    public void Exit() {
        //Clear only what we introduced; motor unlock is time-based.
        enemy.motor.ClearTarget(); 
    }
}
