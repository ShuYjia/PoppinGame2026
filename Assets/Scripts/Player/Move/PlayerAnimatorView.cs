using Poppin.Animation;
using UnityEngine;

/// <summary>
/// 角色动画控制组件：**代码动画有限状态机**的唯一宿主。
///
/// 分工（重构后）：
/// - <see cref="PlayerController"/> 只提供只读状态（是否接地、竖直速度、是否蹲下 / 跑 / 走 / 闪避 / 受伤 / 死亡），
///   不再向 Animator 写任何参数；
/// - 本组件每帧把这些状态拍成快照，交给 <see cref="PlayerAnimationStateSelector"/> 算出目标动画状态，
///   再由 <see cref="AnimationStateMachine"/> 决定是否需要切换，最后由
///   <see cref="AnimatorPlaybackBackend"/> 调 <c>Animator.Play</c> 播具体片段；
/// - Animator 只是播片段的机器：不参与业务过渡、没有参数、没有 AnyState 过渡。
///
/// 优先级固定为 Dead &gt; Hurt &gt; Dodge &gt; Attack &gt; Airborne &gt; Crouch &gt; Run &gt; Walk &gt; Idle，
/// 具体规则见 <see cref="PlayerAnimationStateSelector"/>。
///
/// 纯表现的部分（精灵左右翻转、闪避特效镜像）仍留在本组件：它们与动画状态无关，只跟朝向 / 冲刺方向有关。
/// </summary>
[DefaultExecutionOrder(100)] // 晚于 PlayerController（默认 0）执行，保证读到的是一帧内算完的最新状态
public class PlayerAnimatorView : MonoBehaviour
{
    [Header("动画")]
    [Tooltip("角色的 Animator。留空时自动从本物体获取。它应当挂 ksm_fsm.controller（纯片段、无参数）。")]
    [SerializeField]
    private Animator animator;

    [Header("动画状态绑定")]
    [Tooltip("状态 id → Animator 状态名。stateId 必须与 PlayerAnimationState 的枚举名一致（Hurt / Walk …），playbackName 是控制器里的状态名。")]
    [SerializeField]
    private AnimationStateBinding[] bindings = CreateDefaultBindings();

    [Header("闪避特效")]
    [Tooltip("闪避特效根物体（FirstLevel 场景中为 Player 下的 \"Sprite Mask\"）：sprint 片段通过它驱动子物体 \"111_export-export\" 的激活与位移，它同时是一个 SpriteMask 遮罩窗口（特效在窗口外可见）。向左闪避时会把它按 X 镜像到玩家左侧，配合特效贴图翻转与位置镜像，使特效整体镜像到冲刺方向的后方。留空时按 \"Sprite Mask\" 自动查找。")]
    [SerializeField]
    private Transform dodgeEffectRoot;

    private const string DodgeEffectRootName = "Sprite Mask";    // 闪避特效根物体（SpriteMask 遮罩窗口 + 特效之父）
    private const string DodgeEffectName = "111_export-export"; // 闪避特效子物体（sprint 片段驱动其激活与位移）

    private PlayerController controller;
    private SpriteRenderer spriteRenderer;
    private AnimationStateMachine machine;

    private PlayerAnimationState currentState = PlayerAnimationState.Idle;
    private bool stateStarted;

    /// <summary>本帧有地面攻击请求（由 <see cref="NotifyAttackStarted"/> 置位，Update 消费一次）。</summary>
    private bool attackRequested;

    private Transform dodgeEffect;
    private SpriteRenderer dodgeEffectRenderer;
    private Vector3 dodgeEffectRootBasePosition;
    private int dodgeDirection = 1;

    /// <summary>默认绑定：状态 id 与 PlayerAnimationState 一致，片段名与新 Animator 控制器的状态名一致。</summary>
    private static AnimationStateBinding[] CreateDefaultBindings()
    {
        return new[]
        {
            Bind("Idle", "idle"),
            Bind("Walk", "walk"),
            Bind("Run", "run"),
            Bind("CrouchEnter", "crouchDown", false, 0.86f), // 旧控制器 exitTime 0.9 ≈ 归一化 0.86
            Bind("CrouchIdle", "crouchIdle"),
            Bind("JumpStart", "JumpStart"),
            Bind("JumpRise", "JumpRise"),
            Bind("JumpHighest", "JumpHighest"),
            Bind("JumpFall", "JumpFall"),
            Bind("Attack", "normalAttack", false),
            Bind("Dodge", "sprint", false),
            // 受伤播 hurt（带精灵帧序列的那段），不是 ksmHurt2 —— 后者只有颜色曲线，播出来只是变红。
            Bind("Hurt", "hurt", false),
            Bind("Dead", "ksmDie1", false),
        };
    }

    private static AnimationStateBinding Bind(string stateId, string playbackName, bool loop = true,
        float exitNormalizedTime = 1f)
    {
        return new AnimationStateBinding
        {
            stateId = stateId,
            playbackName = playbackName,
            loop = loop,
            exitNormalizedTime = exitNormalizedTime
        };
    }

    private void Awake()
    {
        if (animator == null)
            animator = GetComponent<Animator>();
        controller = GetComponent<PlayerController>();
        spriteRenderer = GetComponent<SpriteRenderer>();

        // 闪避特效根物体（FirstLevel 场景为 Player 下的 "Sprite Mask"）：sprint 片段通过它
        // 驱动子物体的激活与位移，它同时是遮罩窗口；向左闪避时按方向镜像它的位置。
        if (dodgeEffectRoot == null)
            dodgeEffectRoot = transform.Find(DodgeEffectRootName);
        if (dodgeEffectRoot != null)
        {
            dodgeEffectRootBasePosition = dodgeEffectRoot.localPosition;
            dodgeEffect = dodgeEffectRoot.Find(DodgeEffectName);
            if (dodgeEffect != null)
                dodgeEffectRenderer = dodgeEffect.GetComponent<SpriteRenderer>();
        }

        machine = new AnimationStateMachine(new AnimatorPlaybackBackend(animator), bindings,
            message => Debug.LogWarning(message, this));

        WarnIfLegacyController();
    }

    /// <summary>
    /// 旧 ksm.controller 里的状态名与本组件要播的名字大量重名（idle / walk / JumpStart …），
    /// 挂错控制器不会直接报错，只会被控制器内部的条件过渡抢走画面。参数数量是最可靠的区分：
    /// 新控制器纯片段、零参数；旧控制器有 Speed / isGround / Dodge 等 8 个参数。
    /// </summary>
    private void WarnIfLegacyController()
    {
        if (animator == null || animator.runtimeAnimatorController == null)
            return;

        if (animator.parameterCount > 0)
        {
            Debug.LogWarning(
                "PlayerAnimatorView: Animator 上挂的还是带参数的旧控制器（" +
                animator.runtimeAnimatorController.name + "）。动画已改为代码 FSM 直驱，请换成 ksm_fsm.controller。",
                this);
        }
    }

    private void OnEnable()
    {
        // 每次启用都从 Idle 重新起手：复活 / 场景重载后不残留上一次的动画状态。
        if (machine != null)
        {
            machine.Reset();
            currentState = PlayerAnimationState.Idle;
            stateStarted = false;
            attackRequested = false;
        }
    }

    private void Update()
    {
        if (machine == null || controller == null)
            return;

        machine.Tick(Time.deltaTime);

        PlayerAnimationState target = PlayerAnimationStateSelector.Select(BuildSnapshot());
        currentState = target;
        attackRequested = false; // 攻击请求只在本帧有效

        machine.ChangeState(target.ToString());
        stateStarted = true;
    }

    /// <summary>把 PlayerController 的只读状态拍成本帧快照。这里只读，不改任何业务状态。</summary>
    private PlayerAnimationSnapshot BuildSnapshot()
    {
        return new PlayerAnimationSnapshot
        {
            IsDead = controller.IsDead,
            IsHurt = controller.IsHurt,
            IsDodging = controller.IsDodging,
            AttackRequested = attackRequested,
            IsGrounded = controller.IsGrounded,
            VerticalVelocity = controller.velocityY,
            IsCrouching = controller.IsCrouching,
            IsRunning = controller.IsRunning,
            IsWalking = controller.IsWalking,
            PreviousState = currentState,

            // 首帧还没有播过任何状态，视为「上一个状态已播完」，否则 Idle 之类的状态会被误锁存。
            PreviousStateFinished = !stateStarted || machine.IsFinished
        };
    }

    // ───────────────────────────── 来自 PlayerController 的通知 ─────────────────────────────

    /// <summary>
    /// 通知「本次地面攻击已经开始」。只置一个本帧标记，不碰 Animator——
    /// 具体播什么由 Update 里的选择器决定（空中攻击不会调到这里，所以不会误播地面攻击片段）。
    /// </summary>
    public void NotifyAttackStarted()
    {
        attackRequested = true;
    }

    /// <summary>
    /// 通知「本次闪避已经开始」。闪避不需要额外状态：选择器读 PlayerController.IsDodging 就能得到 Dodge，
    /// 保留这个入口是为了让调用方的语义明确（将来要加闪避特效时序时不必再改 PlayerController）。
    /// </summary>
    public void NotifyDodgeStarted()
    {
        // 目前无需额外记录：Dodge 的锁存由「上一帧状态 + 片段是否播完」推导。
    }

    // ───────────────────────────── 表现层 ─────────────────────────────

    /// <summary>
    /// 按朝向翻转精灵。优先翻 SpriteRenderer，这样碰撞体的 X 偏移不会被一起镜像；
    /// 没有 SpriteRenderer 时退回缩放镜像。
    /// </summary>
    public void SetFacing(int direction)
    {
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

    /// <summary>
    /// 让闪避特效落在冲刺方向的后方：向右冲刺时特效根物体保持原位（特效与遮罩窗口在左后方）；
    /// 向左冲刺时把特效根物体按 X 镜像到玩家左侧（遮罩窗口随之镜像，特效才不会被窗口裁掉），
    /// 并用 SpriteRenderer.flipX 翻转特效贴图——不走 transform 负缩放，避免精灵被背面剔除。
    /// 特效根物体默认按 "Sprite Mask" 自动查找，也可以在 Inspector 中指定；
    /// 缺失时静默跳过（特效不显示但不影响冲刺本身）。
    /// 两次闪避之间特效处于未激活状态，镜像结果不可见，下一次闪避会按方向重新设置。
    /// </summary>
    public void SetDodgeDirection(int direction)
    {
        dodgeDirection = direction >= 0 ? 1 : -1;

        if (dodgeEffectRoot != null)
        {
            Vector3 rootPosition = dodgeEffectRootBasePosition;
            if (dodgeDirection < 0)
                rootPosition.x = -rootPosition.x;
            dodgeEffectRoot.localPosition = rootPosition;
        }

        if (dodgeEffectRenderer != null)
            dodgeEffectRenderer.flipX = dodgeDirection < 0;
    }

    /// <summary>
    /// 向左闪避时镜像特效子物体的水平位移。sprint 片段每帧都会把特效子物体的 LocalPosition
    /// 写回向左漂移的值，本方法在 Animator 写入之后（LateUpdate 晚于动画求值）把 X 取反，
    /// 特效便会出现在玩家右侧并向右漂移，与向右闪避时的左侧表现左右对称。
    /// 镜像条件取「冲刺中」与「特效可见」的并集：dodgeDuration 短于 sprint 片段，
    /// 冲刺结束后特效仍由片段驱动（m_IsActive 到片段末尾才归零），若只覆盖 IsDodging，
    /// 特效会在冲刺结束后跳回未镜像的一侧。
    /// </summary>
    private void LateUpdate()
    {
        if (dodgeEffect == null || dodgeDirection >= 0)
            return;

        bool dodging = controller != null && controller.IsDodging;
        if (!dodging && !dodgeEffect.gameObject.activeSelf)
            return;

        Vector3 localPosition = dodgeEffect.localPosition;
        localPosition.x = -localPosition.x;
        dodgeEffect.localPosition = localPosition;
    }
}
