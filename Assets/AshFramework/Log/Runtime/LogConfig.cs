using System;

namespace AshFramework.Logging
{
    [Serializable]
    public sealed class LogConfig
    {
        public bool Enabled = true;
        public LogLevel MinLevel = LogLevel.Debug;
        public string DisplayPrefix = "";
        public bool ShowTime = true;
        public bool ShowThreadId = true;
        public bool ShowFrameCount;
        public bool EnableConsole = true;
        public LogLevel ConsoleMinLevel = LogLevel.Debug;
        public bool EnableColor = true;
        public bool EnableFile;
        public LogLevel FileMinLevel = LogLevel.Info;
        public string FileDirectory = "";
        public int MaxFileSizeMB = 10;
        public int MaxRetainedFiles = 10;
        public int FlushIntervalMs = 1000;
        public int QueueCapacity = 4096;
        public int MaxEntryLength = 16384;

        internal LogConfig Snapshot()
        {
            var copy = (LogConfig)MemberwiseClone();
            copy.DisplayPrefix = copy.DisplayPrefix ?? "";
            copy.MaxFileSizeMB = Math.Max(1, Math.Min(1024, copy.MaxFileSizeMB));
            copy.MaxRetainedFiles = Math.Max(1, copy.MaxRetainedFiles);
            copy.FlushIntervalMs = Math.Max(10, copy.FlushIntervalMs);
            copy.QueueCapacity = Math.Max(16, copy.QueueCapacity);
            copy.MaxEntryLength = Math.Max(256, Math.Min(1024 * 1024, copy.MaxEntryLength));
            if (!Enum.IsDefined(typeof(LogLevel), copy.MinLevel) ||
                !Enum.IsDefined(typeof(LogLevel), copy.ConsoleMinLevel) ||
                !Enum.IsDefined(typeof(LogLevel), copy.FileMinLevel))
            {
                throw new ArgumentException("日志等级配置无效。", nameof(LogConfig));
            }
            return copy;
        }
    }
}
