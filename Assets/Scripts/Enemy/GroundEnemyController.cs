using Poppin.Animation;
using UnityEngine;

/// <summary>
/// 地面敌人的共用骨架：巡逻、发现玩家、受击、死亡和地形探测。
/// 具体的战斗方式由子类在 TickCombat 中实现。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CharacterState))]
public abstract class GroundEnemyController : MonoBehaviour
{
    public enum GroundEnemyState
    {
        Patrol,
        Combat,
        Hurt,
        Dead
    }

    [Header("巡逻")]
    [Tooltip("巡逻速度（单位/秒）。")]
    public float walkSpeed = 1.5f;
    [Tooltip("以出生点为中心，左右各走多远就掉头；0 = 只靠墙 / 崖边掉头。")]
    public float patrolRange = 4f;
    [Tooltip("掉头前原地停顿时间（秒）。")]
    public float turnPause = 0.5f;
    [Tooltip("出生时朝右。")]
    public bool startFacingRight;

    [Header("发现玩家")]
    [Tooltip("水平视野距离（单位）。")]
    public float sightRange = 7f;
    [Tooltip("上下多高以内算同一层（单位）。")]
    public float sightHeight = 2.5f;
    [Tooltip("背后的近距离感知范围。")]
    public float backSenseRange = 1.5f;
    [Tooltip("看不见玩家多久后脱战回巡逻（秒）。")]
    public float loseTargetTime = 5f;

    [Header("受击")]
    [Tooltip("被打时的击退速度（单位/秒）。")]
    public float knockbackSpeed = 5f;
    [Tooltip("受击硬直时长（秒）。")]
    public float hurtStun = 0.35f;

    [Header("死亡")]
    [Tooltip("死亡后淡出的时长（秒）。")]
    public float deathFadeTime = 0.6f;
    [Tooltip("击杀基础分。")]
    public int killScore = 50;

    [Header("环境探测")]
    [Tooltip("哪些层算地面 / 墙。")]
    public LayerMask groundMask = ~((1 << 6) | (1 << 7));
    [Tooltip("身体半宽（单位）。")]
    public float bodyHalfWidth = 0.7f;
    [Tooltip("崖边探测向下的距离（单位）。")]
    public float cliffProbeDepth = 0.6f;

    [Header("表现")]
    [Tooltip("美术是否朝右绘制。")]
    public bool spriteFacesRight;

    public GroundEnemyState State { get; private set; } = GroundEnemyState.Patrol;
    public int Facing { get; private set; } = -1;

    protected Rigidbody2D rb;
    protected CharacterState stats;
    protected SpriteRenderer spriteRenderer;

    /// <summary>敌人动画 FSM：本类只通知「该播哪个动画状态」，不直接操作序列帧播放器。</summary>
    protected EnemyAnimationStateMachine animFsm;

    protected Transform player;
    protected CharacterState playerStats;

    private Collider2D[] ownColliders;
    private readonly RaycastHit2D[] castHits = new RaycastHit2D[4];
    private float homeX;
    private Vector2 homePosition;
    private float stateTimer;
    private float pauseTimer;
    private float lastSeenTime = -999f;
    private bool playerCollisionIgnored;

    protected virtual void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        stats = GetComponent<CharacterState>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        animFsm = GetComponent<EnemyAnimationStateMachine>();
        ownColliders = GetComponents<Collider2D>();
        rb.freezeRotation = true;
    }

    protected virtual void OnEnable()
    {
        if (stats == null)
            return;

        stats.OnTakeDamage.AddListener(OnHurt);
        stats.Died += OnDied;
    }

    protected virtual void OnDisable()
    {
        if (stats == null)
            return;

        stats.OnTakeDamage.RemoveListener(OnHurt);
        stats.Died -= OnDied;
    }

    protected virtual void Start()
    {
        homeX = transform.position.x;
        homePosition = rb.position;
        SetFacing(startFacingRight ? 1 : -1);
        ResolvePlayer();
        EnterPatrol();
    }

    protected virtual void OnDestroy()
    {
        StopListeningToRespawn();
    }

    protected virtual void FixedUpdate()
    {
        if (player == null)
            ResolvePlayer();

        float dt = Time.fixedDeltaTime;
        bool sees = CanSeePlayer();
        if (sees)
            lastSeenTime = Time.time;

        switch (State)
        {
            case GroundEnemyState.Patrol:
                TickPatrol(dt, sees);
                break;
            case GroundEnemyState.Combat:
                TickCombat(dt, sees);
                if (!sees && Time.time - lastSeenTime > loseTargetTime)
                    EnterPatrol();
                break;
            case GroundEnemyState.Hurt:
                TickHurt(dt);
                break;
            case GroundEnemyState.Dead:
                SetHorizontalVelocity(0f);
                break;
        }
    }

    protected abstract void TickCombat(float deltaTime, bool seesPlayer);

    protected virtual void EnterCombat()
    {
        State = GroundEnemyState.Combat;
        FacePlayer();
        PlayAnimation(EnemyAnimationState.Idle);
    }

    protected virtual void OnDeathStarted()
    {
    }

    protected void EnterPatrol()
    {
        State = GroundEnemyState.Patrol;
        pauseTimer = 0f;
        PlayAnimation(EnemyAnimationState.Walk);
    }

    private void TickPatrol(float deltaTime, bool seesPlayer)
    {
        if (seesPlayer)
        {
            EnterCombat();
            return;
        }

        if (pauseTimer > 0f)
        {
            pauseTimer -= deltaTime;
            SetHorizontalVelocity(0f);
            PlayAnimation(EnemyAnimationState.Idle);
            if (pauseTimer <= 0f)
                SetFacing(-Facing);
            return;
        }

        bool outOfRange = patrolRange > 0f && (transform.position.x - homeX) * Facing >= patrolRange;
        if (outOfRange || IsWallAhead(Facing) || !HasGroundAhead(Facing))
        {
            pauseTimer = turnPause;
            SetHorizontalVelocity(0f);
            return;
        }

        SetHorizontalVelocity(Facing * walkSpeed);
        PlayAnimation(EnemyAnimationState.Walk);
    }

    private void TickHurt(float deltaTime)
    {
        float vx = Mathf.MoveTowards(rb.velocity.x, 0f, 20f * deltaTime);
        if (vx != 0f && !HasGroundAhead((int)Mathf.Sign(vx)))
            vx = 0f;
        SetHorizontalVelocity(vx);

        stateTimer -= deltaTime;
        if (stateTimer <= 0f)
        {
            if (player != null && !playerStatsIsDead())
                EnterCombat();
            else
                EnterPatrol();
        }
    }

    private void ResolvePlayer()
    {
        PlayerController controller = FindObjectOfType<PlayerController>();
        if (controller == null)
            return;

        player = controller.transform;
        playerStats = controller.GetComponent<CharacterState>();
        IgnorePlayerCollision(controller);

        if (LevelManager.Instance != null)
            LevelManager.Instance.PlayerRespawned -= ResetToHome;
        if (LevelManager.Instance != null)
            LevelManager.Instance.PlayerRespawned += ResetToHome;
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

    private bool playerStatsIsDead()
    {
        return playerStats != null && playerStats.IsDead;
    }

    /// <summary>玩家复活时，活着的敌人回出生点并回满血。</summary>
    public virtual void ResetToHome()
    {
        if (State == GroundEnemyState.Dead || !isActiveAndEnabled)
            return;

        rb.velocity = Vector2.zero;
        rb.position = homePosition;
        transform.position = homePosition;
        stats.ResetToFull();
        stats.ClearInvincibility();
        lastSeenTime = -999f;
        SetFacing(startFacingRight ? 1 : -1);
        EnterPatrol();
    }

    private void StopListeningToRespawn()
    {
        if (LevelManager.Instance != null)
            LevelManager.Instance.PlayerRespawned -= ResetToHome;
    }

    private void OnHurt(Transform attacker)
    {
        if (State == GroundEnemyState.Dead)
            return;

        int away = -Facing;
        if (attacker != null)
        {
            float dx = transform.position.x - attacker.position.x;
            if (Mathf.Abs(dx) > 0.0001f)
                away = dx > 0f ? 1 : -1;
        }

        lastSeenTime = Time.time;
        State = GroundEnemyState.Hurt;
        stateTimer = hurtStun;
        SetHorizontalVelocity(HasGroundAhead(away) ? away * knockbackSpeed : 0f);
        PlayAnimation(EnemyAnimationState.Hurt, true);
    }

    private void OnDied()
    {
        State = GroundEnemyState.Dead;
        SetHorizontalVelocity(0f);
        OnDeathStarted();

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
        Color baseColor = spriteRenderer != null ? spriteRenderer.color : Color.white;
        while (timer < deathFadeTime)
        {
            timer += Time.deltaTime;
            if (spriteRenderer != null)
            {
                Color color = baseColor;
                color.a = Mathf.Lerp(1f, 0f, timer / Mathf.Max(0.01f, deathFadeTime));
                spriteRenderer.color = color;
            }
            yield return null;
        }

        gameObject.SetActive(false);
    }

    protected bool CanSeePlayer()
    {
        if (player == null || State == GroundEnemyState.Dead || playerStatsIsDead())
            return false;

        Vector2 me = transform.position;
        Vector2 target = player.position;
        float dx = target.x - me.x;
        float dy = target.y - me.y;
        if (Mathf.Abs(dy) > sightHeight)
            return false;

        bool aggro = State == GroundEnemyState.Combat;
        bool inFront = dx * Facing >= 0f;
        float range = aggro || inFront ? sightRange : backSenseRange;
        if (Mathf.Abs(dx) > range)
            return false;

        Vector2 eye = me + Vector2.up * 0.8f;
        Vector2 targetCenter = target + Vector2.up * 1.2f;
        Vector2 toTarget = targetCenter - eye;
        return !CastGround(eye, toTarget.normalized, toTarget.magnitude);
    }

    protected void FacePlayer()
    {
        if (player == null)
            return;

        float dx = player.position.x - transform.position.x;
        if (Mathf.Abs(dx) > 0.05f)
            SetFacing(dx > 0f ? 1 : -1);
    }

    protected bool HasGroundAhead(int direction)
    {
        Vector2 origin = (Vector2)transform.position + new Vector2(direction * (bodyHalfWidth + 0.1f), 0.2f);
        return CastGround(origin, Vector2.down, cliffProbeDepth + 0.2f);
    }

    protected bool IsWallAhead(int direction)
    {
        Vector2 origin = (Vector2)transform.position + Vector2.up * 0.5f;
        return CastGround(origin, new Vector2(direction, 0f), bodyHalfWidth + 0.15f);
    }

    protected bool CastGround(Vector2 origin, Vector2 direction, float distance)
    {
        ContactFilter2D filter = new ContactFilter2D
        {
            useTriggers = false,
            useLayerMask = true
        };
        filter.SetLayerMask(groundMask);

        int count = Physics2D.Raycast(origin, direction, filter, castHits, distance);
        for (int i = 0; i < count; i++)
        {
            Collider2D hit = castHits[i].collider;
            if (hit != null && hit.attachedRigidbody != rb)
                return true;
        }
        return false;
    }

    /// <summary>
    /// 通知动画 FSM 切换状态（唯一出口）。行为控制器不直接碰 <see cref="SpriteSequencePlayer"/>：
    /// 播哪一段、要不要重播，全由动画 FSM 按绑定表决定。
    /// </summary>
    protected void PlayAnimation(EnemyAnimationState state, bool restart = false)
    {
        if (animFsm != null)
            animFsm.ChangeState(state, restart);
    }

    protected void SetHorizontalVelocity(float velocityX)
    {
        rb.velocity = new Vector2(velocityX, rb.velocity.y);
    }

    protected void SetFacing(int direction)
    {
        Facing = direction >= 0 ? 1 : -1;
        if (spriteRenderer != null)
            spriteRenderer.flipX = spriteFacesRight ? Facing < 0 : Facing > 0;
    }

    protected virtual void OnDrawGizmosSelected()
    {
        Vector3 position = transform.position;
        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.8f);
        Gizmos.DrawLine(new Vector3(position.x - patrolRange, position.y + 0.1f),
            new Vector3(position.x + patrolRange, position.y + 0.1f));
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.5f);
        Gizmos.DrawWireCube(position + Vector3.up * 0.8f,
            new Vector3(sightRange * 2f, sightHeight * 2f, 0f));
    }
}
