using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace AshFramework.Logging.Tests
{
    public sealed class LogOutputTests
    {
        private sealed class Collector : ILogger
        {
            internal readonly ConcurrentQueue<LogEntry> Entries = new ConcurrentQueue<LogEntry>();
            internal bool Disposed;

            public void Log(in LogEntry entry)
            {
                Entries.Enqueue(entry);
            }

            public bool Flush(int timeoutMilliseconds) => true;

            public void Dispose()
            {
                Disposed = true;
            }
        }

        private sealed class BrokenLogger : ILogger
        {
            public void Log(in LogEntry entry)
            {
                throw new IOException("expected test failure");
            }

            public bool Flush(int timeoutMilliseconds) => true;

            public void Dispose()
            {
            }
        }

        private sealed class BlockingLogger : ILogger
        {
            internal readonly ManualResetEventSlim Entered = new ManualResetEventSlim();
            internal readonly ManualResetEventSlim Release = new ManualResetEventSlim();
            internal readonly ManualResetEventSlim Disposed = new ManualResetEventSlim();

            public void Log(in LogEntry entry)
            {
                Entered.Set();
                Release.Wait();
            }

            public bool Flush(int timeoutMilliseconds) => true;

            public void Dispose()
            {
                Disposed.Set();
            }
        }

        [SetUp]
        public void SetUp()
        {
            LogSystem.BindMainThread();
            LogSystem.Shutdown();
        }

        [TearDown]
        public void TearDown()
        {
            LogSystem.Shutdown();
            LogSystem.ResumeFallback();
        }

        [Test]
        public void ConfigurationIsASnapshotAndOutputFailuresAreIsolated()
        {
            var collector = new Collector();
            var config = new LogConfig { EnableConsole = false, MinLevel = LogLevel.Warning };
            LogSystem.Initialize(config, new BrokenLogger(), collector);
            config.MinLevel = LogLevel.Debug;
            LogUtil.Info("filtered", LogCategories.UI);
            LogUtil.Warning("retained", LogCategories.Network);
            LogUtil.Warning("also retained", LogCategories.UI);
            Assert.That(collector.Entries.Count, Is.EqualTo(2));
            Assert.That(collector.Entries.TryPeek(out var entry), Is.True);
            Assert.That(entry.Category, Is.EqualTo(LogCategories.Network));
            LogSystem.Shutdown();
            Assert.That(collector.Disposed, Is.True);
        }

        [Test]
        public void ConcurrentEntriesHaveDistinctSequencesAndOriginalException()
        {
            var collector = new Collector();
            LogSystem.Initialize(new LogConfig { EnableConsole = false }, collector);
            Parallel.For(0, 512, index => LogUtil.Info("entry-" + index, LogCategories.Event));
            Assert.That(collector.Entries.Count, Is.EqualTo(512));
            Assert.That(collector.Entries.Select(entry => entry.Sequence).Distinct().Count(), Is.EqualTo(512));
            Exception captured;
            try
            {
                throw new InvalidOperationException("original exception");
            }
            catch (Exception exception)
            {
                captured = exception;
            }
            LogUtil.Exception(captured, LogCategories.Framework);
            Assert.That(collector.Entries.Last().ExceptionText, Does.Contain(nameof(ConcurrentEntriesHaveDistinctSequencesAndOriginalException)));
        }

        [Test]
        public void TimedOutShutdownDoesNotDisposeAnActiveOutput()
        {
            var logger = new BlockingLogger();
            LogSystem.Initialize(new LogConfig { EnableConsole = false }, logger);
            Task producer = Task.Run(() => LogUtil.Info("in flight"));
            try
            {
                Assert.That(logger.Entered.Wait(5000), Is.True);
                Assert.That(LogSystem.Shutdown(1), Is.False);
                Assert.That(logger.Disposed.IsSet, Is.False);
            }
            finally
            {
                logger.Release.Set();
                Assert.That(producer.Wait(5000), Is.True);
                Assert.That(logger.Disposed.Wait(5000), Is.True);
                logger.Entered.Dispose();
                logger.Release.Dispose();
                logger.Disposed.Dispose();
            }
        }

        [Test]
        public void ConcurrentFileWritesDrainBeforeClose()
        {
            string directory = NewDirectory();
            var logger = new FileLogger(new LogConfig { FileDirectory = directory, QueueCapacity = 4096 });
            try
            {
                Parallel.For(0, 1024, index =>
                {
                    var entry = Entry(index, "record-" + index + "-end");
                    logger.Log(entry);
                });
                Assert.That(logger.Flush(10000), Is.True);
                logger.Dispose();
                Assert.That(logger.WaitForCompletion(10000), Is.True);
                var lines = Directory.GetFiles(directory, "*.log").SelectMany(File.ReadAllLines).ToArray();
                Assert.That(lines.Length, Is.EqualTo(1024));
                Assert.That(lines.Distinct().Count(), Is.EqualTo(1024));
                foreach (string file in Directory.GetFiles(directory))
                {
                    using (File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    {
                    }
                }
            }
            finally
            {
                logger.Dispose();
                logger.WaitForCompletion(10000);
                Directory.Delete(directory, true);
            }
        }

        [Test]
        public void FileRollingRetainsOnlyConfiguredFiles()
        {
            string directory = NewDirectory();
            var logger = new FileLogger(new LogConfig
            {
                FileDirectory = directory,
                MaxFileSizeMB = 1,
                MaxRetainedFiles = 2,
                QueueCapacity = 4096
            });
            try
            {
                for (int index = 0; index < 400; index++)
                {
                    var entry = Entry(index, "record-" + index + " " + new string('x', 8192));
                    logger.Log(entry);
                }
                logger.Dispose();
                Assert.That(logger.WaitForCompletion(10000), Is.True);
                string[] files = Directory.GetFiles(directory, "Ash_*.log");
                Assert.That(files.Length, Is.EqualTo(2));
                Assert.That(files.All(path => new FileInfo(path).Length <= 1024 * 1024), Is.True);
                Assert.That(files.Select(File.ReadAllText).Any(text => text.Contains("record-399 ")), Is.True);
            }
            finally
            {
                logger.Dispose();
                logger.WaitForCompletion(10000);
                Directory.Delete(directory, true);
            }
        }

        [Test]
        public void RetentionDoesNotDeleteUnrelatedFiles()
        {
            string directory = NewDirectory();
            string unrelated = Path.Combine(directory, "Ash_user.log");
            File.WriteAllText(unrelated, "user file");
            File.SetLastWriteTimeUtc(unrelated, DateTime.UtcNow.AddDays(-10));
            var logger = new FileLogger(new LogConfig { FileDirectory = directory, MaxRetainedFiles = 1 });
            try
            {
                logger.Dispose();
                Assert.That(logger.WaitForCompletion(10000), Is.True);
                Assert.That(File.ReadAllText(unrelated), Is.EqualTo("user file"));
            }
            finally
            {
                logger.Dispose();
                logger.WaitForCompletion(10000);
                Directory.Delete(directory, true);
            }
        }

        [Test]
        public void ConsoleColorsDoNotBreakCategorySearchOrPolluteFiles()
        {
            var config = new LogConfig { EnableColor = true };
            var entry = Entry(1, "hello");
            Assert.That(LogFormatter.Format(entry, config, true), Does.StartWith("[Ash][C:Event]"));
            Assert.That(LogFormatter.Format(entry, config, true), Does.Contain("<color="));
            Assert.That(LogFormatter.Format(entry, config, false), Does.Not.Contain("<color="));
        }

        [Test]
        public void WorkerConsoleEntryIsEmittedByMainThreadFlush()
        {
            var logger = new UnityLogger(new LogConfig
            {
                EnableColor = false,
                ShowTime = false,
                ShowThreadId = false
            });
            try
            {
                Task.Run(() =>
                {
                    var entry = Entry(1, "worker message");
                    logger.Log(entry);
                }).Wait();
                LogAssert.Expect(UnityEngine.LogType.Log, "[Ash][C:Event][L:Info] worker message");
                Assert.That(logger.Flush(1000), Is.True);
            }
            finally
            {
                logger.Dispose();
            }
        }

        private static string NewDirectory()
        {
            string path = Path.Combine(Path.GetTempPath(), "AshLogTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static LogEntry Entry(int index, string message)
            => new LogEntry(index, DateTimeOffset.Now, LogLevel.Info,
                LogCategories.Event, message, Thread.CurrentThread.ManagedThreadId);
    }
}
