using System.Collections.Generic;
using NUnit.Framework;
using Poppin.Animation;

namespace Poppin.Animation.Tests
{
    /// <summary>
    /// <see cref="AnimationStateMachine"/> 的核心行为：不重复重播、restart、非循环完成判定、
    /// 映射缺失时的显式报错与回退。用假后端跑，不依赖 Unity 场景与 Animator。
    /// </summary>
    public class AnimationStateMachineTests
    {
        /// <summary>只记账的播放后端：记录调用了哪些片段、当前进度与是否播完。</summary>
        private sealed class FakeBackend : IAnimationPlaybackBackend
        {
            public readonly List<string> played = new List<string>();
            public readonly HashSet<string> clips = new HashSet<string> { "idle", "walk", "attack", "hurt" };
            public float normalized;
            public bool finished;

            public bool Has(string playbackName)
            {
                return clips.Contains(playbackName);
            }

            public void Play(string playbackName, bool loop, bool restart)
            {
                played.Add(playbackName);
                if (restart)
                {
                    normalized = 0f;
                    finished = false;
                }
            }

            public float GetNormalizedTime()
            {
                return normalized;
            }

            public bool HasFinished()
            {
                return finished;
            }
        }

        private readonly List<string> warnings = new List<string>();
        private FakeBackend backend;
        private AnimationStateMachine machine;

        [SetUp]
        public void SetUp()
        {
            warnings.Clear();
            backend = new FakeBackend();
            machine = new AnimationStateMachine(backend, new[]
            {
                new AnimationStateBinding { stateId = "Idle", playbackName = "idle", loop = true },
                new AnimationStateBinding { stateId = "Walk", playbackName = "walk", loop = true },
                new AnimationStateBinding { stateId = "Attack", playbackName = "attack", loop = false },
                new AnimationStateBinding { stateId = "Hurt", playbackName = "hurt", loop = false, exitNormalizedTime = 0.5f },
            }, warnings.Add);
        }

        [Test]
        public void 初始没有状态且切换到Idle后当前状态正确()
        {
            Assert.IsNull(machine.CurrentStateId);
            Assert.IsFalse(machine.HasState);

            Assert.IsTrue(machine.ChangeState("Idle"));

            Assert.AreEqual("Idle", machine.CurrentStateId);
            Assert.IsNull(machine.PreviousStateId);
            Assert.IsTrue(machine.IsInState("Idle"));
            Assert.IsFalse(machine.IsInState("Walk"));
            Assert.AreEqual(1, backend.played.Count);
            Assert.AreEqual("idle", backend.played[0]);
        }

        [Test]
        public void 同一状态重复请求不会重播()
        {
            machine.ChangeState("Idle");
            machine.ChangeState("Idle");
            machine.ChangeState("Idle");

            Assert.AreEqual(1, backend.played.Count, "同一状态重复请求不应该再次调用后端播放");
            Assert.AreEqual("Idle", machine.CurrentStateId);
        }

        [Test]
        public void restart为真时允许同一状态重新开始()
        {
            machine.ChangeState("Idle");
            machine.Tick(0.5f);

            Assert.IsTrue(machine.ChangeState("Idle", true));

            Assert.AreEqual(2, backend.played.Count);
            Assert.AreEqual(0f, machine.CurrentStateTime);
            Assert.AreEqual("Idle", machine.CurrentStateId);
        }

        [Test]
        public void 切换状态会记录上一个状态()
        {
            machine.ChangeState("Idle");
            machine.ChangeState("Walk");

            Assert.AreEqual("Walk", machine.CurrentStateId);
            Assert.AreEqual("Idle", machine.PreviousStateId);
            Assert.AreEqual(2, backend.played.Count);
            Assert.AreEqual("walk", backend.played[1]);
        }

        [Test]
        public void 循环状态永远不算播完()
        {
            machine.ChangeState("Walk");
            backend.normalized = 5f;
            backend.finished = true;

            Assert.IsFalse(machine.IsFinished);
        }

        [Test]
        public void 非循环状态播到末尾算播完()
        {
            machine.ChangeState("Attack");
            Assert.IsFalse(machine.IsFinished);

            backend.normalized = 0.9995f;
            Assert.IsTrue(machine.IsFinished);
        }

        [Test]
        public void 非循环状态按exitNormalizedTime提前判定完成()
        {
            machine.ChangeState("Hurt");
            backend.normalized = 0.4f;
            Assert.IsFalse(machine.IsFinished, "exitNormalizedTime = 0.5，播到 0.4 还不该退出");

            backend.normalized = 0.5f;
            Assert.IsTrue(machine.IsFinished);
        }

        [Test]
        public void 后端报告播完时直接算完成()
        {
            machine.ChangeState("Attack");
            backend.finished = true;
            Assert.IsTrue(machine.IsFinished);
        }

        [Test]
        public void 未配置的状态会报错并保持当前状态()
        {
            machine.ChangeState("Idle");

            Assert.IsFalse(machine.ChangeState("Backflip"));
            Assert.AreEqual("Idle", machine.CurrentStateId);
            Assert.AreEqual(1, warnings.Count);
            StringAssert.Contains("Backflip", warnings[0]);

            // 同一个错误不重复刷日志。
            machine.ChangeState("Backflip");
            Assert.AreEqual(1, warnings.Count);
        }

        [Test]
        public void 后端找不到片段时回退到当前状态并报错()
        {
            machine.ChangeState("Idle");
            machine.SetBindings(new[]
            {
                new AnimationStateBinding { stateId = "Idle", playbackName = "idle" },
                new AnimationStateBinding { stateId = "Ghost", playbackName = "不存在的片段" },
            });
            machine.ChangeState("Idle");

            Assert.IsFalse(machine.ChangeState("Ghost"));
            Assert.AreEqual("Idle", machine.CurrentStateId);
            Assert.AreEqual(1, backend.played.Count, "片段缺失时不应该调用后端播放");
            Assert.IsTrue(warnings.Exists(w => w.Contains("不存在的片段")));
        }

        [Test]
        public void 重复的stateId只保留第一条并报错()
        {
            var local = new AnimationStateMachine(backend, new[]
            {
                new AnimationStateBinding { stateId = "Idle", playbackName = "idle" },
                new AnimationStateBinding { stateId = "Idle", playbackName = "walk" },
            }, warnings.Add);

            local.ChangeState("Idle");

            Assert.AreEqual("idle", backend.played[0]);
            Assert.IsTrue(warnings.Exists(w => w.Contains("重复")));
        }

        [Test]
        public void 空状态id不会崩也不会改状态()
        {
            machine.ChangeState("Idle");

            Assert.IsFalse(machine.ChangeState(null));
            Assert.IsFalse(machine.ChangeState(string.Empty));
            Assert.AreEqual("Idle", machine.CurrentStateId);
        }

        [Test]
        public void Reset回到未初始化状态()
        {
            machine.ChangeState("Idle");
            machine.Reset();

            Assert.IsNull(machine.CurrentStateId);
            Assert.IsNull(machine.PreviousStateId);
            Assert.IsNull(machine.CurrentBinding);
        }
    }
}
