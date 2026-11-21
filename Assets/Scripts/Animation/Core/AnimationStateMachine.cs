using System;
using System.Collections.Generic;
using UnityEngine;

namespace Poppin.Animation
{
    /// <summary>
    /// 通用动画有限状态机（纯 C#，不含 MonoBehaviour）：只负责「当前播放哪个状态」，不负责业务规则。
    ///
    /// 设计边界：
    /// - 状态由调用方的业务代码（行为 FSM / PlayerController 的只读状态）算好后传进来，本类不会反向修改移动、战斗或血量。
    /// - 播放交给 <see cref="IAnimationPlaybackBackend"/>（Animator 或 SpriteSequencePlayer），本类不认识它们的具体类型。
    /// - 只管三件事：不重复播放同一状态、切换时重置进度、非循环状态按播放进度判定结束。
    /// </summary>
    public sealed class AnimationStateMachine
    {
        /// <summary>
        /// 完成判定的容差。取 1e-3 而不是更小的值：归一化时间是 float，
        /// `1f - 0.0001f` 实际比 `0.9999f` 略大，用 1e-4 会让「差一点点就满」的帧判不出来。
        /// </summary>
        private const float CompletionEpsilon = 0.001f;

        private readonly IAnimationPlaybackBackend backend;
        private readonly Dictionary<string, AnimationStateBinding> bindings =
            new Dictionary<string, AnimationStateBinding>(StringComparer.Ordinal);

        /// <summary>日志出口。运行时用 Debug.LogWarning，EditMode 测试里注入收集器断言「有明确错误」。</summary>
        private readonly Action<string> logWarning;

        /// <summary>同一个错误只报一次：动画状态每帧都会被请求，重复刷日志会把 Console 淹掉。</summary>
        private readonly HashSet<string> reported = new HashSet<string>(StringComparer.Ordinal);

        public AnimationStateMachine(IAnimationPlaybackBackend backend)
            : this(backend, null, null)
        {
        }

        public AnimationStateMachine(IAnimationPlaybackBackend backend, IEnumerable<AnimationStateBinding> bindings,
            Action<string> logWarning = null)
        {
            this.backend = backend;
            this.logWarning = logWarning ?? (message => Debug.LogWarning(message));
            SetBindings(bindings);
        }

        /// <summary>当前状态 id（还没播过任何状态时为 null）。</summary>
        public string CurrentStateId { get; private set; }

        /// <summary>上一个状态 id（首次切换前为 null）。</summary>
        public string PreviousStateId { get; private set; }

        /// <summary>当前状态已经持续的时间（秒），仅用于调试与兜底判断。</summary>
        public float CurrentStateTime { get; private set; }

        /// <summary>当前状态的绑定（没有则为 null）。</summary>
        public AnimationStateBinding CurrentBinding { get; private set; }

        /// <summary>是否还有状态在播。</summary>
        public bool HasState => !string.IsNullOrEmpty(CurrentStateId);

        /// <summary>当前状态是否已经播完：循环状态恒为 false，非循环状态看播放进度。</summary>
        public bool IsFinished
        {
            get
            {
                if (CurrentBinding == null || CurrentBinding.loop || backend == null)
                    return false;

                if (backend.HasFinished())
                    return true;

                return backend.GetNormalizedTime() >= CurrentBinding.exitNormalizedTime - CompletionEpsilon;
            }
        }

        /// <summary>替换绑定表（Inspector 改了配置后重新喂进来）。不影响当前正在播的状态。</summary>
        public void SetBindings(IEnumerable<AnimationStateBinding> source)
        {
            bindings.Clear();
            reported.Clear();
            if (source == null)
                return;

            foreach (AnimationStateBinding binding in source)
            {
                if (binding == null || string.IsNullOrEmpty(binding.stateId))
                    continue;

                // 重复的 stateId 保留第一条并报错：静默覆盖会让 Inspector 里明明配了两条却只有一条生效。
                if (bindings.ContainsKey(binding.stateId))
                {
                    Warn($"动画绑定里 stateId=\"{binding.stateId}\" 重复，只保留第一条。");
                    continue;
                }

                bindings.Add(binding.stateId, binding);
            }
        }

        public bool TryGetBinding(string stateId, out AnimationStateBinding binding)
        {
            binding = null;
            return !string.IsNullOrEmpty(stateId) && bindings.TryGetValue(stateId, out binding);
        }

        public bool IsInState(string stateId)
        {
            return !string.IsNullOrEmpty(stateId) && string.Equals(CurrentStateId, stateId, StringComparison.Ordinal);
        }

        /// <summary>
        /// 请求切到 stateId。
        /// - 已经是该状态且 restart = false：返回 false，不重播（避免每帧请求把循环动画卡在第 0 帧）。
        /// - 状态不存在 / 片段缺失 / 后端不认这个片段：报错并保持当前状态，返回 false。
        /// - 其余情况执行切换并返回 true。
        /// </summary>
        public bool ChangeState(string stateId, bool restart = false)
        {
            if (string.IsNullOrEmpty(stateId))
            {
                Warn("ChangeState 收到空的 stateId，保持当前状态。");
                return false;
            }

            if (!bindings.TryGetValue(stateId, out AnimationStateBinding binding))
            {
                if (reported.Add("missing-state:" + stateId))
                    Warn($"动画状态 \"{stateId}\" 没有配置绑定（检查组件上的 bindings 列表），保持当前状态。");
                return false;
            }

            if (string.IsNullOrEmpty(binding.playbackName))
            {
                if (reported.Add("missing-playback:" + stateId))
                    Warn($"动画状态 \"{stateId}\" 的 playbackName 为空，保持当前状态。");
                return false;
            }

            if (backend == null)
            {
                Warn("ChangeState 没有播放后端，保持当前状态。");
                return false;
            }

            if (!backend.Has(binding.playbackName))
            {
                if (reported.Add("unknown-clip:" + binding.playbackName))
                    Warn($"播放后端里找不到片段 \"{binding.playbackName}\"（状态 {stateId}），保持当前状态。");
                return false;
            }

            if (!restart && IsInState(stateId))
                return false;

            if (restart && IsInState(stateId))
            {
                // 同一状态重播：不回退 PreviousStateId（上一次的也是自己，回退没有意义）。
                CurrentStateTime = 0f;
                backend.Play(binding.playbackName, binding.loop, true);
                return true;
            }

            PreviousStateId = CurrentStateId;
            CurrentStateId = stateId;
            CurrentBinding = binding;
            CurrentStateTime = 0f;
            backend.Play(binding.playbackName, binding.loop, true);
            return true;
        }

        /// <summary>推进计时。播放进度以播放后端为准，这里只维护「本状态持续了多久」。</summary>
        public void Tick(float deltaTime)
        {
            if (!HasState)
                return;

            if (deltaTime > 0f)
                CurrentStateTime += deltaTime;
        }

        /// <summary>回到「没有状态」的初始态（对象池复用 / 复活重播时用），不触发任何播放。</summary>
        public void Reset()
        {
            CurrentStateId = null;
            PreviousStateId = null;
            CurrentBinding = null;
            CurrentStateTime = 0f;
            reported.Clear();
        }

        private void Warn(string message)
        {
            logWarning?.Invoke("[AnimationStateMachine] " + message);
        }
    }
}
