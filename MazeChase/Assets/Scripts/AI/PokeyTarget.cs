using UnityEngine;
using MazeChase.Game;

namespace MazeChase.AI
{
    /// <summary>
    /// Pokey (Clyde) targeting strategy — Ghost 3.
    /// Chase: when more than 8 tiles away from the player, targets the player
    /// directly (like Shadow). When within 8 tiles, retreats to its scatter
    /// corner instead. This creates a shy, unpredictable circling pattern.
    /// Scatter: retreats to the bottom-left corner of the maze.
    /// </summary>
    public class PokeyTarget : IGhostTargetStrategy
    {
        private const float ProximityThreshold = 8f;
        private static readonly Vector2Int ScatterCorner = new Vector2Int(0, 0);

        public Vector2Int GetChaseTarget(Ghost ghost, PlayerController player, Ghost[] allGhosts)
        {
            Vector2Int ghostTile = ghost.CurrentTile;
            Vector2Int playerTile = player.CurrentTile;

            // Calculate Euclidean distance in tile space
            float dx = ghostTile.x - playerTile.x;
            float dy = ghostTile.y - playerTile.y;
            float distance = Mathf.Sqrt(dx * dx + dy * dy);

            // If farther than 8 tiles, target the player directly
            if (distance > ProximityThreshold)
            {
                return playerTile;
            }

            // If within 8 tiles, retreat to scatter corner
            return ScatterCorner;
        }

        public Vector2Int GetScatterTarget(Ghost ghost)
        {
            return ScatterCorner;
        }
    }
}
