using System.Collections;
using UnityEngine;
using MazeChase.Infrastructure.Logging;
using MazeChase.Infrastructure.CrashHandling;
using MazeChase.UI;

namespace MazeChase.Core
{
    /// <summary>
    /// Root bootstrap MonoBehaviour for the BootScene.
    /// Ensures critical infrastructure singletons exist, then either runs a
    /// smoke-test exit or hands off to normal gameplay flow.
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
        private GameObject _gameplayObj;

        private void Awake()
        {
            EnsureSingleton<LogManager>("LogManager");
            EnsureSingleton<CrashHandler>("CrashHandler");
            ApplyCommandLineSeed();

            Debug.Log("[GameBootstrap] Game starting...");
            if (RuntimeExecutionMode.SimulationEnabled)
            {
                Debug.Log(
                    $"[GameBootstrap] Simulation mode enabled. presentation={RuntimeExecutionMode.PresentationEnabled}, " +
                    $"quitOnGameOver={RuntimeExecutionMode.QuitOnGameOver}, maxRounds={RuntimeExecutionMode.MaxRounds}");
                Application.runInBackground = true;
            }
        }

        private IEnumerator Start()
        {
            if (CommandLineArgs.HasFlag("--smoke-test"))
            {
                Debug.Log("[GameBootstrap] Smoke-test mode detected. Will quit in 3 seconds.");
                yield return new WaitForSeconds(3f);
                Debug.Log("[GameBootstrap] Smoke-test complete. Exiting with code 0.");
                Application.Quit(0);

#if UNITY_EDITOR
                // Application.Quit does nothing in the editor, so stop play mode.
                UnityEditor.EditorApplication.isPlaying = false;
#endif
                yield break;
            }

            // Simulation / batch mode: skip menu, launch gameplay directly.
            if (RuntimeExecutionMode.SimulationEnabled)
            {
                Debug.Log("[GameBootstrap] Boot complete. Starting gameplay (simulation)...");
                LaunchGameplay();
                yield break;
            }

            // Presentation mode: show title screen via MenuController.
            Debug.Log("[GameBootstrap] Boot complete. Showing title screen...");
            var menuObj = new GameObject("MenuController");
            var menu = menuObj.AddComponent<MenuController>();
            menu.OnStartGame += OnStartGame;
            menu.OnRestartGame += OnRestartGame;
            menu.ShowTitle();
        }

        private void OnStartGame()
        {
            Debug.Log("[GameBootstrap] Start game requested.");
            LaunchGameplay();
        }

        private void OnRestartGame()
        {
            Debug.Log("[GameBootstrap] Restart game requested.");

            // Tear down old gameplay objects.
            if (_gameplayObj != null)
            {
                Destroy(_gameplayObj);
                _gameplayObj = null;
            }

            // Reset score/lives/round.
            if (ScoreManager.Instance != null)
                ScoreManager.Instance.ResetGame();

            LaunchGameplay();
        }

        private void LaunchGameplay()
        {
            // Create persistent RL server once (survives across episode resets).
            if (RuntimeExecutionMode.RLServerEnabled && AI.Autoplay.RLEnvironmentServer.Instance == null)
            {
                var rlObj = new GameObject("RLEnvironmentServer");
                DontDestroyOnLoad(rlObj);
                var rlServer = rlObj.AddComponent<AI.Autoplay.RLEnvironmentServer>();
                rlServer.OnResetRequested += OnRLReset;
            }

            _gameplayObj = new GameObject("GameplaySetup");
            _gameplayObj.AddComponent<Game.GameplaySceneSetup>();
        }

        private void OnRLReset()
        {
            Debug.Log("[GameBootstrap] RL reset requested.");
            if (_gameplayObj != null)
            {
                // Use DestroyImmediate so all singleton Instance references are cleared
                // before we create new gameplay objects. Deferred Destroy would leave stale
                // references that cause new singletons to self-destruct in Awake.
                DestroyImmediate(_gameplayObj);
                _gameplayObj = null;
            }
            if (ScoreManager.Instance != null)
                ScoreManager.Instance.ResetGame();

            _gameplayObj = new GameObject("GameplaySetup");
            _gameplayObj.AddComponent<Game.GameplaySceneSetup>();
        }

        /// <summary>
        /// Ensures a singleton MonoBehaviour of type T exists somewhere in the scene.
        /// If not found, creates a new GameObject with the component attached and
        /// marks it DontDestroyOnLoad.
        /// </summary>
        private static void EnsureSingleton<T>(string objectName) where T : MonoBehaviour
        {
            if (Object.FindAnyObjectByType<T>() != null) return;

            var go = new GameObject(objectName);
            go.AddComponent<T>();
            Object.DontDestroyOnLoad(go);
            Debug.Log($"[GameBootstrap] Created missing singleton: {objectName}");
        }

        private static void ApplyCommandLineSeed()
        {
            string seedArg = CommandLineArgs.GetValue("--ai-seed");
            if (string.IsNullOrWhiteSpace(seedArg))
                return;

            if (!int.TryParse(seedArg, out int seed))
            {
                Debug.LogWarning($"[GameBootstrap] Ignoring invalid --ai-seed value '{seedArg}'.");
                return;
            }

            Random.InitState(seed);
            Debug.Log($"[GameBootstrap] Applied deterministic seed {seed}.");
        }
    }
}
