using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UnityEngine;

namespace AshFramework.Logging
{
    public static class LogSystem
    {
        private sealed class Target
        {
            internal ILogger Logger;
            internal LogLevel Minimum;
            internal volatile bool Failed;
        }

        private sealed class Session
        {
            internal readonly LogConfig Config;
            internal readonly List<Target> Targets;
            private readonly object m_Gate = new object();
            private int m_Active;
            private bool m_Stopping;

            internal Session(LogConfig config, List<Target> targets)
            {
                Config = config;
                Targets = targets;
            }

            internal bool Enter()
            {
                lock (m_Gate)
                {
                    if (m_Stopping)
                    {
                        return false;
                    }
                    m_Active++;
                    return true;
                }
            }

            internal void Leave()
            {
                lock (m_Gate)
                {
                    m_Active--;
                    Monitor.PulseAll(m_Gate);
                }
            }

            internal void Stop()
            {
                lock (m_Gate)
                {
                    m_Stopping = true;
                }
            }

            internal bool WaitIdle(int timeout)
            {
                var watch = Stopwatch.StartNew();
                lock (m_Gate)
                {
                    while (m_Active > 0)
                    {
                        int remaining = Remaining(timeout, watch);
                        if (remaining == 0)
                        {
                            return false;
                        }
                        Monitor.Wait(m_Gate, remaining);
                    }
                    return true;
                }
            }

            internal bool Accepts(LogLevel level)
            {
                if (!Config.Enabled || level < Config.MinLevel || level > LogLevel.Fatal)
                {
                    return false;
                }
                foreach (var target in Targets)
                {
                    if (!target.Failed && level >= target.Minimum)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        private static readonly object s_Gate = new object();
        private static Session s_Session;
        private static UnityLogger s_Fallback;
        private static bool s_Shutdown;
        private static int s_MainThreadId;
        private static long s_Sequence;
        [ThreadStatic] private static bool s_Writing;

        public static bool IsInitialized
        {
            get
            {
                lock (s_Gate)
                {
                    return s_Session != null;
                }
            }
        }

        internal static bool IsMainThread => Thread.CurrentThread.ManagedThreadId == Volatile.Read(ref s_MainThreadId);

        internal static void BindMainThread()
        {
            Volatile.Write(ref s_MainThreadId, Thread.CurrentThread.ManagedThreadId);
        }

        public static void Initialize(LogConfig config, params ILogger[] additionalLoggers)
        {
            if (!IsMainThread)
            {
                throw new InvalidOperationException("请在 Unity 主线程初始化日志系统。");
            }
            lock (s_Gate)
            {
                if (s_Session != null)
                {
                    LogDiagnostics.Report("日志系统已初始化，本次重复初始化被忽略。");
                    return;
                }
                LogConfig snapshot = (config ?? throw new ArgumentNullException(nameof(config))).Snapshot();
                var targets = new List<Target>();
                if (snapshot.EnableConsole)
                {
                    targets.Add(new Target { Logger = new UnityLogger(snapshot), Minimum = snapshot.ConsoleMinLevel });
                }
                if (snapshot.EnableFile)
                {
#if UNITY_WEBGL && !UNITY_EDITOR
                    LogDiagnostics.Report("当前 WebGL 后端不启用文件日志线程。");
#else
                    try
                    {
                        if (string.IsNullOrWhiteSpace(snapshot.FileDirectory))
                        {
                            snapshot.FileDirectory = Path.Combine(Application.persistentDataPath, "Logs");
                        }
                        targets.Add(new Target { Logger = new FileLogger(snapshot), Minimum = snapshot.FileMinLevel });
                    }
                    catch (Exception exception)
                    {
                        LogDiagnostics.Report("无法启用文件日志：" + exception.Message);
                    }
#endif
                }
                if (additionalLoggers != null)
                {
                    foreach (var logger in additionalLoggers)
                    {
                        if (logger == null || targets.Exists(target => ReferenceEquals(target.Logger, logger)))
                        {
                            continue;
                        }
                        targets.Add(new Target { Logger = logger, Minimum = snapshot.MinLevel });
                    }
                }
                if (s_Fallback != null && !s_Fallback.Flush(1000))
                {
                    LogDiagnostics.Report("初始化时默认 Console 队列未完全显示。");
                }
                s_Fallback?.Dispose();
                s_Fallback = null;
                s_Session = new Session(snapshot, targets);
                s_Shutdown = false;
            }
            Application.quitting -= OnQuitting;
            Application.quitting += OnQuitting;
            if (Application.isPlaying)
            {
                LogRuntimeDriver.EnsureCreated();
            }
        }

        public static bool IsEnabled(LogLevel level, string category = LogCategories.Default)
        {
            if (level < LogLevel.Debug || level > LogLevel.Fatal)
            {
                return false;
            }
            lock (s_Gate)
            {
                return !s_Shutdown && (s_Session == null || s_Session.Accepts(level));
            }
        }

        [HideInCallstack]
        internal static void Write(LogLevel level, string message, string category,
            UnityEngine.Object context, LogColor color, string exceptionText = null)
        {
            if (s_Writing)
            {
                LogDiagnostics.Report("已阻止日志输出器的递归记录。");
                return;
            }
            Session session;
            UnityLogger fallback = null;
            lock (s_Gate)
            {
                if (s_Shutdown || level < LogLevel.Debug || level > LogLevel.Fatal)
                {
                    return;
                }
                session = s_Session;
                if (session == null)
                {
                    fallback = s_Fallback ?? (s_Fallback = new UnityLogger(new LogConfig()));
                }
                else if (!session.Accepts(level) || !session.Enter())
                {
                    return;
                }
            }
            s_Writing = true;
            try
            {
                int maximum = session?.Config.MaxEntryLength ?? 16384;
                var entry = new LogEntry(Interlocked.Increment(ref s_Sequence), DateTimeOffset.Now,
                    level, LogCategories.Normalize(category), Truncate(message ?? "<null>", maximum),
                    Thread.CurrentThread.ManagedThreadId,
                    IsMainThread && session != null && session.Config.ShowFrameCount ? (int?)Time.frameCount : null,
                    Truncate(exceptionText, maximum), context, color);
                if (fallback != null)
                {
                    fallback.Log(entry);
                }
                else
                {
                    foreach (var target in session.Targets)
                    {
                        if (target.Failed || level < target.Minimum)
                        {
                            continue;
                        }
                        try
                        {
                            target.Logger.Log(entry);
                        }
                        catch (Exception exception)
                        {
                            target.Failed = true;
                            LogDiagnostics.Report("日志输出器已隔离：" + exception.Message);
                        }
                    }
                }
            }
            finally
            {
                s_Writing = false;
                session?.Leave();
            }
        }

        public static bool Flush(int timeoutMilliseconds = 1000)
        {
            Session session;
            UnityLogger fallback;
            lock (s_Gate)
            {
                session = s_Session;
                fallback = s_Fallback;
            }
            return session != null ? FlushSession(session, Math.Max(0, timeoutMilliseconds)) :
                fallback == null || fallback.Flush(Math.Max(0, timeoutMilliseconds));
        }

        public static bool Shutdown(int timeoutMilliseconds = 2000)
        {
            if (!IsMainThread)
            {
                throw new InvalidOperationException("请在 Unity 主线程关闭日志系统。");
            }
            Session session;
            UnityLogger fallback;
            lock (s_Gate)
            {
                s_Shutdown = true;
                session = s_Session;
                session?.Stop();
                s_Session = null;
                fallback = s_Fallback;
                s_Fallback = null;
            }
            Application.quitting -= OnQuitting;
            var watch = Stopwatch.StartNew();
            int budget = Math.Max(0, timeoutMilliseconds);
            bool success = fallback == null || fallback.Flush(Remaining(budget, watch));
            fallback?.Dispose();
            if (session == null)
            {
                return success;
            }
            if (!session.WaitIdle(Remaining(budget, watch)))
            {
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    session.WaitIdle(int.MaxValue);
                    DisposeSession(session);
                });
                LogDiagnostics.Report("日志关闭等待超时，将继续在后台释放资源。");
                return false;
            }
            success &= FlushSession(session, Remaining(budget, watch));
            success &= DisposeSession(session);
            foreach (var target in session.Targets)
            {
                if (target.Logger is FileLogger file)
                {
                    success &= file.WaitForCompletion(Remaining(budget, watch));
                }
            }
            return success;
        }

        internal static void Pump()
        {
            if (!IsMainThread)
            {
                return;
            }
            Session session;
            UnityLogger fallback;
            lock (s_Gate)
            {
                session = s_Session;
                fallback = s_Fallback;
            }
            fallback?.Pump();
            if (session == null)
            {
                return;
            }
            foreach (var target in session.Targets)
            {
                if (!target.Failed && target.Logger is UnityLogger logger)
                {
                    logger.Pump();
                }
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntime()
        {
            BindMainThread();
            Shutdown();
            lock (s_Gate)
            {
                s_Shutdown = false;
                s_Sequence = 0;
            }
        }

        internal static void ResumeFallback()
        {
            lock (s_Gate)
            {
                if (s_Session == null)
                {
                    s_Shutdown = false;
                }
            }
        }

        private static void OnQuitting()
        {
            Shutdown();
        }

        private static string Truncate(string text, int maximum)
            => text != null && text.Length > maximum ? text.Substring(0, maximum) + "…[truncated]" : text;

        private static int Remaining(int budget, Stopwatch watch)
            => (int)Math.Max(0L, budget - watch.ElapsedMilliseconds);

        private static bool FlushSession(Session session, int budget)
        {
            var watch = Stopwatch.StartNew();
            bool success = true;
            foreach (var target in session.Targets)
            {
                if (target.Failed)
                {
                    success = false;
                    continue;
                }
                try
                {
                    success &= target.Logger.Flush(Remaining(budget, watch));
                }
                catch (Exception exception)
                {
                    success = false;
                    LogDiagnostics.Report("日志刷新失败：" + exception.Message);
                }
            }
            return success;
        }

        private static bool DisposeSession(Session session)
        {
            bool success = true;
            foreach (var target in session.Targets)
            {
                try
                {
                    target.Logger.Dispose();
                }
                catch (Exception exception)
                {
                    success = false;
                    LogDiagnostics.Report("日志资源释放失败：" + exception.Message);
                }
            }
            return success;
        }
    }
}
