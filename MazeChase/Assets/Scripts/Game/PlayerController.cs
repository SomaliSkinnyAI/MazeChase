using System;
using UnityEngine;
using UnityEngine.InputSystem;
using MazeChase.Core;

namespace MazeChase.Game
{
    /// <summary>
    /// Grid-based player controller for the Pac-Man character.
    /// Reads input via Unity's new Input System, buffers the next desired direction,
    /// and smoothly lerps between tile centres at a configurable speed.
    /// </summary>
    public class PlayerController : MonoBehaviour
    {
        // -- Movement state --
        public enum MovementState
        {
            Stopped,
            Moving,
            Frozen
        }

        // -- Inspector fields --
        [Header("Movement")]
        [SerializeField] private float _tilesPerSecond = 5.0f;

        [Header("References")]
        [SerializeField] private MazeRenderer _mazeRenderer;

        // -- Public properties --
        public Vector2Int CurrentTile { get; private set; }
        public Direction CurrentDirection { get; private set; } = Direction.None;
        public Direction QueuedDirection { get; private set; } = Direction.None;
        public MovementState State { get; private set; } = MovementState.Stopped;

        // -- Events --
        /// <summary>
        /// Fired when the player arrives at a tile containing a Pellet or Energizer.
        /// Parameters: tile position, tile type.
        /// </summary>
        public event Action<Vector2Int, MazeTile> OnPelletCollected;

        /// <summary>
        /// Fired when the player dies (caught by a ghost).
        /// </summary>
        public event Action OnDeath;

        // -- Private state --
        private Vector2Int _targetTile;
        private Vector3 _originWorldPos;
        private Vector3 _targetWorldPos;
        private float _lerpT;
        private SpriteRenderer _spriteRenderer;
        private Sprite[] _mouthFrames;   // 0 = closed, 1 = half-open (30°), 2 = wide-open (60°)
        private int _mouthFrameIndex;
        private float _mouthAnimTimer;

        // Input actions created at runtime for WASD / Arrow keys.
        private InputAction _moveAction;

        // -- Unity lifecycle --

        private void Awake()
        {
            // Build a composite WASD / Arrow input action at runtime so the
            // component works without a pre-configured InputActionAsset.
            _moveAction = new InputAction("Move", InputActionType.Value);

            _moveAction.AddCompositeBinding("2DVector")
                .With("Up",    "<Keyboard>/w")
                .With("Down",  "<Keyboard>/s")
                .With("Left",  "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");

            _moveAction.AddCompositeBinding("2DVector")
                .With("Up",    "<Keyboard>/upArrow")
                .With("Down",  "<Keyboard>/downArrow")
                .With("Left",  "<Keyboard>/leftArrow")
                .With("Right", "<Keyboard>/rightArrow");

            _moveAction.Enable();
        }

        private void Start()
        {
            CreatePlayerSprite();
            ResetToSpawn();
        }

        private void OnDestroy()
        {
            _moveAction?.Disable();
            _moveAction?.Dispose();
        }

        private void Update()
        {
            if (State == MovementState.Frozen)
                return;

            ReadInput();

            if (State == MovementState.Moving)
            {
                AdvanceLerp();
            }
            else // Stopped -- try to start moving in the queued or current direction
            {
                TryStartMoving();
            }

            AnimateMouth();
        }

        // -- Public API --

        /// <summary>
        /// Teleports the player to the designated spawn tile and resets movement state.
        /// </summary>
        public void ResetToSpawn()
        {
            if (_mazeRenderer == null)
                _mazeRenderer = FindFirstObjectByType<MazeRenderer>();

            Vector2Int spawn = MazeData.GetPlayerSpawn();
            CurrentTile = spawn;
            _targetTile = spawn;
            if (_mazeRenderer != null)
                transform.position = _mazeRenderer.TileToWorld(spawn);
            CurrentDirection = Direction.None;
            QueuedDirection = Direction.None;
            State = MovementState.Stopped;
            _lerpT = 0f;
        }

        /// <summary>
        /// Sets the movement speed in tiles per second.
        /// </summary>
        public void SetSpeed(float tilesPerSecond)
        {
            _tilesPerSecond = Mathf.Max(0.01f, tilesPerSecond);
        }

        /// <summary>
        /// Freezes the player in place (used during death or round-clear sequences).
        /// </summary>
        public void Freeze()
        {
            State = MovementState.Frozen;
        }

        /// <summary>
        /// Unfreezes the player, returning to Stopped so they can begin moving again.
        /// </summary>
        public void Unfreeze()
        {
            State = MovementState.Stopped;
        }

        /// <summary>
        /// Allows AI autoplay to inject a direction.
        /// Sets both queued AND current direction so the turn happens immediately
        /// at the current tile center, not deferred to the next tile.
        /// </summary>
        public void SetQueuedDirection(Direction dir)
        {
            QueuedDirection = dir;
        }

        /// <summary>
        /// Forces an immediate direction change (used by AI autoplay).
        /// Only works when the player is at a tile center (Stopped state).
        /// This bypasses the queuing system so turns happen at the intended tile.
        /// </summary>
        public void ForceDirection(Direction dir)
        {
            if (State == MovementState.Stopped && dir != Direction.None)
            {
                if (CanMoveInDirection(CurrentTile, dir))
                {
                    QueuedDirection = dir;
                    CurrentDirection = dir;
                }
                else
                {
                    QueuedDirection = dir;
                }
            }
            else
            {
                QueuedDirection = dir;
            }
        }

        /// <summary>
        /// Triggers the player death event. Called externally by <see cref="CollisionManager"/>.
        /// </summary>
        public void Die()
        {
            Freeze();
            OnDeath?.Invoke();
        }

        // -- Input --

        private void ReadInput()
        {
            Vector2 raw = _moveAction.ReadValue<Vector2>();

            // Determine dominant axis to avoid diagonals.
            Direction desired = Direction.None;

            if (Mathf.Abs(raw.x) > Mathf.Abs(raw.y))
            {
                if (raw.x > 0.5f) desired = Direction.Right;
                else if (raw.x < -0.5f) desired = Direction.Left;
            }
            else
            {
                if (raw.y > 0.5f) desired = Direction.Up;
                else if (raw.y < -0.5f) desired = Direction.Down;
            }

            if (desired != Direction.None)
            {
                QueuedDirection = desired;
            }
        }

        // -- Movement --

        private void TryStartMoving()
        {
            // Prefer the queued direction; fall back to current direction.
            if (QueuedDirection != Direction.None && CanMoveInDirection(CurrentTile, QueuedDirection))
            {
                BeginMoveToward(QueuedDirection);
                return;
            }

            if (CurrentDirection != Direction.None && CanMoveInDirection(CurrentTile, CurrentDirection))
            {
                BeginMoveToward(CurrentDirection);
            }
        }

        private void BeginMoveToward(Direction dir)
        {
            Vector2Int offset = DirectionHelper.ToVector(dir);
            _targetTile = CurrentTile + offset;
            CurrentDirection = dir;

            // Clear queued direction if it was successfully applied.
            if (dir == QueuedDirection)
                QueuedDirection = Direction.None;

            _originWorldPos = _mazeRenderer.TileToWorld(CurrentTile);
            _targetWorldPos = _mazeRenderer.TileToWorld(_targetTile);
            _lerpT = 0f;
            State = MovementState.Moving;

            UpdateFacingRotation(dir);
        }

        private void AdvanceLerp()
        {
            _lerpT += _tilesPerSecond * Time.deltaTime;
            transform.position = Vector3.Lerp(_originWorldPos, _targetWorldPos, _lerpT);

            // Check for mid-tile direction reversal.
            if (QueuedDirection != Direction.None
                && QueuedDirection == DirectionHelper.Opposite(CurrentDirection))
            {
                // Reverse immediately -- swap origin and target.
                Vector2Int tempTile = CurrentTile;
                CurrentTile = _targetTile;
                _targetTile = tempTile;

                _originWorldPos = _mazeRenderer.TileToWorld(CurrentTile);
                _targetWorldPos = _mazeRenderer.TileToWorld(_targetTile);
                _lerpT = 1f - _lerpT;

                CurrentDirection = QueuedDirection;
                QueuedDirection = Direction.None;
                UpdateFacingRotation(CurrentDirection);
            }

            if (_lerpT >= 1f)
            {
                ArriveAtTile(_targetTile);
            }
        }

        private void ArriveAtTile(Vector2Int tile)
        {
            CurrentTile = tile;
            transform.position = _mazeRenderer.TileToWorld(tile);
            State = MovementState.Stopped;

            // Warp tunnel handling.
            MazeTile tileType = MazeData.GetTile(tile.x, tile.y);
            if (tileType == MazeTile.Tunnel)
            {
                HandleTunnelWarp(tile);
                // After warping, CurrentTile has been updated.
                tileType = MazeData.GetTile(CurrentTile.x, CurrentTile.y);
            }

            // Pellet / energizer collection.
            if (tileType == MazeTile.Pellet || tileType == MazeTile.Energizer)
            {
                _mazeRenderer.RemovePellet(CurrentTile);
                OnPelletCollected?.Invoke(CurrentTile, tileType);
            }

            // Try to continue moving -- prefer queued direction, then current.
            if (QueuedDirection != Direction.None && CanMoveInDirection(CurrentTile, QueuedDirection))
            {
                BeginMoveToward(QueuedDirection);
            }
            else if (CurrentDirection != Direction.None && CanMoveInDirection(CurrentTile, CurrentDirection))
            {
                BeginMoveToward(CurrentDirection);
            }
            else
            {
                // Hit a wall -- stop.
                State = MovementState.Stopped;
            }
        }

        // -- Tunnel --

        private void HandleTunnelWarp(Vector2Int tile)
        {
            int mazeWidth = MazeData.Width;

            if (tile.x <= 0 && CurrentDirection == Direction.Left)
            {
                // Warp to right side.
                Vector2Int warpTarget = new Vector2Int(mazeWidth - 1, tile.y);
                WarpTo(warpTarget);
            }
            else if (tile.x >= mazeWidth - 1 && CurrentDirection == Direction.Right)
            {
                // Warp to left side.
                Vector2Int warpTarget = new Vector2Int(0, tile.y);
                WarpTo(warpTarget);
            }
        }

        private void WarpTo(Vector2Int tile)
        {
            CurrentTile = tile;
            _targetTile = tile;
            transform.position = _mazeRenderer.TileToWorld(tile);
        }

        // -- Validation --

        private bool CanMoveInDirection(Vector2Int fromTile, Direction dir)
        {
            Vector2Int offset = DirectionHelper.ToVector(dir);
            Vector2Int dest = fromTile + offset;

            // Allow wrapping for tunnels at maze edges.
            if (dest.x < 0 || dest.x >= MazeData.Width)
            {
                MazeTile fromType = MazeData.GetTile(fromTile.x, fromTile.y);
                return fromType == MazeTile.Tunnel;
            }

            return MazeData.IsWalkable(dest.x, dest.y);
        }

        // -- Visuals --

        private void CreatePlayerSprite()
        {
            _spriteRenderer = gameObject.GetComponent<SpriteRenderer>();
            if (_spriteRenderer == null)
                _spriteRenderer = gameObject.AddComponent<SpriteRenderer>();

            // Generate 3 mouth frames: closed (0°), half-open (30°), wide-open (60°)
            // All sprites face RIGHT so that flipping/rotation can orient them correctly.
            float[] halfMouthAngles = { 0f, 15f, 30f }; // half-angle in degrees
            _mouthFrames = new Sprite[3];

            for (int frame = 0; frame < 3; frame++)
            {
                _mouthFrames[frame] = BuildPacManSprite(halfMouthAngles[frame]);
            }

            // Start with half-open frame (classic idle look).
            _mouthFrameIndex = 1;
            _spriteRenderer.sprite = _mouthFrames[1];
            _spriteRenderer.sortingOrder = 10;
        }

        /// <summary>
        /// Builds a single 48x48 Pac-Man sprite with the mouth opening facing right.
        /// <paramref name="halfMouthAngleDeg"/> is the half-angle of the wedge cut
        /// (0 = closed circle, 15 = 30° total opening, 30 = 60° total opening).
        /// </summary>
        private Sprite BuildPacManSprite(float halfMouthAngleDeg)
        {
            const int size = 48;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;

            Color pacColor = new Color(1f, 0.9f, 0f, 1f);
            Color eyeColor = new Color(0.05f, 0.05f, 0.1f, 1f);

            float radius = size * 0.5f;
            float centerX = radius;
            float centerY = radius;

            float halfMouthAngleRad = halfMouthAngleDeg * Mathf.Deg2Rad;

            // Eye in upper-right area (~60% X, ~70% Y from bottom-left origin).
            float eyeCenterX = centerX + radius * 0.20f;
            float eyeCenterY = centerY + radius * 0.40f;
            float eyeRadius = radius * 0.10f;

            for (int py = 0; py < size; py++)
            {
                for (int px = 0; px < size; px++)
                {
                    float dx = px + 0.5f - centerX;
                    float dy = py + 0.5f - centerY;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    // Outside the circle -- anti-aliased edge.
                    if (dist > radius)
                    {
                        if (dist < radius + 1f)
                        {
                            float alpha = 1f - (dist - radius);
                            tex.SetPixel(px, py, new Color(pacColor.r, pacColor.g, pacColor.b, alpha));
                        }
                        else
                        {
                            tex.SetPixel(px, py, Color.clear);
                        }
                        continue;
                    }

                    // Mouth wedge check (opening faces right, i.e. angle 0).
                    if (halfMouthAngleDeg > 0f)
                    {
                        float angle = Mathf.Atan2(dy, dx);
                        if (Mathf.Abs(angle) < halfMouthAngleRad && dx > 0f)
                        {
                            // Anti-alias the wedge edges.
                            float edgeDist = Mathf.Abs(angle) - halfMouthAngleRad;
                            if (edgeDist > -0.04f) // ~2 px band
                            {
                                float alpha = Mathf.Clamp01(-edgeDist / 0.04f);
                                tex.SetPixel(px, py, new Color(pacColor.r, pacColor.g, pacColor.b, alpha));
                            }
                            else
                            {
                                tex.SetPixel(px, py, Color.clear);
                            }
                            continue;
                        }
                    }

                    // Eye dot.
                    float eyeDx = px + 0.5f - eyeCenterX;
                    float eyeDy = py + 0.5f - eyeCenterY;
                    float eyeDist = Mathf.Sqrt(eyeDx * eyeDx + eyeDy * eyeDy);

                    if (eyeDist <= eyeRadius)
                    {
                        tex.SetPixel(px, py, eyeColor);
                    }
                    else
                    {
                        tex.SetPixel(px, py, pacColor);
                    }
                }
            }

            tex.Apply();

            // PPU chosen so the sprite is ~0.38 world units wide.
            float worldSize = 0.38f;
            float ppu = size / worldSize;

            return Sprite.Create(tex,
                new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f),
                ppu);
        }

        /// <summary>
        /// Orients the sprite using flipX and limited Z-rotation so the eye
        /// always stays on top and the mouth opens in the movement direction.
        /// The source sprite has the mouth facing RIGHT.
        /// </summary>
        private void UpdateFacingRotation(Direction dir)
        {
            switch (dir)
            {
                case Direction.Right:
                    _spriteRenderer.flipX = false;
                    transform.rotation = Quaternion.identity;
                    break;
                case Direction.Left:
                    _spriteRenderer.flipX = true;
                    transform.rotation = Quaternion.identity;
                    break;
                case Direction.Up:
                    _spriteRenderer.flipX = false;
                    transform.rotation = Quaternion.Euler(0, 0, 90);
                    break;
                case Direction.Down:
                    _spriteRenderer.flipX = false;
                    transform.rotation = Quaternion.Euler(0, 0, -90);
                    break;
            }
        }

        private void AnimateMouth()
        {
            // When not moving, show the half-open idle frame and reset scale.
            if (State != MovementState.Moving)
            {
                if (_mouthFrameIndex != 1)
                {
                    _mouthFrameIndex = 1;
                    _spriteRenderer.sprite = _mouthFrames[1];
                }
                transform.localScale = Vector3.one;
                return;
            }

            // Cycle through frames: 0 -> 1 -> 2 -> 1 -> 0 -> 1 -> 2 ...
            // Using a ping-pong pattern over the 3 frames.
            _mouthAnimTimer += Time.deltaTime * _tilesPerSecond * 2f;

            // Map the timer to a 0-1-2-1 cycle (period of 4 half-steps = indices 0,1,2,1).
            int rawIndex = Mathf.FloorToInt(_mouthAnimTimer) % 4;
            int frameIndex;
            switch (rawIndex)
            {
                case 0: frameIndex = 0; break;
                case 1: frameIndex = 1; break;
                case 2: frameIndex = 2; break;
                default: frameIndex = 1; break; // case 3
            }

            if (frameIndex != _mouthFrameIndex)
            {
                _mouthFrameIndex = frameIndex;
                _spriteRenderer.sprite = _mouthFrames[frameIndex];
            }

            // Ensure scale is normal (remove any leftover from old pulsing).
            transform.localScale = Vector3.one;
        }
    }
}
