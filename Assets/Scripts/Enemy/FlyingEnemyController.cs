using Poppin.Animation;
using UnityEngine;

/// <summary>
/// 飞行敌人（蝙蝠）：以放置位置为固定点，在圆形范围内随机巡逻；发现玩家后追击并在射程内远程开火；
/// 玩家脱战 / 死亡后回到固定范围继续巡逻。
///
/// 状态机：
/// <code>
/// Patrol ──发现玩家──▶ Chase ──超出追击范围 / 玩家死亡 / 丢失视线超时──▶ Return ──回到巡逻半径──▶ Patrol
///   ▲                    │                                                      │
///   └────────────────────┴──────────── 硬直结束（未脱战回 Chase）◀── Hurt ◀────────┘（途中重新发现玩家）
/// 任意非 Dead 状态受击 ─▶ Hurt（击退 + BatIsHurt）；HP 归零 ─▶ Dead（BatDeath + 计分 + 淡出）
/// </code>
///
/// 与地面敌人的共用体系：CharacterState 管血量/无敌，Attack + EnemyProjectile 管伤害，
/// LevelManager 管计分和复活重置，动画只通过 <see cref="EnemyAnimationStateMachine"/> 通知状态。
/// 不继承 GroundEnemyController：那套基类绑死崖边/墙体/水平轴探测，对飞行敌人全是无效逻辑。
///
/// 物理：Dynamic 刚体 + gravityScale 0 + 冻结旋转，速度驱动；与玩家 IgnoreCollision 防止推挤。
/// 美术朝右绘制（Bat1.png 红眼偏右），朝向用 SpriteRenderer.flipX 镜像。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CharacterState))]
public class FlyingEnemyController : MonoBehaviour
{
    public enum FlyEnemyState
    {
        Patrol,
        Chase,
        Return,
        Hurt,
        Dead
    }

    [Header("巡逻（固定点 = 放置时的初始位置）")]
    [Tooltip("巡逻半径：以出生点为圆心的圆形区域，随机取点飞行（单位）。")]
    public float patrolRadius = 4f;
    [Tooltip("巡逻飞行速度（单位/秒）。")]
    public float patrolSpeed = 2f;
    [Tooltip("到达巡逻点后悬停多久再取下一个点（秒）。")]
    public float patrolPause = 0.8f;
    [Tooltip("单个巡逻点最长飞行时间（秒），超时换点，防止卡在墙边。")]
    public float patrolPointTimeout = 4f;
    [Tooltip("速度趋近目标速度的加速度（单位/秒²），越小越飘。")]
    public float steerAcceleration = 10f;
    [Tooltip("出生时朝右（true）还是朝左（false）。")]
    public bool startFacingRight = true;

    [Header("发现玩家")]
    [Tooltip("面朝方向的扇形检测半径（单位）。")]
    public float sightRange = 6f;
    [Tooltip("扇形总角度（度）。120 = 正前方左右各 60 度。")]
    [Range(1f, 360f)]
    public float sightAngle = 120f;
    [Tooltip("身后多近也算发现（全向小圆，单位）：防止玩家贴脸贴背时看不见。")]
    public float backSenseRange = 1.5f;
    [Tooltip("遮挡视线的层（不要勾 Player / Enemy）。")]
    public LayerMask obstacleMask = ~((1 << 6) | (1 << 7));
    [Tooltip("丢失视线后还会继续追多久（秒）。")]
    public float loseTargetTime = 2f;

    [Header("追击")]
    [Tooltip("追击速度（单位/秒）。")]
    public float chaseSpeed = 3.5f;
    [Tooltip("追击范围（单位）：以敌人自身为圆心，玩家超出即脱战。")]
    public float chaseRange = 9f;

    [Header("远程攻击")]
    [Tooltip("进入该距离后悬停开火（单位）。")]
    public float attackRange = 5f;
    [Tooltip("两次开火之间的冷却（秒）。")]
    public float attackCooldown = 2.5f;
    [Tooltip("开火前悬停蓄力时间（秒）；0 = 立刻发射。")]
    public float attackWindup = 0.3f;
    [Tooltip("投射物预制体（ EnemyProjectile.prefab ）。")]
    public GameObject projectilePrefab;
    [Tooltip("投射物速度（单位/秒）。")]
    public float projectileSpeed = 5f;
    [Tooltip("投射物存活时间（秒）。")]
    public float projectileLifetime = 4f;
    [Tooltip("投射物伤害。")]
    public float projectileDamage = 10f;
    [Tooltip("投射物撞到哪些层会消失。")]
    public LayerMask projectileHitLayers = ~0;
    [Tooltip("发射点相对敌人中心的偏移。")]
    public Vector2 muzzleOffset = new Vector2(0f, -0.1f);
    [Tooltip("瞄准点相对玩家脚底的偏移（默认瞄身体中心）。")]
    public Vector2 aimOffset = new Vector2(0f, 1.1f);

    [Header("受击")]
    [Tooltip("被打时的击退速度（单位/秒）。")]
    public float knockbackSpeed = 4f;
    [Tooltip("受击硬直时长（秒）。")]
    public float hurtStun = 0.3f;

    [Header("死亡")]
    [Tooltip("死亡动画播完后淡出的时长（秒）。BatDeath 约 0.7 秒。")]
    public float deathFadeTime = 0.8f;
    [Tooltip("击杀基础分。")]
    public int killScore = 50;

    [Header("表现")]
    [Tooltip("美术是否朝右绘制。Bat1.png 朝右，保持 true。")]
    public bool spriteFacesRight = true;
    [Tooltip("悬停时的上下浮动幅度（单位/秒）。0 = 完全静止。")]
    public float hoverAmplitude = 0.12f;
    [Tooltip("悬停浮动的频率（弧度/秒）。")]
    public float hoverFrequency = 2.5f;

    public FlyEnemyState State { get; private set; } = FlyEnemyState.Patrol;

    /// <summary>1 朝右，-1 朝左。</summary>
    public int Facing { get; private set; } = 1;

    protected Rigidbody2D rb;
    protected CharacterState stats;
    protected SpriteRenderer spriteRenderer;
    protected EnemyAnimationStateMachine animFsm;

    private Transform player;
    private CharacterState playerStats;
    private Collider2D[] ownColliders;
    private Vector2 homePosition;
    private Vector2 patrolTarget;
    private Vector2 lastKnownPlayerPosition;
    private float lastSeenTime = -999f;
    private float pauseTimer;
    private float pointTimer;
    private float stateTimer;
    private float nextAttackTime;
    private float windupTimer;
    private float hoverPhase;
    private bool playerCollisionIgnored;
    private Color baseColor = Color.white;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        stats = GetComponent<CharacterState>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        animFsm = GetComponent<EnemyAnimationStateMachine>();
        ownColliders = GetComponents<Collider2D>();

        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.gravityScale = 0f;
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        if (spriteRenderer != null)
            baseColor = spriteRenderer.color;
    }

    private void OnEnable()
    {
        if (stats == null)
            return;

        stats.OnTakeDamage.AddListener(OnHurt);
        stats.Died += OnDied;
    }

    private void OnDisable()
    {
        if (stats == null)
            return;

        stats.OnTakeDamage.RemoveListener(OnHurt);
        stats.Died -= OnDied;
    }

    private void Start()
    {
        homePosition = rb.position;
        patrolTarget = homePosition;
        hoverPhase = Random.Range(0f, Mathf.PI * 2f);
        SetFacing(startFacingRight ? 1 : -1);
        ResolvePlayer();
        nextAttackTime = Time.time + attackCooldown * 0.5f;
        EnterPatrol();

        if (LevelManager.Instance != null)
            LevelManager.Instance.PlayerRespawned += ResetToHome;
    }

    private void OnDestroy()
    {
        if (LevelManager.Instance != null)
            LevelManager.Instance.PlayerRespawned -= ResetToHome;
    }

    // ───────────────────────────── 状态机 ─────────────────────────────

    private void FixedUpdate()
    {
        if (player == null)
            ResolvePlayer();

        float dt = Time.fixedDeltaTime;
        bool sees = CanSeePlayer();
        if (sees)
        {
            lastSeenTime = Time.time;
            lastKnownPlayerPosition = player.position;
        }

        switch (State)
        {
            case FlyEnemyState.Patrol:
                TickPatrol(dt, sees);
                break;
            case FlyEnemyState.Chase:
                TickChase(dt, sees);
                break;
            case FlyEnemyState.Return:
                TickReturn(dt, sees);
                break;
            case FlyEnemyState.Hurt:
                TickHurt(dt);
                break;
            case FlyEnemyState.Dead:
                // 死亡后不再主动移动，但保留重力（若开了 fallOnDeath 则由刚体自然下落）。
                break;
        }
    }

    private void TickPatrol(float dt, bool sees)
    {
        if (sees)
        {
            EnterChase();
            return;
        }

        if (pauseTimer > 0f)
        {
            pauseTimer -= dt;
            Steer(HoverVelocity(), dt);
            return;
        }

        pointTimer -= dt;
        Vector2 delta = patrolTarget - rb.position;
        if (delta.magnitude <= arriveDistance() || pointTimer <= 0f)
        {
            pauseTimer = patrolPause;
            PickPatrolTarget();
            return;
        }

        if (Mathf.Abs(delta.x) > 0.05f)
            SetFacing(delta.x > 0f ? 1 : -1);

        Steer(delta.normalized * patrolSpeed, dt);
        PlayAnimation(EnemyAnimationState.Walk);
    }

    private void TickChase(float dt, bool sees)
    {
        if (player == null || playerStats == null || playerStats.IsDead)
        {
            EnterReturn();
            return;
        }

        float distance = Vector2.Distance(rb.position, player.position);
        if (distance > chaseRange || (!sees && Time.time - lastSeenTime > loseTargetTime))
        {
            EnterReturn();
            return;
        }

        Vector2 target = sees ? (Vector2)player.position : lastKnownPlayerPosition;
        if (Mathf.Abs(target.x - rb.position.x) > 0.05f)
            SetFacing(target.x > rb.position.x ? 1 : -1);

        if (windupTimer > 0f)
        {
            windupTimer -= dt;
            Steer(HoverVelocity(), dt);
            if (windupTimer <= 0f)
                FireProjectile();
            return;
        }

        if (sees && distance <= attackRange)
        {
            Steer(HoverVelocity(), dt);
            if (Time.time >= nextAttackTime)
            {
                windupTimer = Mathf.Max(0f, attackWindup);
                PlayAnimation(EnemyAnimationState.Attack, true);
                if (windupTimer <= 0f)
                    FireProjectile();
            }
            return;
        }

        Vector2 delta = target - rb.position;
        if (delta.sqrMagnitude > 0.0001f)
            Steer(delta.normalized * chaseSpeed, dt);
        else
            Steer(HoverVelocity(), dt);

        PlayAnimation(EnemyAnimationState.Run);
    }

    private void TickReturn(float dt, bool sees)
    {
        if (sees && player != null && playerStats != null && !playerStats.IsDead)
        {
            EnterChase();
            return;
        }

        if (Vector2.Distance(rb.position, homePosition) <= arriveDistance())
        {
            EnterPatrol();
            return;
        }

        Vector2 delta = homePosition - rb.position;
        if (Mathf.Abs(delta.x) > 0.05f)
            SetFacing(delta.x > 0f ? 1 : -1);

        Steer(delta.normalized * patrolSpeed, dt);
        PlayAnimation(EnemyAnimationState.Walk);
    }

    private void TickHurt(float dt)
    {
        // 击退冲量自然衰减；飞行敌人没有地面可撞，也不用检查崖边。
        rb.velocity = Vector2.MoveTowards(rb.velocity, Vector2.zero, 12f * dt);
        stateTimer -= dt;
        if (stateTimer > 0f)
            return;

        if (player != null && playerStats != null && !playerStats.IsDead
            && Time.time - lastSeenTime < loseTargetTime)
            EnterChase();
        else
            EnterReturn();
    }

    private void EnterPatrol()
    {
        State = FlyEnemyState.Patrol;
        pauseTimer = 0f;
        pointTimer = patrolPointTimeout;
        windupTimer = 0f;
        PickPatrolTarget();
        PlayAnimation(EnemyAnimationState.Walk);
    }

    private void EnterChase()
    {
        State = FlyEnemyState.Chase;
        windupTimer = 0f;
        if (player != null)
        {
            lastKnownPlayerPosition = player.position;
            if (Mathf.Abs(player.position.x - rb.position.x) > 0.05f)
                SetFacing(player.position.x > rb.position.x ? 1 : -1);
        }
        PlayAnimation(EnemyAnimationState.Run);
    }

    private void EnterReturn()
    {
        State = FlyEnemyState.Return;
        windupTimer = 0f;
        PlayAnimation(EnemyAnimationState.Walk);
    }

    private void PickPatrolTarget()
    {
        pointTimer = patrolPointTimeout;
        for (int i = 0; i < 8; i++)
        {
            Vector2 candidate = homePosition + Random.insideUnitCircle * patrolRadius;
            if (!SegmentBlocked(rb.position, candidate))
            {
                patrolTarget = candidate;
                return;
            }
        }

        // 全被挡住时至少往 home 靠，不会原地卡死。
        patrolTarget = homePosition;
    }

    private float arriveDistance()
    {
        return Mathf.Max(0.2f, patrolRadius * 0.08f);
    }

    // ───────────────────────────── 攻击 ─────────────────────────────

    private void FireProjectile()
    {
        windupTimer = 0f;
        nextAttackTime = Time.time + Mathf.Max(0.1f, attackCooldown);

        if (projectilePrefab == null || player == null)
            return;

        Vector2 muzzle = rb.position + new Vector2(Facing * Mathf.Abs(muzzleOffset.x), muzzleOffset.y);
        Vector2 target = (Vector2)player.position + aimOffset;
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
    }

    // ───────────────────────────── 受击 / 死亡 ─────────────────────────────

    private void OnHurt(Transform attacker)
    {
        if (State == FlyEnemyState.Dead || stats == null)
            return;

        Vector2 away = Facing > 0 ? Vector2.left : Vector2.right;
        if (attacker != null)
        {
            Vector2 delta = rb.position - (Vector2)attacker.position;
            if (delta.sqrMagnitude > 0.0001f)
                away = delta.normalized;
        }

        if (player != null)
            lastKnownPlayerPosition = player.position;
        lastSeenTime = Time.time;
        State = FlyEnemyState.Hurt;
        stateTimer = hurtStun;
        windupTimer = 0f;
        rb.velocity = away * Mathf.Max(0f, knockbackSpeed);
        PlayAnimation(EnemyAnimationState.Hurt, true);
    }

    private void OnDied()
    {
        if (State == FlyEnemyState.Dead)
            return;

        State = FlyEnemyState.Dead;
        rb.velocity = Vector2.zero;
        windupTimer = 0f;

        foreach (Attack attack in GetComponentsInChildren<Attack>(true))
            attack.enabled = false;

        foreach (Collider2D collider in ownColliders)
        {
            if (collider != null && collider.isTrigger)
                collider.enabled = false;
        }

        PlayAnimation(EnemyAnimationState.Dead, true);

        if (LevelManager.Instance != null)
            LevelManager.Instance.RegisterKill(killScore);

        StartCoroutine(FadeOutAndHide());
    }

    private System.Collections.IEnumerator FadeOutAndHide()
    {
        float timer = 0f;
        while (timer < deathFadeTime)
        {
            timer += Time.deltaTime;
            if (spriteRenderer != null)
            {
                Color color = baseColor;
                color.a = Mathf.Lerp(1f, 0f, Mathf.Clamp01(timer / Mathf.Max(0.01f, deathFadeTime)));
                spriteRenderer.color = color;
            }
            yield return null;
        }

        gameObject.SetActive(false);
    }

    /// <summary>玩家复活时：飞回出生点、回满血、重新巡逻。已死亡的敌人不复活。</summary>
    public void ResetToHome()
    {
        if (State == FlyEnemyState.Dead || !isActiveAndEnabled || stats == null)
            return;

        rb.velocity = Vector2.zero;
        rb.position = homePosition;
        transform.position = homePosition;
        stats.ResetToFull();
        stats.ClearInvincibility();
        if (spriteRenderer != null)
            spriteRenderer.color = baseColor;
        lastSeenTime = -999f;
        SetFacing(startFacingRight ? 1 : -1);
        EnterPatrol();
    }

    // ───────────────────────────── 感知 / 运动 ─────────────────────────────

    private bool CanSeePlayer()
    {
        if (player == null || playerStats == null || State == FlyEnemyState.Dead || playerStats.IsDead)
            return false;

        Vector2 me = rb.position;
        Vector2 toTarget = (Vector2)player.position - me;
        float distance = toTarget.magnitude;
        if (distance > sightRange)
            return false;

        if (State != FlyEnemyState.Chase && distance > backSenseRange)
        {
            Vector2 facing = Facing > 0 ? Vector2.right : Vector2.left;
            float cosLimit = Mathf.Cos(sightAngle * 0.5f * Mathf.Deg2Rad);
            if (Vector2.Dot(toTarget.normalized, facing) < cosLimit)
                return false;
        }

        return !SegmentBlocked(me, (Vector2)player.position + aimOffset);
    }

    private bool SegmentBlocked(Vector2 from, Vector2 to)
    {
        if (from == to)
            return false;

        RaycastHit2D[] hits = Physics2D.LinecastAll(from, to, obstacleMask);
        for (int i = 0; i < hits.Length; i++)
        {
            Collider2D hit = hits[i].collider;
            if (hit == null)
                continue;
            if (hit.transform == transform || hit.transform.IsChildOf(transform))
                continue;
            return true;
        }
        return false;
    }

    private void Steer(Vector2 desiredVelocity, float dt)
    {
        rb.velocity = Vector2.MoveTowards(rb.velocity, desiredVelocity,
            Mathf.Max(0f, steerAcceleration) * dt);
    }

    private Vector2 HoverVelocity()
    {
        return new Vector2(0f, Mathf.Sin(Time.time * hoverFrequency + hoverPhase) * hoverAmplitude);
    }

    private void ResolvePlayer()
    {
        PlayerController controller = FindObjectOfType<PlayerController>();
        if (controller == null)
            return;

        player = controller.transform;
        playerStats = controller.GetComponent<CharacterState>();
        IgnorePlayerCollision(controller);
    }

    private void IgnorePlayerCollision(PlayerController controller)
    {
        if (playerCollisionIgnored || controller == null || ownColliders == null)
            return;

        Collider2D[] playerColliders = controller.GetComponentsInChildren<Collider2D>();
        foreach (Collider2D mine in ownColliders)
        {
            if (mine == null || mine.isTrigger)
                continue;
            foreach (Collider2D theirs in playerColliders)
            {
                if (theirs != null)
                    Physics2D.IgnoreCollision(mine, theirs, true);
            }
        }

        playerCollisionIgnored = true;
    }

    private void SetFacing(int direction)
    {
        Facing = direction >= 0 ? 1 : -1;
        if (spriteRenderer != null)
            spriteRenderer.flipX = spriteFacesRight ? Facing < 0 : Facing > 0;
    }

    /// <summary>行为层只通知动画状态；播哪一段、要不要重播由动画 FSM 的绑定表决定。</summary>
    protected void PlayAnimation(EnemyAnimationState state, bool restart = false)
    {
        if (animFsm != null)
            animFsm.ChangeState(state, restart);
    }

    // ───────────────────────────── Scene 面板可视化 ─────────────────────────────

    private void OnDrawGizmosSelected()
    {
        Vector2 center = Application.isPlaying ? rb != null ? rb.position : (Vector2)transform.position
                                               : (Vector2)transform.position;
        Vector2 home = Application.isPlaying ? homePosition : center;
        Vector2 facing = (Application.isPlaying ? Facing : (startFacingRight ? 1 : -1)) > 0
            ? Vector2.right : Vector2.left;

        DrawCircle(home, patrolRadius, new Color(1f, 0.85f, 0.2f, 0.9f));
        Gizmos.color = new Color(1f, 0.85f, 0.2f, 1f);
        Gizmos.DrawLine(home + Vector2.left * 0.2f, home + Vector2.right * 0.2f);
        Gizmos.DrawLine(home + Vector2.down * 0.2f, home + Vector2.up * 0.2f);

        DrawCircle(center, chaseRange, new Color(1f, 0.55f, 0.1f, 0.75f));
        DrawCircle(center, attackRange, new Color(0.9f, 0.2f, 0.8f, 0.9f));
        DrawCircle(center, backSenseRange, new Color(1f, 0.3f, 0.3f, 0.4f));
        DrawSector(center, facing, sightRange, sightAngle, new Color(1f, 0.25f, 0.25f, 0.9f));
    }

    private static void DrawCircle(Vector2 center, float radius, Color color)
    {
        if (radius <= 0f)
            return;

        Gizmos.color = color;
        const int segments = 48;
        Vector2 previous = center + new Vector2(radius, 0f);
        for (int i = 1; i <= segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            Vector2 next = center + new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
            Gizmos.DrawLine(previous, next);
            previous = next;
        }
    }

    private static void DrawSector(Vector2 center, Vector2 facing, float radius, float totalAngle, Color color)
    {
        if (radius <= 0f)
            return;

        Gizmos.color = color;
        float baseAngle = Mathf.Atan2(facing.y, facing.x);
        float half = totalAngle * 0.5f * Mathf.Deg2Rad;
        const int segments = 24;
        Vector2 previous = center + new Vector2(Mathf.Cos(baseAngle - half), Mathf.Sin(baseAngle - half)) * radius;
        Gizmos.DrawLine(center, previous);
        for (int i = 1; i <= segments; i++)
        {
            float angle = baseAngle - half + (2f * half * i / segments);
            Vector2 next = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            Gizmos.DrawLine(previous, next);
            previous = next;
        }
        Gizmos.DrawLine(previous, center);
    }
}
