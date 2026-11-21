using NUnit.Framework;
using Poppin.Animation;

namespace Poppin.Animation.Tests
{
    /// <summary>
    /// <see cref="PlayerAnimationStateSelector"/> 的优先级与锁存规则：
    /// Dead &gt; Hurt &gt; Dodge &gt; Attack &gt; Airborne &gt; Crouch &gt; Run &gt; Walk &gt; Idle。
    /// </summary>
    public class PlayerAnimationStateSelectorTests
    {
        /// <summary>一个「站在地上、不动、活着」的基线快照，各用例只改自己关心的字段。</summary>
        private static PlayerAnimationSnapshot Grounded()
        {
            return new PlayerAnimationSnapshot
            {
                IsDead = false,
                IsHurt = false,
                IsDodging = false,
                AttackRequested = false,
                IsGrounded = true,
                VerticalVelocity = 0f,
                IsCrouching = false,
                IsRunning = false,
                IsWalking = false,
                PreviousState = PlayerAnimationState.Idle,
                PreviousStateFinished = true,
            };
        }

        [Test]
        public void 站着不动是Idle()
        {
            Assert.AreEqual(PlayerAnimationState.Idle, PlayerAnimationStateSelector.Select(Grounded()));
        }

        [Test]
        public void 走路与奔跑分别对应Walk与Run()
        {
            PlayerAnimationSnapshot s = Grounded();
            s.IsWalking = true;
            Assert.AreEqual(PlayerAnimationState.Walk, PlayerAnimationStateSelector.Select(s));

            PlayerAnimationSnapshot r = Grounded();
            r.IsWalking = true;
            r.IsRunning = true;
            Assert.AreEqual(PlayerAnimationState.Run, PlayerAnimationStateSelector.Select(r), "同时成立时跑步优先");
        }

        [Test]
        public void 死亡覆盖其他所有状态()
        {
            PlayerAnimationSnapshot s = Grounded();
            s.IsDead = true;
            s.IsHurt = true;
            s.IsDodging = true;
            s.AttackRequested = true;

            Assert.AreEqual(PlayerAnimationState.Dead, PlayerAnimationStateSelector.Select(s));
        }

        [Test]
        public void 受伤优先于闪避与攻击()
        {
            PlayerAnimationSnapshot s = Grounded();
            s.IsHurt = true;
            s.IsDodging = true;
            s.AttackRequested = true;

            Assert.AreEqual(PlayerAnimationState.Hurt, PlayerAnimationStateSelector.Select(s));
        }

        [Test]
        public void 闪避优先于攻击()
        {
            PlayerAnimationSnapshot s = Grounded();
            s.IsDodging = true;
            s.AttackRequested = true;

            Assert.AreEqual(PlayerAnimationState.Dodge, PlayerAnimationStateSelector.Select(s));
        }

        [Test]
        public void 闪避结束后片段没播完仍停在Dodge()
        {
            PlayerAnimationSnapshot s = Grounded();
            s.IsDodging = false;
            s.PreviousState = PlayerAnimationState.Dodge;
            s.PreviousStateFinished = false;
            s.IsWalking = true;

            Assert.AreEqual(PlayerAnimationState.Dodge, PlayerAnimationStateSelector.Select(s));

            s.PreviousStateFinished = true;
            Assert.AreEqual(PlayerAnimationState.Walk, PlayerAnimationStateSelector.Select(s));
        }

        [Test]
        public void 攻击播放期间不会被走路打断()
        {
            PlayerAnimationSnapshot s = Grounded();
            s.PreviousState = PlayerAnimationState.Attack;
            s.PreviousStateFinished = false;
            s.IsWalking = true;

            Assert.AreEqual(PlayerAnimationState.Attack, PlayerAnimationStateSelector.Select(s));

            s.PreviousStateFinished = true;
            Assert.AreEqual(PlayerAnimationState.Walk, PlayerAnimationStateSelector.Select(s));
        }

        [Test]
        public void 空中攻击请求不会切到地面攻击动画()
        {
            PlayerAnimationSnapshot s = Grounded();
            s.IsGrounded = false;
            s.VerticalVelocity = 2f;
            s.AttackRequested = false;

            Assert.AreEqual(PlayerAnimationState.JumpHighest, PlayerAnimationStateSelector.Select(s));
        }

        [Test]
        public void 空中四段按竖直速度分档()
        {
            PlayerAnimationSnapshot s = Grounded();
            s.IsGrounded = false;

            s.VerticalVelocity = 14f;
            Assert.AreEqual(PlayerAnimationState.JumpStart, PlayerAnimationStateSelector.Select(s));

            s.VerticalVelocity = 7f;
            Assert.AreEqual(PlayerAnimationState.JumpRise, PlayerAnimationStateSelector.Select(s));

            s.VerticalVelocity = 0f;
            Assert.AreEqual(PlayerAnimationState.JumpHighest, PlayerAnimationStateSelector.Select(s));

            s.VerticalVelocity = -8f;
            Assert.AreEqual(PlayerAnimationState.JumpFall, PlayerAnimationStateSelector.Select(s));
        }

        [Test]
        public void 空中状态优先于蹲下()
        {
            PlayerAnimationSnapshot s = Grounded();
            s.IsGrounded = false;
            s.VerticalVelocity = -5f;
            s.IsCrouching = true;

            Assert.AreEqual(PlayerAnimationState.JumpFall, PlayerAnimationStateSelector.Select(s));
        }

        [Test]
        public void 蹲下先播进入再进蹲姿循环()
        {
            PlayerAnimationSnapshot s = Grounded();
            s.IsCrouching = true;
            s.PreviousState = PlayerAnimationState.Idle;

            Assert.AreEqual(PlayerAnimationState.CrouchEnter, PlayerAnimationStateSelector.Select(s), "刚蹲下应该先进 CrouchEnter");

            s.PreviousState = PlayerAnimationState.CrouchEnter;
            s.PreviousStateFinished = false;
            Assert.AreEqual(PlayerAnimationState.CrouchEnter, PlayerAnimationStateSelector.Select(s));

            s.PreviousStateFinished = true;
            Assert.AreEqual(PlayerAnimationState.CrouchIdle, PlayerAnimationStateSelector.Select(s));

            s.PreviousState = PlayerAnimationState.CrouchIdle;
            Assert.AreEqual(PlayerAnimationState.CrouchIdle, PlayerAnimationStateSelector.Select(s), "保持蹲下不应重复播进入动画");
        }

        [Test]
        public void 蹲下优先于走路与跑步()
        {
            PlayerAnimationSnapshot s = Grounded();
            s.IsCrouching = true;
            s.IsWalking = true;
            s.IsRunning = true;
            s.PreviousState = PlayerAnimationState.CrouchIdle;

            Assert.AreEqual(PlayerAnimationState.CrouchIdle, PlayerAnimationStateSelector.Select(s));
        }

        [Test]
        public void 地面攻击请求进入Attack()
        {
            PlayerAnimationSnapshot s = Grounded();
            s.AttackRequested = true;

            Assert.AreEqual(PlayerAnimationState.Attack, PlayerAnimationStateSelector.Select(s));
        }
    }
}
