using System;
using UnityEngine;

namespace MazeChase.AI
{
    /// <summary>
    /// Manages the global scatter/chase mode alternation for all ghosts.
    /// Each round has a data-driven timing table that mirrors the classic
    /// Pac-Man mode sequence. Frightened mode pauses the timer, which
    /// resumes from the same point when frightened ends.
    /// </summary>
    public class GhostModeTimer : MonoBehaviour
    {
        // ── Events ────────────────────────────────────────────────────
        /// <summary>
        /// Fired whenever the global mode changes between Scatter and Chase.
        /// Listeners (typically Ghost instances) should update their state.
        /// </summary>
        public event Action<GhostState> OnModeChanged;

        // ── Runtime state ─────────────────────────────────────────────
        private float phaseTimer;
        private int currentPhaseIndex;
        private bool isPaused;
        private bool isRunning;
        private int currentRound;
        private GhostState currentMode;

        // ── Timing tables ─────────────────────────────────────────────
        // Each entry is a (GhostState mode, float duration) pair.
        // The sequence always ends with Chase lasting forever (float.MaxValue).

        /// <summary>
        /// A single phase in the scatter/chase sequence.
        /// </summary>
        private struct ModePhase
        {
            public GhostState Mode;
            public float Duration;

            public ModePhase(GhostState mode, float duration)
            {
                Mode = mode;
                Duration = duration;
            }
        }

        // Round 1
        private static readonly ModePhase[] Round1Phases =
        {
            new ModePhase(GhostState.Scatter, 7f),
            new ModePhase(GhostState.Chase, 20f),
            new ModePhase(GhostState.Scatter, 7f),
            new ModePhase(GhostState.Chase, 20f),
            new ModePhase(GhostState.Scatter, 5f),
            new ModePhase(GhostState.Chase, 20f),
            new ModePhase(GhostState.Scatter, 5f),
            new ModePhase(GhostState.Chase, float.MaxValue) // Forever
        };

        // Rounds 2-4
        private static readonly ModePhase[] Round2To4Phases =
        {
            new ModePhase(GhostState.Scatter, 7f),
            new ModePhase(GhostState.Chase, 20f),
            new ModePhase(GhostState.Scatter, 7f),
            new ModePhase(GhostState.Chase, 20f),
            new ModePhase(GhostState.Scatter, 5f),
            new ModePhase(GhostState.Chase, 1033f),
            new ModePhase(GhostState.Scatter, 1f / 60f), // 1 frame at 60fps
            new ModePhase(GhostState.Chase, float.MaxValue)
        };

        // Round 5+
        private static readonly ModePhase[] Round5PlusPhases =
        {
            new ModePhase(GhostState.Scatter, 5f),
            new ModePhase(GhostState.Chase, 20f),
            new ModePhase(GhostState.Scatter, 5f),
            new ModePhase(GhostState.Chase, 20f),
            new ModePhase(GhostState.Scatter, 5f),
            new ModePhase(GhostState.Chase, 1037f),
            new ModePhase(GhostState.Scatter, 1f / 60f),
            new ModePhase(GhostState.Chase, float.MaxValue)
        };

        // Active phase table reference
        private ModePhase[] activePhases;

        // ── Public properties ─────────────────────────────────────────
        /// <summary>Current global ghost mode (Scatter or Chase).</summary>
        public GhostState CurrentMode => currentMode;

        /// <summary>Whether the timer is currently paused (e.g., during frightened).</summary>
        public bool IsPaused => isPaused;

        /// <summary>Time remaining in the current phase.</summary>
        public float TimeRemainingInPhase
        {
            get
            {
                if (activePhases == null || currentPhaseIndex >= activePhases.Length)
                    return float.MaxValue;
                return activePhases[currentPhaseIndex].Duration - phaseTimer;
            }
        }

        // ══════════════════════════════════════════════════════════════
        // Unity lifecycle
        // ══════════════════════════════════════════════════════════════

        private void Update()
        {
            if (!isRunning || isPaused)
                return;

            if (activePhases == null || currentPhaseIndex >= activePhases.Length)
                return;

            ModePhase phase = activePhases[currentPhaseIndex];

            // Don't advance if this phase lasts forever
            if (phase.Duration >= float.MaxValue)
                return;

            phaseTimer += Time.deltaTime;

            if (phaseTimer >= phase.Duration)
            {
                AdvancePhase();
            }
        }

        // ══════════════════════════════════════════════════════════════
        // Public methods
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Start the scatter/chase timer for the given round (1-based).
        /// Selects the appropriate timing table and begins with the first phase.
        /// </summary>
        public void StartTimer(int round)
        {
            currentRound = Mathf.Max(1, round);
            activePhases = GetPhasesForRound(currentRound);
            currentPhaseIndex = 0;
            phaseTimer = 0f;
            isPaused = false;
            isRunning = true;

            currentMode = activePhases[0].Mode;
            OnModeChanged?.Invoke(currentMode);
        }

        /// <summary>
        /// Pause the scatter/chase timer. Used when frightened mode activates.
        /// </summary>
        public void PauseTimer()
        {
            isPaused = true;
        }

        /// <summary>
        /// Resume the scatter/chase timer from where it was paused.
        /// Used when frightened mode ends.
        /// </summary>
        public void ResumeTimer()
        {
            isPaused = false;

            // Re-broadcast the current mode so ghosts revert from frightened
            OnModeChanged?.Invoke(currentMode);
        }

        /// <summary>
        /// Completely stop and reset the timer.
        /// </summary>
        public void ResetTimer()
        {
            isRunning = false;
            isPaused = false;
            currentPhaseIndex = 0;
            phaseTimer = 0f;
            activePhases = null;
        }

        /// <summary>
        /// Get the frightened duration for the current round.
        /// </summary>
        public float GetFrightenedDuration()
        {
            return RoundTuningData.GetFrightenedDuration(currentRound);
        }

        /// <summary>
        /// Get the frightened flash count for the current round.
        /// </summary>
        public int GetFrightenedFlashCount()
        {
            return RoundTuningData.GetFrightenedFlashCount(currentRound);
        }

        // ══════════════════════════════════════════════════════════════
        // Internal
        // ══════════════════════════════════════════════════════════════

        private void AdvancePhase()
        {
            currentPhaseIndex++;

            if (currentPhaseIndex >= activePhases.Length)
            {
                // Should not happen as last phase is always forever, but safety check
                currentPhaseIndex = activePhases.Length - 1;
                return;
            }

            phaseTimer = 0f;
            currentMode = activePhases[currentPhaseIndex].Mode;
            OnModeChanged?.Invoke(currentMode);
        }

        private static ModePhase[] GetPhasesForRound(int round)
        {
            if (round <= 1)
                return Round1Phases;
            if (round <= 4)
                return Round2To4Phases;
            return Round5PlusPhases;
        }
    }
}
