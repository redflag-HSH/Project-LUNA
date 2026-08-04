using System.Collections;
using UnityEngine;

public class SimpleAttackState : BaseState
{
    readonly IMonsterCore _e;
    readonly MonoBehaviour _mb;
    bool _attacking;
    IMonsterCore.AttackPattern _pattern;
    Coroutine _attackCoroutine;

    public SimpleAttackState(IMonsterCore e) { _e = e; _mb = (MonoBehaviour)e; }

    public override void Enter()
    {
        _e.IsAwareOfPlayer = true;
        if (Time.time >= _e.NextAttackTime)
        {
            _pattern = _e.PickRandomPattern();
            _attackCoroutine = _mb.StartCoroutine(Attack());
        }
    }

    public override void Perform()
    {
        if (_e.IsDead) return;

        if (!_attacking && Vector2.Distance(_e.Transform.position, _e.Player.position) > _pattern.range)
            StateMachine.ChangeState(new SimpleChaseState(_e));
    }

    public override void Exit()
    {
        if (_attackCoroutine != null)
            _mb.StopCoroutine(_attackCoroutine);
    }

    IEnumerator Attack()
    {
        _attacking = true;
        _e.NextAttackTime = Time.time + _e.AttackCooldown;

        int dir = _e.Player.position.x > _e.Transform.position.x ? 1 : -1;
        _e.FaceDirection(dir);

        _e.Rb.linearVelocity = new Vector2(dir * _pattern.lungeForce, _e.Rb.linearVelocity.y);

        yield return new WaitForSeconds(_pattern.windupTime);

        switch (_pattern.attacktype)
        {
            case IMonsterCore.AttackPattern.attackType.meleeNormal:
            case IMonsterCore.AttackPattern.attackType.meleeSpecial:
                MeleeHit(dir);
                break;

            case IMonsterCore.AttackPattern.attackType.rangeNormal:
            case IMonsterCore.AttackPattern.attackType.rangeSpecial:
                SpawnProjectile();
                break;
        }

        yield return new WaitForSeconds(_pattern.endDelay);

        _attacking = false;
        if (!_e.IsDead)
            StateMachine.ChangeState(new SimpleChaseState(_e));
    }

    void MeleeHit(int dir)
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(_e.Transform.position, _pattern.range, _e.PlayerLayer);
        foreach (var col in hits)
        {
            if (col.TryGetComponent<IDamageable>(out var target))
            {
                if (_pattern.attacktype == IMonsterCore.AttackPattern.attackType.meleeNormal)
                    target.TakeDamage(_pattern.damage);
                else if (col.TryGetComponent<PlayerControl>(out var pc))
                    pc.TakeSpecialDamage(_pattern.damage);
            }
            if (col.TryGetComponent<Rigidbody2D>(out var rb))
                rb.AddForce(new Vector2(dir * _pattern.knockbackForce, 2f), ForceMode2D.Impulse);
        }
    }

    void SpawnProjectile()
    {
        if (_pattern.projectilePrefab == null) return;

        bool isSpecial = _pattern.attacktype == IMonsterCore.AttackPattern.attackType.rangeSpecial;
        Vector2 dir = (_e.Player.position - _e.Transform.position).normalized;
        var go = Object.Instantiate(_pattern.projectilePrefab, _e.Transform.position, Quaternion.identity);
        if (go.TryGetComponent<SimpleProjectile>(out var proj))
            proj.Init(dir, _pattern.projectileSpeed, _pattern.damage, isSpecial, _e.PlayerLayer);
    }
}
