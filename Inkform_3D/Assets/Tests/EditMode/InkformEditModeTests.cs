using NUnit.Framework;
using UnityEngine;
using Inkform.Core;
using Inkform.Data;
using Inkform.Gameplay;

namespace Inkform.Tests
{
    /// <summary>M1 核心纯逻辑的 EditMode 单元测试。</summary>
    public class InkformEditModeTests
    {
        [SetUp]
        public void Setup() => EventBus.Clear();

        [Test]
        public void EventBus_PublishReachesSubscriber_AndUnsubscribeStops()
        {
            int got = -1;
            void H(CheckpointReached e) => got = e.Id;

            EventBus.Subscribe<CheckpointReached>(H);
            EventBus.Publish(new CheckpointReached { Id = 7 });
            Assert.AreEqual(7, got);

            EventBus.Unsubscribe<CheckpointReached>(H);
            EventBus.Publish(new CheckpointReached { Id = 9 });
            Assert.AreEqual(7, got, "取消订阅后不应再收到事件");
        }

        [Test]
        public void InputLock_RefCount_LocksUntilBalanced_AndNeverGoesNegative()
        {
            var l = new InputLock();
            Assert.IsFalse(l.IsLocked);

            l.Acquire();
            l.Acquire();
            Assert.IsTrue(l.IsLocked);

            l.Release();
            Assert.IsTrue(l.IsLocked, "仍有一层未释放");

            l.Release();
            Assert.IsFalse(l.IsLocked);

            l.Release(); // 不应变负
            Assert.IsFalse(l.IsLocked);
            Assert.AreEqual(0, l.Count);
        }

        [Test]
        public void MovementProfile_Default_IsSane()
        {
            var p = MovementProfile.Default;
            Assert.AreEqual(1f, p.MoveSpeedMul);
            Assert.AreEqual(1f, p.MassMul);
            Assert.AreEqual(1f, p.JumpHeightMul);
            Assert.IsTrue(p.CanJump);
        }

        [Test]
        public void GameStateMachine_Set_ChangesStateAndFiresEvent()
        {
            var sm = new GameStateMachine();
            GameState? observed = null;
            sm.OnChanged += (_, next) => observed = next;

            sm.Set(GameState.Playing);

            Assert.AreEqual(GameState.Playing, sm.Current);
            Assert.AreEqual(GameState.Playing, observed);
        }

        [Test]
        public void GameStateMachine_SettingSameState_DoesNotFire()
        {
            var sm = new GameStateMachine();
            sm.Set(GameState.Playing);
            int fired = 0;
            sm.OnChanged += (_, __) => fired++;
            sm.Set(GameState.Playing);
            Assert.AreEqual(0, fired);
        }

        [Test]
        public void ScanField_NoCoverBetween_IsLethal()
        {
            // 空 cover mask ⇒ 遮挡 Raycast 不会命中任何东西 ⇒ 致命。
            LayerMask noCover = 0;
            bool lethal = ScanField.IsLethal(new Vector3(0f, 5f, 0f), Vector3.zero, noCover);
            Assert.IsTrue(lethal);
        }

        [Test]
        public void ScanField_ZeroDistance_IsLethal()
        {
            LayerMask noCover = 0;
            bool lethal = ScanField.IsLethal(Vector3.zero, Vector3.zero, noCover);
            Assert.IsTrue(lethal);
        }
    }
}
