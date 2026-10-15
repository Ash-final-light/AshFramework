using System;

namespace AshFramework.Logging
{
    /// <summary>日志产生时的数据快照；后台输出器不得读取 Context 的 Unity 属性。</summary>
    public readonly struct LogEntry
    {
        public long Sequence
        {
            get;
        }

        public DateTimeOffset Timestamp
        {
            get;
        }

        public LogLevel Level
        {
            get;
        }

        public string Category
        {
            get;
        }

        public string Message
        {
            get;
        }

        public int ThreadId
        {
            get;
        }

        public int? FrameCount
        {
            get;
        }

        public string ExceptionText
        {
            get;
        }

        public UnityEngine.Object Context
        {
            get;
        }

        public LogColor Color
        {
            get;
        }

        public LogEntry(long sequence, DateTimeOffset timestamp, LogLevel level,
            string category, string message, int threadId, int? frameCount = null,
            string exceptionText = null, UnityEngine.Object context = null,
            LogColor color = LogColor.Default)
        {
            Sequence = sequence;
            Timestamp = timestamp;
            Level = level;
            Category = category;
            Message = message;
            ThreadId = threadId;
            FrameCount = frameCount;
            ExceptionText = exceptionText;
            Context = context;
            Color = color;
        }
    }
}
