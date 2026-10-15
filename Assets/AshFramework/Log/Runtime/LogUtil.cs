using System;
using System.Diagnostics;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AshFramework.Logging
{
    public static class LogUtil
    {
        public static bool IsEnabled(LogLevel level, string category = LogCategories.Default)
            => LogSystem.IsEnabled(level, category);

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD"), Conditional("ASH_LOG_DEBUG")]
        [HideInCallstack]
        public static void Debug(string message, string category = LogCategories.Default, Object context = null)
            => Write(LogLevel.Debug, message, category, context);

        [HideInCallstack]
        public static void Info(string message, string category = LogCategories.Default, Object context = null)
            => Write(LogLevel.Info, message, category, context);

        [HideInCallstack]
        public static void Warning(string message, string category = LogCategories.Default, Object context = null)
            => Write(LogLevel.Warning, message, category, context);

        [HideInCallstack]
        public static void Error(string message, string category = LogCategories.Default, Object context = null)
            => Write(LogLevel.Error, message, category, context);

        [HideInCallstack]
        public static void Fatal(string message, string category = LogCategories.Default, Object context = null)
            => Write(LogLevel.Fatal, message, category, context);

        [HideInCallstack]
        public static void Exception(Exception exception, string category = LogCategories.Default, Object context = null)
        {
            if (!IsEnabled(LogLevel.Error, category))
            {
                return;
            }
            LogSystem.Write(LogLevel.Error, exception?.Message ?? "<null exception>",
                category, context, global::AshFramework.Logging.LogColor.Default, exception?.ToString());
        }

        [HideInCallstack]
        public static void LogColor(LogColor color, string message,
            string category = LogCategories.Default, Object context = null)
            => LogSystem.Write(LogLevel.Info, message, category, context, color);

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD"), Conditional("ASH_LOG_DEBUG")]
        [HideInCallstack]
        public static void DebugFormat(string category, string format, params object[] args)
            => Format(LogLevel.Debug, category, format, args);

        [HideInCallstack]
        public static void InfoFormat(string category, string format, params object[] args)
            => Format(LogLevel.Info, category, format, args);

        [HideInCallstack]
        public static void WarningFormat(string category, string format, params object[] args)
            => Format(LogLevel.Warning, category, format, args);

        [HideInCallstack]
        public static void ErrorFormat(string category, string format, params object[] args)
            => Format(LogLevel.Error, category, format, args);

        [HideInCallstack]
        public static void FatalFormat(string category, string format, params object[] args)
            => Format(LogLevel.Fatal, category, format, args);

        [HideInCallstack]
        private static void Write(LogLevel level, string message, string category, Object context)
            => LogSystem.Write(level, message, category, context, global::AshFramework.Logging.LogColor.Default);

        [HideInCallstack]
        private static void Format(LogLevel level, string category, string format, object[] args)
        {
            if (!IsEnabled(level, category))
            {
                return;
            }
            try
            {
                Write(level, string.Format(format, args ?? Array.Empty<object>()), category, null);
            }
            catch (Exception exception)
            {
                LogDiagnostics.Report("日志格式化失败：" + exception.Message);
            }
        }
    }
}
