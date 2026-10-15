using System.Text;

namespace AshFramework.Logging
{
    internal static class LogFormatter
    {
        internal const string FrameworkSearch = "[Ash][C:";

        internal static string CategorySearch(string category)
        {
            return "[Ash][C:" + category + "]";
        }

        internal static string Format(in LogEntry entry, LogConfig config, bool console)
        {
            // 每次调用独立缓冲区，避免并发格式化共享 StringBuilder。
            var text = new StringBuilder(128);
            text.Append(CategorySearch(entry.Category));
            string level = "[L:" + entry.Level + "]";
            if (console && config.EnableColor)
            {
                text.Append("<color=").Append(ColorHex(entry.Color, entry.Level))
                    .Append('>').Append(level).Append("</color>");
            }
            else
            {
                text.Append(level);
            }

            if (!string.IsNullOrEmpty(config.DisplayPrefix))
            {
                text.Append(' ').Append(config.DisplayPrefix);
            }
            if (config.ShowTime)
            {
                text.Append(" [").Append(entry.Timestamp.ToString(console
                    ? "HH:mm:ss.fff" : "yyyy-MM-dd HH:mm:ss.fff zzz")).Append(']');
            }
            if (config.ShowThreadId)
            {
                text.Append("[T:").Append(entry.ThreadId).Append(']');
            }
            if (config.ShowFrameCount && entry.FrameCount.HasValue)
            {
                text.Append("[F:").Append(entry.FrameCount.Value).Append(']');
            }

            text.Append(' ').Append(entry.Message ?? "<null>");
            if (!string.IsNullOrEmpty(entry.ExceptionText))
            {
                text.AppendLine().Append(entry.ExceptionText);
            }
            return text.ToString();
        }

        private static string ColorHex(LogColor color, LogLevel level)
        {
            switch (color)
            {
                case LogColor.Cyan:
                    return "#56D7E8";
                case LogColor.Blue:
                    return "#68A8FF";
                case LogColor.Green:
                    return "#75D993";
                case LogColor.Grey:
                    return "#999999";
                case LogColor.Orange:
                    return "#FFB363";
                case LogColor.Purple:
                    return "#BD96FF";
                case LogColor.Magenta:
                    return "#F58DD3";
                case LogColor.Red:
                    return "#FF7777";
                case LogColor.Yellow:
                    return "#E8CE69";
                default:
                    return level >= LogLevel.Error ? "#FF7777" :
                        level == LogLevel.Warning ? "#E8CE69" :
                        level == LogLevel.Debug ? "#999999" : "#75D993";
            }
        }
    }
}
