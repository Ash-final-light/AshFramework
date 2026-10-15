using System.Collections.Generic;
using UnityEngine;

namespace AshFramework.Logging
{
    public sealed class LogSettings : ScriptableObject
    {
        public const string ResourceName = "AshLogSettings";
        public bool EnableInPlayer = true;
        public LogConfig Config = new LogConfig();
        public List<string> CustomCategories = new List<string>();

        public LogConfig CreateConfig()
        {
            LogConfig config = (Config ?? new LogConfig()).Snapshot();
#if !UNITY_EDITOR
            config.Enabled &= EnableInPlayer;
#endif
            return config;
        }
    }
}
