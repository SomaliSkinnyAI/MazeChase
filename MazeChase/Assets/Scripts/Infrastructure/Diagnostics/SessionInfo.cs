using System;
using UnityEngine;

namespace MazeChase.Infrastructure.Diagnostics
{
    /// <summary>
    /// Provides session-level metadata. Generates a unique SessionId on first access.
    /// </summary>
    public static class SessionInfo
    {
        private static string _sessionId;
        private static DateTime _startTime;
        private static bool _initialized;

        /// <summary>
        /// A unique GUID identifying this play session. Generated on first access.
        /// </summary>
        public static string SessionId
        {
            get
            {
                EnsureInitialized();
                return _sessionId;
            }
        }

        /// <summary>
        /// The time this session started (first access to SessionInfo).
        /// </summary>
        public static DateTime StartTime
        {
            get
            {
                EnsureInitialized();
                return _startTime;
            }
        }

        /// <summary>
        /// The application version as set in Player Settings.
        /// </summary>
        public static string AppVersion => Application.version;

        /// <summary>
        /// The Unity engine version.
        /// </summary>
        public static string UnityVersion => Application.unityVersion;

        /// <summary>
        /// The runtime platform (e.g., WindowsPlayer, LinuxPlayer, OSXPlayer).
        /// </summary>
        public static RuntimePlatform Platform => Application.platform;

        private static void EnsureInitialized()
        {
            if (_initialized) return;
            _sessionId = Guid.NewGuid().ToString();
            _startTime = DateTime.UtcNow;
            _initialized = true;
        }
    }
}
