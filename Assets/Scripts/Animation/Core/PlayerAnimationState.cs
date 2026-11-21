namespace Poppin.Animation
{
    /// <summary>
    /// 玩家的动画状态。名字就是 <see cref="AnimationStateBinding.stateId"/>，
    /// 绑定表里写 "Hurt" 才能命中 <see cref="PlayerAnimationState.Hurt"/>。
    /// </summary>
    public enum PlayerAnimationState
    {
        Idle,
        Walk,
        Run,
        CrouchEnter,
        CrouchIdle,
        JumpStart,
        JumpRise,
        JumpHighest,
        JumpFall,
        Attack,
        Dodge,
        Hurt,
        Dead
    }

    /// <summary>
    /// 某一帧决定「该播哪个动画」所需的全部输入。全部是只读快照：
    /// 选择器不查组件、不改状态，只根据这些值和上一帧的状态算出目标状态，因此可以直接做单元测试。
    /// </summary>
    public struct PlayerAnimationSnapshot
    {
        public bool IsDead;
        public bool IsHurt;
        public bool IsDodging;

        /// <summary>本帧有「地面普通攻击」请求（由 PlayerController 通知，空中攻击不置位）。</summary>
        public bool AttackRequested;

        public bool IsGrounded;

        /// <summary>带符号的竖直速度：向上为正。</summary>
        public float VerticalVelocity;

        public bool IsCrouching;
        public bool IsRunning;
        public bool IsWalking;

        /// <summary>上一帧的动画状态，用于非循环状态「播完之前不被打断」的锁存。</summary>
        public PlayerAnimationState PreviousState;

        /// <summary>上一帧的动画状态是否已经播完（循环状态恒为 false）。</summary>
        public bool PreviousStateFinished;
    }

    /// <summary>
    /// 玩家动画状态选择器：把「运动状态」翻译成「动画状态」。
    ///
    /// 优先级固定为：
    /// Dead &gt; Hurt &gt; Dodge &gt; Attack &gt; Airborne &gt; Crouch &gt; Run &gt; Walk &gt; Idle。
    /// 同一帧多个条件同时成立时，取优先级最高的那个。
    /// </summary>
    public static class PlayerAnimationStateSelector
    {
        /// <summary>
        /// 空中四段的切换点。旧 ksm.controller 的 1D 混合树按 velocityY 混合
        /// （JumpFall = -3 / JumpHighest = 1 / JumpRise = 9 / JumpStart = 13），
        /// 这里取相邻阈值的中间值当离散切换点，使每个速度区间的「主导片段」和混合树一致。
        /// </summary>
        public const float JumpStartVelocity = 11f;

        public const float JumpRiseVelocity = 5f;

        public const float JumpFallVelocity = -1f;

        public static PlayerAnimationState Select(PlayerAnimationSnapshot s)
        {
            // 1. 死亡最高：死亡动画播完也不会被别的状态顶掉（复活由 IsDead 变 false 自然解除）。
            if (s.IsDead)
                return PlayerAnimationState.Dead;

            // 2. 受伤：整段硬直期间都停在 Hurt，动画播完就停在最后一帧，等硬直结束。
            if (s.IsHurt)
                return PlayerAnimationState.Hurt;

            // 3. 闪避：IsDodging 只覆盖冲刺时长（0.2s），sprint 片段更长（0.28s），
            //    所以「上一帧是 Dodge 且片段没播完」也要继续锁在闪避动画上。
            if (s.IsDodging || IsLatched(s, PlayerAnimationState.Dodge))
                return PlayerAnimationState.Dodge;

            // 4. 攻击：窗口刚开始，或攻击动画还没播完（一次攻击只播一遍，不重复排队）。
            if (s.AttackRequested || IsLatched(s, PlayerAnimationState.Attack))
                return PlayerAnimationState.Attack;

            // 5. 空中四段：只按竖直速度分档，不再依赖 Animator 的混合树。
            if (!s.IsGrounded)
            {
                if (s.VerticalVelocity >= JumpStartVelocity)
                    return PlayerAnimationState.JumpStart;
                if (s.VerticalVelocity >= JumpRiseVelocity)
                    return PlayerAnimationState.JumpRise;
                if (s.VerticalVelocity >= JumpFallVelocity)
                    return PlayerAnimationState.JumpHighest;
                return PlayerAnimationState.JumpFall;
            }

            // 6. 蹲下：第一次进入播 crouchDown（CrouchEnter），播完转蹲姿循环（CrouchIdle）。
            if (s.IsCrouching)
            {
                if (s.PreviousState == PlayerAnimationState.CrouchEnter)
                    return s.PreviousStateFinished ? PlayerAnimationState.CrouchIdle : PlayerAnimationState.CrouchEnter;

                if (s.PreviousState == PlayerAnimationState.CrouchIdle)
                    return PlayerAnimationState.CrouchIdle;

                return PlayerAnimationState.CrouchEnter;
            }

            // 7. 地面移动三档。
            if (s.IsRunning)
                return PlayerAnimationState.Run;
            if (s.IsWalking)
                return PlayerAnimationState.Walk;
            return PlayerAnimationState.Idle;
        }

        /// <summary>上一帧就是 state 且它的非循环片段还没播完 → 继续锁在这个状态上。</summary>
        private static bool IsLatched(PlayerAnimationSnapshot s, PlayerAnimationState state)
        {
            return s.PreviousState == state && !s.PreviousStateFinished;
        }
    }
}
