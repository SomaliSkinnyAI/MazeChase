using UnityEngine;
using MazeChase.Game;

namespace MazeChase.AI
{
    /// <summary>
    /// Strategy interface for ghost targeting. Each ghost type implements its own
    /// chase and scatter targeting logic, following the classic Pac-Man algorithms.
    /// </summary>
    public interface IGhostTargetStrategy
    {
        /// <summary>
        /// Returns the target tile during Chase mode.
        /// </summary>
        /// <param name="ghost">The ghost requesting a target.</param>
        /// <param name="player">The player controller for position/direction info.</param>
        /// <param name="allGhosts">All ghost instances (needed by some strategies like Bashful).</param>
        Vector2Int GetChaseTarget(Ghost ghost, PlayerController player, Ghost[] allGhosts);

        /// <summary>
        /// Returns the target tile during Scatter mode.
        /// Each ghost retreats to its designated corner of the maze.
        /// </summary>
        /// <param name="ghost">The ghost requesting a target.</param>
        Vector2Int GetScatterTarget(Ghost ghost);
    }
}
