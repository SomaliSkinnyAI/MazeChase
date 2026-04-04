using UnityEngine;

namespace MazeChase.Infrastructure.Logging
{
    public class ConsoleSink : ILogSink
    {
        public void Write(LogEntry entry)
        {
            string line = entry.ToLogLine();

            switch (entry.Severity)
            {
                case LogSeverity.Trace:
                case LogSeverity.Debug:
                case LogSeverity.Info:
                    UnityEngine.Debug.Log(line);
                    break;

                case LogSeverity.Warning:
                    UnityEngine.Debug.LogWarning(line);
                    break;

                case LogSeverity.Error:
                case LogSeverity.Critical:
                    UnityEngine.Debug.LogError(line);
                    break;

                default:
                    UnityEngine.Debug.Log(line);
                    break;
            }
        }

        public void Flush()
        {
            // Unity console does not require explicit flushing.
        }

        public void Dispose()
        {
            // Nothing to dispose for the Unity console.
        }
    }
}
