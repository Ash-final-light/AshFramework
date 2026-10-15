using System;
using UnityEditor;
using UnityEngine;

namespace AshFramework.Logging.Editor
{
    internal sealed class ConsoleFilterController
    {
        private const string SessionKey = "AshFramework.Log.ConsoleFilter.v1";
        private static ConsoleFilterController s_Shared;
        private readonly IConsoleSearch m_Search;
        private readonly Action m_Save;
        internal ConsoleFilterState State
        {
            get;
        }

        internal string CurrentSearch { get; private set; } = "";
        internal string LastError
        {
            get; private set;
        }

        internal static ConsoleFilterController Shared
        {
            get
            {
                if (s_Shared != null)
                {
                    return s_Shared;
                }
                ConsoleFilterState state;
                try
                {
                    state = JsonUtility.FromJson<ConsoleFilterState>(SessionState.GetString(SessionKey, "{}"));
                }
                catch
                {
                    state = null;
                }
                state = state ?? new ConsoleFilterState();
                var captured = state;
                s_Shared = new ConsoleFilterController(new NativeConsoleSearch(), state,
                    () => SessionState.SetString(SessionKey, JsonUtility.ToJson(captured)));
                s_Shared.RefreshFromConsole();
                return s_Shared;
            }
        }

        internal ConsoleFilterController(IConsoleSearch search, ConsoleFilterState state, Action save = null)
        {
            m_Search = search ?? throw new ArgumentNullException(nameof(search));
            State = state ?? throw new ArgumentNullException(nameof(state));
            m_Save = save;
        }

        internal bool ApplyCategory(string category)
        {
            if (!LogCategories.IsValid(category))
            {
                LastError = "分类名称无效。";
                return false;
            }
            return Apply(LogFormatter.CategorySearch(category), ConsoleFilterMode.Category, category);
        }

        internal bool ShowFramework() => Apply(LogFormatter.FrameworkSearch, ConsoleFilterMode.Framework, "");

        internal bool ShowAll()
        {
            if (!m_Search.TryWrite("", out string error))
            {
                LastError = error;
                return false;
            }
            CurrentSearch = "";
            State.Mode = ConsoleFilterMode.None;
            State.OwnsSearch = false;
            State.PreviousSearchText = State.LastAppliedText = State.SelectedCategory = "";
            LastError = null;
            m_Save?.Invoke();
            return true;
        }

        internal bool RestorePrevious()
        {
            RefreshFromConsole();
            if (!State.OwnsSearch)
            {
                LastError = "当前搜索已由用户修改或未由本工具接管，保留现有输入。";
                return false;
            }
            if (!m_Search.TryRead(out string actual) || actual != State.LastAppliedText)
            {
                RefreshFromConsole();
                LastError = "搜索条件已变化，未覆盖现有输入。";
                return false;
            }
            string previous = State.PreviousSearchText ?? "";
            if (!m_Search.TryWrite(previous, out string error))
            {
                LastError = error;
                return false;
            }
            CurrentSearch = previous;
            State.Mode = previous.Length == 0 ? ConsoleFilterMode.None : ConsoleFilterMode.Manual;
            State.OwnsSearch = false;
            State.SelectedCategory = State.LastAppliedText = State.PreviousSearchText = "";
            LastError = null;
            m_Save?.Invoke();
            return true;
        }

        internal void RefreshFromConsole()
        {
            if (!m_Search.TryRead(out string actual))
            {
                return;
            }
            CurrentSearch = actual;
            if (State.OwnsSearch && actual == State.LastAppliedText)
            {
                return;
            }
            var mode = actual.Length == 0 ? ConsoleFilterMode.None : ConsoleFilterMode.Manual;
            bool changed = State.OwnsSearch || State.Mode != mode;
            State.OwnsSearch = false;
            State.Mode = mode;
            State.SelectedCategory = "";
            if (changed)
            {
                m_Save?.Invoke();
            }
        }

        private bool Apply(string query, ConsoleFilterMode mode, string category)
        {
            if (!m_Search.TryRead(out string actual))
            {
                LastError = "无法读取 Console 搜索条件，请复制分类搜索词手动使用。";
                return false;
            }
            bool owns = State.OwnsSearch && actual == State.LastAppliedText;
            string previous = owns ? State.PreviousSearchText : actual;
            if (!m_Search.TryWrite(query, out string error))
            {
                LastError = error;
                return false;
            }
            State.PreviousSearchText = previous;
            State.LastAppliedText = query;
            State.OwnsSearch = true;
            State.Mode = mode;
            State.SelectedCategory = category;
            CurrentSearch = query;
            LastError = null;
            m_Save?.Invoke();
            return true;
        }
    }
}
