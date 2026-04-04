using UnityEngine;
using System.Collections.Generic;
using MazeChase.Core;

namespace MazeChase.UI
{
    /// <summary>
    /// In-game HUD using OnGUI for reliable rendering without font assets.
    /// Shows score, high score, lives, round, FPS, center messages,
    /// floating score popups, and combo text.
    /// </summary>
    public class HUDController : MonoBehaviour
    {
        public static HUDController Instance { get; private set; }

        private GUIStyle _scoreStyle;
        private GUIStyle _highScoreStyle;
        private GUIStyle _infoStyle;
        private GUIStyle _messageStyle;
        private GUIStyle _fpsStyle;
        private GUIStyle _popupStyle;
        private GUIStyle _comboStyle;
        private bool _stylesCreated;

        private string _message = "";
        private float _messageTimer;
        private int _fps;
        private float _fpsTimer;
        private int _frameCount;

        // Score popup system
        private struct ScorePopup
        {
            public string text;
            public float timer;
            public float duration;
            public float startY;
        }
        private List<ScorePopup> _popups = new List<ScorePopup>();

        // Combo text system
        private string _comboText = "";
        private float _comboTimer;
        private float _comboDuration = 1.2f;

        // Track previous score for change detection
        private int _previousScore;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Update()
        {
            // FPS
            _frameCount++;
            _fpsTimer += Time.unscaledDeltaTime;
            if (_fpsTimer >= 0.5f)
            {
                _fps = Mathf.RoundToInt(_frameCount / _fpsTimer);
                _frameCount = 0;
                _fpsTimer = 0f;
            }

            // Message timer
            if (_messageTimer > 0f)
            {
                _messageTimer -= Time.deltaTime;
                if (_messageTimer <= 0f) _message = "";
            }

            // Update popups
            for (int i = _popups.Count - 1; i >= 0; i--)
            {
                var p = _popups[i];
                p.timer += Time.deltaTime;
                _popups[i] = p;
                if (p.timer >= p.duration)
                    _popups.RemoveAt(i);
            }

            // Update combo text
            if (_comboTimer > 0f)
            {
                _comboTimer -= Time.deltaTime;
                if (_comboTimer <= 0f) _comboText = "";
            }
        }

        private void CreateStyles()
        {
            if (_stylesCreated) return;
            _stylesCreated = true;

            _scoreStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 24,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperLeft
            };
            _scoreStyle.normal.textColor = Color.white;

            _highScoreStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 24,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperCenter
            };
            _highScoreStyle.normal.textColor = Color.yellow;

            _infoStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                alignment = TextAnchor.MiddleLeft
            };
            _infoStyle.normal.textColor = Color.white;

            _messageStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 40,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            _messageStyle.normal.textColor = Color.yellow;

            _fpsStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                alignment = TextAnchor.UpperRight
            };
            _fpsStyle.normal.textColor = new Color(0.6f, 0.6f, 0.6f, 0.7f);

            _popupStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 32,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            _popupStyle.normal.textColor = Color.white;

            _comboStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 48,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            _comboStyle.normal.textColor = new Color(0.2f, 1f, 1f, 1f);
        }

        private void OnGUI()
        {
            CreateStyles();

            var score = ScoreManager.Instance;
            int scoreVal = score != null ? score.Score : 0;
            int highScore = score != null ? score.HighScore : 0;
            int lives = score != null ? score.Lives : 3;
            int round = score != null ? score.CurrentRound : 1;

            float w = Screen.width;
            float h = Screen.height;

            // Semi-transparent dark background bar behind top HUD
            GUI.color = new Color(0f, 0f, 0.05f, 0.6f);
            GUI.DrawTexture(new Rect(0, 0, w, 50), Texture2D.whiteTexture);

            // Semi-transparent dark background bar behind bottom HUD
            GUI.DrawTexture(new Rect(0, h - 45, w, 45), Texture2D.whiteTexture);
            GUI.color = Color.white;

            // Score (top-left)
            GUI.Label(new Rect(15, 10, 300, 40), $"SCORE  {scoreVal}", _scoreStyle);

            // High Score (top-center)
            GUI.Label(new Rect(w / 2 - 150, 10, 300, 40), $"HIGH SCORE  {highScore}", _highScoreStyle);

            // Round (top-right)
            GUI.Label(new Rect(w - 200, 10, 185, 40), $"ROUND {round}", _scoreStyle);

            // Lives (bottom-left)
            GUI.Label(new Rect(15, h - 40, 200, 30), $"LIVES: {lives}", _infoStyle);

            // FPS (bottom-right)
            GUI.Label(new Rect(w - 100, h - 30, 90, 25), $"FPS: {_fps}", _fpsStyle);

            // Score popups (floating text that drifts upward and fades)
            for (int i = 0; i < _popups.Count; i++)
            {
                var p = _popups[i];
                float t = p.timer / p.duration;
                float alpha = Mathf.Lerp(1f, 0f, t);
                float yOffset = Mathf.Lerp(0f, -60f, t);
                float scale = Mathf.Lerp(1f, 0.7f, t);

                int fontSize = Mathf.RoundToInt(32 * scale);
                _popupStyle.fontSize = Mathf.Max(fontSize, 16);
                _popupStyle.normal.textColor = new Color(1f, 1f, 0.5f, alpha);

                float popupY = p.startY + yOffset;
                GUI.Label(new Rect(w / 2 - 100, popupY, 200, 50), p.text, _popupStyle);
            }

            // Combo text (large centered text that fades)
            if (!string.IsNullOrEmpty(_comboText) && _comboTimer > 0f)
            {
                float comboT = 1f - (_comboTimer / _comboDuration);
                float comboAlpha = Mathf.Lerp(1f, 0f, comboT);
                float comboScale = Mathf.Lerp(1.2f, 0.8f, comboT);

                _comboStyle.fontSize = Mathf.RoundToInt(48 * comboScale);
                _comboStyle.normal.textColor = new Color(0.2f, 1f, 1f, comboAlpha);

                float comboY = h * 0.35f - 20f * comboT;
                GUI.Label(new Rect(w / 2 - 150, comboY, 300, 60), _comboText, _comboStyle);
            }

            // Center message
            if (!string.IsNullOrEmpty(_message))
            {
                // Dark background behind message
                GUI.color = new Color(0, 0, 0, 0.7f);
                GUI.DrawTexture(new Rect(w / 2 - 200, h / 2 - 30, 400, 60), Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.Label(new Rect(w / 2 - 200, h / 2 - 30, 400, 60), _message, _messageStyle);
            }
        }

        public void ShowMessage(string message, float duration = 3f)
        {
            _message = message;
            _messageTimer = duration > 0f ? duration : 99999f;
        }

        public void HideMessage()
        {
            _message = "";
            _messageTimer = 0f;
        }

        /// <summary>
        /// Shows a floating score popup near the top of the screen.
        /// </summary>
        public void ShowScorePopup(int points)
        {
            if (points < 200) return; // Only show for significant scores

            _popups.Add(new ScorePopup
            {
                text = $"+{points}",
                timer = 0f,
                duration = 1.0f,
                startY = 60f
            });
        }

        /// <summary>
        /// Shows combo text for ghost eating chains (200, 400, 800, 1600).
        /// </summary>
        public void ShowComboText(int points)
        {
            _comboText = $"{points}!";
            _comboTimer = _comboDuration;
        }

        public void UpdateScoreDisplay(int s) { } // OnGUI reads directly
        public void UpdateLivesDisplay(int l) { }
        public void UpdateRoundDisplay(int r) { }
    }
}
