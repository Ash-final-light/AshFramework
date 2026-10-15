using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AshFramework.Logging
{
    /// <summary>单消费者拥有文件流。Dispose 只通知完成；关闭等待由 LogSystem 统一安排。</summary>
    public sealed class FileLogger : ILogger
    {
        private sealed class Work
        {
            internal LogEntry Entry;
            internal TaskCompletionSource<bool> Barrier;
        }

        private static readonly object s_FilesGate = new object();
        private static readonly Regex s_OwnFileName = new Regex(
            @"^Ash_[0-9]{8}_[0-9]{6}_[0-9]{3}_[0-9a-f]{32}_[0-9]{4,}\.log$",
            RegexOptions.CultureInvariant);
        private static readonly HashSet<string> s_ActiveFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly object m_Gate = new object();
        private readonly Queue<Work> m_Queue = new Queue<Work>();
        private readonly LogConfig m_Config;
        private readonly string m_Session;
        private readonly Encoding m_Encoding = new UTF8Encoding(false);
        private readonly TaskCompletionSource<bool> m_Completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Thread m_Thread;
        private StreamWriter m_Writer;
        private string m_CurrentPath;
        private long m_FileBytes;
        private int m_FileIndex;
        private int m_RecordCount;
        private int m_Dropped;
        private bool m_Stopping;
        private bool m_Failed;

        public FileLogger(LogConfig config)
        {
            m_Config = (config ?? throw new ArgumentNullException(nameof(config))).Snapshot();
            if (string.IsNullOrWhiteSpace(m_Config.FileDirectory))
            {
                throw new ArgumentException("FileLogger 需要已解析的 FileDirectory。", nameof(config));
            }
            m_Config.FileDirectory = Path.GetFullPath(m_Config.FileDirectory);
            Directory.CreateDirectory(m_Config.FileDirectory);
            m_Session = "Ash_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N");
            OpenNextFile();
            m_Thread = new Thread(Consume) { IsBackground = true, Name = "Ash File Logger" };
            try
            {
                m_Thread.Start();
            }
            catch
            {
                CloseFile();
                throw;
            }
        }

        public void Log(in LogEntry entry)
        {
            lock (m_Gate)
            {
                if (m_Stopping || m_Failed)
                {
                    return;
                }
                int limit = entry.Level >= LogLevel.Error ? m_Config.QueueCapacity :
                    m_Config.QueueCapacity - Math.Max(1, m_Config.QueueCapacity / 10);
                if (m_RecordCount >= limit)
                {
                    m_Dropped++;
                    if (entry.Level >= LogLevel.Error)
                    {
                        LogDiagnostics.Report("文件队列满载，错误记录未能入队：" + entry.Message);
                    }
                    return;
                }
                m_Queue.Enqueue(new Work { Entry = entry });
                m_RecordCount++;
                Monitor.Pulse(m_Gate);
            }
        }

        public bool Flush(int timeoutMilliseconds)
        {
            Task<bool> task;
            lock (m_Gate)
            {
                if (m_Failed)
                {
                    return false;
                }
                if (m_Stopping)
                {
                    task = m_Completed.Task;
                }
                else
                {
                    var barrier = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    m_Queue.Enqueue(new Work { Barrier = barrier });
                    Monitor.Pulse(m_Gate);
                    task = barrier.Task;
                }
            }
            return task.Wait(Math.Max(0, timeoutMilliseconds)) && task.Result;
        }

        public void Dispose()
        {
            lock (m_Gate)
            {
                m_Stopping = true;
                Monitor.PulseAll(m_Gate);
            }
        }

        internal bool WaitForCompletion(int timeoutMilliseconds)
            => m_Completed.Task.Wait(Math.Max(0, timeoutMilliseconds)) && m_Completed.Task.Result;

        private void Consume()
        {
            bool success = false;
            var flushWatch = Stopwatch.StartNew();
            try
            {
                while (true)
                {
                    Work work = null;
                    int dropped;
                    bool done;
                    lock (m_Gate)
                    {
                        if (m_Queue.Count == 0 && !m_Stopping)
                        {
                            Monitor.Wait(m_Gate, m_Config.FlushIntervalMs);
                        }
                        if (m_Queue.Count > 0)
                        {
                            work = m_Queue.Dequeue();
                            if (work.Barrier == null)
                            {
                                m_RecordCount--;
                            }
                        }
                        dropped = m_Dropped;
                        m_Dropped = 0;
                        done = m_Stopping && work == null && m_Queue.Count == 0;
                    }
                    if (dropped > 0)
                    {
                        WriteText("[Ash][C:Framework][L:Warning] 文件队列满载，丢弃 " + dropped + " 条日志。");
                    }
                    if (work != null)
                    {
                        if (work.Barrier != null)
                        {
                            try
                            {
                                m_Writer.Flush();
                                work.Barrier.TrySetResult(true);
                            }
                            catch
                            {
                                work.Barrier.TrySetResult(false);
                                throw;
                            }
                        }
                        else
                        {
                            WriteText(LogFormatter.Format(work.Entry, m_Config, false));
                        }
                    }
                    if (flushWatch.ElapsedMilliseconds >= m_Config.FlushIntervalMs || done)
                    {
                        m_Writer.Flush();
                        flushWatch.Restart();
                    }
                    if (done)
                    {
                        break;
                    }
                }
                success = true;
            }
            catch (Exception exception)
            {
                lock (m_Gate)
                {
                    m_Failed = true;
                }
                LogDiagnostics.Report("文件日志输出失败：" + exception.Message);
            }
            finally
            {
                try
                {
                    CloseFile();
                }
                catch (Exception exception)
                {
                    success = false;
                    LogDiagnostics.Report("关闭日志文件失败：" + exception.Message);
                }
                lock (m_Gate)
                {
                    m_Stopping = true;
                    m_Failed |= !success;
                    while (m_Queue.Count > 0)
                    {
                        m_Queue.Dequeue().Barrier?.TrySetResult(false);
                    }
                    m_RecordCount = 0;
                }
                m_Completed.TrySetResult(success);
            }
        }

        private void WriteText(string text)
        {
            int bytes = m_Encoding.GetByteCount(text) + m_Encoding.GetByteCount(Environment.NewLine);
            if (m_FileBytes > 0 && m_FileBytes + bytes > (long)m_Config.MaxFileSizeMB * 1024 * 1024)
            {
                CloseFile();
                OpenNextFile();
            }
            m_Writer.WriteLine(text);
            m_FileBytes += bytes;
        }

        private void OpenNextFile()
        {
            m_CurrentPath = Path.Combine(m_Config.FileDirectory, m_Session + "_" + (++m_FileIndex).ToString("D4") + ".log");
            lock (s_FilesGate)
            {
                m_Writer = new StreamWriter(new FileStream(m_CurrentPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read), m_Encoding);
                s_ActiveFiles.Add(m_CurrentPath);
            }
            m_FileBytes = 0;
            CleanupOldFiles();
        }

        private void CloseFile()
        {
            try
            {
                m_Writer?.Dispose();
            }
            finally
            {
                m_Writer = null;
                lock (s_FilesGate)
                {
                    if (m_CurrentPath != null)
                    {
                        s_ActiveFiles.Remove(m_CurrentPath);
                    }
                }
            }
        }

        private void CleanupOldFiles()
        {
            try
            {
                var files = Array.FindAll(new DirectoryInfo(m_Config.FileDirectory).GetFiles("Ash_*.log"),
                    file => s_OwnFileName.IsMatch(file.Name));
                Array.Sort(files, (left, right) =>
                {
                    int compare = right.LastWriteTimeUtc.CompareTo(left.LastWriteTimeUtc);
                    return compare != 0 ? compare : string.CompareOrdinal(right.Name, left.Name);
                });
                int retained = 0;
                // 活跃文件优先保留，清理只作用于该后端生成的文件。
                lock (s_FilesGate)
                {
                    foreach (var file in files)
                    {
                        if (s_ActiveFiles.Contains(file.FullName))
                        {
                            retained++;
                        }
                    }
                    foreach (var file in files)
                    {
                        if (s_ActiveFiles.Contains(file.FullName))
                        {
                            continue;
                        }
                        if (retained++ < m_Config.MaxRetainedFiles)
                        {
                            continue;
                        }
                        try
                        {
                            file.Delete();
                        }
                        catch (IOException)
                        {
                        }
                        catch (UnauthorizedAccessException)
                        {
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                LogDiagnostics.Report("日志保留清理失败：" + exception.Message);
            }
        }
    }
}
