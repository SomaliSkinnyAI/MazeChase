using UnityEngine;
using MazeChase.Game;

namespace MazeChase.AI
{
    /// <summary>
    /// Bashful (Inky) targeting strategy — Ghost 2.
    /// Chase: computes a tile 2 ahead of the player, then draws a vector from
    /// Shadow's (ghost 0) position to that tile and doubles it. This creates
    /// unpredictable, flanking behavior that complements Shadow's direct pursuit.
    /// Like Speedy, the Up-direction overflow bug applies to the 2-tile look-ahead.
    /// Scatter: retreats to the bottom-right corner of the maze.
    /// </summary>
    public class BashfulTarget : IGhostTargetStrategy
    {
        private const int LookAheadTiles = 2;
        private static readonly Vector2Int ScatterCorner = new Vector2Int(27, 0);

        public Vector2Int GetChaseTarget(Ghost ghost, PlayerController player, Ghost[] allGhosts)
        {
            Vector2Int playerTile = player.CurrentTile;
            Direction playerDir = player.CurrentDirection;

            // Step 1: Get the pivot tile — 2 tiles ahead of the player
            Vector2 dirVec = DirectionHelper.ToVector(playerDir);
            Vector2Int pivotOffset = new Vector2Int(
                Mathf.RoundToInt(dirVec.x) * LookAheadTiles,
                Mathf.RoundToInt(dirVec.y) * LookAheadTiles
            );

            // Include the same overflow bug as Speedy: facing Up offsets left too
            if (playerDir == Direction.Up)
            {
                pivotOffset.x -= LookAheadTiles;
            }

            Vector2Int pivotTile = playerTile + pivotOffset;

            // Step 2: Find Shadow (ghost index 0) position
            Vector2Int shadowTile = Vector2Int.zero;
            if (allGhosts != null)
            {
                for (int i = 0; i < allGhosts.Length; i++)
                {
                    if (allGhosts[i] != null && allGhosts[i].GhostIndex == 0)
                    {
                        shadowTile = allGhosts[i].CurrentTile;
                        break;
                    }
                }
            }

            // Step 3: Vector from Shadow to pivot, then double it
            // target = pivot + (pivot - shadow) = 2 * pivot - shadow
            Vector2Int target = new Vector2Int(
                2 * pivotTile.x - shadowTile.x,
                2 * pivotTile.y - shadowTile.y
            );

            return target;
        }

        public Vector2Int GetScatterTarget(Ghost ghost)
        {
            return ScatterCorner;
        }
    }
}
