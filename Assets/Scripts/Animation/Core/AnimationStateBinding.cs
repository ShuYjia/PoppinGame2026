using System;
using UnityEngine;

namespace Poppin.Animation
{
    /// <summary>
    /// 一条「动画状态 → 播放后端片段」的绑定，挂在组件上用 Inspector 配置。
    ///
    /// 为什么不用 ScriptableObject：绑定是某个角色 / 某个预制体的局部配置，
    /// 一个敌人一份、一个玩家一份，做成资产只会让引用链更难追。
    /// </summary>
    [Serializable]
    public class AnimationStateBinding
    {
        [Tooltip("状态 id：必须是角色动画枚举的名字（例如 PlayerAnimationState.Hurt → \"Hurt\"）。代码按它查表。")]
        public string stateId = "Idle";

        [Tooltip("播放后端认识的片段名：Player 填 Animator 状态名，敌人填 SpriteSequencePlayer 的 Clip.name。")]
        public string playbackName = "idle";

        [Tooltip("是否循环。非循环状态由播放进度判定结束。")]
        public bool loop = true;

        [Tooltip("非循环状态的退出点（归一化时间）。1 = 播到最后一帧；小于 1 时提前退出，用来对齐旧控制器的 exitTime。")]
        [Range(0.01f, 1f)]
        public float exitNormalizedTime = 1f;
    }
}
