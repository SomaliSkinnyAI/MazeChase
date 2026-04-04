using System.Collections.Generic;

namespace MazeChase.Infrastructure.Logging
{
    public interface IGameLogger
    {
        void Log(LogSeverity severity, string category, string message, Dictionary<string, string> fields = null);

        void Trace(string category, string message, Dictionary<string, string> fields = null);
        void Debug(string category, string message, Dictionary<string, string> fields = null);
        void Info(string category, string message, Dictionary<string, string> fields = null);
        void Warning(string category, string message, Dictionary<string, string> fields = null);
        void Error(string category, string message, Dictionary<string, string> fields = null);
        void Critical(string category, string message, Dictionary<string, string> fields = null);
    }
}
