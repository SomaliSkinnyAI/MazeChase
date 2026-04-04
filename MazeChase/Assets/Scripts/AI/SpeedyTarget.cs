using UnityEngine;
using MazeChase.Game;

namespace MazeChase.AI
{
    /// <summary>
    /// Speedy (Pinky) targeting strategy — Ghost 1.
    /// Chase: targets 4 tiles ahead of the player in the player's current direction.
    /// Includes the classic overflow bug: when the player faces Up, the target is
    /// offset 4 tiles up AND 4 tiles to the left (due to the original Z80
    /// implementation reading both X and Y from the same memory operation).
    /// Scatter: retreats to the top-left corner of the maze.
    /// </summary>
    public class SpeedyTarget : IGhostTargetStrategy
    {
        private const int LookAheadTiles = 4;
        private static readonly Vector2Int ScatterCorner = new Vector2Int(0, 30);

        public Vector2Int GetChaseTarget(Ghost ghost, PlayerController player, Ghost[] allGhosts)
        {
            Vector2Int playerTile = player.CurrentTile;
            Direction playerDir = player.CurrentDirection;

            // Get the offset for the player's facing direction
            Vector2 dirVec = DirectionHelper.ToVector(playerDir);
            Vector2Int offset = new Vector2Int(
                Mathf.RoundToInt(dirVec.x) * LookAheadTiles,
                Mathf.RoundToInt(dirVec.y) * LookAheadTiles
            );

            // Classic overflow bug: when facing Up, also offset 4 tiles to the left.
            // In the original arcade, the "up" case loaded both the X and Y offset
            // from the same pointer, causing the X to also be modified by -4.
            if (playerDir == Direction.Up)
            {
                offset.x -= LookAheadTiles;
            }

            return playerTile + offset;
        }

        public Vector2Int GetScatterTarget(Ghost ghost)
        {
            return ScatterCorner;
        }
    }
}
