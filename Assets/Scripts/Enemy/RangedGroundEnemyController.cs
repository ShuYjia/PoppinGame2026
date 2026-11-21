using Poppin.Animation;
using UnityEngine;

/// <summary>
/// 地面远程敌人：保持与玩家的距离，在视线内周期性发射投射物。
/// 外观和地面碰撞结构可直接沿用 Boar 预制体。
/// </summary>
public class RangedGroundEnemyController : GroundEnemyController
{
    [Header("远程攻击")]
    [Tooltip("希望与玩家保持的水平距离（单位）。")]
    public float preferredDistance = 5f;
    [Tooltip("围绕 preferredDistance 的容差。")]
    public float distanceTolerance = 0.6f;
    [Tooltip("可发射的最大水平距离（单位）。")]
    public float attackRange = 8f;
    [Tooltip("两次发射之间的冷却（秒）。")]
    public float attackCooldown = 2f;
    [Tooltip("投射物预制体。")]
    public GameObject projectilePrefab;
    [Tooltip("投射物速度（单位/秒）。")]
    public float projectileSpeed = 7f;
    [Tooltip("投射物存活时间（秒）。")]
    public float projectileLifetime = 3f;
    [Tooltip("投射物伤害。")]
    public float projectileDamage = 5f;
    [Tooltip("投射物撞到哪些层会消失。")]
    public LayerMask projectileHitLayers = ~0;
    [Tooltip("投射物从敌人中心偏移的发射点。")]
    public Vector2 muzzleOffset = new Vector2(0.8f, 1.1f);
    [Tooltip("发射表现持续时间（秒）：这段时间内保持 Attack 动画，不切回 Idle / Walk。")]
    public float attackVisualDuration = 0.35f;

    private float nextAttackTime;
    private float attackVisualTimer;

    protected override void Start()
    {
        base.Start();
        nextAttackTime = Time.time + attackCooldown * 0.5f;
    }

    protected override void TickCombat(float deltaTime, bool seesPlayer)
    {
        if (player == null || playerStats == null || playerStats.IsDead)
        {
            SetHorizontalVelocity(0f);
            return;
        }

        FacePlayer();
        float dx = player.position.x - transform.position.x;
        float dy = player.position.y - transform.position.y;
        float absDx = Mathf.Abs(dx);
        int towardPlayer = dx >= 0f ? 1 : -1;

        // 开火表现窗口：这段时间里保持 Attack 动画，不切回 Idle / Walk（位移照常进行）。
        // 窗口只影响「播哪段」，攻击本身（冷却 / 投射物）由下面的逻辑与控制无关地驱动。
        bool firing = attackVisualTimer > 0f;
        if (firing)
        {
            attackVisualTimer -= deltaTime;
            firing = attackVisualTimer > 0f;
            PlayAnimation(EnemyAnimationState.Attack);
        }

        if (!seesPlayer)
        {
            SetHorizontalVelocity(0f);
        }
        else if (absDx > preferredDistance + distanceTolerance
                 && !IsWallAhead(towardPlayer)
                 && HasGroundAhead(towardPlayer))
        {
            SetHorizontalVelocity(towardPlayer * walkSpeed);
            if (!firing)
                PlayAnimation(EnemyAnimationState.Walk);
        }
        else if (absDx < preferredDistance - distanceTolerance
                 && !IsWallAhead(-towardPlayer)
                 && HasGroundAhead(-towardPlayer))
        {
            SetHorizontalVelocity(-towardPlayer * walkSpeed);
            if (!firing)
                PlayAnimation(EnemyAnimationState.Walk);
        }
        else
        {
            SetHorizontalVelocity(0f);
            if (!firing)
                PlayAnimation(EnemyAnimationState.Idle);
        }

        if (seesPlayer && absDx <= attackRange && Mathf.Abs(dy) <= sightHeight
            && Time.time >= nextAttackTime)
        {
            FireProjectile();
        }
    }

    private void FireProjectile()
    {
        if (projectilePrefab == null || player == null)
            return;

        Vector2 muzzle = (Vector2)transform.position
                         + new Vector2(Facing * Mathf.Abs(muzzleOffset.x), muzzleOffset.y);
        Vector2 target = (Vector2)player.position + Vector2.up * 1.1f;
        Vector2 direction = target - muzzle;
        if (direction.sqrMagnitude < 0.0001f)
            direction = new Vector2(Facing, 0f);

        GameObject projectileObject = Instantiate(projectilePrefab, muzzle, Quaternion.identity);
        EnemyProjectile projectile = projectileObject.GetComponent<EnemyProjectile>();
        if (projectile == null)
        {
            Destroy(projectileObject);
            return;
        }

        projectile.Launch(direction, transform, projectileSpeed, projectileLifetime,
            projectileDamage, projectileHitLayers);
        nextAttackTime = Time.time + Mathf.Max(0.1f, attackCooldown);
        attackVisualTimer = attackVisualDuration;
        PlayOneShot(EnemyAnimationState.Attack);
    }

    /// <summary>一次性表现：从第 0 帧重播 Attack（不等同于普通状态切换，所以走 PlayOneShot）。</summary>
    private void PlayOneShot(EnemyAnimationState state)
    {
        if (animFsm != null)
            animFsm.PlayOneShot(state);
    }
}
