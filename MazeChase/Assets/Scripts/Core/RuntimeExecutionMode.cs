using UnityEngine;

namespace MazeChase.Core
{
    /// <summary>
    /// Centralizes runtime execution-profile flags so the build can switch between
    /// normal interactive play and automation-friendly simulation runs.
    /// </summary>
    public static class RuntimeExecutionMode
    {
        private static bool _initialized;
        private static bool _simulationEnabled;
        private static bool _suppressPresentation;
        private static bool _quitOnGameOver;
        private static int _maxRounds;

        public static bool SimulationEnabled
        {
            get
            {
                EnsureInitialized();
                return _simulationEnabled;
            }
        }

        public static bool SuppressPresentation
        {
            get
            {
                EnsureInitialized();
                return _suppressPresentation;
            }
        }

        public static bool PresentationEnabled => !SuppressPresentation;

        public static bool QuitOnGameOver
        {
            get
            {
                EnsureInitialized();
                return _quitOnGameOver;
            }
        }

        public static int MaxRounds
        {
            get
            {
                EnsureInitialized();
                return _maxRounds;
            }
        }

        public static float ReadyDelaySeconds => SimulationEnabled ? 0f : 2f;
        public static float DeathPauseSeconds => SimulationEnabled ? 0f : 1.5f;
        public static float DeathReadyDelaySeconds => SimulationEnabled ? 0f : 2f;
        public static float RoundClearPauseSeconds => SimulationEnabled ? 0f : 1f;
        public static float MaxAllowedTimeScale => SimulationEnabled ? 100f : 20f;

        public static float ClampTimeScale(float requestedTimeScale)
        {
            return Mathf.Clamp(requestedTimeScale, 1f, MaxAllowedTimeScale);
        }

        private static void EnsureInitialized()
        {
            if (_initialized)
                return;

            _simulationEnabled = Application.isBatchMode || CommandLineArgs.HasFlag("--ai-headless");
            _suppressPresentation = _simulationEnabled || CommandLineArgs.HasFlag("--ai-no-render");
            _quitOnGameOver = _simulationEnabled || CommandLineArgs.HasFlag("--ai-quit-on-game-over");
            _maxRounds = Mathf.Max(0, CommandLineArgs.GetInt("--ai-max-rounds", 0));
            _initialized = true;
        }
    }
}
