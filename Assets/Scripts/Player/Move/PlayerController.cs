using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// FirstLevel 的 2D 平台角色控制器。
/// 键位：A/D 或 ←/→ 移动（键盘默认奔跑），Ctrl 走路，S/↓ 或 C 蹲下，Space 跳跃，Shift 闪避。
/// 手柄：左摇杆移动（推动幅度决定速度，默认走路），左肩键奔跑，右肩键闪避，西键（方块）蹲下，南键跳跃。
///
/// 设计要点：
/// 1. 输入在 Update 中采样，物理在 FixedUpdate 中执行，两者通过“跳跃缓冲”衔接，
///    因此 FixedUpdate 与 Update 不同步时也不会丢输入。
/// 2. 地面检测使用贴合脚底的薄盒，配合“土狼时间”和“起跳后锁定”，
///    既不会悬空误判，也不会一帧内连跳。
/// 3. 起跳速度由跳跃高度和当前重力反推，改重力后不需要重新调参。
/// 4. 移动速度只有走 / 跑两档：键盘默认奔跑、按住 Ctrl 降为走路；手柄默认走路、
///    左肩键奔跑。摇杆推动幅度在档位内连续缩放（死区之外线性映射）。
/// 5. 蹲下（接地）时不能主动水平移动，但可被场景机关 / 敌人推动，且可以直接起跳；
///    在空中新按下蹲下键会锁存一次“下潜”：强制高速下落直至落地并转入蹲下，
///    期间保留空中左右操控、禁止起跳。
/// 6. 动画由 ksm.controller 驱动：代码只把水平速度写进 Animator 的 Speed 参数，
///    由 idle / walk / run 的条件转换自行切换；空中、蹲下、下潜暂不驱动参数
///    （保持落地前最后播放的状态），等对应动画片段补全后再接入。
/// 7. 闪避（Shift / 手柄右肩键）是一次固定时长、固定速度的水平冲刺：触发 Animator 的
///    Dodge 触发器进入 sprint 片段，该片段同时驱动子物体 "Sprite Mask/111_export-export"
///    的激活与后滑特效；冲刺期间锁定水平速度并暂停常规动画参数，结束后进入冷却。
///    冲刺距离 ≈ dodgeSpeed × dodgeDuration，两者均可在 Inspector 中调整。
///    无敌帧尚未实装——待受伤系统实现时，在 IsDodging 为真期间屏蔽伤害（已预留）。
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

    // ─────────────────────────────── 动画 ───────────────────────────────

    [Header("动画")]
    [Tooltip("角色的 Animator。留空时自动从本物体获取（FirstLevel 的 Player 已挂 ksm.controller）。")]
    [SerializeField]
    private Animator animator;

    /// <summary>Animator 的 Speed 参数哈希，避免每帧字符串查找。</summary>
    private static readonly int SpeedParam = Animator.StringToHash("Speed");

    /// <summary>Animator 的 Dodge 触发器哈希（ksm.controller 中新增的参数）。</summary>
    private static readonly int DodgeParam = Animator.StringToHash("Dodge");

    // ─────────────────────────────── 移动 ───────────────────────────────

    [Header("移动")]
    [Tooltip("走路速度。保留 speed 字段名以兼容 FirstLevel 场景里的既有数据。键盘按住 Ctrl 或手柄默认移动时使用。")]
    public float speed = 5f;
    [Tooltip("奔跑速度（键盘默认移动或手柄左肩键；Shift 已改定为闪避键）。")]
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
    [Tooltip("起跳高度（世界单位）。起跳速度按当前重力自动换算，改重力后无需重新调参。")]
    public float jumpHeight = 3.2f;
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
    [Tooltip("上升时的重力倍率。")]
    public float riseGravityMultiplier = 1f;
    [Tooltip("最大下落速度（单位/秒），避免高速穿模。<= 0 表示不限制。")]
    public float maxFallSpeed = 25f;
    [Tooltip("起跳后强制屏蔽落地判定的时长（秒），杜绝同一次起跳被判定成连跳。")]
    public float groundLockAfterJump = 0.08f;

    // ───────────────────────────── 地面检测 ─────────────────────────────

    [Header("地面检测")]
    [Tooltip("哪些层算作地面。")]
    public LayerMask groundLayer = ~0;
    [Tooltip("脚下探测盒向下延伸的距离（世界单位）。越大越容易判定为“站在地上”。")]
    public float groundCheckDistance = 0.08f;
    [Range(0.1f, 1f)]
    [Tooltip("脚下探测盒宽度占碰撞体宽度的比例。")]
    public float groundCheckWidthScale = 0.9f;

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
    public float dodgeSpeed = 12f;
    [Tooltip("闪避持续时间（秒）。与 ksm.controller 的 sprint 片段长度（0.4667 秒）对齐；改动会同时影响冲刺距离与动画时长。")]
    public float dodgeDuration = 0.4667f;
    [Tooltip("闪避结束后的冷却时间（秒），冷却期间不能再次闪避。")]
    public float dodgeCooldown = 0.6f;

    [Header("调试")]
    [SerializeField]
    [Tooltip("在 Scene 视图里画出脚下的地面探测盒。")]
    private bool drawGizmos = true;

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

    [HideInInspector]
    [Tooltip("自动从本物体获取，无需在 Inspector 里指定。")]
    public Rigidbody2D rb;

    // ─────────────────────────────── 内部字段 ───────────────────────────────

    private const float ProbeSlack = 0.01f;               // 探测盒越过脚底线的余量
    private const float GroundedVelocityTolerance = 0.5f; // 向上速度超过该值一律视为离地
    private const float DefaultGravity = 9.81f;           // Physics2D.gravity 为 0 时的兜底
    private const int MaxProbeResults = 8;

    private Collider2D col;
    private CapsuleCollider2D capsule;
    private BoxCollider2D box;
    private SpriteRenderer spriteRenderer;

    private Vector2 standingSize;
    private Vector2 standingOffset;
    private float standingBottom;
    private bool canResizeCollider;

    private float baseGravityScale = 1f;
    private float appliedGravityScale;
    private bool sprintInput;
    private bool crouchInput;
    private bool walkInput;
    private bool crouchInputPrev;
    private bool diveLatch;
    private bool dodgeInputPrev;
    private float dodgeTimer;
    private float dodgeCooldownTimer;
    private int dodgeDirection = 1;
    private bool usingKeyboard = true;

    private float jumpBufferTimer;
    private float coyoteTimer;
    private float groundLockTimer;
    private bool jumpCutPending;
    private bool isJumping;
    private Collider2D[] probeBuffer;

    // ─────────────────────────────── 生命周期 ───────────────────────────────

    private void Awake()
    {
        if (inputControl == null)
            inputControl = new PlayerInputControl();

        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (animator == null)
            animator = GetComponent<Animator>();

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

        capsule = col as CapsuleCollider2D;
        box = col as BoxCollider2D;

        CaptureStandingShape();
        baseGravityScale = rb.gravityScale;
        appliedGravityScale = baseGravityScale;
        probeBuffer = new Collider2D[MaxProbeResults];

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
        // 奔跑键仅剩手柄左肩键：键盘默认即为奔跑，Shift 已改定为闪避键。
        sprintInput = IsSprintPressed();
        // 方向键向下或 C 蹲下；Ctrl 是走路键，不再触发蹲下。
        crouchInput = InputDirection.y < -0.5f || IsCrouchPressed();
        walkInput = IsWalkPressed();

        // 闪避：Shift（键盘）/ 右肩键（手柄），边沿触发；冲刺中或冷却中忽略。
        bool dodgeInput = IsDodgePressed();
        if (dodgeInput && !dodgeInputPrev)
            RequestDodge();
        dodgeInputPrev = dodgeInput;

        // 记录本次移动输入来自键盘还是手柄：键盘没有模拟量，默认即为奔跑（Ctrl 降为走路）；
        // 手柄默认走路，左肩键奔跑。
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
        bool groundedNow = ProbeGround();

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

        // 先判定起跳再处理松键截断：这样“同帧按下又松开”的极短点按也能正确变成小跳。
        TryJump();
        ApplyJumpCut();
        ApplyGravity();

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

        // 蹲下时不能奔跑也不能走路；无输入时也不算奔跑，便于动画回到 Idle。
        // 键盘默认奔跑、按住 Ctrl 降为走路；手柄默认走路、左肩键奔跑。
        IsRunning = hasInput && !IsCrouching && !walkInput && (usingKeyboard || sprintInput);
        IsWalking = hasInput && !IsCrouching && !IsRunning;

        // 蹲下时目标速度为 0：用 deceleration 缓停而不是硬置零，机关 / 敌人仍然推得动。
        float targetSpeed = IsCrouching ? 0f : (IsRunning ? runSpeed : speed);
        float targetVelocity = inputX * targetSpeed;

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
    /// 把水平速度写进 Animator 的 Speed 参数，由 ksm.controller 的条件转换
    /// 自行切换 idle / walk / run（阈值 0.1 / 6，夹在走速 5 与跑速 8 之间）。
    /// 空中、蹲下、下潜不驱动参数：保持落地前最后播放的状态；
    /// 闪避期间同样冻结——此时 Animator 由 Dodge 触发器切入 sprint 片段，
    /// Speed 不再参与该片段的状态转换。
    /// </summary>
    private void UpdateAnimation()
    {
        if (animator == null || rb == null)
            return;

        // 空中、蹲下、下潜、闪避均不驱动 Speed：分别保持落地前最后状态或播放冲刺片段。
        if (!IsGrounded || IsCrouching || diveLatch || IsDodging)
            return;

        animator.SetFloat(SpeedParam, Mathf.Abs(rb.velocity.x));
    }

    // ─────────────────────────────── 闪避 ───────────────────────────────

    /// <summary>
    /// 请求一次闪避：不在冲刺中且冷却结束时生效。方向取当前水平输入（超过死区），
    /// 否则取当前朝向；触发 Animator 的 Dodge 触发器进入 sprint 片段。
    /// 供 Update 的输入采样调用，将来的技能系统也可直接调用。
    /// </summary>
    public void RequestDodge()
    {
        if (rb == null || IsDodging || dodgeCooldownTimer > 0f)
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
            if (spriteRenderer != null)
                spriteRenderer.flipX = dodgeDirection < 0;
        }

        IsDodging = true;
        dodgeTimer = dodgeDuration;

        if (animator != null)
            animator.SetTrigger(DodgeParam);

        Dodged?.Invoke();
    }

    // ─────────────────────────────── 跳跃 ───────────────────────────────

    /// <summary>立即起跳（不作条件判定，供外部事件调用）。</summary>
    public void Jump()
    {
        if (rb == null)
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

        Jump();
    }

    /// <summary>由跳跃高度反推起跳速度；jumpVelocityOverride > 0 时直接使用该值。</summary>
    private float GetJumpVelocity()
    {
        if (jumpVelocityOverride > 0f)
            return jumpVelocityOverride;

        float gravity = Mathf.Abs(Physics2D.gravity.y * baseGravityScale);
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
    /// 用贴住脚底的一条薄盒判定是否站在地面上。
    /// 探测距离只有 groundCheckDistance，所以不会出现“离地一段距离仍算落地”导致的连跳。
    /// </summary>
    private bool ProbeGround()
    {
        if (col == null || rb == null)
            return false;

        // 刚起跳的几帧强制离地，彻底杜绝同一次起跳被重复判定。
        if (groundLockTimer > 0f)
            return false;

        // 正在上升时一律不算落地，避免上升途中再次触发跳跃。
        if (rb.velocity.y > GroundedVelocityTolerance)
            return false;

        Bounds bounds = col.bounds;
        float reach = Mathf.Max(0.005f, groundCheckDistance);
        Vector2 size = new Vector2(Mathf.Max(0.01f, bounds.size.x * groundCheckWidthScale), reach + ProbeSlack);
        Vector2 center = new Vector2(bounds.center.x, bounds.min.y - reach * 0.5f + ProbeSlack * 0.5f);

        return HasForeignCollider(center, size);
    }

    /// <summary>区域内是否命中“不属于自己”的碰撞体。</summary>
    private bool HasForeignCollider(Vector2 center, Vector2 size)
    {
        if (probeBuffer == null)
            probeBuffer = new Collider2D[MaxProbeResults];

        int count = Physics2D.OverlapBox(center, size, 0f, BuildGroundFilter(), probeBuffer);
        for (int i = 0; i < count; i++)
        {
            Collider2D hit = probeBuffer[i];
            if (hit == null || hit.isTrigger || hit == col)
                continue;
            // 自己以及挂在同一个刚体上的子碰撞体都不算障碍。
            if (rb != null && hit.attachedRigidbody == rb)
                continue;
            if (hit.transform == transform || hit.transform.IsChildOf(transform))
                continue;
            return true;
        }

        return false;
    }

    private ContactFilter2D BuildGroundFilter()
    {
        ContactFilter2D filter = new ContactFilter2D();
        filter.useTriggers = false;
        filter.useLayerMask = true;
        filter.layerMask = groundLayer;
        return filter;
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

        return !HasForeignCollider(center, size);
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

        // 优先翻转 SpriteRenderer：这样碰撞体的 X 偏移不会被一起镜像。
        if (spriteRenderer != null)
        {
            spriteRenderer.flipX = direction < 0;
            return;
        }

        Vector3 scale = transform.localScale;
        float absX = Mathf.Abs(scale.x);
        if (absX <= 0f)
            absX = 1f;
        scale.x = absX * direction;
        transform.localScale = scale;
    }

    // ─────────────────────────────── 输入读取 ───────────────────────────────

    /// <summary>奔跑键：仅手柄左肩键。键盘默认即为奔跑；Shift 已改定为闪避键。</summary>
    private bool IsSprintPressed()
    {
        Gamepad gamepad = Gamepad.current;
        return gamepad != null && gamepad.leftShoulder.isPressed;
    }

    /// <summary>闪避键：Shift（键盘）或右肩键（手柄）。</summary>
    private bool IsDodgePressed()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed))
            return true;

        Gamepad gamepad = Gamepad.current;
        return gamepad != null && gamepad.rightShoulder.isPressed;
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
        spriteRenderer = GetComponent<SpriteRenderer>();

        // 没有碰撞体时不刷警告，避免脚本重载时刷屏（Awake 里已有明确报错）。
        if (col != null)
            CaptureStandingShape(false);
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos)
            return;

        if (col == null)
            col = GetComponent<Collider2D>();
        if (col == null)
            return;

        Bounds bounds = col.bounds;
        float reach = Mathf.Max(0.005f, groundCheckDistance);
        Vector2 size = new Vector2(Mathf.Max(0.01f, bounds.size.x * groundCheckWidthScale), reach + ProbeSlack);
        Vector2 center = new Vector2(bounds.center.x, bounds.min.y - reach * 0.5f + ProbeSlack * 0.5f);

        Gizmos.color = Application.isPlaying && IsGrounded ? Color.green : Color.yellow;
        Gizmos.DrawWireCube(center, size);
    }
}
