using System;
using System.IO;
using UnityEngine;
using MazeChase.Infrastructure.Logging;
using MazeChase.Infrastructure.Diagnostics;

namespace MazeChase.Infrastructure.CrashHandling
{
    /// <summary>
    /// MonoBehaviour singleton that captures unhandled exceptions and Unity-level
    /// exceptions, logs them through LogManager, and writes a crash marker file.
    /// </summary>
    public class CrashHandler : MonoBehaviour
    {
        private static CrashHandler _instance;

        public static CrashHandler Instance => _instance;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            // Hook CLR unhandled exceptions.
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            // Hook Unity log callback for LogType.Exception.
            Application.logMessageReceivedThreaded += OnUnityLogMessage;
        }

        private void OnDestroy()
        {
            AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
            Application.logMessageReceivedThreaded -= OnUnityLogMessage;

            if (_instance == this)
            {
                _instance = null;
            }
        }

        // -----------------------------------------------------------------
        // Handlers
        // -----------------------------------------------------------------

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs args)
        {
            string message = args.ExceptionObject != null
                ? args.ExceptionObject.ToString()
                : "Unknown unhandled exception";

            HandleCrash("AppDomain.UnhandledException", message);
        }

        private void OnUnityLogMessage(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Exception) return;

            string fullMessage = condition;
            if (!string.IsNullOrEmpty(stackTrace))
            {
                fullMessage += "\n" + stackTrace;
            }

            HandleCrash("Unity.Exception", fullMessage);
        }

        // -----------------------------------------------------------------
        // Core crash handling
        // -----------------------------------------------------------------

        private void HandleCrash(string source, string details)
        {
            try
            {
                // Log through LogManager if available.
                LogManager.Critical("CrashHandler", $"[{source}] {details}");

                // Flush all sinks so the crash data is persisted.
                LogManager.Logger?.FlushAll();

                // Write a crash marker file so the next launch can detect a previous crash.
                WriteCrashMarker(source, details);
            }
            catch (Exception)
            {
                // Last-resort: if logging itself fails, try to write directly.
                try
                {
                    WriteCrashMarker(source, details);
                }
                catch
                {
                    // Nothing more we can do.
                }
            }
        }

        private void WriteCrashMarker(string source, string details)
        {
            try
            {
                string crashDir = Path.Combine(Application.persistentDataPath, "Logs", "crashes");
                if (!Directory.Exists(crashDir))
                {
                    Directory.CreateDirectory(crashDir);
                }

                string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                string markerPath = Path.Combine(crashDir, $"crash_{timestamp}.txt");

                string content =
                    $"Crash Marker\n" +
                    $"============\n" +
                    $"Timestamp  : {DateTime.UtcNow:o}\n" +
                    $"Session    : {SessionInfo.SessionId}\n" +
                    $"Source     : {source}\n" +
                    $"App Version: {SessionInfo.AppVersion}\n" +
                    $"Unity      : {SessionInfo.UnityVersion}\n" +
                    $"Platform   : {SessionInfo.Platform}\n" +
                    $"OS         : {SystemInfo.operatingSystem}\n" +
                    $"\nDetails:\n{details}\n";

                File.WriteAllText(markerPath, content, System.Text.Encoding.UTF8);
            }
            catch (Exception)
            {
                // Cannot write crash marker; nothing more we can do.
            }
        }
    }
}
