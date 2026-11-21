using UnityEngine;

/// <summary>
/// 【已废弃 — 2026-11 动画 FSM 重构后不再使用】
///
/// 玩家动画已改为代码有限状态机（PlayerAnimatorView + Poppin.Animation.AnimationStateMachine），
/// 受伤硬直只由 PlayerController 的 hurtDuration 计时结束，动画不再参与解除硬直。
/// 新控制器 ksm_fsm.controller 里没有任何 StateMachineBehaviour，本脚本不会被调用。
///
/// 保留原因：旧的 ksm.controller 作为备份仍在仓库里（Demo.unity 还引用它），
/// 直接删除本文件会让那份控制器出现缺失脚本引用。等旧控制器正式下线时，连同本文件与 .meta 一起删除。
///
/// 历史说明（原始实现）：
/// 只负责一件纯表现的事——**受伤动画播完的瞬间解除玩家的受伤硬直**。
///
/// 为什么放在动画里而不是用固定计时器：硬直时长本来就该等于动画实际播完的时间（含退出过渡），
/// 由动画自己说话就不会出现「人还在抽搐却能跑了」或「动画早播完了人还僵着」的错位。
/// PlayerController 里另有一条 hurtDuration 兜底计时，防止本脚本没挂上 / 动画缺失时把玩家永久锁死。
///
/// 分工：<see cref="PlayerController.GetHurt"/> 负责「进入硬直」（锁操作、击退），
/// 本脚本负责「退出硬直」，两者通过 <see cref="PlayerController.EndHurt"/> 汇合。
/// </summary>
public class HurtAnimation : StateMachineBehaviour
{
    /// <summary>
    /// 受伤状态结束（动画播完、开始退回默认空状态）时调用：解除玩家硬直，恢复操作。
    /// 注意这里是「退出」，所以调的是 EndHurt 而不是把 IsHurt 置真——
    /// 置真会把玩家永久锁死（IsHurt 是 private set，外部也改不了，只能走 EndHurt）。
    /// 取不到 PlayerController（挂在别的 Animator 上）时静默跳过，不报错。
    /// </summary>
    public override void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        if (animator == null)
            return;

        PlayerController controller = animator.GetComponent<PlayerController>();
        if (controller == null)
            return;

        controller.EndHurt();
    }
}
