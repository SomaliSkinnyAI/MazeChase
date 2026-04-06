using UnityEngine;
using MazeChase.AI;
using MazeChase.Game;

namespace MazeChase.AI.Autoplay
{
    public struct TileForecast
    {
        public float Danger;
        public float Opportunity;
        public int NearestThreatDistance;
        public int NearestOpportunityDistance;
    }

    /// <summary>
    /// Converts the live ghost state into a graph-aware pressure estimate. This is
    /// not a perfect emulation of the runtime ghost component; it is a planning
    /// model tuned to highlight intercept risk, trap pressure, and frightened
    /// opportunities a few tiles ahead.
    /// </summary>
    public sealed class GhostForecastEngine
    {
        private static readonly Vector2Int HouseExitTile = new Vector2Int(13, 19);
        private readonly MazeGraph _graph;

        public GhostForecastEngine(MazeGraph graph)
        {
            _graph = graph;
        }

        public TileForecast EvaluateTile(GameStateSnapshot snapshot, Vector2Int tile, Direction plannedDirection, int futureSteps)
        {
            TileForecast forecast = new TileForecast
            {
                Danger = 0f,
                Opportunity = 0f,
                NearestThreatDistance = 999,
                NearestOpportunityDistance = 999
            };

            int nodeIndex = _graph.GetNodeIndex(tile);
            int deadEndDepth = _graph.GetDeadEndDepth(nodeIndex);
            bool isTunnel = _graph.IsTunnel(nodeIndex);
            RoundTuning tuning = RoundTuningData.GetTuning(Mathf.Max(1, snapshot.Round));
            float chaseAdvancePerPlayerTile = tuning.GhostSpeed / Mathf.Max(0.1f, tuning.PlayerSpeed);

            Vector2Int shadowTile = FindGhostTile(snapshot, 0);

            for (int i = 0; i < snapshot.Ghosts.Length; i++)
            {
                GhostSnapshot ghost = snapshot.Ghosts[i];
                int distance = _graph.GetDistance(tile, ghost.Tile);

                if (ghost.State == GhostState.Frightened)
                {
                    forecast.NearestOpportunityDistance = Mathf.Min(forecast.NearestOpportunityDistance, distance);
                    forecast.Opportunity += ScoreFrightenedOpportunity(distance, futureSteps);
                    continue;
                }

                if (ghost.State == GhostState.Eaten)
                    continue;

                if (ghost.State == GhostState.InHouse)
                {
                    int houseDistance = _graph.GetDistance(tile, HouseExitTile);
                    if (houseDistance <= 2)
                        forecast.Danger += 8f / Mathf.Max(1f, houseDistance);
                    continue;
                }

                if (ghost.State == GhostState.ExitingHouse)
                {
                    int exitDistance = _graph.GetDistance(tile, HouseExitTile);
                    if (exitDistance <= 5)
                        forecast.Danger += 40f / Mathf.Max(1f, exitDistance);
                    continue;
                }

                forecast.NearestThreatDistance = Mathf.Min(forecast.NearestThreatDistance, distance);

                float interceptMargin = distance - ((futureSteps + 1) * chaseAdvancePerPlayerTile);
                float threat = ScoreThreat(interceptMargin);

                Vector2Int nextGhostTile = ghost.Tile + DirectionHelper.ToVector(ghost.Direction);
                int distanceIfGhostContinues = _graph.GetDistance(tile, nextGhostTile);
                if (distanceIfGhostContinues < distance)
                    threat += 14f;

                if (plannedDirection != Direction.None && plannedDirection == DirectionHelper.Opposite(ghost.Direction) && distance <= 3)
                    threat += 20f;

                Vector2Int target = GetTargetTile(ghost, snapshot.PlayerTile, snapshot.PlayerDirection, snapshot.GlobalGhostMode, shadowTile);
                int targetDistance = _graph.GetDistance(tile, target);
                if (targetDistance <= 4)
                    threat += (5 - targetDistance) * 6f;

                if (deadEndDepth > 0 && distance <= deadEndDepth + 3)
                    threat *= 1.25f + (deadEndDepth * 0.06f);

                if (isTunnel && distance >= 4)
                    threat *= 0.75f;

                forecast.Danger += threat;
            }

            return forecast;
        }

        public float[] BuildDangerHeatmap(GameStateSnapshot snapshot)
        {
            float[] heatmap = new float[_graph.NodeCount];
            for (int nodeIndex = 0; nodeIndex < _graph.NodeCount; nodeIndex++)
            {
                Vector2Int tile = _graph.GetTile(nodeIndex);
                heatmap[nodeIndex] = EvaluateTile(snapshot, tile, Direction.None, 0).Danger;
            }

            return heatmap;
        }

        private static float ScoreThreat(float interceptMargin)
        {
            if (interceptMargin <= 0f)
                return 320f;
            if (interceptMargin <= 1f)
                return 180f;
            if (interceptMargin <= 2f)
                return 110f;
            if (interceptMargin <= 4f)
                return 60f;
            if (interceptMargin <= 6f)
                return 25f;

            return 0f;
        }

        private static float ScoreFrightenedOpportunity(int distance, int futureSteps)
        {
            if (distance <= 1)
                return 180f;
            if (distance <= 3)
                return 100f;
            if (distance <= 6)
                return 45f;
            if (distance <= 10 && futureSteps <= 2)
                return 15f;

            return 0f;
        }

        private static Vector2Int FindGhostTile(GameStateSnapshot snapshot, int ghostIndex)
        {
            for (int i = 0; i < snapshot.Ghosts.Length; i++)
            {
                if (snapshot.Ghosts[i].GhostIndex == ghostIndex)
                    return snapshot.Ghosts[i].Tile;
            }

            return Vector2Int.zero;
        }

        private static Vector2Int GetTargetTile(
            GhostSnapshot ghost,
            Vector2Int playerTile,
            Direction playerDirection,
            GhostState globalMode,
            Vector2Int shadowTile)
        {
            GhostState effectiveState = ghost.State == GhostState.Scatter || ghost.State == GhostState.Chase
                ? ghost.State
                : globalMode;

            if (effectiveState == GhostState.Scatter)
            {
                switch (ghost.GhostIndex)
                {
                    case 0: return new Vector2Int(27, 30);
                    case 1: return new Vector2Int(0, 30);
                    case 2: return new Vector2Int(27, 0);
                    default: return new Vector2Int(0, 0);
                }
            }

            switch (ghost.GhostIndex)
            {
                case 0:
                    return playerTile;
                case 1:
                    return playerTile + GetPlayerLookAhead(playerDirection, 4, true);
                case 2:
                {
                    Vector2Int pivot = playerTile + GetPlayerLookAhead(playerDirection, 2, true);
                    return new Vector2Int((2 * pivot.x) - shadowTile.x, (2 * pivot.y) - shadowTile.y);
                }
                case 3:
                {
                    float dx = ghost.Tile.x - playerTile.x;
                    float dy = ghost.Tile.y - playerTile.y;
                    float distance = Mathf.Sqrt((dx * dx) + (dy * dy));
                    return distance > 8f ? playerTile : new Vector2Int(0, 0);
                }
                default:
                    return playerTile;
            }
        }

        private static Vector2Int GetPlayerLookAhead(Direction playerDirection, int lookAheadTiles, bool includeUpBug)
        {
            Vector2Int offset = DirectionHelper.ToVector(playerDirection) * lookAheadTiles;
            if (includeUpBug && playerDirection == Direction.Up)
                offset.x -= lookAheadTiles;

            return offset;
        }
    }
}
