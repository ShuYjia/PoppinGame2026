using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// FirstLevel 的 2D 平台角色控制器（只负责角色自己的规则与运动）。
/// 键位：A/D 或 ←/→ 移动（键盘默认奔跑），Ctrl 走路，S/↓ 或 C 蹲下，Space 跳跃，Shift 闪避，J 攻击。
/// 手柄：左摇杆移动（推动幅度线性决定速度，推满即奔跑），右肩键闪避，西键（方块）蹲下，南键跳跃，北键（△）攻击。
///
/// 本组件与三个协作组件挂在同一个 GameObject 上：
/// - <see cref="PlayerGroundSensor"/>：物理检测（脚底薄盒探测、区域遮挡），不含规则状态。
/// - <see cref="PlayerAnimatorView"/>：Animator 参数写入、精灵翻转、闪避特效镜像。
/// - <see cref="PlayerState"/>：体力 / 血量双资源（HP + SP）的数值与事件，动作规则仍由本组件判定。
///
/// 设计要点：
/// 1. 输入在 Update 中采样，物理在 FixedUpdate 中执行，两者通过“跳跃缓冲”衔接，
///    因此 FixedUpdate 与 Update 不同步时也不会丢输入。
/// 2. 地面检测使用贴合脚底的薄盒（几何计算在 PlayerGroundSensor），配合“土狼时间”和“起跳后锁定”，
///    既不会悬空误判，也不会一帧内连跳。
/// 3. 起跳速度由跳跃高度和当前重力反推，改重力后不需要重新调参；
///    riseGravityMultiplier 经调校使 v0 ≥ 13，对齐 ksm.controller 空中混合树的
///    JumpStart 阈值（v0 = √(2·g·rise·jumpHeight)，下落仍由 fallGravityMultiplier 主导）。
///    jumpHeight 是理想连续高度：固定步长（0.02s）积分会让实测顶点低约 v0·dt/2，
///    已按 rise=3.58 对应的 v0 做补偿（2.90 → 实测顶点 ≈2.76）。
/// 4. 移动速度分为走 / 跑两档（speed=5 / runSpeed=8）：键盘默认奔跑、按住 Ctrl
///    降为走路；手柄左摇杆推动幅度在 0 ~ 奔跑速度之间线性映射（死区之外），
///    推满即为奔跑，轻推可慢速微调。走 / 跑状态按目标速度 > 6 判定，与动画阈值一致。
/// 5. 蹲下（接地）时不能主动水平移动，但可被场景机关 / 敌人推动，且可以直接起跳；
///    在空中新按下蹲下键会锁存一次“下潜”：强制高速下落直至落地并转入蹲下，
///    期间保留空中左右操控、禁止起跳。
/// 6. 动画由 ksm.controller 驱动：水平速度写 Speed，竖向速度写 velocityY（空中混合树），
///    接地状态写 isGround，蹲下状态写 IsCrouching（驱动 crouchDown 渐蹲过渡与 crouchIdle
///    蹲姿摆动两个状态）；参数写入集中在 PlayerAnimatorView。
/// 7. 闪避（Shift / 手柄右肩键）是一次固定时长、固定速度的水平冲刺：触发 Animator 的
///    Dodge 触发器进入 sprint 片段，该片段同时驱动子物体 "Sprite Mask/111_export-export"
///    的激活与后滑特效；冲刺期间锁定水平速度并暂停常规动画参数，结束后进入冷却。
///    冲刺距离 ≈ dodgeSpeed × dodgeDuration，两者均可在 Inspector 中调整；dodgeDuration
///    刻意短于 sprint 片段长度，冲刺结束后角色在冲刺姿势中惯性滑行至停止、片段照常播完。
///    玩家本身只用 SpriteRenderer.flipX 翻转、不会镜像子物体，因此向左冲刺时会：把特效
///    根物体（同时是 SpriteMask 遮罩窗口）镜像到玩家左侧、用 flipX 翻转特效贴图、并在
///    LateUpdate（晚于动画写入）把特效子物体的水平位移取反，使特效整体镜像到另一侧，
///    保证特效始终出现在冲刺方向的后方（实现在 PlayerAnimatorView）。
///    无敌帧尚未实装——待受伤系统实现时，在 IsDodging 为真期间屏蔽伤害（已预留）。
/// 8. 普通攻击（J / 手柄北键 △）：触发 ksm.controller 的 normalAttack 片段，并在角色前方
///    发射一枚音波（纯表现：飞行、撞墙/超时消失，无伤害；体力消耗见第 9 条）。攻击效果窗口
///    （attackDuration）内移动速度按 attackMoveMultiplier 降低（不锁定）。
///    空中攻击不触发攻击片段——AnyState→空中状态的既有结构会在一帧内把它切回空中姿势，
///    且残留的触发器会在落地时误触发一次攻击；空中攻击改为纯机械效果：立即由上升切入
///    下落，并在攻击窗口内把下落速度限制在 attackFallSpeed，到期后恢复正常下落。
///    伤害 / 命中等战斗数值仍留待战斗系统实现时接入（音波预制体即扩展点）。
/// 9. 体力 / 血量由同物体上的 <see cref="PlayerState"/> 托管（双资源模型，策划案 3.1 定稿）：
///    跳跃 / 闪避 / 攻击为一次性消耗，地面奔跑按秒持续消耗，走路 / 站立 / 空中操控 /
///    蹲下不消耗；当前 SP 小于消耗量时对应动作整个不发生（不部分扣除、不进冷却），
///    地面奔跑中 SP 归零时自动降级为走路速度（不硬停，保留操控）。HP 暂只托管数值
///    与事件，受伤 / 死亡规则待受伤系统接入。消耗数值见下方「体力消耗」节，均可调。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : MonoBehaviour
{
    /// <summary>角色的运动状态，供动画 / 音效逻辑查询。</summary>
    public enum MoveState
    {
        Idle,
        Walking,
        Running,
        Crouching,
        Airborne,
        Dodging
    }

    // ─────────────────────────────── 输入 ───────────────────────────────

    [Header("输入")]
    [Tooltip("留空时自动创建默认输入资产。")]
    public PlayerInputControl inputControl;

    // ───────────────────────────── 协作组件 ─────────────────────────────

    [Header("协作组件")]
    [Tooltip("物理检测组件（脚底探测 / 区域遮挡），与控制器挂在同一个 GameObject 上。")]
    [SerializeField]
    private PlayerGroundSensor groundSensor;

    [Tooltip("动画控制组件（Animator 参数 / 精灵翻转 / 闪避特效），与控制器挂在同一个 GameObject 上。")]
    [SerializeField]
    private PlayerAnimatorView animatorView;

    [Tooltip("体力 / 血量组件（双资源 HP + SP），与控制器挂在同一个 GameObject 上。")]
    [SerializeField]
    private PlayerState state;

    // ─────────────────────────────── 移动 ───────────────────────────────

    [Header("移动")]
    [Tooltip("走路速度。保留 speed 字段名以兼容 FirstLevel 场景里的既有数据。键盘按住 Ctrl 时使用；手柄的移动速度由摇杆幅度连续映射（幅度 × runSpeed）决定，无独立档位。")]
    public float speed = 5f;
    [Tooltip("奔跑速度。键盘默认移动即达到本速度；手柄摇杆推满（幅度 1）时达到本速度。")]
    public float runSpeed = 8f;
    [Tooltip("在空中按下蹲下键触发的下潜速度（单位/秒），落地后转入蹲下。")]
    public float crouchDiveSpeed = 20f;
    [Tooltip("水平加速度（单位/秒²）。<= 0 表示立即到达目标速度。")]
    public float acceleration = 60f;
    [Tooltip("松开方向键后的减速度（单位/秒²）。<= 0 表示立即停止。")]
    public float deceleration = 80f;
    [Range(0f, 1f)]
    [Tooltip("空中水平操控力相对地面的比例，越小越“跳出去就拉不回来”。")]
    public float airControl = 0.6f;
    [Range(0f, 1f)]
    [Tooltip("空中松开方向键后减速度相对地面的比例。越小越能保留跳跃惯性。")]
    public float airBrakeControl = 0.25f;
    [Range(0f, 0.9f)]
    [Tooltip("输入死区：水平输入绝对值小于该值视为无输入，避免摇杆漂移。")]
    public float inputDeadZone = 0.15f;

    // ─────────────────────────────── 跳跃 ───────────────────────────────

    [Header("跳跃")]
    [Tooltip("起跳高度（世界单位，理想连续高度）。起跳速度按当前重力自动换算，改重力后无需重新调参。注意：固定步长（0.02s）积分会让实测顶点低约 v0·dt/2（当前参数下 ≈0.14）；当前值 2.90 对应实测顶点 ≈2.76。")]
    public float jumpHeight = 2.9f;
    [Tooltip("起跳速度的硬编码覆盖值：> 0 时忽略 jumpHeight。正常流程请只调 jumpHeight。")]
    public float jumpVelocityOverride = 0f;
    [Range(0f, 1f)]
    [Tooltip("上升过程中松开跳跃键后保留的向上速度比例，用于实现“短跳”。1 表示不截断。")]
    public float jumpCutMultiplier = 0.45f;
    [Tooltip("土狼时间：离开地面后仍然允许起跳的宽限时长（秒）。")]
    public float coyoteTime = 0.1f;
    [Tooltip("跳跃缓冲：落地前提前按下跳跃键的缓存时长（秒）。")]
    public float jumpBufferTime = 0.12f;
    [Tooltip("下落时的重力倍率，让下落更干脆。")]
    public float fallGravityMultiplier = 1.6f;
    [Tooltip("上升时的重力倍率。当前值经调校：使起跳初速度 ≥ ksm.controller 空中混合树的 JumpStart 阈值 13（v0 = √(2·g·rise·jumpHeight)）。")]
    public float riseGravityMultiplier = 3.58f;
    [Tooltip("最大下落速度（单位/秒），避免高速穿模。<= 0 表示不限制。")]
    public float maxFallSpeed = 25f;
    [Tooltip("起跳后强制屏蔽落地判定的时长（秒），杜绝同一次起跳被判定成连跳。")]
    public float groundLockAfterJump = 0.08f;
    [Tooltip("当前的竖直速度（每帧刷新，供动画 / 调试查看）。")]
    public float velocityY;

    // ─────────────────────────────── 蹲下 ───────────────────────────────

    [Header("蹲下")]
    [Range(0.25f, 0.9f)]
    [Tooltip("蹲下时碰撞体高度相对于站立高度的比例。")]
    public float crouchHeightMultiplier = 0.55f;
    [Tooltip("起身前头顶检测向外扩张的余量（世界单位）。")]
    public float ceilingCheckPadding = 0.03f;

    // ─────────────────────────────── 闪避 ───────────────────────────────

    [Header("闪避")]
    [Tooltip("闪避的水平速度（单位/秒）。冲刺距离 ≈ 本值 × 闪避时长。")]
    public float dodgeSpeed = 22.4f;
    [Tooltip("闪避持续时间（秒），只锁定时长内的水平速度。刻意短于 ksm.controller 的 sprint 片段长度：冲刺结束后角色会在冲刺姿势中惯性滑行至停止，sprint 片段照常播完。")]
    public float dodgeDuration = 0.2f;
    [Tooltip("闪避结束后的冷却时间（秒），冷却期间不能再次闪避。")]
    public float dodgeCooldown = 0.6f;

    // ─────────────────────────────── 攻击 ───────────────────────────────

    [Header("攻击")]
    [Tooltip("普通攻击冷却（秒）：从按下攻击键起算，冷却期间忽略新的攻击输入。")]
    public float attackCooldown = 0.4f;
    [Tooltip("攻击效果窗口（秒）：与 normalAttack 片段长度（0.375s）对齐。窗口内降低移动速度；空中攻击窗口内同时限制下落速度，到期后恢复。")]
    public float attackDuration = 0.375f;
    [Tooltip("攻击窗口内的水平速度倍率：降低移动速度但不锁定（0.5 ≈ 奔跑 8 降到 4）。")]
    public float attackMoveMultiplier = 0.5f;
    [Tooltip("空中攻击窗口内的最大下落速度（单位/秒）：减缓下落；窗口结束后恢复正常下落。")]
    public float attackFallSpeed = 3f;
    [Tooltip("音波生成点相对碰撞体中心的水平偏移（世界单位，朝向前方）。")]
    public float waveSpawnOffset = 0.3f;
    [Tooltip("音波预制体（Assets/Scripts/Player/Attack/PlayerAttackWave.prefab）：飞行速度、存活时间、撞击层都在预制体上调。")]
    public GameObject wavePrefab;

    // ───────────────────────────── 体力消耗 ─────────────────────────────

    [Header("体力消耗")]
    [Tooltip("起跳消耗的体力（由 PlayerState 支付）。不足时本次起跳不执行：跳跃缓冲会被吃掉，不会在体力恢复后的某一帧突然自己跳起来。设为 0 即恢复不耗体力的跳跃。")]
    public float jumpStaminaCost = 10f;
    [Tooltip("一次闪避消耗的体力。不足时闪避不执行：不进入冷却、不变朝向，蹲下状态也不会先起身。设为 0 即恢复免费闪避。")]
    public float dodgeStaminaCost = 10f;
    [Tooltip("一次普通攻击消耗的体力（策划案定稿 10）。不足时攻击不执行：不进入冷却、不发射音波、不触发 Attacked 事件。设为 0 即恢复免费攻击。")]
    public float attackStaminaCost = 10f;
    [Tooltip("地面奔跑时每秒消耗的体力。走路 / 空中操控 / 蹲下不消耗；奔跑中体力归零自动降级为走路速度（不硬停）。设为 0 即恢复免费奔跑。")]
    public float runStaminaDrainPerSecond = 10f;

    // ───────────────────────────── 运行时状态 ─────────────────────────────

    /// <summary>当前帧的原始移动输入。</summary>
    public Vector2 InputDirection { get; private set; }

    /// <summary>本物理帧是否踩在地面上。</summary>
    public bool IsGrounded { get; private set; }

    /// <summary>是否处于蹲下状态。</summary>
    public bool IsCrouching { get; private set; }

    /// <summary>是否正在奔跑（按住奔跑键且在地面移动）。</summary>
    public bool IsRunning { get; private set; }

    /// <summary>是否正在走路（有输入、未奔跑、未蹲下）。</summary>
    public bool IsWalking { get; private set; }

    /// <summary>是否正在闪避（冲刺中）。供动画 / 将来的受伤系统查询（无敌帧预留位）。</summary>
    public bool IsDodging { get; private set; }

    /// <summary>是否处于攻击效果窗口内（按下攻击键后的 attackDuration 秒）。</summary>
    public bool IsAttacking => attackTimer > 0f;

    /// <summary>体力 / 血量组件（同物体上的 PlayerState），供技能系统 / HUD 查询。</summary>
    public PlayerState State => state;

    /// <summary>1 表示朝右，-1 表示朝左。</summary>
    public int FacingDirection { get; private set; } = 1;

    /// <summary>水平速度的绝对值，供动画混合使用。</summary>
    public float HorizontalSpeed => rb != null ? Mathf.Abs(rb.velocity.x) : 0f;

    /// <summary>当前运动状态。</summary>
    public MoveState CurrentState
    {
        get
        {
            if (IsDodging)
                return MoveState.Dodging;
            if (!IsGrounded)
                return MoveState.Airborne;
            if (IsCrouching)
                return MoveState.Crouching;
            if (Mathf.Abs(InputDirection.x) < inputDeadZone)
                return MoveState.Idle;
            return IsRunning ? MoveState.Running : MoveState.Walking;
        }
    }

    /// <summary>起跳瞬间触发（普通起跳、土狼跳、缓冲跳都会触发）。</summary>
    public event Action Jumped;

    /// <summary>落地瞬间触发。</summary>
    public event Action Landed;

    /// <summary>闪避成功触发瞬间（进入冲刺时）。</summary>
    public event Action Dodged;

    /// <summary>普通攻击触发瞬间（进入冷却并在前方发射音波时）。</summary>
    public event Action Attacked;

    [HideInInspector]
    [Tooltip("自动从本物体获取，无需在 Inspector 里指定。")]
    public Rigidbody2D rb;

    // ─────────────────────────────── 内部字段 ───────────────────────────────

    private const float ProbeSlack = 0.01f;               // 落地判定允许的微小向上速度
    private const float GroundedVelocityTolerance = 0.5f; // 向上速度超过该值一律视为离地
    private const float DefaultGravity = 9.81f;           // Physics2D.gravity 为 0 时的兜底

    private Collider2D col;
    private CapsuleCollider2D capsule;
    private BoxCollider2D box;

    private Vector2 standingSize;
    private Vector2 standingOffset;
    private float standingBottom;
    private bool canResizeCollider;

    private float baseGravityScale = 1f;
    private float appliedGravityScale;
    private bool crouchInput;
    private bool walkInput;
    private bool crouchInputPrev;
    private bool diveLatch;
    private bool dodgeInputPrev;
    private float dodgeTimer;
    private float dodgeCooldownTimer;
    private int dodgeDirection = 1;
    private float attackCooldownTimer;
    private float attackTimer;
    private bool attackInputPrev;
    private bool airAttackCutPending;
    private bool usingKeyboard = true;

    private float jumpBufferTimer;
    private float coyoteTimer;
    private float groundLockTimer;
    private bool jumpCutPending;
    private bool isJumping;

    // ─────────────────────────────── 生命周期 ───────────────────────────────

    private void Awake()
    {
        if (inputControl == null)
            inputControl = new PlayerInputControl();

        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();

        if (rb == null)
        {
            Debug.LogError("PlayerController 需要 Rigidbody2D。", this);
            enabled = false;
            return;
        }

        if (col == null)
        {
            Debug.LogError("PlayerController 需要 Collider2D。", this);
            enabled = false;
            return;
        }

        if (groundSensor == null)
            groundSensor = GetComponent<PlayerGroundSensor>();
        if (animatorView == null)
            animatorView = GetComponent<PlayerAnimatorView>();
        if (state == null)
            state = GetComponent<PlayerState>();

        if (groundSensor == null || animatorView == null || state == null)
        {
            Debug.LogError("PlayerController 需要同物体上的 PlayerGroundSensor、PlayerAnimatorView 与 PlayerState。", this);
            enabled = false;
            return;
        }

        capsule = col as CapsuleCollider2D;
        box = col as BoxCollider2D;

        CaptureStandingShape();
        baseGravityScale = rb.gravityScale;
        appliedGravityScale = baseGravityScale;

        // 降低墙面摩擦，防止跳跃时贴墙滑住。
        if (col.sharedMaterial == null)
        {
            PhysicsMaterial2D noFriction = new PhysicsMaterial2D("PlayerNoFriction");
            noFriction.friction = 0f;
            noFriction.bounciness = 0f;
            col.sharedMaterial = noFriction;
        }
    }

    private void OnEnable()
    {
        if (inputControl == null)
            inputControl = new PlayerInputControl();
        inputControl.Enable();
    }

    private void OnDisable()
    {
        if (inputControl != null)
            inputControl.Disable();
    }

    private void OnDestroy()
    {
        if (inputControl != null)
            inputControl.Dispose();
    }

    private void Update()
    {
        if (inputControl == null || rb == null)
            return;

        InputDirection = inputControl.GamePlay.Move.ReadValue<Vector2>();
        // 方向键向下或 C 蹲下；Ctrl 是走路键，不再触发蹲下。
        crouchInput = InputDirection.y < -0.5f || IsCrouchPressed();
        walkInput = IsWalkPressed();

        // 闪避：Shift（键盘）/ 右肩键（手柄），边沿触发；冲刺中或冷却中忽略。
        bool dodgeInput = IsDodgePressed();
        if (dodgeInput && !dodgeInputPrev)
            RequestDodge();
        dodgeInputPrev = dodgeInput;

        // 攻击：J（键盘）/ 北面键 △/Y（手柄），边沿触发；冷却中忽略。
        bool attackInput = IsAttackPressed();
        if (attackInput && !attackInputPrev)
            RequestAttack();
        attackInputPrev = attackInput;

        // 记录本次移动输入来自键盘还是手柄：键盘没有模拟量，默认即为奔跑（Ctrl 降为走路）；
        // 手柄的移动速度完全由摇杆幅度线性映射决定（0 ~ 奔跑速度），无独立档位。
        InputControl moveControl = inputControl.GamePlay.Move.activeControl;
        if (moveControl != null && moveControl.device != null)
            usingKeyboard = moveControl.device is Keyboard;

        // 在空中新按下蹲下键 → 锁存下潜：强制高速下落直至落地（松手也会落到底），
        // 落地后若仍按住蹲下键则转入蹲下。
        if (crouchInput && !crouchInputPrev && !IsGrounded)
            diveLatch = true;
        crouchInputPrev = crouchInput;

        // 跳跃键只在这里采样一次：按下进缓冲、松开记一次截断，
        // 具体起跳留给 FixedUpdate 判定，避免 FixedUpdate 与 Update 不同步时丢输入。
        if (inputControl.GamePlay.Jump.WasPressedThisFrame())
            jumpBufferTimer = Mathf.Max(jumpBufferTimer, jumpBufferTime);

        if (inputControl.GamePlay.Jump.WasReleasedThisFrame())
            jumpCutPending = true;

        // 闪避期间强制站姿：冲刺中不接受蹲下（蹲下状态触发闪避会先尝试起身）。
        SetCrouching(crouchInput && IsGrounded && !IsDodging);
        UpdateFacing();
        UpdateAnimation();

    }

    private void FixedUpdate()
    {
        if (rb == null || col == null)
            return;

        float dt = Time.fixedDeltaTime;
        if (jumpBufferTimer > 0f)
            jumpBufferTimer = Mathf.Max(0f, jumpBufferTimer - dt);
        if (groundLockTimer > 0f)
            groundLockTimer = Mathf.Max(0f, groundLockTimer - dt);
        if (dodgeCooldownTimer > 0f)
            dodgeCooldownTimer = Mathf.Max(0f, dodgeCooldownTimer - dt);
        if (attackCooldownTimer > 0f)
            attackCooldownTimer = Mathf.Max(0f, attackCooldownTimer - dt);
        if (attackTimer > 0f)
            attackTimer = Mathf.Max(0f, attackTimer - dt);
        if (IsDodging)
        {
            dodgeTimer -= dt;
            if (dodgeTimer <= 0f)
            {
                IsDodging = false;
                dodgeCooldownTimer = dodgeCooldown;
            }
        }

        bool wasGrounded = IsGrounded;
        bool groundedNow = ProbeGroundedNow();

        // 下潜锁存到落地即解除。
        if (diveLatch && groundedNow)
            diveLatch = false;

        if (groundedNow)
        {
            coyoteTimer = coyoteTime;
            isJumping = false;
            if (!wasGrounded && rb.velocity.y <= ProbeSlack)
                Landed?.Invoke();
        }
        else
        {
            coyoteTimer = Mathf.Max(0f, coyoteTimer - dt);
        }

        IsGrounded = groundedNow;

        // 空中攻击：立即由上升切入下落（无论之前是否在上升），随后在攻击窗口内减缓下落。
        if (airAttackCutPending)
        {
            airAttackCutPending = false;
            if (!IsGrounded)
                rb.velocity = new Vector2(rb.velocity.x, Mathf.Min(rb.velocity.y, 0f));
        }

        // 先判定起跳再处理松键截断：这样“同帧按下又松开”的极短点按也能正确变成小跳。
        TryJump();
        ApplyJumpCut();
        ApplyGravity();

        // 攻击窗口内减缓下落速度（下潜优先：下潜是主动高速下坠，不被攻击窗口覆盖）。
        if (attackTimer > 0f && !diveLatch && !IsGrounded && rb.velocity.y < -attackFallSpeed)
            rb.velocity = new Vector2(rb.velocity.x, -attackFallSpeed);

        // 下潜期间强制高速下落；水平操控保留，由 Move 的空中控制处理。
        if (diveLatch)
            rb.velocity = new Vector2(rb.velocity.x, -crouchDiveSpeed);

        // 闪避期间水平速度由冲刺锁定，跳过常规移动控制。
        if (IsDodging)
            rb.velocity = new Vector2(dodgeDirection * dodgeSpeed, rb.velocity.y);
        else
            Move();
    }

    // ─────────────────────────────── 移动 ───────────────────────────────

    /// <summary>
    /// 在物理帧更新水平速度，避免在 Update 中直接改 Rigidbody2D 造成抖动。
    /// </summary>
    public void Move()
    {
        if (rb == null)
            return;

        float inputX = Mathf.Abs(InputDirection.x) < inputDeadZone ? 0f : InputDirection.x;
        bool hasInput = Mathf.Abs(inputX) > Mathf.Epsilon;

        // 目标速度：蹲下 = 0（缓停，保证机关 / 敌人推得动）；
        // 键盘默认奔跑（runSpeed）、按住 Ctrl 降为走路（speed）两档；
        // 手柄摇杆幅度在 0 ~ runSpeed 之间线性映射，推满即为奔跑。
        float targetSpeed = IsCrouching ? 0f : (usingKeyboard && walkInput ? speed : runSpeed);
        // 体力耗尽时不再允许奔跑：降级为走路速度（不硬停——保留加速 / 转向操控，
        // 避免运动中突然定住的手感；体力恢复后自动回到奔跑档）。
        if (!IsCrouching && state != null && state.IsExhausted)
            targetSpeed = Mathf.Min(targetSpeed, speed);
        // 攻击效果窗口内降低移动速度（不锁定：输入仍然有效，只是目标速度打折）。
        if (attackTimer > 0f)
            targetSpeed *= attackMoveMultiplier;
        float targetVelocity = inputX * targetSpeed;

        // 走 / 跑状态与动画阈值一致：目标速度超过 6（约为摇杆 75% 幅度）记为奔跑。
        IsRunning = hasInput && !IsCrouching && Mathf.Abs(targetVelocity) > 6f;
        IsWalking = hasInput && !IsCrouching && !IsRunning;

        // 只有地面奔跑持续消耗体力：空中速度是跳跃惯性的延续，不另算；
        // 蹲下 / 走路 / 站立的目标速度到不了奔跑档（> 6），不会进入本分支。
        if (IsRunning && IsGrounded && state != null)
            state.DrainStamina(runStaminaDrainPerSecond * Time.fixedDeltaTime);

        float rate = (hasInput && !IsCrouching) ? acceleration : deceleration;
        if (!IsGrounded)
            rate *= Mathf.Clamp01(hasInput ? airControl : airBrakeControl);

        float horizontalVelocity = rate <= 0f
            ? targetVelocity
            : Mathf.MoveTowards(rb.velocity.x, targetVelocity, rate * Time.fixedDeltaTime);

        rb.velocity = new Vector2(horizontalVelocity, rb.velocity.y);
    }

    // ─────────────────────────────── 动画 ───────────────────────────────

    /// <summary>
    /// 把速度与接地状态交给动画组件。竖直速度、接地状态与蹲下状态每帧都写
    /// （空中混合树依赖前两者，蹲下动画 crouchDown / crouchIdle 依赖 IsCrouching）；
    /// Speed 只在接地且未蹲下 / 未下潜 / 未闪避时刷新，其余情况保持上一帧的值，
    /// 让 idle / walk / run 维持在当前档位。
    /// </summary>
    private void UpdateAnimation()
    {
        if (animatorView == null || rb == null)
            return;

        velocityY = rb.velocity.y;

        // 空中、蹲下、下潜、闪避均不驱动 Speed：分别保持落地前最后状态或播放冲刺片段。
        bool driveLocomotion = IsGrounded && !IsCrouching && !diveLatch && !IsDodging;
        animatorView.ApplyMotion(rb.velocity.x, velocityY, IsGrounded, driveLocomotion, IsCrouching);
    }

    // ─────────────────────────────── 闪避 ───────────────────────────────

    /// <summary>
    /// 请求一次闪避：不在冲刺中、冷却结束且体力足够时生效。方向取当前水平输入（超过死区），
    /// 否则取当前朝向；触发 Animator 的 Dodge 触发器进入 sprint 片段。
    /// 体力门控放在起身判定之前：被拒绝时没有任何状态变化（不起身、不进冷却、不变朝向）。
    /// 供 Update 的输入采样调用，将来的技能系统也可直接调用。
    /// </summary>
    public void RequestDodge()
    {
        if (rb == null || IsDodging || dodgeCooldownTimer > 0f)
            return;

        // 体力不足则整个放弃本次闪避（不消耗、不进冷却、不起身、不触发特效）。
        if (state != null && !state.TryConsumeStamina(dodgeStaminaCost))
            return;

        // 蹲下状态触发闪避先尝试起身；头顶有障碍起不来则放弃本次闪避。
        if (IsCrouching)
        {
            SetCrouching(false);
            if (IsCrouching)
                return;
        }

        dodgeDirection = Mathf.Abs(InputDirection.x) >= inputDeadZone
            ? (InputDirection.x > 0f ? 1 : -1)
            : FacingDirection;

        // 冲刺方向即新朝向，立即翻转精灵（冲刺期间朝向锁定）。
        if (dodgeDirection != FacingDirection)
        {
            FacingDirection = dodgeDirection;
            if (animatorView != null)
                animatorView.SetFacing(dodgeDirection);
        }

        // 特效镜像必须在片段写入位移之前设置（详见 PlayerAnimatorView）。
        if (animatorView != null)
            animatorView.SetDodgeDirection(dodgeDirection);

        IsDodging = true;
        dodgeTimer = dodgeDuration;

        if (animatorView != null)
            animatorView.TriggerDodge();

        Dodged?.Invoke();
    }

    // ─────────────────────────────── 攻击 ───────────────────────────────

    /// <summary>
    /// 请求一次普通攻击：冷却结束且体力足够时生效。地面攻击触发 ksm.controller 的 normalAttack 片段，
    /// 并在角色前方发射一枚音波；攻击效果窗口（attackDuration）内移动速度降低（不锁定）。
    /// 空中攻击不触发攻击片段——AnyState→空中状态的既有结构会在一帧内把它切回空中姿势，
    /// 且残留的触发器会在落地时误触发一次攻击；空中攻击改为纯机械效果：立即由上升切入
    /// 下落，并在攻击窗口内把下落速度限制在 attackFallSpeed，到期后恢复正常下落。
    /// 体力消耗已接入（attackStaminaCost，策划案定稿 10；不足时整个攻击不发生：不进冷却、
    /// 不发音波、不触发 Attacked 事件）；伤害 / 命中等战斗数值仍留待战斗系统实现时接入
    /// （音波预制体即扩展点）。
    /// 供 Update 的输入采样调用，将来的技能系统也可直接调用。
    /// </summary>
    public void RequestAttack()
    {
        if (rb == null || attackCooldownTimer > 0f)
            return;

        // 体力不足则整个放弃本次攻击（不消耗、不进冷却、不发音波、不触发事件）。
        if (state != null && !state.TryConsumeStamina(attackStaminaCost))
            return;

        attackCooldownTimer = attackCooldown;
        attackTimer = attackDuration;

        if (!IsGrounded)
            airAttackCutPending = true;

        // 只有地面攻击播放攻击片段（空中的处理见方法注释）。
        if (IsGrounded && animatorView != null)
            animatorView.TriggerAttack();

        SpawnAttackWave();

        Attacked?.Invoke();
    }

    /// <summary>在碰撞体正前方（朝向前）生成一枚音波，并按当前朝向发射。</summary>
    private void SpawnAttackWave()
    {
        if (wavePrefab == null || col == null)
            return;

        Bounds bounds = col.bounds;
        Vector2 spawnPosition = new Vector2(
            bounds.center.x + FacingDirection * (bounds.extents.x + waveSpawnOffset),
            bounds.center.y);

        GameObject wave = Instantiate(wavePrefab, spawnPosition, Quaternion.identity);
        PlayerAttackWave waveComponent = wave.GetComponent<PlayerAttackWave>();
        if (waveComponent != null)
            waveComponent.Launch(FacingDirection, transform);
    }

    // ─────────────────────────────── 跳跃 ───────────────────────────────

    /// <summary>立即起跳（不作条件判定，供外部事件调用）。体力不足时不起跳、不消耗。</summary>
    public void Jump()
    {
        if (rb == null)
            return;

        // 体力门控放在这里而不是只放在 TryJump：技能系统等外部调用同样受消耗约束，
        // 且能保证「跳起来」与「扣体力」永远成对发生。
        if (state != null && !state.TryConsumeStamina(jumpStaminaCost))
            return;

        jumpBufferTimer = 0f;
        coyoteTimer = 0f;
        groundLockTimer = Mathf.Max(groundLockTimer, groundLockAfterJump);
        IsGrounded = false;
        isJumping = true;

        rb.velocity = new Vector2(rb.velocity.x, GetJumpVelocity());

        Jumped?.Invoke();
    }

    /// <summary>处理缓冲 + 土狼时间，决定本物理帧是否真的起跳。</summary>
    private void TryJump()
    {
        if (jumpBufferTimer <= 0f)
            return;

        // 下潜期间不允许起跳；缓冲会一直保留到落地那一帧。
        // 闪避期间同样不允许起跳（与下潜一致），缓冲保留到冲刺结束。
        if (IsDodging || diveLatch || (!IsGrounded && coyoteTimer <= 0f))
            return;

        // 体力不足：吃掉本次跳跃缓冲后放弃——否则体力恢复后的某一帧角色会突然自己跳起来。
        if (state != null && !state.HasStamina(jumpStaminaCost))
        {
            jumpBufferTimer = 0f;
            return;
        }

        Jump();
    }

    /// <summary>
    /// 由跳跃高度反推起跳速度；jumpVelocityOverride > 0 时直接使用该值。
    /// 上升阶段实际生效的重力是「基础重力 × riseGravityMultiplier」，
    /// 因此按这个值反推，否则调大上升重力后实际跳跃高度会低于 jumpHeight。
    /// </summary>
    private float GetJumpVelocity()
    {
        if (jumpVelocityOverride > 0f)
            return jumpVelocityOverride;

        float gravity = Mathf.Abs(Physics2D.gravity.y * baseGravityScale) * Mathf.Max(0.01f, riseGravityMultiplier);
        if (gravity < 0.01f)
            gravity = DefaultGravity;

        return Mathf.Sqrt(2f * gravity * Mathf.Max(0.01f, jumpHeight));
    }

    /// <summary>松开跳跃键时截断上升速度，实现“按多久跳多高”。</summary>
    private void ApplyJumpCut()
    {
        if (!jumpCutPending)
            return;

        jumpCutPending = false;
        if (!isJumping || rb.velocity.y <= 0f)
            return;

        rb.velocity = new Vector2(rb.velocity.x, rb.velocity.y * Mathf.Clamp01(jumpCutMultiplier));
    }

    /// <summary>按上升 / 下落阶段切换重力倍率，并限制最大下落速度。</summary>
    private void ApplyGravity()
    {
        // 如果 gravityScale 被外部（Inspector 或其它脚本）改过，就把新值当作基准，
        // 这样运行时调参不会被本脚本覆盖。
        if (!Mathf.Approximately(rb.gravityScale, appliedGravityScale))
            baseGravityScale = rb.gravityScale;

        float verticalVelocity = rb.velocity.y;

        float multiplier = 1f;
        if (verticalVelocity < -0.01f)
            multiplier = Mathf.Max(0.01f, fallGravityMultiplier);
        else if (verticalVelocity > 0.01f)
            multiplier = Mathf.Max(0.01f, riseGravityMultiplier);

        appliedGravityScale = baseGravityScale * multiplier;
        rb.gravityScale = appliedGravityScale;

        if (maxFallSpeed > 0f && rb.velocity.y < -maxFallSpeed)
            rb.velocity = new Vector2(rb.velocity.x, -maxFallSpeed);
    }

    // ───────────────────────────── 地面检测 ─────────────────────────────

    /// <summary>
    /// 判定本物理帧是否接地：几何探测交给 PlayerGroundSensor，
    /// 「什么算接地」的规则留在这里——刚起跳的锁定时间内、以及正在上升时都不算落地。
    /// </summary>
    private bool ProbeGroundedNow()
    {
        if (groundSensor == null || rb == null)
            return false;

        // 刚起跳的几帧强制离地，彻底杜绝同一次起跳被重复判定。
        if (groundLockTimer > 0f)
            return false;

        // 正在上升时一律不算落地，避免上升途中再次触发跳跃。
        if (rb.velocity.y > GroundedVelocityTolerance)
            return false;

        return groundSensor.ProbeGround();
    }

    // ─────────────────────────────── 蹲下 ───────────────────────────────

    /// <summary>
    /// 只有站立状态且头顶没有障碍物时才能起身。
    /// </summary>
    public void SetCrouching(bool shouldCrouch)
    {
        if (!canResizeCollider)
        {
            IsCrouching = false;
            return;
        }

        if (shouldCrouch)
        {
            if (!IsCrouching)
            {
                ApplyColliderHeight(crouchHeightMultiplier);
                IsCrouching = true;
            }
            return;
        }

        if (IsCrouching && CanStand())
        {
            ApplyColliderHeight(1f);
            IsCrouching = false;
        }
    }

    /// <summary>头顶是否有足够空间让角色站起来。</summary>
    private bool CanStand()
    {
        if (col == null || standingSize == Vector2.zero)
            return true;

        Bounds current = col.bounds;
        float desiredTop = transform.TransformPoint(
            new Vector2(standingOffset.x, standingBottom + standingSize.y)).y;
        float extraHeight = desiredTop - current.max.y;
        if (extraHeight <= 0f)
            return true;

        float padding = Mathf.Max(0f, ceilingCheckPadding);
        Vector2 size = new Vector2(Mathf.Max(0.01f, current.size.x * 0.9f), extraHeight + padding * 2f);
        Vector2 center = new Vector2(current.center.x, current.max.y + extraHeight * 0.5f);

        if (groundSensor == null)
            return true;

        return !groundSensor.IsAreaBlocked(center, size);
    }

    private void CaptureStandingShape(bool warnIfUnsupported = true)
    {
        if (capsule != null)
        {
            standingSize = capsule.size;
            standingOffset = capsule.offset;
            canResizeCollider = true;
        }
        else if (box != null)
        {
            standingSize = box.size;
            standingOffset = box.offset;
            canResizeCollider = true;
        }
        else
        {
            standingSize = Vector2.zero;
            standingOffset = Vector2.zero;
            canResizeCollider = false;
            if (warnIfUnsupported)
                Debug.LogWarning("蹲下碰撞体缩放仅支持 CapsuleCollider2D 或 BoxCollider2D。", this);
        }

        standingBottom = standingOffset.y - standingSize.y * 0.5f;
    }

    private void ApplyColliderHeight(float multiplier)
    {
        multiplier = Mathf.Clamp(multiplier, 0.25f, 1f);

        if (capsule != null)
        {
            // 竖直胶囊的高度不能小于宽度，否则 Unity 会把它压扁成横向胶囊。
            Vector2 size = standingSize;
            size.y = Mathf.Max(standingSize.x, standingSize.y * multiplier);
            capsule.size = size;
            capsule.offset = OffsetKeepingBottom(size.y);
        }
        else if (box != null)
        {
            Vector2 size = standingSize;
            size.y = Mathf.Max(0.01f, standingSize.y * multiplier);
            box.size = size;
            box.offset = OffsetKeepingBottom(size.y);
        }
    }

    private Vector2 OffsetKeepingBottom(float height)
    {
        Vector2 offset = standingOffset;
        offset.y = standingBottom + height * 0.5f;
        return offset;
    }

    // ─────────────────────────────── 朝向 ───────────────────────────────

    private void UpdateFacing()
    {
        // 冲刺方向在触发时锁定，期间不随输入翻转。
        if (IsDodging)
            return;

        if (Mathf.Abs(InputDirection.x) < inputDeadZone)
            return;

        int direction = InputDirection.x > 0f ? 1 : -1;
        if (direction == FacingDirection)
            return;

        FacingDirection = direction;

        // 翻转交给动画组件：优先翻 SpriteRenderer，避免碰撞体的 X 偏移被一起镜像。
        if (animatorView != null)
            animatorView.SetFacing(direction);
    }

    // ─────────────────────────────── 输入读取 ───────────────────────────────

    /// <summary>闪避键：Shift（键盘）或右肩键（手柄）。</summary>
    private bool IsDodgePressed()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed))
            return true;

        Gamepad gamepad = Gamepad.current;
        return gamepad != null && gamepad.rightShoulder.isPressed;
    }

    /// <summary>攻击键：J（键盘）或北面键 △/Y（手柄）。</summary>
    private bool IsAttackPressed()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.jKey.isPressed)
            return true;

        Gamepad gamepad = Gamepad.current;
        return gamepad != null && gamepad.buttonNorth.isPressed;
    }

    private bool IsCrouchPressed()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.cKey.isPressed)
            return true;

        // PS 手柄对应方块键（西键）。
        Gamepad gamepad = Gamepad.current;
        return gamepad != null && gamepad.buttonWest.isPressed;
    }

    private bool IsWalkPressed()
    {
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed);
    }

    // ─────────────────────────────── 编辑器辅助 ───────────────────────────────

    private void OnValidate()
    {
        if (Application.isPlaying)
            return;

        col = GetComponent<Collider2D>();
        capsule = col as CapsuleCollider2D;
        box = col as BoxCollider2D;

        // 没有碰撞体时不刷警告，避免脚本重载时刷屏（Awake 里已有明确报错）。
        if (col != null)
            CaptureStandingShape(false);
    }
}
