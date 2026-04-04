using System;

namespace MazeChase.Infrastructure.Logging
{
    public interface ILogSink : IDisposable
    {
        void Write(LogEntry entry);
        void Flush();
    }
}
