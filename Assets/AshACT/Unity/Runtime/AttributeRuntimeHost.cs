using System;
using System.Collections.Generic;
using UnityEngine;

namespace AshACT.Unity
{
    [DisallowMultipleComponent]
    public sealed class AttributeRuntimeHost : MonoBehaviour
    {
        public AttributeSessionSetup Setup { get; private set; }
        public AttributeSession Session { get; private set; }
        private bool m_IsNotifyingSessionChange;

        /// <summary>成功初始化后传入旧、新会话；监听方释放旧订阅并绑定新会话。</summary>
        public event Action<AttributeSession, AttributeSession> SessionChanged;

        public bool TryInitialize(out IReadOnlyList<ConfigurationIssue> issues)
        {
            return TryInitialize(Setup, out issues);
        }

        public bool TryInitialize(AttributeSessionSetup setup, out IReadOnlyList<ConfigurationIssue> issues)
        {
            if (m_IsNotifyingSessionChange || (Session != null && Session.Runtime.IsPublishingNotifications))
            {
                issues = Array.AsReadOnly(new[]
                {
                    new ConfigurationIssue("NotificationReentrancy", null, "Session",
                        "通知回调期间不能重新初始化会话。")
                });
                return false;
            }

            if (!ConfigurationCompiler.TryCompileSession(setup, out CompiledSessionConfiguration compiled, out issues))
            {
                return false;
            }

            AttributeSession session = new AttributeSession(compiled);
            AttributeSession previous = Session;
            Setup = setup;
            Session = session;
            NotifySessionChanged(previous, session);
            return true;
        }

        private void NotifySessionChanged(AttributeSession previous, AttributeSession session)
        {
            if (SessionChanged == null)
            {
                return;
            }

            m_IsNotifyingSessionChange = true;
            try
            {
                foreach (Action<AttributeSession, AttributeSession> callback in SessionChanged.GetInvocationList())
                {
                    try
                    {
                        callback(previous, session);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception, this);
                    }
                }
            }
            finally
            {
                m_IsNotifyingSessionChange = false;
            }
        }
    }
}
