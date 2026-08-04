using System.Collections;
using UnityEngine;

public class MiddleAttackState : BaseState
{
    readonly MiddleEnemy _e;
    readonly MonoBehaviour _mb;
    bool _attacking;
    IMonsterCore.AttackPattern _pattern;
    Coroutine _attackCoroutine;

    public MiddleAttackState(MiddleEnemy e) { _e = e; _mb = e; }

    public override void Enter()
    {
        ((IMonsterCore)_e).IsAwareOfPlayer = true;
        if (Time.time >= _e.nextAttackTime)
        {
            bool playerIsDown = _e.PlayerCtrl != null && _e.PlayerCtrl.IsDown;
            _pattern = playerIsDown ? _e.PickSpecialPattern() : _e.PickRandomPattern();
            _attackCoroutine = _mb.StartCoroutine(Attack());
        }
    }

    public override void Perform()
    {
        if (_e.IsDead) return;

        if (!_attacking && Vector2.Distance(_e.transform.position, _e.Player.position) > _pattern.range)
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
        _e.nextAttackTime = Time.time + _e.attackCooldown;

        int dir = _e.Player.position.x > _e.transform.position.x ? 1 : -1;
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
        bool isSpecial = _pattern.attacktype == IMonsterCore.AttackPattern.attackType.meleeSpecial;
        bool hitPlayer = false;

        Collider2D[] hits = Physics2D.OverlapCircleAll(_e.transform.position, _pattern.range, _e.playerLayer);
        foreach (var col in hits)
        {
            if (col.TryGetComponent<IDamageable>(out var target))
            {
                if (!isSpecial)
                    target.TakeDamage(_pattern.damage);
                else if (col.TryGetComponent<PlayerControl>(out var pc))
                {
                    pc.TakeSpecialDamage(_pattern.damage);
                    hitPlayer = true;
                }
            }
            if (col.TryGetComponent<Rigidbody2D>(out var rb))
                rb.AddForce(new Vector2(dir * _pattern.knockbackForce, 2f), ForceMode2D.Impulse);
        }

        if (isSpecial && hitPlayer && _e.HasCuttedLimbs)
            _e.HealRandomLimb();
    }

    void SpawnProjectile()
    {
        if (_pattern.projectilePrefab == null) return;

        bool isSpecial = _pattern.attacktype == IMonsterCore.AttackPattern.attackType.rangeSpecial;
        Vector2 dir = (_e.Player.position - _e.transform.position).normalized;
        var go = Object.Instantiate(_pattern.projectilePrefab, _e.transform.position, Quaternion.identity);
        if (go.TryGetComponent<SimpleProjectile>(out var proj))
        {
            proj.Init(dir, _pattern.projectileSpeed, _pattern.damage, isSpecial, _e.playerLayer);
            if (isSpecial)
                proj.onPlayerHit = () => { if (_e.HasCuttedLimbs) _e.HealRandomLimb(); };
        }
    }
}
