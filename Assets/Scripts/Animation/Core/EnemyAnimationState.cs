namespace Poppin.Animation
{
    /// <summary>
    /// 敌人的动画状态。名字就是 <see cref="AnimationStateBinding.stateId"/>，
    /// 绑定表里写 "Walk" 才能命中 <see cref="EnemyAnimationState.Walk"/>。
    ///
    /// 敌人没有独立的空中 / 蹲下 / 闪避概念，所以状态比玩家少；具体敌人（野猪 / 远程野猪）
    /// 的业务状态（Alert / Charge / Recover …）由各自的行为 FSM 决定，再映射到这几个动画状态上。
    /// </summary>
    public enum EnemyAnimationState
    {
        Idle,
        Walk,
        Run,
        Attack,
        Hurt,
        Dead
    }
}
