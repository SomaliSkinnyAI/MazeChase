using UnityEngine;
using MazeChase.Core;

namespace MazeChase.UI
{
    /// <summary>
    /// Handles title screen, pause overlay, and game-over screen using OnGUI.
    /// Listens to <see cref="GameStateManager.OnStateChanged"/> to show/hide itself.
    /// </summary>
    public class MenuController : MonoBehaviour
    {
        public static MenuController Instance { get; private set; }

        /// <summary>Invoked when the player presses "Start Game" on the title screen.</summary>
        public event System.Action OnStartGame;

        /// <summary>Invoked when the player presses "Restart" on the game-over screen.</summary>
        public event System.Action OnRestartGame;

        private enum MenuScreen { None, Title, Paused, GameOver }
        private MenuScreen _screen = MenuScreen.Title;

        private GUIStyle _titleStyle;
        private GUIStyle _subtitleStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _infoStyle;
        private bool _stylesCreated;

        // Tracks the state before pause so we can restore it.
        private GameState _stateBeforePause;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnEnable()
        {
            if (GameStateManager.Instance != null)
                GameStateManager.Instance.OnStateChanged += OnGameStateChanged;
        }

        private void OnDisable()
        {
            if (GameStateManager.Instance != null)
                GameStateManager.Instance.OnStateChanged -= OnGameStateChanged;
        }

        private void Update()
        {
            if (Application.isBatchMode || RuntimeExecutionMode.SimulationEnabled)
                return;

            // Title screen: Enter or Space to start.
            if (_screen == MenuScreen.Title)
            {
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space))
                    StartGame();
                return;
            }

            // Pause toggle: ESC or P while playing or already paused.
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.P))
            {
                if (_screen == MenuScreen.Paused)
                    ResumeGame();
                else if (_screen == MenuScreen.None && GameStateManager.Instance != null
                         && GameStateManager.Instance.GetCurrentState() == GameState.Playing)
                    PauseGame();
            }

            // Game-over screen: R to restart, Q/ESC to quit.
            if (_screen == MenuScreen.GameOver)
            {
                if (Input.GetKeyDown(KeyCode.R))
                    RestartGame();
                else if (Input.GetKeyDown(KeyCode.Q))
                    QuitGame();
            }
        }

        /// <summary>Show the title screen (called by GameBootstrap before gameplay starts).</summary>
        public void ShowTitle()
        {
            _screen = MenuScreen.Title;
            Time.timeScale = 1f;
        }

        // ── Actions ─────────────────────────────────────────────────────────

        private void StartGame()
        {
            _screen = MenuScreen.None;
            OnStartGame?.Invoke();
        }

        private void PauseGame()
        {
            var gsm = GameStateManager.Instance;
            if (gsm == null) return;

            _stateBeforePause = gsm.GetCurrentState();
            gsm.ChangeState(GameState.Paused);
            Time.timeScale = 0f;
            _screen = MenuScreen.Paused;
        }

        private void ResumeGame()
        {
            Time.timeScale = 1f;
            _screen = MenuScreen.None;

            var gsm = GameStateManager.Instance;
            if (gsm != null)
                gsm.ChangeState(_stateBeforePause != GameState.Paused ? _stateBeforePause : GameState.Playing);
        }

        private void RestartGame()
        {
            Time.timeScale = 1f;
            _screen = MenuScreen.None;
            OnRestartGame?.Invoke();
        }

        private static void QuitGame()
        {
            Debug.Log("[MenuController] Quit requested.");
            Application.Quit();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        // ── State listener ──────────────────────────────────────────────────

        private void OnGameStateChanged(GameState oldState, GameState newState)
        {
            if (newState == GameState.GameOver)
                _screen = MenuScreen.GameOver;
        }

        // ── GUI ─────────────────────────────────────────────────────────────

        private void CreateStyles()
        {
            if (_stylesCreated) return;
            _stylesCreated = true;

            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 64,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            _titleStyle.normal.textColor = Color.yellow;

            _subtitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 24,
                alignment = TextAnchor.MiddleCenter
            };
            _subtitleStyle.normal.textColor = Color.white;

            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 28,
                fontStyle = FontStyle.Bold,
                fixedHeight = 50,
                fixedWidth = 260
            };
            _buttonStyle.normal.textColor = Color.white;
            _buttonStyle.hover.textColor = Color.yellow;

            _infoStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                alignment = TextAnchor.MiddleCenter
            };
            _infoStyle.normal.textColor = new Color(0.7f, 0.7f, 0.7f);
        }

        private void OnGUI()
        {
            if (_screen == MenuScreen.None) return;
            CreateStyles();

            switch (_screen)
            {
                case MenuScreen.Title:    DrawTitle(); break;
                case MenuScreen.Paused:   DrawPaused(); break;
                case MenuScreen.GameOver: DrawGameOver(); break;
            }
        }

        private void DrawTitle()
        {
            float w = Screen.width;
            float h = Screen.height;

            // Full-screen dark background.
            GUI.color = new Color(0f, 0f, 0.05f, 0.95f);
            GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture);
            GUI.color = Color.white;

            // Title.
            GUI.Label(new Rect(0, h * 0.15f, w, 80), "MAZE CHASE", _titleStyle);

            // Pac-Man character hint.
            _subtitleStyle.normal.textColor = new Color(1f, 0.9f, 0f);
            GUI.Label(new Rect(0, h * 0.28f, w, 40), "A Pac-Man Experience", _subtitleStyle);

            // Controls.
            _subtitleStyle.normal.textColor = Color.white;
            float controlsY = h * 0.42f;
            GUI.Label(new Rect(0, controlsY, w, 30), "WASD / Arrow Keys - Move", _subtitleStyle);
            GUI.Label(new Rect(0, controlsY + 35, w, 30), "F2 - AI Autoplay   F3 - Planner AI   F4 - Manual", _subtitleStyle);
            GUI.Label(new Rect(0, controlsY + 70, w, 30), "P / ESC - Pause", _subtitleStyle);

            // Start button.
            float btnX = (w - 260) / 2f;
            if (GUI.Button(new Rect(btnX, h * 0.68f, 260, 50), "START GAME", _buttonStyle))
                StartGame();

            // Keyboard hint.
            _infoStyle.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
            GUI.Label(new Rect(0, h * 0.78f, w, 30), "Press ENTER or SPACE to start", _infoStyle);

            // High score.
            var score = ScoreManager.Instance;
            if (score != null && score.HighScore > 0)
            {
                _subtitleStyle.normal.textColor = Color.yellow;
                GUI.Label(new Rect(0, h * 0.88f, w, 30), $"HIGH SCORE: {score.HighScore}", _subtitleStyle);
            }
        }

        private void DrawPaused()
        {
            float w = Screen.width;
            float h = Screen.height;

            // Semi-transparent overlay.
            GUI.color = new Color(0f, 0f, 0.05f, 0.85f);
            GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.Label(new Rect(0, h * 0.3f, w, 80), "PAUSED", _titleStyle);

            float btnX = (w - 260) / 2f;
            if (GUI.Button(new Rect(btnX, h * 0.5f, 260, 50), "RESUME", _buttonStyle))
                ResumeGame();

            if (GUI.Button(new Rect(btnX, h * 0.58f, 260, 50), "QUIT", _buttonStyle))
                QuitGame();

            _infoStyle.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
            GUI.Label(new Rect(0, h * 0.72f, w, 30), "Press P or ESC to resume", _infoStyle);
        }

        private void DrawGameOver()
        {
            float w = Screen.width;
            float h = Screen.height;

            // Semi-transparent overlay.
            GUI.color = new Color(0.1f, 0f, 0f, 0.9f);
            GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture);
            GUI.color = Color.white;

            _titleStyle.normal.textColor = Color.red;
            GUI.Label(new Rect(0, h * 0.2f, w, 80), "GAME OVER", _titleStyle);
            _titleStyle.normal.textColor = Color.yellow; // Restore for other screens.

            // Final score.
            var score = ScoreManager.Instance;
            if (score != null)
            {
                _subtitleStyle.normal.textColor = Color.white;
                GUI.Label(new Rect(0, h * 0.35f, w, 35), $"SCORE: {score.Score}", _subtitleStyle);

                _subtitleStyle.normal.textColor = Color.yellow;
                GUI.Label(new Rect(0, h * 0.40f, w, 35), $"HIGH SCORE: {score.HighScore}", _subtitleStyle);

                _subtitleStyle.normal.textColor = Color.white;
                GUI.Label(new Rect(0, h * 0.45f, w, 35), $"ROUND: {score.CurrentRound}", _subtitleStyle);
            }

            float btnX = (w - 260) / 2f;
            if (GUI.Button(new Rect(btnX, h * 0.58f, 260, 50), "PLAY AGAIN", _buttonStyle))
                RestartGame();

            if (GUI.Button(new Rect(btnX, h * 0.66f, 260, 50), "QUIT", _buttonStyle))
                QuitGame();

            _infoStyle.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
            GUI.Label(new Rect(0, h * 0.78f, w, 30), "R - Restart    Q - Quit", _infoStyle);
        }
    }
}
