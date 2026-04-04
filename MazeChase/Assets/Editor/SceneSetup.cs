using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using MazeChase.Core;

namespace MazeChase.Editor
{
    /// <summary>
    /// Editor utility that creates the BootScene with all required bootstrap objects.
    /// Can be invoked from the Unity menu or via the command line with
    /// -executeMethod MazeChase.Editor.SceneSetup.SetupBootScene
    /// </summary>
    public static class SceneSetup
    {
        private const string BootScenePath = "Assets/Scenes/BootScene.unity";

        [MenuItem("MazeChase/Create Boot Scene")]
        public static void CreateBootScene()
        {
            // Create a fresh, empty scene
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ── GameBootstrap root object ───────────────────────────────────
            var bootstrapGO = new GameObject("GameBootstrap");
            bootstrapGO.AddComponent<GameBootstrap>();

            // ── Infrastructure singletons ───────────────────────────────────
            // LogManager and CrashHandler are also created at runtime by
            // GameBootstrap.EnsureSingleton, but placing them in the scene
            // makes the dependency visible and lets designers configure fields.
            AddComponentIfTypeExists(bootstrapGO, "MazeChase.Infrastructure.Logging.LogManager");
            AddComponentIfTypeExists(bootstrapGO, "MazeChase.Infrastructure.CrashHandling.CrashHandler");
            AddComponentIfTypeExists(bootstrapGO, "MazeChase.Infrastructure.Diagnostics.DiagnosticsOverlay");

            // ── Core managers ───────────────────────────────────────────────
            bootstrapGO.AddComponent<GameStateManager>();
            bootstrapGO.AddComponent<ScoreManager>();

            // ── Main Camera ────────────────────────────────────────────────
            var cameraGO = new GameObject("Main Camera");
            cameraGO.tag = "MainCamera";
            var cam = cameraGO.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 9f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 100f;
            cam.backgroundColor = new Color(0.05f, 0.05f, 0.1f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cameraGO.transform.position = new Vector3(0f, -0.5f, -10f);
            cameraGO.AddComponent<AudioListener>();

            // ── Ensure output directory exists ──────────────────────────────
            if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
            {
                AssetDatabase.CreateFolder("Assets", "Scenes");
            }

            // ── Save ────────────────────────────────────────────────────────
            EditorSceneManager.SaveScene(scene, BootScenePath);
            AssetDatabase.Refresh();

            Debug.Log($"[SceneSetup] BootScene created and saved to {BootScenePath}");
        }

        /// <summary>
        /// Entry point for command-line batch invocation:
        /// Unity.exe -batchmode -executeMethod MazeChase.Editor.SceneSetup.SetupBootScene -quit
        /// </summary>
        public static void SetupBootScene()
        {
            Debug.Log("[SceneSetup] SetupBootScene invoked via command line");
            CreateBootScene();
            Debug.Log("[SceneSetup] SetupBootScene completed");
        }

        /// <summary>
        /// Attempts to add a component by fully-qualified type name.
        /// Silently skips if the type has not been compiled yet (allows
        /// the scene to be created before infrastructure scripts exist).
        /// </summary>
        private static void AddComponentIfTypeExists(GameObject target, string fullyQualifiedTypeName)
        {
            var type = System.Type.GetType(fullyQualifiedTypeName);

            // Type.GetType may fail for assembly-qualified names; fall back to
            // scanning all loaded assemblies.
            if (type == null)
            {
                foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    type = assembly.GetType(fullyQualifiedTypeName);
                    if (type != null) break;
                }
            }

            if (type != null && typeof(Component).IsAssignableFrom(type))
            {
                target.AddComponent(type);
                Debug.Log($"[SceneSetup] Added component: {fullyQualifiedTypeName}");
            }
            else
            {
                Debug.LogWarning($"[SceneSetup] Skipped missing component type: {fullyQualifiedTypeName}. " +
                                 "It will be created at runtime by GameBootstrap.");
            }
        }
    }
}
