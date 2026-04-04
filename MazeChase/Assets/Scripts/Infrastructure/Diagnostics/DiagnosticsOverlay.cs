using UnityEngine;
using UnityEngine.SceneManagement;
using MazeChase.Infrastructure.Diagnostics;
using MazeChase.Infrastructure.Logging;

namespace MazeChase.Infrastructure.Diagnostics
{
    /// <summary>
    /// MonoBehaviour singleton that renders a debug overlay toggled with F1.
    /// Displays FPS, current scene, session ID, error/warning counts, and log file path.
    /// Starts hidden by default.
    /// </summary>
    public class DiagnosticsOverlay : MonoBehaviour
    {
        private static DiagnosticsOverlay _instance;

        public static DiagnosticsOverlay Instance => _instance;

        private bool _visible;
        private float _deltaTime;
        private int _warningCount;
        private int _errorCount;

        private GUIStyle _boxStyle;
        private GUIStyle _labelStyle;
        private bool _stylesInitialized;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            _visible = false;

            // Subscribe to Unity log messages to count warnings and errors.
            Application.logMessageReceivedThreaded += OnLogMessage;
        }

        private void OnDestroy()
        {
            Application.logMessageReceivedThreaded -= OnLogMessage;

            if (_instance == this)
            {
                _instance = null;
            }
        }

        private void Update()
        {
            // Smoothed delta time for FPS display.
            _deltaTime += (Time.unscaledDeltaTime - _deltaTime) * 0.1f;

            // Toggle visibility with F1.
            if (Input.GetKeyDown(KeyCode.F1))
            {
                _visible = !_visible;
            }
        }

        private void OnGUI()
        {
            if (!_visible) return;

            EnsureStyles();

            float width = 420f;
            float height = 200f;
            float x = 10f;
            float y = 10f;

            GUI.Box(new Rect(x, y, width, height), GUIContent.none, _boxStyle);

            float lineHeight = 22f;
            float padding = 8f;
            float currentY = y + padding;

            // FPS
            float fps = 1.0f / _deltaTime;
            DrawLabel(x + padding, currentY, width - padding * 2, lineHeight, $"FPS: {fps:F1}");
            currentY += lineHeight;

            // Current scene
            string sceneName;
            try
            {
                sceneName = SceneManager.GetActiveScene().name;
            }
            catch
            {
                sceneName = "Unknown";
            }
            DrawLabel(x + padding, currentY, width - padding * 2, lineHeight, $"Scene: {sceneName}");
            currentY += lineHeight;

            // Session ID
            DrawLabel(x + padding, currentY, width - padding * 2, lineHeight, $"Session: {SessionInfo.SessionId}");
            currentY += lineHeight;

            // Warnings / Errors
            DrawLabel(x + padding, currentY, width - padding * 2, lineHeight, $"Warnings: {_warningCount}  |  Errors: {_errorCount}");
            currentY += lineHeight;

            // App version
            DrawLabel(x + padding, currentY, width - padding * 2, lineHeight, $"Version: {SessionInfo.AppVersion}");
            currentY += lineHeight;

            // Unity version
            DrawLabel(x + padding, currentY, width - padding * 2, lineHeight, $"Unity: {SessionInfo.UnityVersion}");
            currentY += lineHeight;

            // Log file path
            string logPath = Application.persistentDataPath + "/Logs/runtime/latest.log";
            DrawLabel(x + padding, currentY, width - padding * 2, lineHeight, $"Log: {logPath}");
            currentY += lineHeight;
        }

        // -----------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------

        private void EnsureStyles()
        {
            if (_stylesInitialized) return;

            _boxStyle = new GUIStyle(GUI.skin.box);
            var bgTexture = new Texture2D(1, 1);
            bgTexture.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.8f));
            bgTexture.Apply();
            _boxStyle.normal.background = bgTexture;

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Normal
            };
            _labelStyle.normal.textColor = Color.white;

            _stylesInitialized = true;
        }

        private void DrawLabel(float x, float y, float width, float height, string text)
        {
            GUI.Label(new Rect(x, y, width, height), text, _labelStyle);
        }

        private void OnLogMessage(string condition, string stackTrace, LogType type)
        {
            switch (type)
            {
                case LogType.Warning:
                    _warningCount++;
                    break;
                case LogType.Error:
                case LogType.Exception:
                case LogType.Assert:
                    _errorCount++;
                    break;
            }
        }
    }
}
