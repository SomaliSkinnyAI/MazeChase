using System;
using System.IO;

namespace MazeChase.Infrastructure.Logging
{
    public class FileSink : ILogSink
    {
        private readonly StreamWriter _writer;
        private readonly object _lock = new object();
        private bool _disposed;

        public FileSink(string filePath)
        {
            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _writer = new StreamWriter(filePath, append: true, encoding: System.Text.Encoding.UTF8)
            {
                AutoFlush = false
            };
        }

        public void Write(LogEntry entry)
        {
            if (_disposed) return;

            string line = entry.ToLogLine();

            lock (_lock)
            {
                if (_disposed) return;
                _writer.WriteLine(line);

                if (entry.Severity >= LogSeverity.Error)
                {
                    _writer.Flush();
                }
            }
        }

        public void Flush()
        {
            if (_disposed) return;

            lock (_lock)
            {
                if (_disposed) return;
                _writer.Flush();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;

                try
                {
                    _writer.Flush();
                    _writer.Dispose();
                }
                catch (Exception)
                {
                    // Swallow exceptions during disposal to avoid crashes on shutdown.
                }
            }
        }
    }
}
