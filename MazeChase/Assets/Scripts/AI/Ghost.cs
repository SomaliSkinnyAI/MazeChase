using UnityEngine;
using MazeChase.Game;
using MazeChase.Core;

namespace MazeChase.AI
{
    /// <summary>
    /// Core ghost MonoBehaviour controlling movement, state transitions, targeting,
    /// and visual appearance. Ghosts navigate the maze on a tile grid using smooth
    /// lerp-based movement and choose directions at intersections by minimizing
    /// Euclidean distance to a target tile (classic Pac-Man AI).
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class Ghost : MonoBehaviour
    {
        // -- Inspector fields --
        [Header("Identity")]
        [Tooltip("Ghost index 0-3: Shadow, Speedy, Bashful, Pokey")]
        [SerializeField] private int ghostIndex;

        [Header("References")]
        [SerializeField] private PlayerController player;
        [SerializeField] private MazeRenderer mazeRenderer;
        [SerializeField] private GhostModeTimer ghostModeTimer;

        [Header("Speed (tiles per second)")]
        [SerializeField] private float normalSpeed = 3.75f;
        [SerializeField] private float frightenedSpeed = 2.5f;
        [SerializeField] private float eatenSpeed = 8.0f;
        [SerializeField] private float tunnelSpeed = 2.0f;

        // -- Runtime state --
        private Vector2Int currentTile;
        private Vector2Int nextTile;
        private Direction currentDirection = Direction.Left;
        private Direction queuedDirection = Direction.None;
        private GhostState currentState = GhostState.InHouse;
        private bool frozen;

        // Movement interpolation
        private Vector3 moveOrigin;
        private Vector3 moveDestination;
        private float moveProgress;
        private bool isMoving;

        // Targeting
        private IGhostTargetStrategy targetStrategy;
        private Ghost[] allGhosts;

        // Visual
        private SpriteRenderer spriteRenderer;
        private Color ghostColor;

        // Animation frames (2 frames for wavy bottom)
        private Sprite[] bodyFrames;       // 2 frames for normal body animation
        private Sprite[] frightenedFrames; // 2 frames for frightened body animation
        private float animTimer;
        private int currentFrame;
        private const float AnimFrameDuration = 0.15f;

        // Eye child objects (pupils move based on direction)
        private Transform leftPupil;
        private Transform rightPupil;
        private SpriteRenderer leftPupilRenderer;
        private SpriteRenderer rightPupilRenderer;
        private Transform leftEyeWhite;
        private Transform rightEyeWhite;
        private SpriteRenderer leftEyeWhiteRenderer;
        private SpriteRenderer rightEyeWhiteRenderer;
        private const float PupilOffsetAmount = 0.025f;
        // Base positions for eye children (in local space)
        private Vector3 leftEyeBasePos;
        private Vector3 rightEyeBasePos;
        private Vector3 leftPupilBasePos;
        private Vector3 rightPupilBasePos;

        // Frightened flashing
        private float frightenedTimer;
        private float frightenedDuration;
        private int frightenedFlashCount;
        private bool frightenedFlashing;

        // Spawn position
        private Vector2Int spawnTile;
        private bool startedOutsideHouse;

        // Elroy mode (Blinky speedup when few dots remain)
        private bool _elroy1;
        private bool _elroy2;
        private float _elroy1Speed;
        private float _elroy2Speed;

        // -- Public properties --
        public Vector2Int CurrentTile => currentTile;
        public Direction CurrentDirection => currentDirection;
        public GhostState CurrentState => currentState;
        public int GhostIndex => ghostIndex;

        // -- Ghost colors --
        private static readonly Color[] GhostColors =
        {
            Color.red,                          // 0 - Shadow (Blinky)
            new Color(1f, 0.7f, 0.8f),          // 1 - Speedy (Pinky)
            Color.cyan,                         // 2 - Bashful (Inky)
            new Color(1f, 0.6f, 0f)             // 3 - Pokey (Clyde)
        };

        // -- Direction tie-breaking priority --
        // When distances are equal: Up > Left > Down > Right
        private static readonly Direction[] DirectionPriority =
        {
            Direction.Up,
            Direction.Left,
            Direction.Down,
            Direction.Right
        };

        // -- Ghost house navigation targets --
        private static readonly Vector2Int HouseDoorTile = new Vector2Int(13, 12);
        private static readonly Vector2Int HouseExitTile = new Vector2Int(13, 11);

        // ================================================================
        // Unity lifecycle
        // ================================================================

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
        }

        private void Start()
        {
            // Auto-find references if not assigned via inspector
            if (player == null)
                player = FindFirstObjectByType<PlayerController>();

            if (mazeRenderer == null)
                mazeRenderer = FindFirstObjectByType<MazeRenderer>();

            if (ghostModeTimer == null)
                ghostModeTimer = FindFirstObjectByType<GhostModeTimer>();

            // Initialize targeting strategy
            targetStrategy = CreateTargetStrategy(ghostIndex);

            // Find all ghosts in the scene
            allGhosts = FindObjectsByType<Ghost>(FindObjectsSortMode.None);

            // Record spawn tile
            spawnTile = currentTile;
        }

        private void Update()
        {
            if (frozen)
                return;

            UpdateFrightenedVisuals();
            UpdateBodyAnimation();
            UpdateEyeDirection();
            Move();
        }

        // ================================================================
        // Initialization
        // ================================================================

        /// <summary>
        /// Initialize ghost position and state. Called by GhostHouse or level setup.
        /// </summary>
        public void Initialize(int index, Vector2Int tile, bool outsideHouse)
        {
            ghostIndex = index;

            if (spriteRenderer == null)
                spriteRenderer = GetComponent<SpriteRenderer>();
            if (mazeRenderer == null)
                mazeRenderer = FindFirstObjectByType<MazeChase.Game.MazeRenderer>();

            // Set ghost color based on index
            int colorIdx = Mathf.Clamp(ghostIndex, 0, GhostColors.Length - 1);
            ghostColor = GhostColors[colorIdx];
            CreateGhostSprite();

            currentTile = tile;
            spawnTile = tile;
            startedOutsideHouse = outsideHouse;

            if (mazeRenderer != null)
            {
                transform.position = mazeRenderer.TileToWorld(tile.x, tile.y);
            }

            moveOrigin = transform.position;
            moveDestination = transform.position;
            moveProgress = 1f;
            isMoving = false;

            currentDirection = Direction.Left;

            if (outsideHouse)
                currentState = GhostState.Scatter;
            else
                currentState = GhostState.InHouse;

            ApplyVisuals();
        }

        /// <summary>
        /// Configure speeds from round tuning data.
        /// </summary>
        public void ApplyTuning(RoundTuning tuning)
        {
            normalSpeed = tuning.GhostSpeed;
            frightenedSpeed = tuning.FrightenedGhostSpeed;
            eatenSpeed = tuning.EatenGhostSpeed;
            tunnelSpeed = tuning.TunnelGhostSpeed;
            frightenedDuration = tuning.FrightenedDuration;
            frightenedFlashCount = tuning.FrightenedFlashCount;
        }

        // ================================================================
        // Movement
        // ================================================================

        private void Move()
        {
            if (currentState == GhostState.InHouse)
            {
                // Bob up and down inside the ghost house
                BobInHouse();
                return;
            }

            float speed = GetCurrentSpeed();

            if (isMoving)
            {
                // Continue interpolating toward the next tile
                float tileDistance = Vector3.Distance(moveOrigin, moveDestination);
                if (tileDistance > 0.001f)
                {
                    moveProgress += (speed / tileDistance) * Time.deltaTime;
                }
                else
                {
                    moveProgress = 1f;
                }

                transform.position = Vector3.Lerp(moveOrigin, moveDestination, moveProgress);

                if (moveProgress >= 1f)
                {
                    // Arrived at tile center
                    moveProgress = 1f;
                    transform.position = moveDestination;
                    currentTile = nextTile;
                    isMoving = false;

                    OnReachedTileCenter();
                }
            }

            if (!isMoving)
            {
                // Choose next direction and start moving
                Direction chosenDir = ChooseDirection();
                if (chosenDir != Direction.None)
                {
                    StartMoveInDirection(chosenDir);
                }
            }
        }

        private void BobInHouse()
        {
            // Simple vertical bobbing animation while waiting in the ghost house
            float bobSpeed = 2f;
            float bobAmount = 0.15f;
            Vector3 basePos = mazeRenderer != null
                ? (Vector3)mazeRenderer.TileToWorld(currentTile.x, currentTile.y)
                : transform.position;
            float yOffset = Mathf.Sin(Time.time * bobSpeed) * bobAmount;
            transform.position = basePos + new Vector3(0f, yOffset, 0f);
        }

        private void StartMoveInDirection(Direction dir)
        {
            if (dir == Direction.None || mazeRenderer == null)
                return;

            Vector2 dirVec = DirectionHelper.ToVector(dir);
            Vector2Int targetTile = currentTile + new Vector2Int(
                Mathf.RoundToInt(dirVec.x),
                Mathf.RoundToInt(dirVec.y)
            );

            // Handle tunnel wrapping
            targetTile = WrapTunnel(targetTile);

            if (!CanEnterTile(targetTile))
                return;

            nextTile = targetTile;
            currentDirection = dir;
            moveOrigin = transform.position;
            moveDestination = mazeRenderer.TileToWorld(nextTile.x, nextTile.y);
            moveProgress = 0f;
            isMoving = true;
        }

        private void OnReachedTileCenter()
        {
            // Check if we reached the ghost house after being eaten
            if (currentState == GhostState.Eaten)
            {
                if (currentTile == HouseDoorTile || IsGhostHouseTile(currentTile))
                {
                    SetState(GhostState.ExitingHouse);
                    return;
                }
            }

            // Check if we've exited the ghost house
            if (currentState == GhostState.ExitingHouse)
            {
                if (currentTile == HouseExitTile || !IsGhostHouseTile(currentTile))
                {
                    // We've exited -- adopt the active global mode.
                    SetState(GetModeAfterHouseExit());
                    return;
                }
            }
        }

        private float GetCurrentSpeed()
        {
            MazeTile tile = MazeData.GetTile(currentTile.x, currentTile.y);
            bool inTunnel = tile == MazeTile.Tunnel;

            switch (currentState)
            {
                case GhostState.Frightened:
                    return frightenedSpeed;
                case GhostState.Eaten:
                    return eatenSpeed;
                default:
                    if (inTunnel) return tunnelSpeed;
                    // Elroy mode: Blinky (ghost 0) speeds up when few dots remain
                    if (ghostIndex == 0)
                    {
                        if (_elroy2) return _elroy2Speed;
                        if (_elroy1) return _elroy1Speed;
                    }
                    return normalSpeed;
            }
        }

        /// <summary>
        /// Called by GameplaySceneSetup to update Elroy state based on remaining pellets.
        /// Blinky gets faster as fewer pellets remain.
        /// </summary>
        public void UpdateElroyState(int pelletsRemaining, int totalPellets)
        {
            if (ghostIndex != 0) return;

            // Elroy 1: activates when 40% of pellets remain
            int elroy1Threshold = Mathf.RoundToInt(totalPellets * 0.40f);
            // Elroy 2: activates when 20% of pellets remain
            int elroy2Threshold = Mathf.RoundToInt(totalPellets * 0.20f);

            _elroy1Speed = normalSpeed * 1.15f;  // 15% faster
            _elroy2Speed = normalSpeed * 1.30f;  // 30% faster

            _elroy1 = pelletsRemaining <= elroy1Threshold;
            _elroy2 = pelletsRemaining <= elroy2Threshold;
        }

        private Vector2Int WrapTunnel(Vector2Int tile)
        {
            int width = MazeData.Width;
            if (tile.x < 0)
                tile.x = width - 1;
            else if (tile.x >= width)
                tile.x = 0;
            return tile;
        }

        private bool CanEnterTile(Vector2Int tile)
        {
            // Wrap for bounds check
            tile = WrapTunnel(tile);

            if (!MazeData.IsWalkable(tile.x, tile.y))
                return false;

            MazeTile tileType = MazeData.GetTile(tile.x, tile.y);

            // Only eaten ghosts and ghosts exiting/in house can pass through the door
            if (tileType == MazeTile.GhostDoor)
            {
                return currentState == GhostState.Eaten
                    || currentState == GhostState.InHouse
                    || currentState == GhostState.ExitingHouse;
            }

            // Ghosts normally cannot enter the ghost house area
            if (tileType == MazeTile.GhostHouse && currentState != GhostState.Eaten
                && currentState != GhostState.InHouse
                && currentState != GhostState.ExitingHouse)
            {
                return false;
            }

            return true;
        }

        private bool IsGhostHouseTile(Vector2Int tile)
        {
            MazeTile tileType = MazeData.GetTile(tile.x, tile.y);
            return tileType == MazeTile.GhostHouse || tileType == MazeTile.GhostDoor;
        }

        // ================================================================
        // Direction choice (core AI)
        // ================================================================

        /// <summary>
        /// At the current tile, choose the direction that minimizes Euclidean
        /// distance to the target tile. Ghosts cannot reverse direction. In case
        /// of a tie, prefer Up > Left > Down > Right (classic Pac-Man behavior).
        /// </summary>
        public Direction ChooseDirection()
        {
            Vector2Int target = GetTargetTile();
            Direction reverse = DirectionHelper.Opposite(currentDirection);

            Direction bestDir = Direction.None;
            float bestDist = float.MaxValue;

            for (int i = 0; i < DirectionPriority.Length; i++)
            {
                Direction dir = DirectionPriority[i];

                // Cannot reverse
                if (dir == reverse)
                    continue;

                Vector2 dirVec = DirectionHelper.ToVector(dir);
                Vector2Int neighbor = currentTile + new Vector2Int(
                    Mathf.RoundToInt(dirVec.x),
                    Mathf.RoundToInt(dirVec.y)
                );

                neighbor = WrapTunnel(neighbor);

                if (!CanEnterTile(neighbor))
                    continue;

                // Euclidean distance from the neighbor tile to the target
                float dx = neighbor.x - target.x;
                float dy = neighbor.y - target.y;
                float dist = dx * dx + dy * dy; // Compare squared distance (avoids sqrt)

                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestDir = dir;
                }
                // Ties are broken by DirectionPriority order (Up > Left > Down > Right)
                // since we iterate in that order and use strict less-than
            }

            return bestDir;
        }

        /// <summary>
        /// Returns the current target tile based on ghost state.
        /// </summary>
        public Vector2Int GetTargetTile()
        {
            if (targetStrategy == null)
                return currentTile;

            switch (currentState)
            {
                case GhostState.Chase:
                    return targetStrategy.GetChaseTarget(this, player, allGhosts);

                case GhostState.Scatter:
                    return targetStrategy.GetScatterTarget(this);

                case GhostState.Frightened:
                    // Random target -- effectively random movement at intersections
                    return GetRandomTarget();

                case GhostState.Eaten:
                    // Eaten ghosts head for the house door to regenerate.
                    return HouseDoorTile;

                case GhostState.ExitingHouse:
                    // Leave the house through the exit tile above the door.
                    return HouseExitTile;

                case GhostState.InHouse:
                    return currentTile;

                default:
                    return currentTile;
            }
        }

        private Vector2Int GetRandomTarget()
        {
            // In frightened mode, ghosts choose a random direction at each
            // intersection. We simulate this by picking a pseudo-random target
            // that changes each time they reach a tile.
            int hash = currentTile.x * 31 + currentTile.y * 17 +
                       Mathf.FloorToInt(Time.time * 3f);
            int rx = (hash % MazeData.Width + MazeData.Width) % MazeData.Width;
            int ry = ((hash / MazeData.Width) % MazeData.Height + MazeData.Height) % MazeData.Height;
            return new Vector2Int(rx, ry);
        }

        private GhostState GetModeAfterHouseExit()
        {
            if (ghostModeTimer == null)
                ghostModeTimer = FindFirstObjectByType<GhostModeTimer>();

            if (ghostModeTimer != null &&
                (ghostModeTimer.CurrentMode == GhostState.Scatter || ghostModeTimer.CurrentMode == GhostState.Chase))
            {
                return ghostModeTimer.CurrentMode;
            }

            return GhostState.Scatter;
        }

        // ================================================================
        // State management
        // ================================================================

        /// <summary>
        /// Transition to a new ghost state. Handles direction reversal on
        /// scatter/chase mode switches and visual updates.
        /// </summary>
        public void SetState(GhostState newState)
        {
            GhostState previousState = currentState;

            // Ghosts still waiting inside the house should ignore the live mode
            // until they are explicitly released. Ghosts already exiting the house
            // may still transition into scatter/chase once they reach open play.
            if (currentState == GhostState.InHouse
                && (newState == GhostState.Scatter || newState == GhostState.Chase
                    || newState == GhostState.Frightened))
            {
                return;
            }

            // Ghosts exiting the house should not be frightened until they fully
            // rejoin normal play, but they can adopt scatter/chase on exit.
            if (currentState == GhostState.ExitingHouse && newState == GhostState.Frightened)
            {
                return;
            }

            // Don't interrupt eaten state with frightened/scatter/chase
            if (currentState == GhostState.Eaten &&
                (newState == GhostState.Scatter || newState == GhostState.Chase
                 || newState == GhostState.Frightened))
            {
                return;
            }

            currentState = newState;

            // Direction reversal on scatter <-> chase transitions
            bool wasScatterOrChase = previousState == GhostState.Scatter || previousState == GhostState.Chase;
            bool isScatterOrChase = newState == GhostState.Scatter || newState == GhostState.Chase;
            if (wasScatterOrChase && isScatterOrChase && previousState != newState)
            {
                ReverseDirection();
            }

            // Frightened activation causes reversal
            if (newState == GhostState.Frightened && wasScatterOrChase)
            {
                ReverseDirection();
                frightenedTimer = 0f;
                frightenedFlashing = false;
            }

            if (newState == GhostState.ExitingHouse && previousState != GhostState.ExitingHouse)
            {
                // Reset the direction so the ghost can choose the cleanest route
                // out of the house instead of being blocked by reverse rules.
                currentDirection = Direction.None;
            }

            ApplyVisuals();
        }

        /// <summary>
        /// Force reverse the ghost's current direction. Called when the global
        /// mode switches between scatter and chase, or when frightened activates.
        /// </summary>
        public void ReverseDirection()
        {
            currentDirection = DirectionHelper.Opposite(currentDirection);

            // If currently moving between tiles, swap origin/destination
            if (isMoving)
            {
                Vector3 temp = moveOrigin;
                moveOrigin = moveDestination;
                moveDestination = temp;

                // Also swap current/next tile
                Vector2Int tempTile = currentTile;
                currentTile = nextTile;
                nextTile = tempTile;

                moveProgress = 1f - moveProgress;
            }
        }

        /// <summary>
        /// Return the ghost to its initial spawn position and state.
        /// </summary>
        public void ResetToSpawn()
        {
            currentTile = spawnTile;

            if (mazeRenderer != null)
                transform.position = mazeRenderer.TileToWorld(spawnTile.x, spawnTile.y);

            moveOrigin = transform.position;
            moveDestination = transform.position;
            moveProgress = 1f;
            isMoving = false;

            currentDirection = Direction.Left;
            frightenedTimer = 0f;
            frightenedFlashing = false;
            frozen = false;

            if (startedOutsideHouse)
                currentState = GhostState.Scatter;
            else
                currentState = GhostState.InHouse;

            ApplyVisuals();
        }

        /// <summary>Stop all ghost movement and AI.</summary>
        public void Freeze()
        {
            frozen = true;
        }

        /// <summary>Resume ghost movement and AI.</summary>
        public void Unfreeze()
        {
            frozen = false;
        }

        /// <summary>
        /// Called externally when frightened mode should start (e.g., energizer eaten).
        /// </summary>
        public void StartFrightened(float duration, int flashCount)
        {
            frightenedDuration = duration;
            frightenedFlashCount = flashCount;

            if (duration <= 0f)
                return; // Some rounds have no frightened time

            if (currentState == GhostState.Frightened)
            {
                frightenedTimer = 0f;
                frightenedFlashing = false;
                ApplyVisuals();
                return;
            }

            SetState(GhostState.Frightened);
        }

        // ================================================================
        // Visuals
        // ================================================================

        private bool _spriteCreated;

        private void CreateGhostSprite()
        {
            // Guard against duplicate creation (Initialize is called multiple times)
            if (_spriteCreated)
            {
                // Just update the color
                spriteRenderer.color = ghostColor;
                return;
            }
            _spriteCreated = true;

            if (RuntimeExecutionMode.SuppressPresentation)
            {
                if (spriteRenderer != null)
                    spriteRenderer.enabled = false;
                return;
            }

            // Create 2 body animation frames (wavy bottom offset between frames)
            // and 2 frightened animation frames. Eyes/pupils are separate child objects.
            const int size = 48;
            const float worldSize = 0.38f;
            float ppu = size / worldSize;

            bodyFrames = new Sprite[2];
            frightenedFrames = new Sprite[2];

            for (int frame = 0; frame < 2; frame++)
            {
                bodyFrames[frame] = CreateBodyTexture(size, ppu, frame, false);
                frightenedFrames[frame] = CreateBodyTexture(size, ppu, frame, true);
            }

            spriteRenderer.sprite = bodyFrames[0];
            spriteRenderer.color = ghostColor;
            spriteRenderer.sortingOrder = 8;

            // Create eye child objects (white ovals + dark pupils)
            CreateEyeChildren(size, ppu);
        }

        /// <summary>
        /// Generate a single ghost body sprite frame. The body has a rounded dome top
        /// and a 3-bump scalloped bottom. Frame 0 and frame 1 offset the scallop phase
        /// to create a wobble/floating animation. Eyes are NOT drawn on the body texture
        /// since they are separate child GameObjects.
        /// </summary>
        private Sprite CreateBodyTexture(int size, float ppu, int frame, bool frightened)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;

            float halfSize = size * 0.5f;
            float bodyRadius = halfSize * 0.92f;

            // Scallop parameters
            int scallops = 3;
            float scallopDepth = size * 0.10f;
            float bodyBottom = size * 0.10f;
            // Phase offset between frames for wobble animation
            float phaseOffset = frame * Mathf.PI / scallops;

            // Frightened mode: draw a small wavy mouth
            float mouthY = size * 0.35f;
            float mouthLeft = size * 0.25f;
            float mouthRight = size * 0.75f;

            for (int py = 0; py < size; py++)
            {
                for (int px = 0; px < size; px++)
                {
                    float dx = px + 0.5f - halfSize;
                    float dy = py + 0.5f - halfSize;

                    bool inBody = false;

                    if (py >= (int)(halfSize)) // Upper half: dome (semicircle)
                    {
                        float dist = Mathf.Sqrt(dx * dx + dy * dy);
                        inBody = dist <= bodyRadius;
                    }
                    else // Lower half: rectangle with scalloped bottom
                    {
                        bool horizontallyInside = Mathf.Abs(dx) <= bodyRadius;
                        if (horizontallyInside)
                        {
                            float normalizedX = (px + 0.5f - (halfSize - bodyRadius)) / (bodyRadius * 2f);
                            float wave = Mathf.Cos(normalizedX * scallops * Mathf.PI * 2f + phaseOffset);
                            float bottomEdge = bodyBottom + scallopDepth * (1f + wave) * 0.5f;
                            inBody = py + 0.5f >= bottomEdge;
                        }
                    }

                    if (!inBody)
                    {
                        tex.SetPixel(px, py, Color.clear);
                        continue;
                    }

                    if (frightened)
                    {
                        // Draw wavy mouth for frightened appearance
                        float fpx = px + 0.5f;
                        float fpy = py + 0.5f;
                        if (fpx >= mouthLeft && fpx <= mouthRight)
                        {
                            float mouthNorm = (fpx - mouthLeft) / (mouthRight - mouthLeft);
                            float mouthWave = mouthY + Mathf.Sin(mouthNorm * Mathf.PI * 4f) * 2f;
                            if (Mathf.Abs(fpy - mouthWave) < 1.2f)
                            {
                                tex.SetPixel(px, py, new Color(0.9f, 0.8f, 0.7f, 1f));
                                continue;
                            }
                        }

                        // Small dot eyes for frightened mode (drawn on body since pupils are hidden)
                        float frightenedEyeY = size * 0.55f;
                        float frightenedLeftX = size * 0.35f;
                        float frightenedRightX = size * 0.65f;
                        float frightenedEyeR = 2.0f;
                        float ldx = fpx - frightenedLeftX;
                        float ldy = fpy - frightenedEyeY;
                        float rdx = fpx - frightenedRightX;
                        float rdy = fpy - frightenedEyeY;
                        if ((ldx * ldx + ldy * ldy) <= frightenedEyeR * frightenedEyeR ||
                            (rdx * rdx + rdy * rdy) <= frightenedEyeR * frightenedEyeR)
                        {
                            tex.SetPixel(px, py, new Color(0.9f, 0.8f, 0.7f, 1f));
                            continue;
                        }
                    }

                    // Body: white (tinted by SpriteRenderer.color)
                    tex.SetPixel(px, py, Color.white);
                }
            }

            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), ppu);
        }

        /// <summary>
        /// Create child GameObjects for eye whites and pupils. These are positioned
        /// relative to the ghost body and the pupils shift based on movement direction.
        /// </summary>
        private void CreateEyeChildren(int size, float ppu)
        {
            // Eye positions in normalized coordinates (0..1 range mapped to sprite)
            float eyeYNorm = 0.62f;  // vertical center of eyes
            float leftXNorm = 0.33f;
            float rightXNorm = 0.67f;

            // Convert to local-space offsets from sprite center
            float worldSize = size / ppu;
            float lx = (leftXNorm - 0.5f) * worldSize;
            float rx = (rightXNorm - 0.5f) * worldSize;
            float ey = (eyeYNorm - 0.5f) * worldSize;

            leftEyeBasePos = new Vector3(lx, ey, 0f);
            rightEyeBasePos = new Vector3(rx, ey, 0f);

            // Eye white dimensions (8x10 pixels -> oval)
            int eyeW = 8;
            int eyeH = 10;
            Sprite eyeWhiteSprite = CreateOvalSprite(eyeW, eyeH, Color.white, ppu);

            // Pupil dimensions (4x5 pixels -> smaller oval)
            int pupilW = 4;
            int pupilH = 5;
            Color pupilColor = new Color(0.1f, 0.15f, 0.4f, 1f);
            Sprite pupilSprite = CreateOvalSprite(pupilW, pupilH, pupilColor, ppu);

            // Create left eye white
            leftEyeWhite = CreateChildSprite("LeftEyeWhite", eyeWhiteSprite, leftEyeBasePos, 9);
            leftEyeWhiteRenderer = leftEyeWhite.GetComponent<SpriteRenderer>();

            // Create right eye white
            rightEyeWhite = CreateChildSprite("RightEyeWhite", eyeWhiteSprite, rightEyeBasePos, 9);
            rightEyeWhiteRenderer = rightEyeWhite.GetComponent<SpriteRenderer>();

            // Pupil base positions are centered on the eye whites
            leftPupilBasePos = leftEyeBasePos;
            rightPupilBasePos = rightEyeBasePos;

            // Create left pupil
            leftPupil = CreateChildSprite("LeftPupil", pupilSprite, leftPupilBasePos, 10);
            leftPupilRenderer = leftPupil.GetComponent<SpriteRenderer>();

            // Create right pupil
            rightPupil = CreateChildSprite("RightPupil", pupilSprite, rightPupilBasePos, 10);
            rightPupilRenderer = rightPupil.GetComponent<SpriteRenderer>();
        }

        private Transform CreateChildSprite(string name, Sprite sprite, Vector3 localPos, int sortOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPos;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = sortOrder;
            return go.transform;
        }

        /// <summary>
        /// Create a small oval sprite of given pixel dimensions.
        /// </summary>
        private static Sprite CreateOvalSprite(int w, int h, Color color, float ppu)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            float cx = w * 0.5f;
            float cy = h * 0.5f;
            float rx = w * 0.5f;
            float ry = h * 0.5f;
            for (int py = 0; py < h; py++)
            {
                for (int px = 0; px < w; px++)
                {
                    float nx = (px + 0.5f - cx) / rx;
                    float ny = (py + 0.5f - cy) / ry;
                    tex.SetPixel(px, py, (nx * nx + ny * ny) <= 1f ? color : Color.clear);
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), ppu);
        }

        /// <summary>
        /// Shift pupil positions based on the ghost's current movement direction.
        /// </summary>
        private void UpdateEyeDirection()
        {
            if (leftPupil == null || rightPupil == null)
                return;

            Vector3 offset = Vector3.zero;
            switch (currentDirection)
            {
                case Direction.Right:
                    offset = new Vector3(PupilOffsetAmount, 0f, 0f);
                    break;
                case Direction.Left:
                    offset = new Vector3(-PupilOffsetAmount, 0f, 0f);
                    break;
                case Direction.Up:
                    offset = new Vector3(0f, PupilOffsetAmount, 0f);
                    break;
                case Direction.Down:
                    offset = new Vector3(0f, -PupilOffsetAmount, 0f);
                    break;
            }

            leftPupil.localPosition = leftPupilBasePos + offset;
            rightPupil.localPosition = rightPupilBasePos + offset;
        }

        /// <summary>
        /// Alternate between the two body animation frames to create a wavy bottom effect.
        /// </summary>
        private void UpdateBodyAnimation()
        {
            if (bodyFrames == null || bodyFrames.Length < 2)
                return;

            animTimer += Time.deltaTime;
            if (animTimer >= AnimFrameDuration)
            {
                animTimer -= AnimFrameDuration;
                currentFrame = 1 - currentFrame;

                if (currentState == GhostState.Frightened && frightenedFrames != null)
                {
                    spriteRenderer.sprite = frightenedFrames[currentFrame];
                }
                else if (currentState != GhostState.Eaten)
                {
                    spriteRenderer.sprite = bodyFrames[currentFrame];
                }
            }
        }

        private void ApplyVisuals()
        {
            if (spriteRenderer == null)
                return;

            if (RuntimeExecutionMode.SuppressPresentation)
            {
                spriteRenderer.enabled = false;
                transform.localScale = Vector3.one;
                return;
            }

            bool showEyes = true;
            bool showPupils = true;

            switch (currentState)
            {
                case GhostState.Frightened:
                    // Blue body with frightened face, hide normal eye children
                    spriteRenderer.color = Color.blue;
                    if (frightenedFrames != null && frightenedFrames.Length > 0)
                        spriteRenderer.sprite = frightenedFrames[currentFrame];
                    transform.localScale = Vector3.one;
                    showEyes = false;
                    showPupils = false;
                    break;

                case GhostState.Eaten:
                    // Only eyes visible -- hide the body sprite (fully transparent)
                    spriteRenderer.color = new Color(1f, 1f, 1f, 0f);
                    transform.localScale = Vector3.one;
                    break;

                default:
                    spriteRenderer.color = ghostColor;
                    if (bodyFrames != null && bodyFrames.Length > 0)
                        spriteRenderer.sprite = bodyFrames[currentFrame];
                    transform.localScale = Vector3.one;
                    break;
            }

            // Toggle eye visibility
            if (leftEyeWhiteRenderer != null)
                leftEyeWhiteRenderer.enabled = showEyes;
            if (rightEyeWhiteRenderer != null)
                rightEyeWhiteRenderer.enabled = showEyes;
            if (leftPupilRenderer != null)
                leftPupilRenderer.enabled = showPupils;
            if (rightPupilRenderer != null)
                rightPupilRenderer.enabled = showPupils;
        }

        private void UpdateFrightenedVisuals()
        {
            if (currentState != GhostState.Frightened)
                return;

            frightenedTimer += Time.deltaTime;

            // Check if frightened mode has ended
            if (frightenedTimer >= frightenedDuration)
            {
                // Revert to scatter (the GhostModeTimer will assign the correct mode)
                SetState(GhostState.Scatter);
                return;
            }

            // Pulse/throb effect: modulate scale between 0.95 and 1.05
            float pulseScale = 1f + 0.05f * Mathf.Sin(Time.time * 8f);
            transform.localScale = new Vector3(pulseScale, pulseScale, 1f);

            // Flash white near the end of frightened mode
            float flashStartTime = frightenedDuration - (frightenedFlashCount * 0.4f);
            if (frightenedTimer >= flashStartTime && frightenedFlashCount > 0)
            {
                frightenedFlashing = true;
                // Alternate between blue and white
                float flashCycle = (frightenedTimer - flashStartTime) % 0.4f;
                spriteRenderer.color = flashCycle < 0.2f ? Color.white : Color.blue;
            }
            else
            {
                spriteRenderer.color = Color.blue;
            }
        }

        // ================================================================
        // Targeting strategy factory
        // ================================================================

        private static IGhostTargetStrategy CreateTargetStrategy(int index)
        {
            switch (index)
            {
                case 0: return new ShadowTarget();
                case 1: return new SpeedyTarget();
                case 2: return new BashfulTarget();
                case 3: return new PokeyTarget();
                default: return new ShadowTarget();
            }
        }

        /// <summary>
        /// Allows overriding the target strategy at runtime (for testing or
        /// custom ghost behavior).
        /// </summary>
        public void SetTargetStrategy(IGhostTargetStrategy strategy)
        {
            targetStrategy = strategy;
        }
    }
}
