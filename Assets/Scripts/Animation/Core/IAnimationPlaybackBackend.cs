namespace Poppin.Animation
{
    /// <summary>
    /// 动画播放后端：Animator（Player）与 SpriteSequencePlayer（敌人）都实现它。
    /// <see cref="AnimationStateMachine"/> 只通过这个接口说话，因此状态机本身不依赖任何一种播放方式。
    /// </summary>
    public interface IAnimationPlaybackBackend
    {
        /// <summary>后端是否认得这个片段名（用于「映射写错了」的显式报错）。</summary>
        bool Has(string playbackName);

        /// <summary>开始播放。restart = false 时后端可以自行决定是否忽略重复播放。</summary>
        void Play(string playbackName, bool loop, bool restart);

        /// <summary>当前片段已播放的归一化进度（0~1 起，非循环片段到 1 表示播完）。</summary>
        float GetNormalizedTime();

        /// <summary>非循环片段是否已经播到最后一帧。循环片段恒为 false。</summary>
        bool HasFinished();
    }
}
