using System;
using UnityEngine;

namespace MazeChase.Core
{
    /// <summary>
    /// Singleton that tracks score, high score, lives, round number, and the
    /// ghost-eat combo multiplier (200 / 400 / 800 / 1600).
    /// High score is persisted via PlayerPrefs.
    /// </summary>
    public class ScoreManager : MonoBehaviour
    {
        public static ScoreManager Instance { get; private set; }

        private const string HighScoreKey = "MazeChase_HighScore";
        private const int DefaultLives = 3;
        private const int DefaultRound = 1;

        // Ghost-eat combo sequence per the original Pac-Man rules.
        private static readonly int[] GhostComboValues = { 200, 400, 800, 1600 };

        // ── Events ──────────────────────────────────────────────────────────
        public event Action<int> OnScoreChanged;
        public event Action<int> OnLivesChanged;
        public event Action<int> OnRoundChanged;

        // ── State ───────────────────────────────────────────────────────────
        public int Score { get; private set; }
        public int HighScore { get; private set; }
        public int Lives { get; private set; } = DefaultLives;
        public int CurrentRound { get; set; } = DefaultRound;

        private int _ghostComboIndex;

        // ── Unity lifecycle ─────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            HighScore = PlayerPrefs.GetInt(HighScoreKey, 0);
        }

        // ── Public API ──────────────────────────────────────────────────────

        /// <summary>
        /// Adds points to the current score and updates the high score if surpassed.
        /// </summary>
        public void AddScore(int points)
        {
            if (points <= 0)
            {
                Debug.LogWarning($"[ScoreManager] AddScore called with non-positive value: {points}");
                return;
            }

            Score += points;

            if (Score > HighScore)
            {
                HighScore = Score;
                PlayerPrefs.SetInt(HighScoreKey, HighScore);
                PlayerPrefs.Save();
            }

            OnScoreChanged?.Invoke(Score);
        }

        /// <summary>
        /// Returns the next ghost-eat combo value (200, 400, 800, 1600) and advances
        /// the internal combo index. Subsequent calls without a reset keep escalating
        /// until the maximum is reached, after which the max value is repeated.
        /// </summary>
        public int GetNextGhostComboValue()
        {
            int value = GhostComboValues[Mathf.Min(_ghostComboIndex, GhostComboValues.Length - 1)];
            _ghostComboIndex++;
            return value;
        }

        /// <summary>
        /// Resets the ghost-eat combo index. Call this when the energizer effect wears off.
        /// </summary>
        public void ResetGhostCombo()
        {
            _ghostComboIndex = 0;
        }

        /// <summary>
        /// Decrements lives by one. Fires <see cref="OnLivesChanged"/>.
        /// </summary>
        public void LoseLife()
        {
            Lives = Mathf.Max(0, Lives - 1);
            Debug.Log($"[ScoreManager] Life lost. Lives remaining: {Lives}");
            OnLivesChanged?.Invoke(Lives);
        }

        /// <summary>
        /// Grants an extra life. Fires <see cref="OnLivesChanged"/>.
        /// </summary>
        public void GainLife()
        {
            Lives++;
            Debug.Log($"[ScoreManager] Extra life gained. Lives: {Lives}");
            OnLivesChanged?.Invoke(Lives);
        }

        /// <summary>
        /// Advances to the next round. Fires <see cref="OnRoundChanged"/>.
        /// </summary>
        public void NextRound()
        {
            CurrentRound++;
            Debug.Log($"[ScoreManager] Advanced to round {CurrentRound}");
            OnRoundChanged?.Invoke(CurrentRound);
        }

        /// <summary>
        /// Resets score, lives, round, and combo back to defaults.
        /// Does NOT reset the high score.
        /// </summary>
        public void ResetGame()
        {
            Score = 0;
            Lives = DefaultLives;
            CurrentRound = DefaultRound;
            _ghostComboIndex = 0;

            Debug.Log("[ScoreManager] Game reset");

            OnScoreChanged?.Invoke(Score);
            OnLivesChanged?.Invoke(Lives);
            OnRoundChanged?.Invoke(CurrentRound);
        }
    }
}
