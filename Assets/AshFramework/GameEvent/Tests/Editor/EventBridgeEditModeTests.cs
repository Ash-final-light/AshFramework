using System;
using NUnit.Framework;
using UnityEditor;

namespace AshFramework.Events.Tests.Editor
{
    public sealed class EventBridgeEditModeTests
    {
        [TearDown]
        public void TearDown() => GameEvent.Shutdown();

        [Test]
        public void RuntimeHasAshFrameworkNamespaceAndNoEngineDependency()
        {
            Assert.That(typeof(GameEvent).Namespace, Is.EqualTo("AshFramework.Events"));
            foreach (var reference in typeof(GameEvent).Assembly.GetReferencedAssemblies())
                Assert.That(reference.Name, Does.Not.StartWith("UnityEngine").And.Not.EqualTo("TEngine"));
        }

        [Test]
        public void StringAndIntegerIdsUseSameDispatcher()
        {
            var value = 0;
            var name = "AshFramework.Tests.StringAndIntegerIds";
            Action<int> callback = received => value = received;
            GameEvent.AddEventListener(name, callback);
            GameEvent.Send(RuntimeId.ToRuntimeId(name), 42);
            Assert.That(value, Is.EqualTo(42));
            GameEvent.RemoveEventListener(name, callback);
            GameEvent.Send(name, 100);
            Assert.That(value, Is.EqualTo(42));
        }

        [Test]
        public void ScopedManagerClearOnlyRemovesOwnedListeners()
        {
            var id = RuntimeId.ToRuntimeId("AshFramework.Tests.ScopedClear");
            var firstCalls = 0;
            var otherCalls = 0;
            var owner = new GameEventMgr();
            owner.AddEvent(id, () => firstCalls++);
            GameEvent.AddEventListener(id, () => otherCalls++);
            owner.Clear();
            GameEvent.Send(id);
            Assert.That(firstCalls, Is.Zero);
            Assert.That(otherCalls, Is.EqualTo(1));
        }

        [Test]
        public void MenuAndExplicitCheckEntryExist()
        {
            var method = typeof(AshFramework.Events.Editor.EventBridgeMenu).GetMethod("Generate");
            var menu = (MenuItem)Attribute.GetCustomAttribute(method, typeof(MenuItem));
            Assert.That(menu.menuItem, Is.EqualTo("Tools/Generate Event Bridge"));
            Assert.That(typeof(AshFramework.Events.Editor.EventBridgeMenu).GetMethod("CheckGenerated"), Is.Not.Null);
        }

        [Test]
        public void ActualMenuRunsAndGeneratedFilesRemainCurrent()
        {
            Assert.That(EditorApplication.ExecuteMenuItem("Tools/Generate Event Bridge"), Is.True);
            AshFramework.Events.Editor.EventBridgeMenu.CheckGenerated();
        }
    }
}
