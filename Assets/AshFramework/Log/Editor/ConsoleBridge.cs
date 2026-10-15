using System;
using System.Reflection;
using UnityEditor;

namespace AshFramework.Logging.Editor
{
    [Flags]
    internal enum ConsoleCapabilities
    {
        None = 0,
        ReadSearch = 1,
        WriteSearch = 2,
        Toolbar = 4
    }

    internal interface IConsoleSearch
    {
        bool TryRead(out string text);
        bool TryWrite(string text, out string error);
    }

    internal sealed class NativeConsoleSearch : IConsoleSearch
    {
        public bool TryRead(out string text) => ConsoleBridge.TryGetSearchText(out text);

        public bool TryWrite(string text, out string error) => ConsoleBridge.TrySetSearchText(text, out error);
    }

    internal static class ConsoleBridge
    {
        private const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Type s_ConsoleType;
        private static readonly FieldInfo s_WindowField;
        private static readonly MethodInfo s_SetFilter;
        private static readonly MethodInfo s_GetFilter;
        private static readonly EventInfo s_ToolbarEvent;
        internal static ConsoleCapabilities Capabilities
        {
            get; private set;
        }

        internal static string FailureReason
        {
            get; private set;
        }

        static ConsoleBridge()
        {
            try
            {
                var assembly = typeof(EditorWindow).Assembly;
                s_ConsoleType = assembly.GetType("UnityEditor.ConsoleWindow");
                s_WindowField = s_ConsoleType?.GetField("ms_ConsoleWindow", StaticFlags);
                s_SetFilter = s_ConsoleType?.GetMethod("SetFilter", InstanceFlags, null, new[] { typeof(string) }, null);

                var entriesType = assembly.GetType("UnityEditor.LogEntries");
                s_GetFilter = entriesType?.GetMethod("GetFilteringText", StaticFlags, null, Type.EmptyTypes, null);
                s_ToolbarEvent = s_ConsoleType?.GetEvent("drawCustomToolbarGui", StaticFlags);

                if (s_GetFilter?.ReturnType == typeof(string))
                {
                    Capabilities |= ConsoleCapabilities.ReadSearch;
                }
                if (s_SetFilter?.ReturnType == typeof(void) &&
                    s_WindowField != null && typeof(EditorWindow).IsAssignableFrom(s_ConsoleType))
                {
                    Capabilities |= ConsoleCapabilities.WriteSearch;
                }

                if (s_ToolbarEvent?.EventHandlerType == typeof(Action) &&
                    s_ToolbarEvent.GetAddMethod(true) != null && s_ToolbarEvent.GetRemoveMethod(true) != null)
                {
                    Capabilities |= ConsoleCapabilities.Toolbar;
                }
            }
            catch (Exception exception)
            {
                FailureReason = exception.GetBaseException().Message;
            }
        }

        internal static bool TryGetSearchText(out string text)
        {
            text = "";
            if ((Capabilities & ConsoleCapabilities.ReadSearch) == 0)
            {
                return false;
            }

            try
            {
                text = s_GetFilter.Invoke(null, null) as string ?? "";
                return true;
            }
            catch (Exception exception)
            {
                Disable(ConsoleCapabilities.ReadSearch, exception);
                return false;
            }
        }

        internal static bool TrySetSearchText(string text, out string error)
        {
            error = "当前 Unity 版本不支持 Console 搜索写入，请复制搜索词手动粘贴。";
            if ((Capabilities & ConsoleCapabilities.WriteSearch) == 0)
            {
                return false;
            }
            try
            {
                var window = s_WindowField.GetValue(null) as EditorWindow;
                if (window == null)
                {
                    window = OpenConsole();
                }
                if (window == null)
                {
                    return false;
                }
                s_SetFilter.Invoke(window, new object[] { text ?? "" });
                window.Repaint();

                if (!TryGetSearchText(out string actual) || actual != (text ?? ""))
                {
                    throw new InvalidOperationException("Console 未接受搜索条件。");
                }
                error = null;
                return true;
            }
            catch (Exception exception)
            {
                Disable(ConsoleCapabilities.WriteSearch, exception);
                error = FailureReason;
                return false;
            }
        }

        internal static EditorWindow OpenConsole()
        {
            if (s_ConsoleType == null)
            {
                return null;
            }
            try
            {
                return EditorWindow.GetWindow(s_ConsoleType);
            }
            catch (Exception exception)
            {
                FailureReason = exception.GetBaseException().Message;
                return null;
            }
        }

        internal static bool TryRegisterToolbar(Action draw, out IDisposable registration)
        {
            registration = null;
            if ((Capabilities & ConsoleCapabilities.Toolbar) == 0)
            {
                return false;
            }
            try
            {
                s_ToolbarEvent.GetAddMethod(true).Invoke(null, new object[] { draw });
                registration = new ToolbarRegistration(draw);
                return true;
            }
            catch (Exception exception)
            {
                Disable(ConsoleCapabilities.Toolbar, exception);
                return false;
            }
        }

        private static void Disable(ConsoleCapabilities capability, Exception exception)
        {
            Capabilities &= ~capability;
            FailureReason = exception.GetBaseException().Message;
        }

        private sealed class ToolbarRegistration : IDisposable
        {
            private Action m_Draw;

            internal ToolbarRegistration(Action draw)
            {
                m_Draw = draw;
            }

            public void Dispose()
            {
                if (m_Draw == null)
                {
                    return;
                }
                try
                {
                    s_ToolbarEvent.GetRemoveMethod(true).Invoke(null, new object[] { m_Draw });
                }
                catch (Exception exception)
                {
                    Disable(ConsoleCapabilities.Toolbar, exception);
                }
                m_Draw = null;
            }
        }
    }
}
