using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using UnityEngine;

namespace AshFramework.Logging
{
    public sealed class UnityLogger : ILogger
    {
        private readonly object m_Gate = new object();
        private readonly Queue<LogEntry> m_Queue = new Queue<LogEntry>();
        private readonly LogConfig m_Config;
        private int m_Dropped;
        private int m_Emitting;
        private bool m_Disposed;

        public UnityLogger(LogConfig config)
        {
            m_Config = (config ?? throw new ArgumentNullException(nameof(config))).Snapshot();
        }

        [HideInCallstack]
        public void Log(in LogEntry entry)
        {
            if (LogSystem.IsMainThread)
            {
                lock (m_Gate)
                {
                    if (m_Disposed)
                    {
                        return;
                    }
                    m_Emitting++;
                }
                try
                {
                    Emit(entry);
                }
                finally
                {
                    lock (m_Gate)
                    {
                        m_Emitting--;
                        Monitor.PulseAll(m_Gate);
                    }
                }
                return;
            }
            lock (m_Gate)
            {
                if (m_Disposed)
                {
                    return;
                }
                int limit = entry.Level >= LogLevel.Error ? m_Config.QueueCapacity :
                    m_Config.QueueCapacity - Math.Max(1, m_Config.QueueCapacity / 10);
                if (m_Queue.Count >= limit)
                {
                    m_Dropped++;
                    return;
                }
                m_Queue.Enqueue(entry);
                Monitor.PulseAll(m_Gate);
            }
        }

        internal void Pump(int maximum = 256)
        {
            if (!LogSystem.IsMainThread)
            {
                return;
            }
            int dropped;
            lock (m_Gate)
            {
                dropped = m_Dropped;
                m_Dropped = 0;
            }
            if (dropped > 0)
            {
                LogDiagnostics.Report("Console 队列满载，丢弃 " + dropped + " 条显示记录。");
            }
            for (int index = 0; index < maximum; index++)
            {
                LogEntry entry;
                lock (m_Gate)
                {
                    if (m_Disposed || m_Queue.Count == 0)
                    {
                        return;
                    }
                    entry = m_Queue.Dequeue();
                    m_Emitting++;
                }
                try
                {
                    Emit(entry);
                }
                finally
                {
                    lock (m_Gate)
                    {
                        m_Emitting--;
                        Monitor.PulseAll(m_Gate);
                    }
                }
            }
        }

        public bool Flush(int timeoutMilliseconds)
        {
            var watch = Stopwatch.StartNew();
            if (LogSystem.IsMainThread)
            {
                do
                {
                    Pump(256);
                    lock (m_Gate)
                    {
                        if (m_Queue.Count == 0 && m_Emitting == 0)
                        {
                            return true;
                        }
                    }
                }
                while (watch.ElapsedMilliseconds < Math.Max(0, timeoutMilliseconds));
                return false;
            }
            lock (m_Gate)
            {
                while ((m_Queue.Count > 0 || m_Emitting > 0) && !m_Disposed)
                {
                    int remaining = Math.Max(0, timeoutMilliseconds - (int)watch.ElapsedMilliseconds);
                    if (remaining == 0)
                    {
                        return false;
                    }
                    Monitor.Wait(m_Gate, remaining);
                }
                return m_Queue.Count == 0 && m_Emitting == 0;
            }
        }

        public void Dispose()
        {
            lock (m_Gate)
            {
                m_Disposed = true;
                m_Queue.Clear();
                Monitor.PulseAll(m_Gate);
            }
        }

        [HideInCallstack]
        private void Emit(in LogEntry entry)
        {
            string message = LogFormatter.Format(entry, m_Config, true);
            if (entry.Level >= LogLevel.Error)
            {
                UnityEngine.Debug.LogError(message, entry.Context);
            }
            else if (entry.Level == LogLevel.Warning)
            {
                UnityEngine.Debug.LogWarning(message, entry.Context);
            }
            else
            {
                UnityEngine.Debug.Log(message, entry.Context);
            }
        }
    }
}
