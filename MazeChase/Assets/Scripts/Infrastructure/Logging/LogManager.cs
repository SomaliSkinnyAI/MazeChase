using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using MazeChase.Infrastructure.Diagnostics;

namespace MazeChase.Infrastructure.Logging
{
    /// <summary>
    /// MonoBehaviour singleton that bootstraps the logging system.
    /// Persists across scene loads via DontDestroyOnLoad.
    /// </summary>
    public class LogManager : MonoBehaviour
    {
        private static LogManager _instance;
        private GameLogger _logger;
        private bool _isShuttingDown;

        /// <summary>
        /// The singleton instance. Null if the LogManager has not been initialized.
        /// </summary>
        public static LogManager Instance => _instance;

        /// <summary>
        /// The underlying GameLogger. Null if the LogManager has not been initialized.
        /// </summary>
        public static GameLogger Logger => _instance != null ? _instance._logger : null;

        // -----------------------------------------------------------------
        // Static convenience methods
        // -----------------------------------------------------------------

        public static void Log(LogSeverity severity, string category, string message, Dictionary<string, string> fields = null)
        {
            Logger?.Log(severity, category, message, fields);
        }

        public static void Trace(string category, string message, Dictionary<string, string> fields = null)
        {
            Logger?.Trace(category, message, fields);
        }

        public static void LogDebug(string category, string message, Dictionary<string, string> fields = null)
        {
            Logger?.Debug(category, message, fields);
        }

        public static void Info(string category, string message, Dictionary<string, string> fields = null)
        {
            Logger?.Info(category, message, fields);
        }

        public static void Warning(string category, string message, Dictionary<string, string> fields = null)
        {
            Logger?.Warning(category, message, fields);
        }

        public static void Error(string category, string message, Dictionary<string, string> fields = null)
        {
            Logger?.Error(category, message, fields);
        }

        public static void Critical(string category, string message, Dictionary<string, string> fields = null)
        {
            Logger?.Critical(category, message, fields);
        }

        // -----------------------------------------------------------------
        // Unity lifecycle
        // -----------------------------------------------------------------

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            InitializeLogging();
        }

        private void OnApplicationQuit()
        {
            _isShuttingDown = true;

            _logger?.Info("LogManager", "Application shutting down.");
            _logger?.FlushAll();
            _logger?.DisposeAll();
        }

        private void OnDestroy()
        {
            Application.logMessageReceivedThreaded -= OnUnityLogMessage;

            if (_instance == this)
            {
                _instance = null;
            }
        }

        // -----------------------------------------------------------------
        // Initialization
        // -----------------------------------------------------------------

        private void InitializeLogging()
        {
            _logger = new GameLogger();

            string sessionId = SessionInfo.SessionId;
            string logsRoot = Path.Combine(Application.persistentDataPath, "Logs", "runtime");
            string sessionsRoot = Path.Combine(logsRoot, "sessions");

            // Latest log files (overwritten each run).
            string latestLogPath = Path.Combine(logsRoot, "latest.log");
            string latestJsonlPath = Path.Combine(logsRoot, "latest.jsonl");

            // Session-specific log files.
            string sessionLogPath = Path.Combine(sessionsRoot, sessionId + ".log");
            string sessionJsonlPath = Path.Combine(sessionsRoot, sessionId + ".jsonl");

            // Create sinks.
            _logger.AddSink(new ConsoleSink());
            _logger.AddSink(new FileSink(latestLogPath));
            _logger.AddSink(new FileSink(sessionLogPath));
            _logger.AddSink(new JsonlSink(latestJsonlPath));
            _logger.AddSink(new JsonlSink(sessionJsonlPath));

            // Hook Unity's built-in log system to capture logs from third-party code.
            Application.logMessageReceivedThreaded += OnUnityLogMessage;

            // Log startup banner.
            LogStartupBanner(logsRoot);
        }

        private void LogStartupBanner(string logsRoot)
        {
            _logger.Info("LogManager", "========================================");
            _logger.Info("LogManager", "  MazeChase - Logging Initialized");
            _logger.Info("LogManager", "========================================");
            _logger.Info("LogManager", $"App Version      : {SessionInfo.AppVersion}");
            _logger.Info("LogManager", $"Unity Version    : {SessionInfo.UnityVersion}");
            _logger.Info("LogManager", $"Platform         : {SessionInfo.Platform}");
            _logger.Info("LogManager", $"Session ID       : {SessionInfo.SessionId}");
            _logger.Info("LogManager", $"Session Start    : {SessionInfo.StartTime:o}");
            _logger.Info("LogManager", $"OS               : {SystemInfo.operatingSystem}");
            _logger.Info("LogManager", $"Device           : {SystemInfo.deviceModel}");
            _logger.Info("LogManager", $"GPU              : {SystemInfo.graphicsDeviceName}");
            _logger.Info("LogManager", $"System Memory    : {SystemInfo.systemMemorySize} MB");
            _logger.Info("LogManager", $"Persistent Path  : {Application.persistentDataPath}");
            _logger.Info("LogManager", $"Command Line     : {Environment.CommandLine}");
            _logger.Info("LogManager", "========================================");
        }

        // -----------------------------------------------------------------
        // Unity log capture
        // -----------------------------------------------------------------

        private void OnUnityLogMessage(string condition, string stackTrace, LogType type)
        {
            if (_isShuttingDown) return;

            // Avoid re-logging messages that originated from our own ConsoleSink.
            // ConsoleSink formats lines starting with '[' (timestamp bracket).
            if (!string.IsNullOrEmpty(condition) && condition.StartsWith("["))
                return;

            LogSeverity severity;
            switch (type)
            {
                case LogType.Error:
                    severity = LogSeverity.Error;
                    break;
                case LogType.Assert:
                    severity = LogSeverity.Error;
                    break;
                case LogType.Warning:
                    severity = LogSeverity.Warning;
                    break;
                case LogType.Log:
                    severity = LogSeverity.Debug;
                    break;
                case LogType.Exception:
                    severity = LogSeverity.Critical;
                    break;
                default:
                    severity = LogSeverity.Info;
                    break;
            }

            string sceneName;
            try
            {
                sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            }
            catch
            {
                sceneName = "Unknown";
            }

            var entry = new LogEntry
            {
                Timestamp = DateTime.UtcNow,
                Severity = severity,
                Category = "Unity",
                Message = condition,
                SessionId = SessionInfo.SessionId,
                SceneName = sceneName,
                Fields = null,
                Exception = string.IsNullOrEmpty(stackTrace) ? null : stackTrace
            };

            // Write to all sinks except ConsoleSink to avoid double-logging in the Unity console.
            _logger.Log(entry);
        }
    }
}
