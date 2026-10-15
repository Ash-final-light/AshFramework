using System;

namespace AshFramework.Logging
{
    /// <summary>
    /// 输出器必须支持并发 Log。Flush 有时间预算；Dispose 应幂等且不能无限等待。
    /// 注册后由 LogSystem 接管生命周期。
    /// </summary>
    public interface ILogger : IDisposable
    {
        void Log(in LogEntry entry);
        bool Flush(int timeoutMilliseconds);
    }
}
