using UnityEngine;

/// <summary>
/// 受伤状态的回调脚本（挂在 ksm.controller 里 HurtLayer 的受伤状态上）：
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
