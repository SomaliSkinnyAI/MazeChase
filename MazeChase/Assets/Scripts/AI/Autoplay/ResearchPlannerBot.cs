using UnityEngine;
using MazeChase.Game;

namespace MazeChase.AI.Autoplay
{
    /// <summary>
    /// Strong graph-search teacher bot. It searches a few tiles ahead with
    /// threat-aware scoring and serves both as the high-signal benchmark bot and
    /// as the fallback when no trained policy weights are available.
    /// </summary>
    public sealed class ResearchPlannerBot : IAutoplayBot
    {
        private readonly bool _attractMode;
        private AutoplayContext _context;

        public ResearchPlannerBot(bool attractMode = false)
        {
            _attractMode = attractMode;
        }

        public string Name => _attractMode ? "Attract" : "ResearchPlanner";

        public void Initialize(AutoplayContext context)
        {
            _context = context;
        }

        public void ResetForRound()
        {
        }

        public BotDecision Evaluate(GameStateSnapshot snapshot)
        {
            if (_context == null || _context.Graph == null)
                return BotDecision.None;

            Direction[] legalDirections = new Direction[4];
            int legalCount = CollectLegalDirections(snapshot.PlayerTile, legalDirections);
            if (legalCount == 0)
                return BotDecision.None;

            if (legalCount == 1)
            {
                BotDecision forced = new BotDecision(legalDirections[0], 1f, 0.5f, AutoplayObjective.Survive, "Forced corridor move");
                forced.Source = Name;
                foreach (Direction direction in DirectionHelper.AllDirections)
                    forced.DirectionScores[(int)direction] = direction == legalDirections[0] ? 1f : float.NegativeInfinity;
                forced.DirectionScores[(int)legalDirections[0]] = 1f;
                return forced;
            }

            if (!_attractMode && snapshot.RemainingPellets <= 4 && _context.GetNoProgressDecisionStreak() >= 12)
            {
                Vector2Int nearestPelletTile = _context.Graph.FindNearestPelletTile(snapshot.PlayerTile, _context.Pellets);
                Direction opposite = snapshot.PlayerDirection != Direction.None ? DirectionHelper.Opposite(snapshot.PlayerDirection) : Direction.None;
                Direction pathDirection = _context.Graph.GetBestDirectionToward(snapshot.PlayerTile, nearestPelletTile, opposite);
                if (pathDirection != Direction.None)
                {
                    BotDecision pathDecision = new BotDecision(pathDirection, 1f, 0.8f, AutoplayObjective.ClearPellets, "Endgame direct pathfind");
                    pathDecision.Source = Name;
                    foreach (Direction direction in DirectionHelper.AllDirections)
                        pathDecision.DirectionScores[(int)direction] = direction == pathDirection ? 1f : float.NegativeInfinity;
                    return pathDecision;
                }
            }

            int nodeCount = _context.Graph.NodeCount;
            bool[] claimedPellets = new bool[nodeCount];
            int[] visitCounts = new int[nodeCount];
            int currentIndex = _context.Graph.GetNodeIndex(snapshot.PlayerTile);
            if (currentIndex >= 0)
                visitCounts[currentIndex] = 1;

            int depth = _attractMode ? 6 : (snapshot.RemainingPellets <= 12 ? 12 : 8);
            float[] directionScores = new float[5];
            foreach (Direction direction in DirectionHelper.AllDirections)
                directionScores[(int)direction] = float.NegativeInfinity;
            Direction bestDirection = Direction.None;
            float bestScore = float.NegativeInfinity;

            for (int i = 0; i < legalCount; i++)
            {
                Direction direction = legalDirections[i];
                float score = EvaluateMove(snapshot, snapshot.PlayerTile, snapshot.PlayerDirection, direction, 0, depth, claimedPellets, visitCounts);
                directionScores[(int)direction] = score;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestDirection = direction;
                }
            }

            float confidence = GraphPolicyModel.ComputeConfidence(directionScores, bestDirection);
            float valueEstimate = (float)System.Math.Tanh(bestScore / (_attractMode ? 260f : 220f));
            AutoplayObjective objective = DetermineObjective(snapshot, bestDirection);

            var decision = new BotDecision(bestDirection, confidence, valueEstimate, objective, $"Route score {bestScore:F1}")
            {
                Source = Name,
                DirectionScores = directionScores
            };

            return decision;
        }

        private float EvaluateMove(
            GameStateSnapshot snapshot,
            Vector2Int currentTile,
            Direction previousDirection,
            Direction direction,
            int depth,
            int maxDepth,
            bool[] claimedPellets,
            int[] visitCounts)
        {
            int currentIndex = _context.Graph.GetNodeIndex(currentTile);
            if (currentIndex < 0)
                return -100000f;

            int nextIndex = _context.Graph.GetNeighbor(currentIndex, direction);
            if (nextIndex < 0)
                return -100000f;

            Vector2Int nextTile = _context.Graph.GetTile(nextIndex);
            float score = ScoreTransition(snapshot, currentTile, previousDirection, direction, nextTile, nextIndex, depth, claimedPellets, visitCounts, out bool claimedPellet);

            visitCounts[nextIndex]++;
            if (claimedPellet)
                claimedPellets[nextIndex] = true;

            if (depth + 1 >= maxDepth)
            {
                score += EvaluateTerminal(snapshot, nextTile, nextIndex);
            }
            else
            {
                Direction[] legalDirections = new Direction[4];
                int legalCount = CollectLegalDirections(nextTile, legalDirections);

                if (legalCount == 0)
                {
                    score -= 120f;
                }
                else
                {
                    float bestChild = float.NegativeInfinity;
                    for (int i = 0; i < legalCount; i++)
                    {
                        float childScore = EvaluateMove(snapshot, nextTile, direction, legalDirections[i], depth + 1, maxDepth, claimedPellets, visitCounts);
                        if (childScore > bestChild)
                            bestChild = childScore;
                    }

                    score += (_attractMode ? 0.95f : 0.92f) * bestChild;
                }
            }

            if (claimedPellet)
                claimedPellets[nextIndex] = false;
            visitCounts[nextIndex]--;

            return score;
        }

        private float ScoreTransition(
            GameStateSnapshot snapshot,
            Vector2Int currentTile,
            Direction previousDirection,
            Direction direction,
            Vector2Int nextTile,
            int nextIndex,
            int depth,
            bool[] claimedPellets,
            int[] visitCounts,
            out bool claimedPellet)
        {
            claimedPellet = false;
            TileForecast immediate = _context.ForecastEngine.EvaluateTile(snapshot, nextTile, direction, depth);
            TileForecast future = _context.ForecastEngine.EvaluateTile(snapshot, nextTile, direction, depth + 2);
            MazeTile nextTileType = MazeData.GetTile(nextTile.x, nextTile.y);
            bool hasPellet = _context.Pellets != null && _context.Pellets.HasPellet(nextTile.x, nextTile.y);
            float endgamePressure = ComputeEndgamePressure(snapshot);

            float score = 0f;

            if (hasPellet && !claimedPellets[nextIndex])
            {
                claimedPellet = true;
                score += nextTileType == MazeTile.Energizer ? 130f : Mathf.Lerp(18f, 58f, endgamePressure);
            }

            if (snapshot.FruitActive && nextTile == snapshot.FruitTile)
                score += _attractMode ? 40f : 90f;

            score -= immediate.Danger * (_attractMode ? 0.72f : 0.88f);
            score -= future.Danger * (_attractMode ? 0.30f : 0.42f);
            score += immediate.Opportunity * (_attractMode ? 0.18f : 0.45f);

            int pelletsAhead = _context.Graph.CountPelletsInDirection(currentTile, direction, _context.Pellets, 8);
            int localPellets = _context.Graph.CountPelletsWithin(nextIndex, _context.Pellets, 6);
            int nearestPellet = _context.Graph.FindNearestPelletDistance(nextTile, _context.Pellets);
            int nearestEnergizer = _context.Graph.FindNearestEnergizerDistance(nextTile, _context.Pellets);
            int degree = _context.Graph.GetDegree(nextIndex);
            int deadEndDepth = _context.Graph.GetDeadEndDepth(nextIndex);
            int recentVisitCount = _context.GetRecentVisitCount(nextIndex);
            int recentVisitAge = _context.GetRecentVisitAge(nextIndex);

            score += Mathf.Min(pelletsAhead, 10) * (_attractMode ? 3.5f : 5.2f);
            score += Mathf.Min(localPellets, 12) * Mathf.Lerp(2.5f, 3.8f, endgamePressure);
            score += Mathf.Max(0, 12 - nearestPellet) * Mathf.Lerp(1.4f, 3.2f, endgamePressure);
            score += degree >= 3 ? (_attractMode ? 12f : 8f) : 0f;

            if (immediate.Danger > 45f && nearestEnergizer < 999)
                score += Mathf.Max(0, 8 - nearestEnergizer) * (_attractMode ? 6f : 14f);

            if (deadEndDepth > 0)
                score -= deadEndDepth * (immediate.Danger > 20f ? 22f : 8f);

            if (_context.Graph.IsTunnel(nextIndex) && immediate.Danger > 40f)
                score += _attractMode ? 10f : 24f;

            if (direction == previousDirection)
                score += _attractMode ? 10f : 4f;
            else if (previousDirection != Direction.None && direction == DirectionHelper.Opposite(previousDirection))
                score -= immediate.Danger > 90f ? 10f : (_attractMode ? 22f : 34f);
            else
                score += degree >= 3 ? 4f : 0f;

            if (visitCounts[nextIndex] > 0)
                score -= visitCounts[nextIndex] * (_attractMode ? 24f : 40f);

            if (recentVisitCount > 0)
            {
                float basePenalty = Mathf.Lerp(_attractMode ? 10f : 12f, _attractMode ? 18f : 28f, endgamePressure);
                float recencyMultiplier = recentVisitAge <= 10 ? 2.2f : (recentVisitAge <= 24 ? 1.0f : 0.4f);
                score -= recentVisitCount * basePenalty * recencyMultiplier;
            }

            if (recentVisitAge <= 12)
                score -= Mathf.Lerp(_attractMode ? 6f : 12f, _attractMode ? 14f : 28f, endgamePressure) * (13 - recentVisitAge);

            if (!hasPellet && pelletsAhead == 0 && immediate.Danger < 25f)
            {
                float corridorPenalty = snapshot.RemainingPellets <= 12
                    ? Mathf.Lerp(_attractMode ? 6f : 10f, _attractMode ? 10f : 20f, endgamePressure)
                    : Mathf.Lerp(_attractMode ? 10f : 35f, _attractMode ? 24f : 80f, endgamePressure);
                score -= corridorPenalty;
            }

            if (endgamePressure > 0f && hasPellet)
                score += Mathf.Lerp(0f, 40f, endgamePressure);

            if (endgamePressure > 0f && nearestPellet <= 2)
                score += Mathf.Max(0, 3 - nearestPellet) * 14f * endgamePressure;

            return score;
        }

        private float EvaluateTerminal(GameStateSnapshot snapshot, Vector2Int tile, int nodeIndex)
        {
            TileForecast terminal = _context.ForecastEngine.EvaluateTile(snapshot, tile, Direction.None, 0);
            int nearestPellet = _context.Graph.FindNearestPelletDistance(tile, _context.Pellets);
            int localPellets = _context.Graph.CountPelletsWithin(nodeIndex, _context.Pellets, 8);
            int degree = _context.Graph.GetDegree(nodeIndex);
            int deadEndDepth = _context.Graph.GetDeadEndDepth(nodeIndex);
            float endgamePressure = ComputeEndgamePressure(snapshot);
            int recentVisitCount = _context.GetRecentVisitCount(nodeIndex);
            int recentVisitAge = _context.GetRecentVisitAge(nodeIndex);

            float score = 0f;
            score += Mathf.Max(0, 14 - nearestPellet) * Mathf.Lerp(1.6f, 4.2f, endgamePressure);
            if (snapshot.RemainingPellets <= 12 && nearestPellet < 999)
                score += 80f / (1f + nearestPellet);
            score += Mathf.Min(localPellets, 14) * Mathf.Lerp(2.8f, 3.6f, endgamePressure);
            score += degree * 5f;
            score -= terminal.Danger * 0.35f;
            score += terminal.Opportunity * 0.25f;
            score -= deadEndDepth * (terminal.Danger > 18f ? 16f : 4f);
            if (recentVisitCount > 0)
            {
                float termRecencyMultiplier = recentVisitAge <= 10 ? 2.0f : (recentVisitAge <= 24 ? 1.0f : 0.4f);
                score -= recentVisitCount * Mathf.Lerp(6f, 14f, endgamePressure) * termRecencyMultiplier;
            }
            if (recentVisitAge <= 12)
                score -= (13 - recentVisitAge) * Mathf.Lerp(2f, 5f, endgamePressure);

            return score;
        }

        private int CollectLegalDirections(Vector2Int tile, Direction[] buffer)
        {
            int index = _context.Graph.GetNodeIndex(tile);
            if (index < 0)
                return 0;

            int count = 0;
            foreach (Direction direction in DirectionHelper.AllDirections)
            {
                if (_context.Graph.GetNeighbor(index, direction) < 0)
                    continue;

                buffer[count++] = direction;
            }

            return count;
        }

        private AutoplayObjective DetermineObjective(GameStateSnapshot snapshot, Direction direction)
        {
            if (direction == Direction.None)
                return AutoplayObjective.Survive;

            int playerIndex = _context.Graph.GetNodeIndex(snapshot.PlayerTile);
            if (playerIndex < 0)
                return AutoplayObjective.Survive;

            int nextIndex = _context.Graph.GetNeighbor(playerIndex, direction);
            if (nextIndex < 0)
                return AutoplayObjective.Survive;

            Vector2Int nextTile = _context.Graph.GetTile(nextIndex);
            TileForecast forecast = _context.ForecastEngine.EvaluateTile(snapshot, nextTile, direction, 0);
            int nearestEnergizer = _context.Graph.FindNearestEnergizerDistance(nextTile, _context.Pellets);

            if (forecast.Opportunity > forecast.Danger && forecast.NearestOpportunityDistance <= 3)
                return AutoplayObjective.ChaseFrightened;
            if (snapshot.FruitActive && nextTile == snapshot.FruitTile)
                return AutoplayObjective.CollectFruit;
            if (forecast.Danger > 80f && nearestEnergizer <= 5)
                return AutoplayObjective.SeekEnergizer;
            if (forecast.Danger > 80f)
                return AutoplayObjective.EscapeTrap;
            if (snapshot.RemainingPellets <= 24)
                return AutoplayObjective.ClearPellets;
            if (_attractMode)
                return AutoplayObjective.AttractRoute;

            return AutoplayObjective.ClearPellets;
        }

        private static float ComputeEndgamePressure(GameStateSnapshot snapshot)
        {
            if (snapshot == null)
                return 0f;

            return Mathf.Clamp01((24f - snapshot.RemainingPellets) / 24f);
        }
    }
}
