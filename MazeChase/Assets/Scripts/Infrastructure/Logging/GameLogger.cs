using System;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using MazeChase.Infrastructure.Diagnostics;

namespace MazeChase.Infrastructure.Logging
{
    /// <summary>
    /// Thread-safe logger implementation that dispatches log entries to multiple sinks.
    /// </summary>
    public class GameLogger : IGameLogger
    {
        private readonly List<ILogSink> _sinks = new List<ILogSink>();
        private readonly object _lock = new object();

        public void AddSink(ILogSink sink)
        {
            if (sink == null) return;

            lock (_lock)
            {
                _sinks.Add(sink);
            }
        }

        public void RemoveSink(ILogSink sink)
        {
            if (sink == null) return;

            lock (_lock)
            {
                _sinks.Remove(sink);
            }
        }

        public void Log(LogSeverity severity, string category, string message, Dictionary<string, string> fields = null)
        {
            string sceneName;
            try
            {
                sceneName = SceneManager.GetActiveScene().name;
            }
            catch
            {
                sceneName = "Unknown";
            }

            var entry = new LogEntry
            {
                Timestamp = DateTime.UtcNow,
                Severity = severity,
                Category = category ?? "General",
                Message = message ?? string.Empty,
                SessionId = SessionInfo.SessionId,
                SceneName = sceneName,
                Fields = fields,
                Exception = null
            };

            WriteToSinks(entry);
        }

        public void Log(LogEntry entry)
        {
            WriteToSinks(entry);
        }

        public void Trace(string category, string message, Dictionary<string, string> fields = null)
        {
            Log(LogSeverity.Trace, category, message, fields);
        }

        public void Debug(string category, string message, Dictionary<string, string> fields = null)
        {
            Log(LogSeverity.Debug, category, message, fields);
        }

        public void Info(string category, string message, Dictionary<string, string> fields = null)
        {
            Log(LogSeverity.Info, category, message, fields);
        }

        public void Warning(string category, string message, Dictionary<string, string> fields = null)
        {
            Log(LogSeverity.Warning, category, message, fields);
        }

        public void Error(string category, string message, Dictionary<string, string> fields = null)
        {
            Log(LogSeverity.Error, category, message, fields);
        }

        public void Critical(string category, string message, Dictionary<string, string> fields = null)
        {
            Log(LogSeverity.Critical, category, message, fields);
        }

        public void FlushAll()
        {
            lock (_lock)
            {
                foreach (var sink in _sinks)
                {
                    try
                    {
                        sink.Flush();
                    }
                    catch (Exception)
                    {
                        // Swallow flush errors to avoid cascading failures.
                    }
                }
            }
        }

        public void DisposeAll()
        {
            lock (_lock)
            {
                foreach (var sink in _sinks)
                {
                    try
                    {
                        sink.Dispose();
                    }
                    catch (Exception)
                    {
                        // Swallow dispose errors to avoid crashes on shutdown.
                    }
                }
                _sinks.Clear();
            }
        }

        private void WriteToSinks(LogEntry entry)
        {
            lock (_lock)
            {
                foreach (var sink in _sinks)
                {
                    try
                    {
                        sink.Write(entry);
                    }
                    catch (Exception)
                    {
                        // Swallow write errors to prevent one broken sink from blocking others.
                    }
                }
            }
        }
    }
}
