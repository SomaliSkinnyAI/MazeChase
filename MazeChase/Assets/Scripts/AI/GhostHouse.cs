using UnityEngine;
using MazeChase.Game;

namespace MazeChase.AI
{
    /// <summary>
    /// Manages ghost exit order from the ghost house. Controls when each ghost
    /// is released based on pellet counters and a global inactivity timer.
    ///
    /// Ghost 0 (Shadow) always starts outside the house.
    /// Ghosts 1-3 start inside and are released based on:
    ///   - Per-ghost pellet thresholds (Speedy: 0, Bashful: 30, Pokey: 60)
    ///   - A global timer that releases the next ghost if no pellets are
    ///     eaten for approximately 4 seconds
    ///
    /// When a ghost is eaten and returns to the house, it re-enters and then
    /// exits again without waiting for pellet counters.
    /// </summary>
    public class GhostHouse : MonoBehaviour
    {
        // ── Configuration ─────────────────────────────────────────────
        [Header("References")]
        [SerializeField] private Ghost[] ghosts;
        [SerializeField] private MazeRenderer mazeRenderer;

        [Header("Pellet Release Thresholds")]
        [Tooltip("Number of pellets eaten before each ghost is released. Index matches ghost index.")]
        [SerializeField] private int[] pelletThresholds = { 0, 0, 30, 60 };

        [Header("Global Release Timer")]
        [Tooltip("Seconds of inactivity (no pellets eaten) before forcing next ghost release.")]
        [SerializeField] private float globalReleaseTimeout = 4f;

        // ── Runtime state ─────────────────────────────────────────────
        private int pelletsEaten;
        private float timeSinceLastPellet;
        private bool[] hasBeenReleased;
        private bool initialized;

        // Ghost house positions
        private Vector2Int houseCenter = new Vector2Int(13, 14);
        private Vector2Int houseDoor = new Vector2Int(13, 12);

        // Exit tile (above the ghost door)
        private static readonly Vector2Int HouseExitTile = new Vector2Int(13, 19);
        // Inside-house tiles for each ghost slot
        private static readonly Vector2Int[] HouseSlots =
        {
            new Vector2Int(13, 19), // Ghost 0 — starts outside (not used as house slot)
            new Vector2Int(13, 17), // Ghost 1 — center of house
            new Vector2Int(11, 17), // Ghost 2 — left side
            new Vector2Int(15, 17)  // Ghost 3 — right side
        };

        // ── Public properties ─────────────────────────────────────────
        public int PelletsEaten => pelletsEaten;
        public Vector2Int HouseCenter => houseCenter;
        public Vector2Int HouseDoor => houseDoor;

        /// <summary>
        /// Returns whether the specified ghost has been released from the house.
        /// </summary>
        public bool IsReleased(int ghostIndex)
        {
            return ghostIndex >= 0 && ghostIndex < hasBeenReleased.Length && hasBeenReleased[ghostIndex];
        }

        // ══════════════════════════════════════════════════════════════
        // Initialization
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Initialize with external references. Call this before the first frame.
        /// </summary>
        public void Init(Ghost[] ghostArray, MazeRenderer renderer)
        {
            ghosts = ghostArray;
            mazeRenderer = renderer;
            InitializeInternal();
        }

        private void Start()
        {
            InitializeInternal();
        }

        private void InitializeInternal()
        {
            if (initialized)
                return;

            if (ghosts == null || ghosts.Length == 0)
            {
                ghosts = FindObjectsByType<Ghost>(FindObjectsSortMode.None);
                // Sort by ghost index
                System.Array.Sort(ghosts, (a, b) => a.GhostIndex.CompareTo(b.GhostIndex));
            }

            if (mazeRenderer == null)
                mazeRenderer = FindFirstObjectByType<MazeRenderer>();

            hasBeenReleased = new bool[4];
            pelletsEaten = 0;
            timeSinceLastPellet = 0f;

            // Ghost 0 (Shadow) starts outside the house — always released
            hasBeenReleased[0] = true;

            // Initialize ghost positions
            for (int i = 0; i < ghosts.Length && i < 4; i++)
            {
                if (i == 0)
                {
                    // Shadow starts outside
                    ghosts[i].Initialize(i, HouseExitTile, outsideHouse: true);
                }
                else
                {
                    // Other ghosts start inside
                    Vector2Int slot = (i < HouseSlots.Length) ? HouseSlots[i] : HouseSlots[1];
                    ghosts[i].Initialize(i, slot, outsideHouse: false);
                }
            }

            initialized = true;
        }

        // ══════════════════════════════════════════════════════════════
        // Unity lifecycle
        // ══════════════════════════════════════════════════════════════

        private void Update()
        {
            if (!initialized || ghosts == null)
                return;

            // Global timer: if no pellets eaten recently, release next ghost
            timeSinceLastPellet += Time.deltaTime;

            if (timeSinceLastPellet >= globalReleaseTimeout)
            {
                ReleaseNextWaitingGhost();
                timeSinceLastPellet = 0f; // Reset timer after releasing
            }
        }

        // ══════════════════════════════════════════════════════════════
        // Public methods
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Called when a pellet or energizer is eaten. Increments the counter
        /// and checks if any ghost should be released.
        /// </summary>
        public void OnPelletEaten()
        {
            pelletsEaten++;
            timeSinceLastPellet = 0f;

            // Check each ghost for release eligibility
            for (int i = 1; i < 4; i++)
            {
                if (hasBeenReleased[i])
                    continue;

                if (!ShouldRelease(i))
                    continue;

                Ghost ghost = GetGhost(i);
                if (ghost != null && ghost.CurrentState == GhostState.InHouse)
                {
                    ReleaseGhost(ghost);
                    hasBeenReleased[i] = true;
                }
            }
        }

        /// <summary>
        /// Determines whether the ghost at the given index has met its
        /// pellet threshold for release.
        /// </summary>
        public bool ShouldRelease(int ghostIndex)
        {
            if (ghostIndex < 0 || ghostIndex >= pelletThresholds.Length)
                return false;

            return pelletsEaten >= pelletThresholds[ghostIndex];
        }

        /// <summary>
        /// Initiate the ghost's exit sequence from the house.
        /// Sets the ghost to ExitingHouse state so it navigates out.
        /// </summary>
        public void ReleaseGhost(Ghost ghost)
        {
            if (ghost == null)
                return;

            ghost.SetState(GhostState.ExitingHouse);
            Debug.Log($"[GhostHouse] Releasing ghost {ghost.GhostIndex} ({ghost.name})");
        }

        /// <summary>
        /// Called when a ghost has been eaten and is returning to the house.
        /// Marks it as unreleased so it can re-enter and exit.
        /// </summary>
        public void GhostReturningToHouse(int ghostIndex)
        {
            if (ghostIndex < 0 || ghostIndex >= hasBeenReleased.Length)
                return;
            // The ghost is in Eaten state and will navigate to the house.
            // Once it arrives, Ghost.OnReachedTileCenter sets it to ExitingHouse,
            // allowing it to leave again without waiting for pellet counters.
            hasBeenReleased[ghostIndex] = false;
        }

        /// <summary>
        /// Called when an eaten ghost reaches the house door.
        /// Transitions it through the house and back out.
        /// </summary>
        public void GhostEnteredHouse(int ghostIndex)
        {
            if (ghostIndex < 0 || ghostIndex >= ghosts.Length)
                return;

            Ghost ghost = GetGhost(ghostIndex);
            if (ghost == null)
                return;

            ghost.SetState(GhostState.InHouse);
            // Mark as unreleased; the Update loop will release via global timer
            // or immediately since threshold is already met
            hasBeenReleased[ghostIndex] = false;
        }

        /// <summary>
        /// Reset the ghost house for a new round. Resets pellet counters and
        /// places all ghosts back in their starting positions.
        /// </summary>
        public void ResetForNewRound()
        {
            pelletsEaten = 0;
            timeSinceLastPellet = 0f;
            hasBeenReleased = new bool[4];
            hasBeenReleased[0] = true; // Shadow always starts outside

            if (ghosts == null)
                return;

            // Ghost 0 starts outside
            if (ghosts.Length > 0 && ghosts[0] != null)
                ghosts[0].SetState(GhostState.Scatter);

            // Ghost 1 (Speedy) has threshold 0 — release immediately
            hasBeenReleased[1] = true;
            if (ghosts.Length > 1 && ghosts[1] != null)
            {
                ghosts[1].SetState(GhostState.ExitingHouse);
            }

            // Ghosts 2-3 start inside, wait for pellet thresholds
            for (int i = 2; i < ghosts.Length && i < 4; i++)
            {
                if (ghosts[i] != null)
                {
                    ghosts[i].ResetToSpawn();
                    ghosts[i].SetState(GhostState.InHouse);
                }
            }
        }

        /// <summary>
        /// Reset after the player loses a life. Pellet count persists,
        /// but ghosts return to their starting positions. Ghosts that were
        /// already released will be re-released based on current pellet count.
        /// </summary>
        public void ResetAfterDeath()
        {
            timeSinceLastPellet = 0f;

            if (ghosts == null)
                return;

            for (int i = 0; i < ghosts.Length && i < 4; i++)
            {
                ghosts[i].ResetToSpawn();
            }

            // Re-check release for ghosts that already met their threshold
            hasBeenReleased = new bool[4];
            hasBeenReleased[0] = true;

            for (int i = 1; i < 4; i++)
            {
                if (ShouldRelease(i))
                {
                    Ghost ghost = GetGhost(i);
                    if (ghost != null)
                    {
                        ReleaseGhost(ghost);
                        hasBeenReleased[i] = true;
                    }
                }
            }
        }

        /// <summary>
        /// Apply round-specific tuning to all ghosts.
        /// </summary>
        public void ApplyRoundTuning(int round)
        {
            RoundTuning tuning = RoundTuningData.GetTuning(round);
            for (int i = 0; i < ghosts.Length; i++)
            {
                ghosts[i].ApplyTuning(tuning);
            }
        }

        /// <summary>
        /// Freeze all ghosts (e.g., during level transition or death animation).
        /// </summary>
        public void FreezeAll()
        {
            for (int i = 0; i < ghosts.Length; i++)
            {
                ghosts[i].Freeze();
            }
        }

        /// <summary>
        /// Unfreeze all ghosts.
        /// </summary>
        public void UnfreezeAll()
        {
            for (int i = 0; i < ghosts.Length; i++)
            {
                ghosts[i].Unfreeze();
            }
        }

        /// <summary>
        /// Start frightened mode on all eligible ghosts.
        /// </summary>
        public void FrightenAll(float duration, int flashCount)
        {
            for (int i = 0; i < ghosts.Length; i++)
            {
                ghosts[i].StartFrightened(duration, flashCount);
            }
        }

        // ══════════════════════════════════════════════════════════════
        // Internal helpers
        // ══════════════════════════════════════════════════════════════

        private Ghost GetGhost(int index)
        {
            if (ghosts == null)
                return null;

            for (int i = 0; i < ghosts.Length; i++)
            {
                if (ghosts[i].GhostIndex == index)
                    return ghosts[i];
            }
            return null;
        }

        /// <summary>
        /// Release the next ghost that is still waiting in the house.
        /// Used by the global inactivity timer.
        /// </summary>
        private void ReleaseNextWaitingGhost()
        {
            for (int i = 1; i < 4; i++)
            {
                if (hasBeenReleased[i])
                    continue;

                Ghost ghost = GetGhost(i);
                if (ghost != null && ghost.CurrentState == GhostState.InHouse)
                {
                    ReleaseGhost(ghost);
                    hasBeenReleased[i] = true;
                    return; // Only release one at a time
                }
            }
        }
    }
}
