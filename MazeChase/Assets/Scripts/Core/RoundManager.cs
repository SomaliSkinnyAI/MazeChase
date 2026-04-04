using System;
using UnityEngine;

namespace MazeChase.Core
{
    /// <summary>
    /// Manages the lifecycle of a single round: start, pellet tracking,
    /// round-clear sequence, and transition to the next round.
    /// </summary>
    public class RoundManager : MonoBehaviour
    {
        /// <summary>Fired when a new round begins.</summary>
        public event Action<int> OnRoundStarted;

        /// <summary>Fired when all pellets are cleared.</summary>
        public event Action<int> OnRoundCleared;

        private int _totalPellets;
        private int _pelletsRemaining;
        private bool _roundActive;

        // ── Public API ──────────────────────────────────────────────────────

        /// <summary>
        /// Initializes and starts a new round with the given number of pellets.
        /// </summary>
        public void StartRound(int totalPellets)
        {
            _totalPellets = totalPellets;
            _pelletsRemaining = totalPellets;
            _roundActive = true;

            int round = ScoreManager.Instance != null ? ScoreManager.Instance.CurrentRound : 1;
            Debug.Log($"[RoundManager] Round {round} started with {_totalPellets} pellets");
            OnRoundStarted?.Invoke(round);
        }

        /// <summary>
        /// Called each time a pellet is consumed. When all pellets are eaten
        /// the round-clear sequence begins.
        /// </summary>
        public void PelletEaten()
        {
            if (!_roundActive) return;

            _pelletsRemaining = Mathf.Max(0, _pelletsRemaining - 1);

            if (_pelletsRemaining <= 0)
            {
                RoundClear();
            }
        }

        /// <summary>
        /// Returns the number of pellets still on the board.
        /// </summary>
        public int GetPelletsRemaining()
        {
            return _pelletsRemaining;
        }

        /// <summary>
        /// Returns true if a round is currently in progress.
        /// </summary>
        public bool IsRoundActive()
        {
            return _roundActive;
        }

        // ── Internal ────────────────────────────────────────────────────────

        private void RoundClear()
        {
            _roundActive = false;

            int round = ScoreManager.Instance != null ? ScoreManager.Instance.CurrentRound : 1;
            Debug.Log($"[RoundManager] Round {round} cleared!");

            // Transition game state to RoundClear
            if (GameStateManager.Instance != null)
            {
                GameStateManager.Instance.ChangeState(GameState.RoundClear);
            }

            OnRoundCleared?.Invoke(round);
        }

        /// <summary>
        /// Advances to the next round via ScoreManager and restarts.
        /// Call this after the round-clear animation / intermission is finished.
        /// </summary>
        public void AdvanceToNextRound(int totalPellets)
        {
            if (ScoreManager.Instance != null)
            {
                ScoreManager.Instance.NextRound();
                ScoreManager.Instance.ResetGhostCombo();
            }

            Debug.Log("[RoundManager] Advancing to next round");
            StartRound(totalPellets);

            if (GameStateManager.Instance != null)
            {
                GameStateManager.Instance.ChangeState(GameState.Playing);
            }
        }
    }
}
