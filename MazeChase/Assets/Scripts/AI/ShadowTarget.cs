using UnityEngine;
using MazeChase.Game;

namespace MazeChase.AI
{
    /// <summary>
    /// Shadow (Blinky) targeting strategy — Ghost 0.
    /// Chase: directly targets the player's current tile.
    /// Scatter: retreats to the top-right corner of the maze.
    /// The most aggressive ghost; always knows exactly where the player is.
    /// </summary>
    public class ShadowTarget : IGhostTargetStrategy
    {
        private static readonly Vector2Int ScatterCorner = new Vector2Int(27, 30);

        public Vector2Int GetChaseTarget(Ghost ghost, PlayerController player, Ghost[] allGhosts)
        {
            return player.CurrentTile;
        }

        public Vector2Int GetScatterTarget(Ghost ghost)
        {
            return ScatterCorner;
        }
    }
}
