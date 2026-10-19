using UnityEngine;

/// <summary>
/// 角色动画控制组件：把 PlayerController 算出的运动状态写入 Animator 参数，
/// 并处理纯表现的部分（精灵左右翻转、闪避特效镜像）。三类 Animator 参数的含义：
///
/// - Speed(Float)：水平速度绝对值，idle / walk / run 的切换条件（阈值 0.1 起走 / 6 起跑）。
/// - velocityY(Float)：竖直速度，空中状态「13」的 1D 混合树参数。
/// - isGround(Bool)：是否接地，false 时 AnyState 进空中混合树，true 时回 JumpGround → idle。
/// - Dodge(Trigger)：闪避，AnyState → sprint。
/// - Attack(Trigger)：普通攻击，AnyState → normalAttack（仅地面攻击触发，见 PlayerController）。
///
/// velocityY 与 isGround 必须每帧都写：漏写时混合树会停在默认值 velocityY = 0（最高点姿势），
/// 而 isGround 一直是 false 时角色永远不会从空中状态落回。
/// </summary>
public class PlayerAnimatorView : MonoBehaviour
{
    [Header("动画")]
    [Tooltip("角色的 Animator。留空时自动从本物体获取（FirstLevel 的 Player 已挂 ksm.controller）。")]
    [SerializeField]
    private Animator animator;

    [Header("闪避特效")]
    [Tooltip("闪避特效根物体（FirstLevel 场景中为 Player 下的 \"Sprite Mask\"）：sprint 片段通过它驱动子物体 \"111_export-export\" 的激活与位移，它同时是一个 SpriteMask 遮罩窗口（特效在窗口外可见）。向左闪避时会把它按 X 镜像到玩家左侧，配合特效贴图翻转与位置镜像，使特效整体镜像到冲刺方向的后方。留空时按 \"Sprite Mask\" 自动查找。")]
    [SerializeField]
    private Transform dodgeEffectRoot;

    /// <summary>Animator 的 Speed 参数哈希，避免每帧字符串查找。</summary>
    private static readonly int SpeedParam = Animator.StringToHash("Speed");

    /// <summary>竖直速度参数（空中混合树的 1D 混合参数）。</summary>
    private static readonly int VerticalVelocityParam = Animator.StringToHash("velocityY");

    /// <summary>接地状态参数（驱动 AnyState → 空中状态 / 空中状态 → JumpGround）。</summary>
    private static readonly int IsGroundParam = Animator.StringToHash("isGround");

    /// <summary>Animator 的 Dodge 触发器哈希（ksm.controller 中新增的参数）。</summary>
    private static readonly int DodgeParam = Animator.StringToHash("Dodge");

    /// <summary>Animator 的 Attack 触发器哈希（ksm.controller 中新增的参数）。</summary>
    private static readonly int AttackParam = Animator.StringToHash("Attack");

    private const string DodgeEffectRootName = "Sprite Mask";    // 闪避特效根物体（SpriteMask 遮罩窗口 + 特效之父）
    private const string DodgeEffectName = "111_export-export"; // 闪避特效子物体（sprint 片段驱动其激活与位移）

    private PlayerController controller;
    private SpriteRenderer spriteRenderer;

    private Transform dodgeEffect;
    private SpriteRenderer dodgeEffectRenderer;
    private Vector3 dodgeEffectRootBasePosition;
    private int dodgeDirection = 1;

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
    }

    // ───────────────────────────── 动画参数 ─────────────────────────────

    /// <summary>
    /// 写入本帧的动画参数。
    /// </summary>
    /// <param name="horizontalVelocity">带符号的水平速度，写入 Speed（取绝对值）。</param>
    /// <param name="verticalVelocity">带符号的竖直速度，写入 velocityY（空中混合树）。</param>
    /// <param name="grounded">是否接地，写入 isGround。</param>
    /// <param name="driveLocomotion">
    /// 是否刷新 Speed：接地且未蹲下 / 未下潜 / 未闪避时为 true；
    /// 为 false 时保持上一帧的 Speed，让 idle / walk / run 维持在当前档位。
    /// </param>
    public void ApplyMotion(float horizontalVelocity, float verticalVelocity, bool grounded, bool driveLocomotion)
    {
        if (animator == null)
            return;

        animator.SetFloat(VerticalVelocityParam, verticalVelocity);
        animator.SetBool(IsGroundParam, grounded);

        if (!driveLocomotion)
            return;

        animator.SetFloat(SpeedParam, Mathf.Abs(horizontalVelocity));
    }

    /// <summary>触发一次闪避（ksm.controller 的 AnyState → sprint）。</summary>
    public void TriggerDodge()
    {
        if (animator != null)
            animator.SetTrigger(DodgeParam);
    }

    /// <summary>触发一次普通攻击（ksm.controller 的 AnyState → normalAttack）。</summary>
    public void TriggerAttack()
    {
        if (animator != null)
            animator.SetTrigger(AttackParam);
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
