using System.Collections;
using UnityEngine;
using MazeChase.Infrastructure.Logging;
using MazeChase.Infrastructure.CrashHandling;

namespace MazeChase.Core
{
    /// <summary>
    /// Root bootstrap MonoBehaviour for the BootScene.
    /// Ensures critical infrastructure singletons exist, then either runs a
    /// smoke-test exit or hands off to normal gameplay flow.
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
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

            Debug.Log("[GameBootstrap] Boot complete. Starting gameplay...");

            // Create gameplay setup
            var gameplayObj = new GameObject("GameplaySetup");
            gameplayObj.AddComponent<Game.GameplaySceneSetup>();
        }

        private void Update()
        {
            if (Application.isBatchMode)
                return;

            // ESC to quit
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Debug.Log("[GameBootstrap] ESC pressed — quitting.");
                Application.Quit();
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#endif
            }
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
