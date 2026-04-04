using System;
using System.Collections.Generic;
using System.Text;

namespace MazeChase.Infrastructure.Logging
{
    public struct LogEntry
    {
        public DateTime Timestamp;
        public LogSeverity Severity;
        public string Category;
        public string Message;
        public string SessionId;
        public string SceneName;
        public Dictionary<string, string> Fields;
        public string Exception;

        /// <summary>
        /// Formats the entry as: [TIMESTAMP] [SEVERITY] [CATEGORY] MESSAGE
        /// </summary>
        public string ToLogLine()
        {
            string timestamp = Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff");
            string severity = Severity.ToString().ToUpperInvariant();
            string category = Category ?? "General";

            var sb = new StringBuilder(256);
            sb.Append('[').Append(timestamp).Append("] ");
            sb.Append('[').Append(severity).Append("] ");
            sb.Append('[').Append(category).Append("] ");
            sb.Append(Message ?? string.Empty);

            if (!string.IsNullOrEmpty(Exception))
            {
                sb.Append(" | Exception: ").Append(Exception);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Outputs the entry as a single-line JSON object (manual serialization).
        /// </summary>
        public string ToJsonLine()
        {
            var sb = new StringBuilder(512);
            sb.Append('{');

            sb.Append("\"timestamp\":\"").Append(EscapeJson(Timestamp.ToString("o"))).Append('"');
            sb.Append(",\"severity\":\"").Append(EscapeJson(Severity.ToString())).Append('"');
            sb.Append(",\"category\":\"").Append(EscapeJson(Category ?? string.Empty)).Append('"');
            sb.Append(",\"message\":\"").Append(EscapeJson(Message ?? string.Empty)).Append('"');
            sb.Append(",\"sessionId\":\"").Append(EscapeJson(SessionId ?? string.Empty)).Append('"');
            sb.Append(",\"sceneName\":\"").Append(EscapeJson(SceneName ?? string.Empty)).Append('"');

            if (!string.IsNullOrEmpty(Exception))
            {
                sb.Append(",\"exception\":\"").Append(EscapeJson(Exception)).Append('"');
            }

            if (Fields != null && Fields.Count > 0)
            {
                sb.Append(",\"fields\":{");
                bool first = true;
                foreach (var kvp in Fields)
                {
                    if (!first) sb.Append(',');
                    sb.Append('"').Append(EscapeJson(kvp.Key)).Append("\":\"").Append(EscapeJson(kvp.Value ?? string.Empty)).Append('"');
                    first = false;
                }
                sb.Append('}');
            }

            sb.Append('}');
            return sb.ToString();
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var sb = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20)
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
