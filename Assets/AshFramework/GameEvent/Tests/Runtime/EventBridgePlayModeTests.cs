using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AshFramework.Events.Tests
{
    public sealed class EventBridgeSubscriptionOwner : MonoBehaviour
    {
        public readonly GameEventMgr Events = new GameEventMgr();
        private void OnDestroy() => Events.Clear();
    }

    public sealed class EventBridgePlayModeTests
    {
        [SetUp]
        public void SetUp() { GameEvent.Shutdown(); GameEventHelper.Init(); }

        [TearDown]
        public void TearDown() => GameEvent.Shutdown();

        [Test]
        public void InterfaceBridgeSendsTypedParameters()
        {
            var result = 0;
            GameEventHelper.AddEvent.EventBridgeTest.Notify((current, maximum) => result = current * 1000 + maximum);
            GameEvent.Get<IEventBridgeTest>().Notify(80, 100);
            Assert.That(result, Is.EqualTo(80100));
        }

        [Test]
        public void GeneratedRemoveStopsDelivery()
        {
            var calls = 0;
            Action callback = () => calls++;
            GameEventHelper.AddEvent.EventBridgeTest.Ping(callback);
            GameEvent.Get<IEventBridgeTest>().Ping();
            GameEventHelper.RemoveEvent.EventBridgeTest.Ping(callback);
            GameEvent.Get<IEventBridgeTest>().Ping();
            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void InitIsIdempotentAndCanRestartAfterShutdown()
        {
            var original = GameEvent.Get<IEventBridgeTest>();
            GameEventHelper.Init();
            Assert.That(GameEvent.Get<IEventBridgeTest>(), Is.SameAs(original));
            GameEvent.Shutdown();
            Assert.That(GameEvent.Get<IEventBridgeTest>(), Is.Null);
            GameEventHelper.Init();
            Assert.That(GameEvent.Get<IEventBridgeTest>(), Is.Not.Null.And.Not.SameAs(original));
        }

        [Test]
        public void BridgeSupportsSixParameters()
        {
            var result = 0;
            GameEventHelper.AddEvent.EventBridgeTest.Six((a, b, c, d, e, f) => result = a + b + c + d + e + f);
            GameEvent.Get<IEventBridgeTest>().Six(1, 2, 3, 4, 5, 6);
            Assert.That(result, Is.EqualTo(21));
        }

        [Test]
        public void AddingListenerDuringDeliveryTakesEffectNextTime()
        {
            var calls = 0;
            var added = false;
            Action later = () => calls++;
            GameEventHelper.AddEvent.EventBridgeTest.Ping(() =>
            {
                if (!added) { added = true; GameEventHelper.AddEvent.EventBridgeTest.Ping(later); }
            });
            GameEvent.Get<IEventBridgeTest>().Ping();
            Assert.That(calls, Is.Zero);
            GameEvent.Get<IEventBridgeTest>().Ping();
            Assert.That(calls, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator DestroyedOwnerStopsReceivingEvents()
        {
            var calls = 0;
            var instance = new GameObject("Event Bridge Subscription Test");
            var owner = instance.AddComponent<EventBridgeSubscriptionOwner>();
            owner.Events.AddEvent(IEventBridgeTest_Event.Ping, () => calls++);
            GameEvent.Get<IEventBridgeTest>().Ping();
            Assert.That(calls, Is.EqualTo(1));
            UnityEngine.Object.Destroy(instance);
            yield return null;
            GameEvent.Get<IEventBridgeTest>().Ping();
            Assert.That(calls, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator HiddenOwnerKeepsSubscriptionUntilDestroyed()
        {
            var calls = 0;
            var instance = new GameObject("Event Bridge Hidden Subscription Test");
            var owner = instance.AddComponent<EventBridgeSubscriptionOwner>();
            owner.Events.AddEvent(IEventBridgeTest_Event.Ping, () => calls++);
            instance.SetActive(false);
            GameEvent.Get<IEventBridgeTest>().Ping();
            Assert.That(calls, Is.EqualTo(1));
            UnityEngine.Object.Destroy(instance);
            yield return null;
            GameEvent.Get<IEventBridgeTest>().Ping();
            Assert.That(calls, Is.EqualTo(1));
        }
    }
}
