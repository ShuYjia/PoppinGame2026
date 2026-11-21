using System;
using System.Linq;
using NUnit.Framework;
using Poppin.Animation;

namespace Poppin.Animation.Tests
{
    /// <summary>
    /// 绑定名契约：<see cref="AnimationStateBinding.stateId"/> 用的是枚举名，
    /// 组件和预制体里的字符串写错时不会报编译错误，所以在这里钉死。
    /// </summary>
    public class AnimationStateIdContractTests
    {
        [Test]
        public void 玩家状态id覆盖全部枚举值且名字稳定()
        {
            string[] names = Enum.GetNames(typeof(PlayerAnimationState));

            CollectionAssert.AreEquivalent(new[]
            {
                "Idle", "Walk", "Run", "CrouchEnter", "CrouchIdle",
                "JumpStart", "JumpRise", "JumpHighest", "JumpFall",
                "Attack", "Dodge", "Hurt", "Dead"
            }, names);
        }

        [Test]
        public void 敌人状态id覆盖全部枚举值且名字稳定()
        {
            string[] names = Enum.GetNames(typeof(EnemyAnimationState));

            CollectionAssert.AreEquivalent(new[] { "Idle", "Walk", "Run", "Attack", "Hurt", "Dead" }, names);
        }

        [Test]
        public void 全部枚举名字都能被绑定表命中()
        {
            var bindings = Enum.GetNames(typeof(PlayerAnimationState))
                .Select(n => new AnimationStateBinding { stateId = n, playbackName = n })
                .ToArray();

            var machine = new AnimationStateMachine(null, bindings, _ => { });

            foreach (string name in bindings.Select(b => b.stateId))
                Assert.IsTrue(machine.TryGetBinding(name, out AnimationStateBinding binding), name);

            // 枚举名与大小写必须严格一致：绑定表用 Ordinal 比较，写小写不会命中。
            Assert.IsFalse(machine.TryGetBinding("hurt", out _));
        }
    }
}
