using System;

namespace AshFramework.Logging.Editor
{
    internal enum ConsoleFilterMode
    {
        None, Framework, Category, Manual
    }

    [Serializable]
    internal sealed class ConsoleFilterState
    {
        public ConsoleFilterMode Mode;
        public string SelectedCategory = "";
        public string PreviousSearchText = "";
        public string LastAppliedText = "";
        public bool OwnsSearch;
    }
}
