using UnityEngine;
using MazeChase.Game;

namespace MazeChase.AI
{
    /// <summary>
    /// Manages ghost release order from the house and keeps pellet-based release
    /// progress consistent across rounds and deaths.
    /// </summary>
    public class GhostHouse : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Ghost[] ghosts;
        [SerializeField] private MazeRenderer mazeRenderer;

        [Header("Pellet Release Thresholds")]
        [Tooltip("Number of pellets eaten before each ghost is released. Index matches ghost index.")]
        [SerializeField] private int[] pelletThresholds = { 0, 0, 30, 60 };

        [Header("Global Release Timer")]
        [Tooltip("Seconds of inactivity before forcing the next waiting ghost out.")]
        [SerializeField] private float globalReleaseTimeout = 4f;

        private int pelletsEaten;
        private float timeSinceLastPellet;
        private bool[] hasBeenReleased;
        private bool initialized;

        private static readonly Vector2Int HouseCenterTile = new Vector2Int(13, 14);
        private static readonly Vector2Int HouseDoorTile = new Vector2Int(13, 12);
        private static readonly Vector2Int[] StartTiles =
        {
            new Vector2Int(13, 11), // Blinky starts outside the house.
            new Vector2Int(13, 14), // Pinky starts in the center slot.
            new Vector2Int(12, 13), // Inky starts on the left slot.
            new Vector2Int(15, 13)  // Clyde starts on the right slot.
        };

        public int PelletsEaten => pelletsEaten;
        public Vector2Int HouseCenter => HouseCenterTile;
        public Vector2Int HouseDoor => HouseDoorTile;

        public bool IsReleased(int ghostIndex)
        {
            return hasBeenReleased != null
                && ghostIndex >= 0
                && ghostIndex < hasBeenReleased.Length
                && hasBeenReleased[ghostIndex];
        }

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

        private void Update()
        {
            if (!initialized || ghosts == null)
                return;

            timeSinceLastPellet += Time.deltaTime;
            if (timeSinceLastPellet >= globalReleaseTimeout)
            {
                ReleaseNextWaitingGhost();
                timeSinceLastPellet = 0f;
            }
        }

        public void OnPelletEaten()
        {
            pelletsEaten++;
            timeSinceLastPellet = 0f;

            for (int ghostIndex = 1; ghostIndex < 4; ghostIndex++)
            {
                if (IsReleased(ghostIndex) || !ShouldRelease(ghostIndex))
                    continue;

                Ghost ghost = GetGhost(ghostIndex);
                if (ghost == null || ghost.CurrentState != GhostState.InHouse)
                    continue;

                ReleaseGhost(ghost);
                hasBeenReleased[ghostIndex] = true;
            }
        }

        public bool ShouldRelease(int ghostIndex)
        {
            return ghostIndex >= 0
                && ghostIndex < pelletThresholds.Length
                && pelletsEaten >= pelletThresholds[ghostIndex];
        }

        public void ReleaseGhost(Ghost ghost)
        {
            if (ghost == null)
                return;

            ghost.SetState(GhostState.ExitingHouse);
            Debug.Log($"[GhostHouse] Releasing ghost {ghost.GhostIndex} ({ghost.name})");
        }

        public void GhostReturningToHouse(int ghostIndex)
        {
            if (hasBeenReleased == null || ghostIndex < 0 || ghostIndex >= hasBeenReleased.Length)
                return;

            hasBeenReleased[ghostIndex] = false;
        }

        public void GhostEnteredHouse(int ghostIndex)
        {
            Ghost ghost = GetGhost(ghostIndex);
            if (ghost == null)
                return;

            ghost.SetState(GhostState.InHouse);
            if (hasBeenReleased != null && ghostIndex >= 0 && ghostIndex < hasBeenReleased.Length)
                hasBeenReleased[ghostIndex] = false;
        }

        public void ResetForNewRound()
        {
            pelletsEaten = 0;
            timeSinceLastPellet = 0f;
            hasBeenReleased = new bool[4];
            hasBeenReleased[0] = true;

            if (ghosts == null)
                return;

            for (int ghostIndex = 0; ghostIndex < ghosts.Length && ghostIndex < 4; ghostIndex++)
            {
                Ghost ghost = ghosts[ghostIndex];
                if (ghost == null)
                    continue;

                ghost.ResetToSpawn();

                if (ghostIndex == 0)
                {
                    ghost.SetState(GhostState.Scatter);
                    continue;
                }

                if (ghostIndex == 1)
                {
                    ghost.SetState(GhostState.ExitingHouse);
                    hasBeenReleased[ghostIndex] = true;
                    continue;
                }

                ghost.SetState(GhostState.InHouse);
            }
        }

        public void ResetAfterDeath()
        {
            timeSinceLastPellet = 0f;

            if (ghosts == null)
                return;

            for (int ghostIndex = 0; ghostIndex < ghosts.Length && ghostIndex < 4; ghostIndex++)
            {
                Ghost ghost = ghosts[ghostIndex];
                if (ghost == null)
                    continue;

                ghost.ResetToSpawn();
            }

            hasBeenReleased = new bool[4];
            hasBeenReleased[0] = true;

            for (int ghostIndex = 1; ghostIndex < 4; ghostIndex++)
            {
                Ghost ghost = GetGhost(ghostIndex);
                if (ghost == null)
                    continue;

                if (ShouldRelease(ghostIndex))
                {
                    ReleaseGhost(ghost);
                    hasBeenReleased[ghostIndex] = true;
                }
                else
                {
                    ghost.SetState(GhostState.InHouse);
                }
            }
        }

        public void ApplyRoundTuning(int round)
        {
            if (ghosts == null)
                return;

            RoundTuning tuning = RoundTuningData.GetTuning(round);
            for (int i = 0; i < ghosts.Length; i++)
            {
                if (ghosts[i] != null)
                    ghosts[i].ApplyTuning(tuning);
            }
        }

        public void FreezeAll()
        {
            if (ghosts == null)
                return;

            for (int i = 0; i < ghosts.Length; i++)
            {
                if (ghosts[i] != null)
                    ghosts[i].Freeze();
            }
        }

        public void UnfreezeAll()
        {
            if (ghosts == null)
                return;

            for (int i = 0; i < ghosts.Length; i++)
            {
                if (ghosts[i] != null)
                    ghosts[i].Unfreeze();
            }
        }

        public void FrightenAll(float duration, int flashCount)
        {
            if (ghosts == null)
                return;

            for (int i = 0; i < ghosts.Length; i++)
            {
                if (ghosts[i] != null)
                    ghosts[i].StartFrightened(duration, flashCount);
            }
        }

        private void InitializeInternal()
        {
            if (initialized)
                return;

            if (ghosts == null || ghosts.Length == 0)
            {
                ghosts = FindObjectsByType<Ghost>(FindObjectsSortMode.None);
                System.Array.Sort(ghosts, (a, b) => a.GhostIndex.CompareTo(b.GhostIndex));
            }

            if (mazeRenderer == null)
                mazeRenderer = FindFirstObjectByType<MazeRenderer>();

            hasBeenReleased = new bool[4];
            pelletsEaten = 0;
            timeSinceLastPellet = 0f;
            hasBeenReleased[0] = true;

            for (int ghostIndex = 0; ghostIndex < ghosts.Length && ghostIndex < 4; ghostIndex++)
            {
                Ghost ghost = ghosts[ghostIndex];
                if (ghost == null)
                    continue;

                ghost.Initialize(ghostIndex, StartTiles[ghostIndex], outsideHouse: ghostIndex == 0);
            }

            initialized = true;
        }

        private Ghost GetGhost(int index)
        {
            if (ghosts == null)
                return null;

            for (int i = 0; i < ghosts.Length; i++)
            {
                Ghost ghost = ghosts[i];
                if (ghost != null && ghost.GhostIndex == index)
                    return ghost;
            }

            return null;
        }

        private void ReleaseNextWaitingGhost()
        {
            if (ghosts == null)
                return;

            for (int ghostIndex = 1; ghostIndex < 4; ghostIndex++)
            {
                if (IsReleased(ghostIndex))
                    continue;

                Ghost ghost = GetGhost(ghostIndex);
                if (ghost == null || ghost.CurrentState != GhostState.InHouse)
                    continue;

                ReleaseGhost(ghost);
                hasBeenReleased[ghostIndex] = true;
                return;
            }
        }
    }
}
