using UnityEngine;

namespace Poppin.Animation
{
    /// <summary>
    /// Animator 播放适配：只负责「按状态名播片段 + 回报播放进度」，不参与任何业务判断。
    ///
    /// - 状态名转哈希后缓存，避免每帧做字符串查找。
    /// - 一律用 <see cref="Animator.Play(int, int, float)"/> 硬切到第 0 帧：本项目的动画过渡全是
    ///   duration = 0，不需要 CrossFade，也不依赖 Animator 里的 AnyState 过渡。
    /// - 不再使用 SetTrigger / SetBool / SetFloat —— 控制器里也没有这些参数了。
    /// </summary>
    public sealed class AnimatorPlaybackBackend : IAnimationPlaybackBackend
    {
        private readonly Animator animator;
        private readonly int layer;

        private int currentHash = int.MinValue;

        /// <summary>调用 Play 的那一帧：同一帧里 Animator 还没求值，读到的仍是旧状态，必须挡掉。</summary>
        private int playedFrame = -1;

        public AnimatorPlaybackBackend(Animator animator, int layer = 0)
        {
            this.animator = animator;
            this.layer = layer;
        }

        public bool Has(string playbackName)
        {
            if (animator == null || animator.runtimeAnimatorController == null || string.IsNullOrEmpty(playbackName))
                return false;

            return animator.HasState(layer, Animator.StringToHash(playbackName));
        }

        public void Play(string playbackName, bool loop, bool restart)
        {
            if (animator == null || string.IsNullOrEmpty(playbackName))
                return;

            int hash = Animator.StringToHash(playbackName);
            if (!restart && hash == currentHash)
                return;

            currentHash = hash;
            playedFrame = Time.frameCount;
            animator.Play(hash, layer, 0f);
        }

        public float GetNormalizedTime()
        {
            if (animator == null || Time.frameCount <= playedFrame)
                return 0f;

            return animator.GetCurrentAnimatorStateInfo(layer).normalizedTime;
        }

        public bool HasFinished()
        {
            if (animator == null || Time.frameCount <= playedFrame)
                return false;

            AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(layer);
            return !info.loop && info.normalizedTime >= 1f;
        }
    }
}
