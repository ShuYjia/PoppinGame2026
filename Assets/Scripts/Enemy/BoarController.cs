using UnityEngine;

/// <summary>
/// 野猪（地面敌人 Enemy1，策划案 6.1 / 6.3）：巡逻 → 发现玩家 → 蓄力 → 冲撞 → 喘气 → 回巡逻。
///
/// 状态机：
/// <code>
/// Patrol ──看见玩家──▶ Alert（原地蓄力 alertDuration）──▶ Charge（直线冲撞）
///   ▲                                                    │ 撞墙 / 到崖边 / 冲满时长
///   └── 丢失目标 loseTargetTime 秒 ◀── Recover（喘气，能被白打）◀┘
/// 任意状态受击 ─▶ Hurt（击退 + 闪白 hurtStun 秒）─▶ Alert（被打必定转向玩家）
/// HP 归零 ─▶ Dead（停伤害、闪白淡出、计分后隐藏）
/// </code>
///
/// 规则（策划案 6.1）：只在地面移动、不跳、**不会走下悬崖**（巡逻和冲撞都会在崖边停住），
/// 丢失目标 3 秒脱战。数值（HP 100 / 接触伤害 10 / 受伤无敌 0.5 秒）在同物体的
/// <see cref="CharacterState"/> 与 <see cref="Attack"/> 上调，本组件只管行为。
///
/// 与玩家不发生物理推挤：Start 时对玩家的碰撞体做 IgnoreCollision，伤害只走 Attack 触发器。
/// 美术朝左绘制（spriteFacesRight = false），朝向用 SpriteRenderer.flipX 镜像。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CharacterState))]
public class BoarController : MonoBehaviour
{
    public enum BoarState
    {
        Patrol,
        Alert,
        Charge,
        Recover,
        Hurt,
        Dead
    }

    [Header("巡逻")]
    [Tooltip("巡逻走路速度（单位/秒）。")]
    public float walkSpeed = 1.5f;
    [Tooltip("以出生点为中心，左右各走多远就掉头（单位）。0 = 只靠墙 / 崖边掉头。")]
    public float patrolRange = 4f;
    [Tooltip("掉头时原地停顿的时间（秒）。")]
    public float turnPause = 0.5f;
    [Tooltip("出生时朝右（true）还是朝左（false）。")]
    public bool startFacingRight;

    [Header("发现玩家")]
    [Tooltip("水平视野距离（单位）。策划案「中档」= 5 格。")]
    public float sightRange = 6f;
    [Tooltip("上下多高以内算同一层（单位）。")]
    public float sightHeight = 2.5f;
    [Tooltip("背后的感知距离（单位）：玩家贴到身后这么近也会被发现。")]
    public float backSenseRange = 1.5f;
    [Tooltip("看不见玩家多久后脱战回巡逻（秒）。策划案 Enemy1 = 3 秒。")]
    public float loseTargetTime = 3f;

    [Header("冲撞")]
    [Tooltip("发现玩家后原地蓄力多久再冲（秒），给玩家反应时间。")]
    public float alertDuration = 0.5f;
    [Tooltip("冲撞速度（单位/秒）。")]
    public float chargeSpeed = 7f;
    [Tooltip("单次冲撞最长持续时间（秒）。")]
    public float maxChargeTime = 1.4f;
    [Tooltip("冲过玩家多远后刹车（单位）。")]
    public float overshootDistance = 2f;
    [Tooltip("冲完喘气多久（秒），这段时间可以放心输出。")]
    public float recoverTime = 0.9f;

    [Header("受击")]
    [Tooltip("被打时的击退速度（单位/秒）。")]
    public float knockbackSpeed = 5f;
    [Tooltip("受击硬直时长（秒）。")]
    public float hurtStun = 0.35f;

    [Header("死亡")]
    [Tooltip("死亡后闪白淡出的时长（秒），结束后隐藏物体。")]
    public float deathFadeTime = 0.6f;
    [Tooltip("击杀基础分（策划案 4.3.1：地面敌人 50）。")]
    public int killScore = 50;

    [Header("环境探测")]
    [Tooltip("哪些层算地面 / 墙（不要勾 Player、Enemy）。")]
    public LayerMask groundMask = ~((1 << 6) | (1 << 7));
    [Tooltip("身体半宽（单位）：崖边 / 撞墙探测从身体前沿开始。")]
    public float bodyHalfWidth = 0.7f;
    [Tooltip("崖边探测向下的距离（单位）。")]
    public float cliffProbeDepth = 0.6f;

    [Header("表现")]
    [Tooltip("美术是否朝右绘制。野猪素材朝左，保持 false。")]
    public bool spriteFacesRight;

    // 序列帧片段名，与预制体上 SpriteSequencePlayer 的片段名对应。
    private const string ClipIdle = "Idle";
    private const string ClipWalk = "Walk";
    private const string ClipRun = "Run";
    private const string ClipHit = "Hit";

    public BoarState State { get; private set; } = BoarState.Patrol;

    /// <summary>1 朝右，-1 朝左。</summary>
    public int Facing { get; private set; } = -1;

    private Rigidbody2D rb;
    private CharacterState stats;
    private SpriteRenderer spriteRenderer;
    private SpriteSequencePlayer sequence;
    private Attack contactAttack;
    private Collider2D[] ownColliders;
    private readonly RaycastHit2D[] castHits = new RaycastHit2D[4];

    private Transform player;
    private CharacterState playerStats;

    private float homeX;
    private Vector2 homePosition;
    private float stateTimer;
    private float pauseTimer;
    private float lastSeenTime = -999f;
    private int chargeDirection;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        stats = GetComponent<CharacterState>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        sequence = GetComponent<SpriteSequencePlayer>();
        contactAttack = GetComponent<Attack>();
        ownColliders = GetComponents<Collider2D>();
        rb.freezeRotation = true;
    }

    private void OnEnable()
    {
        stats.OnTakeDamage.AddListener(OnHurt);
        stats.Died += OnDied;
    }

    private void OnDisable()
    {
        stats.OnTakeDamage.RemoveListener(OnHurt);
        stats.Died -= OnDied;
    }

    private void Start()
    {
        homeX = transform.position.x;
        homePosition = rb.position;
        SetFacing(startFacingRight ? 1 : -1);
        FindPlayer();
        EnterPatrol();

        if (LevelManager.Instance != null)
            LevelManager.Instance.PlayerRespawned += ResetToHome;
    }

    private void OnDestroy()
    {
        if (LevelManager.Instance != null)
            LevelManager.Instance.PlayerRespawned -= ResetToHome;
    }

    /// <summary>
    /// 玩家复活时调用：活着的野猪回到出生点、回满血、重新巡逻。
    /// 否则野猪会守在检查点旁边，玩家复活无敌一结束就被撞死，陷入死亡循环。
    /// 已死的野猪不复活。
    /// </summary>
    public void ResetToHome()
    {
        if (State == BoarState.Dead || !isActiveAndEnabled)
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

    private void FindPlayer()
    {
        PlayerController controller = FindObjectOfType<PlayerController>();
        if (controller == null)
            return;

        player = controller.transform;
        playerStats = controller.GetComponent<CharacterState>();

        // 不和玩家物理推挤：伤害只走 Attack 触发器，冲撞时直接穿过玩家。
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
    }

    // ───────────────────────────── 状态机 ─────────────────────────────

    private void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        bool sees = CanSeePlayer();
        if (sees)
            lastSeenTime = Time.time;

        switch (State)
        {
            case BoarState.Patrol:
                TickPatrol(dt, sees);
                break;
            case BoarState.Alert:
                SetHorizontalVelocity(0f);
                FacePlayer();
                stateTimer -= dt;
                if (stateTimer <= 0f)
                    EnterCharge();
                break;
            case BoarState.Charge:
                TickCharge(dt);
                break;
            case BoarState.Recover:
                SetHorizontalVelocity(Mathf.MoveTowards(rb.velocity.x, 0f, 30f * dt));
                stateTimer -= dt;
                if (stateTimer <= 0f)
                {
                    if (sees)
                        EnterAlert();
                    else if (Time.time - lastSeenTime > loseTargetTime)
                        EnterPatrol();
                }
                break;
            case BoarState.Hurt:
                // 击退速度逐渐衰减；崖边不往下掉。
                float vx = Mathf.MoveTowards(rb.velocity.x, 0f, 20f * dt);
                if (vx != 0f && !HasGroundAhead((int)Mathf.Sign(vx)))
                    vx = 0f;
                SetHorizontalVelocity(vx);
                stateTimer -= dt;
                if (stateTimer <= 0f)
                    EnterAlert();
                break;
            case BoarState.Dead:
                SetHorizontalVelocity(0f);
                break;
        }
    }

    private void TickPatrol(float dt, bool sees)
    {
        if (sees)
        {
            EnterAlert();
            return;
        }

        if (pauseTimer > 0f)
        {
            pauseTimer -= dt;
            SetHorizontalVelocity(0f);
            PlayClip(ClipIdle);
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
        PlayClip(ClipWalk);
    }

    private void TickCharge(float dt)
    {
        stateTimer -= dt;

        bool blocked = IsWallAhead(chargeDirection) || !HasGroundAhead(chargeDirection);
        bool overshot = player != null
                        && (transform.position.x - player.position.x) * chargeDirection > overshootDistance;

        if (blocked || overshot || stateTimer <= 0f)
        {
            SetHorizontalVelocity(0f);
            EnterRecover();
            return;
        }

        SetHorizontalVelocity(chargeDirection * chargeSpeed);
    }

    private void EnterPatrol()
    {
        State = BoarState.Patrol;
        pauseTimer = 0f;
        PlayClip(ClipWalk);
    }

    private void EnterAlert()
    {
        State = BoarState.Alert;
        stateTimer = alertDuration;
        FacePlayer();
        PlayClip(ClipIdle);
    }

    private void EnterCharge()
    {
        State = BoarState.Charge;
        stateTimer = maxChargeTime;
        chargeDirection = Facing;
        PlayClip(ClipRun);
    }

    private void EnterRecover()
    {
        State = BoarState.Recover;
        stateTimer = recoverTime;
        PlayClip(ClipIdle);
    }

    // ───────────────────────────── 受击 / 死亡 ─────────────────────────────

    /// <summary>CharacterState.OnTakeDamage 回调：真正扣到血才会走到这里。</summary>
    private void OnHurt(Transform attacker)
    {
        if (State == BoarState.Dead)
            return;

        int away = -Facing;
        if (attacker != null)
        {
            float dx = transform.position.x - attacker.position.x;
            if (Mathf.Abs(dx) > 0.0001f)
                away = dx > 0f ? 1 : -1;
        }

        // 被打就记住玩家：硬直结束后直接转向玩家蓄力，不会傻傻地继续巡逻。
        lastSeenTime = Time.time;
        State = BoarState.Hurt;
        stateTimer = hurtStun;
        SetHorizontalVelocity(HasGroundAhead(away) ? away * knockbackSpeed : 0f);
        PlayClip(ClipHit, true);
    }

    private void OnDied()
    {
        State = BoarState.Dead;
        SetHorizontalVelocity(0f);

        // 尸体不再造成伤害、不再挡路（只保留脚底碰撞体，免得穿过地面掉下去）。
        if (contactAttack != null)
            contactAttack.enabled = false;
        foreach (Collider2D c in ownColliders)
        {
            if (c != null && c.isTrigger)
                c.enabled = false;
        }

        PlayClip(ClipHit, true);

        if (LevelManager.Instance != null)
            LevelManager.Instance.RegisterKill(killScore);

        StartCoroutine(FadeOutAndHide());
    }

    private System.Collections.IEnumerator FadeOutAndHide()
    {
        float t = 0f;
        Color baseColor = spriteRenderer != null ? spriteRenderer.color : Color.white;
        while (t < deathFadeTime)
        {
            t += Time.deltaTime;
            if (spriteRenderer != null)
            {
                Color c = baseColor;
                c.a = Mathf.Lerp(1f, 0f, t / Mathf.Max(0.01f, deathFadeTime));
                spriteRenderer.color = c;
            }
            yield return null;
        }
        gameObject.SetActive(false);
    }

    // ───────────────────────────── 感知 / 探测 ─────────────────────────────

    private bool CanSeePlayer()
    {
        if (player == null || State == BoarState.Dead)
            return false;
        if (playerStats != null && playerStats.IsDead)
            return false;

        Vector2 me = transform.position;
        Vector2 target = player.position;
        float dx = target.x - me.x;
        float dy = target.y - me.y;
        if (Mathf.Abs(dy) > sightHeight)
            return false;

        // 已经盯上玩家（蓄力 / 喘气中）时不看朝向；巡逻时只看前方 + 身后很近的一小段。
        bool aggro = State == BoarState.Alert || State == BoarState.Recover;
        bool inFront = dx * Facing >= 0f;
        float range = aggro || inFront ? sightRange : backSenseRange;
        if (Mathf.Abs(dx) > range)
            return false;

        // 视线被地形挡住就看不见。
        Vector2 eye = me + Vector2.up * 0.8f;
        Vector2 targetCenter = target + Vector2.up * 1.2f;
        Vector2 toTarget = targetCenter - eye;
        return !CastGround(eye, toTarget.normalized, toTarget.magnitude);
    }

    private void FacePlayer()
    {
        if (player == null)
            return;
        float dx = player.position.x - transform.position.x;
        if (Mathf.Abs(dx) > 0.05f)
            SetFacing(dx > 0f ? 1 : -1);
    }

    /// <summary>身体前沿往下探一小段：探不到地面 = 前面是悬崖。</summary>
    private bool HasGroundAhead(int direction)
    {
        Vector2 origin = (Vector2)transform.position + new Vector2(direction * (bodyHalfWidth + 0.1f), 0.2f);
        return CastGround(origin, Vector2.down, cliffProbeDepth + 0.2f);
    }

    /// <summary>身体中段朝前探一小段：碰到地形 = 前面是墙 / 台阶。</summary>
    private bool IsWallAhead(int direction)
    {
        Vector2 origin = (Vector2)transform.position + new Vector2(0f, 0.5f);
        return CastGround(origin, new Vector2(direction, 0f), bodyHalfWidth + 0.15f);
    }

    /// <summary>
    /// 只打实体地形的射线：显式排除触发器（告示牌范围、拾取物、死亡区都是触发器，
    /// Physics2D 默认会打到它们，会被误判成「前面有地面」而走下悬崖）。
    /// </summary>
    private bool CastGround(Vector2 origin, Vector2 direction, float distance)
    {
        ContactFilter2D filter = new ContactFilter2D();
        filter.useTriggers = false;
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

    /// <summary>切换序列帧片段（Unity 对象不能用 ?. 判空：编辑器里缺组件时拿到的是「假 null」）。</summary>
    private void PlayClip(string clipName, bool restart = false)
    {
        if (sequence != null)
            sequence.Play(clipName, restart);
    }

    private void SetHorizontalVelocity(float vx)
    {
        rb.velocity = new Vector2(vx, rb.velocity.y);
    }

    private void SetFacing(int direction)
    {
        Facing = direction >= 0 ? 1 : -1;
        if (spriteRenderer != null)
            spriteRenderer.flipX = spriteFacesRight ? Facing < 0 : Facing > 0;
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 p = transform.position;
        float home = Application.isPlaying ? homeX : p.x;
        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.8f);
        Gizmos.DrawLine(new Vector3(home - patrolRange, p.y + 0.1f), new Vector3(home + patrolRange, p.y + 0.1f));
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.5f);
        Gizmos.DrawWireCube(p + Vector3.up * 0.8f, new Vector3(sightRange * 2f, sightHeight * 2f, 0f));
    }
}
