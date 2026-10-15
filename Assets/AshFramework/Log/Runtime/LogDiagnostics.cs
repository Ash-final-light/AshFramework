using System.Threading;

namespace AshFramework.Logging
{
    internal static class LogDiagnostics
    {
        private static int s_Count;
        [System.ThreadStatic] private static bool s_Reporting;

        internal static void Report(string message)
        {
            if (s_Reporting || Interlocked.Increment(ref s_Count) > 16)
            {
                return;
            }
            s_Reporting = true;
            try
            {
                System.Diagnostics.Trace.TraceError("[Ash Logging] " + message);
                if (LogSystem.IsMainThread)
                {
                    UnityEngine.Debug.LogWarning("[Ash Logging] " + message);
                }
                else
                {
                    System.Console.Error.WriteLine("[Ash Logging] " + message);
                }
            }
            finally
            {
                s_Reporting = false;
            }
        }
    }
}
