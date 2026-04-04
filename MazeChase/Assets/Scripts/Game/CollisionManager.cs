using System;
using UnityEngine;
using MazeChase.AI;
using MazeChase.Core;

namespace MazeChase.Game
{
    /// <summary>
    /// Checks for tile-based overlap between the player and ghosts each frame.
    /// When a collision is detected the outcome depends on the ghost's current state:
    ///   Frightened  -> player eats the ghost.
    ///   Scatter/Chase -> player is caught.
    ///   Eaten/InHouse/ExitingHouse -> no collision.
    /// </summary>
    public class CollisionManager : MonoBehaviour
    {
        // ── Inspector fields ────────────────────────────────────────────────
        [SerializeField] private PlayerController _player;

        /// <summary>
        /// Maximum tile-space distance at which a collision is detected.
        /// Set slightly below 0.5 to require meaningful overlap.
        /// </summary>
        [SerializeField] private float _collisionThreshold = 0.4f;

        // ── Events ──────────────────────────────────────────────────────────
        /// <summary>
        /// Fired when the player eats a frightened ghost.
        /// </summary>
        public event Action<Ghost> OnGhostEaten;

        /// <summary>
        /// Fired when a ghost in Scatter or Chase mode catches the player.
        /// </summary>
        public event Action<Ghost> OnPlayerCaught;

        // ── Private state ───────────────────────────────────────────────────
        private Ghost[] _ghosts = Array.Empty<Ghost>();

        // ── Public API ──────────────────────────────────────────────────────

        /// <summary>
        /// Assigns the set of ghosts to check collisions against.
        /// Call this once after all ghosts have been spawned, or when the
        /// ghost roster changes.
        /// </summary>
        public void SetGhosts(Ghost[] ghosts)
        {
            _ghosts = ghosts ?? Array.Empty<Ghost>();
        }

        // ── Unity lifecycle ─────────────────────────────────────────────────

        private void Start()
        {
            if (_player == null)
                _player = FindFirstObjectByType<PlayerController>();
        }

        private void Update()
        {
            // Only check collisions while actually playing.
            if (GameStateManager.Instance == null
                || GameStateManager.Instance.GetCurrentState() != GameState.Playing)
                return;

            if (_player == null || _ghosts.Length == 0)
                return;

            Vector2Int playerTile = _player.CurrentTile;

            for (int i = 0; i < _ghosts.Length; i++)
            {
                Ghost ghost = _ghosts[i];
                if (ghost == null)
                    continue;

                // Skip ghosts that cannot collide.
                if (ghost.CurrentState == GhostState.Eaten
                    || ghost.CurrentState == GhostState.InHouse
                    || ghost.CurrentState == GhostState.ExitingHouse)
                    continue;

                // Tile-distance check.
                Vector2Int ghostTile = ghost.CurrentTile;
                float dx = playerTile.x - ghostTile.x;
                float dy = playerTile.y - ghostTile.y;
                float distSq = dx * dx + dy * dy;

                if (distSq > _collisionThreshold * _collisionThreshold)
                    continue;

                // Collision detected — resolve based on ghost state.
                if (ghost.CurrentState == GhostState.Frightened)
                {
                    OnGhostEaten?.Invoke(ghost);
                }
                else // Scatter or Chase
                {
                    OnPlayerCaught?.Invoke(ghost);
                    // Only process the first lethal collision per frame.
                    return;
                }
            }
        }
    }

}
