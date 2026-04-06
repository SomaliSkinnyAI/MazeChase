using System;
using UnityEngine;
using MazeChase.AI;
using MazeChase.Game;

namespace MazeChase.AI.Autoplay
{
    /// <summary>
    /// Encodes the live game state into a fixed feature vector for policy/value
    /// inference. The vector is graph-aware but small enough to ship as a custom
    /// C# runtime model without external ML dependencies.
    /// </summary>
    public sealed class ObservationEncoder
    {
        private const int FeaturesPerDirection = 18;
        private const int BaseGlobalFeatureCount = 11;
        private const int EnhancedGlobalFeatureCount = 11;
        private const int TemporalFeatureCount = 8;
        private const int LegacyInputSizeValue = (FeaturesPerDirection * 4) + BaseGlobalFeatureCount + TemporalFeatureCount;
        private const int BaseSectionSize = (FeaturesPerDirection * 4) + BaseGlobalFeatureCount;
        private readonly AutoplayContext _context;

        public ObservationEncoder(AutoplayContext context)
        {
            _context = context;
        }

        public int InputSize => (FeaturesPerDirection * 4) + BaseGlobalFeatureCount + EnhancedGlobalFeatureCount + TemporalFeatureCount;
        public int LegacyInputSize => LegacyInputSizeValue;

        public PolicyFeatureFrame Encode(GameStateSnapshot snapshot)
        {
            float[] input = new float[InputSize];
            float[] directionScores = new float[5];
            bool[] legalMask = new bool[5];
            int writeIndex = 0;

            foreach (Direction direction in DirectionHelper.AllDirections)
            {
                EncodeDirection(snapshot, direction, input, ref writeIndex, directionScores, legalMask);
            }

            input[writeIndex++] = Mathf.Clamp01(snapshot.Round / 10f);
            input[writeIndex++] = Mathf.Clamp01(snapshot.Lives / 5f);
            input[writeIndex++] = Mathf.Clamp01(snapshot.PelletProgress);
            input[writeIndex++] = snapshot.FruitActive ? 1f : 0f;
            input[writeIndex++] = snapshot.GlobalGhostMode == GhostState.Chase ? 1f : 0f;
            input[writeIndex++] = snapshot.GlobalGhostMode == GhostState.Scatter ? 1f : 0f;
            input[writeIndex++] = Mathf.Clamp01(snapshot.ModeTimeRemaining / 20f);
            input[writeIndex++] = snapshot.PlayerDirection == Direction.Up ? 1f : 0f;
            input[writeIndex++] = snapshot.PlayerDirection == Direction.Down ? 1f : 0f;
            input[writeIndex++] = snapshot.PlayerDirection == Direction.Left ? 1f : 0f;
            input[writeIndex++] = snapshot.PlayerDirection == Direction.Right ? 1f : 0f;

            Vector2 playerVelocity = EncodeVelocity(snapshot.PlayerDirection);
            input[writeIndex++] = playerVelocity.x;
            input[writeIndex++] = playerVelocity.y;
            int nearestJunctionDistance = _context.Graph != null
                ? _context.Graph.FindNearestJunctionDistance(snapshot.PlayerTile)
                : 999;
            input[writeIndex++] = DistanceAffinity(nearestJunctionDistance, 8);

            for (int ghostIndex = 0; ghostIndex < 4; ghostIndex++)
            {
                Direction ghostDirection =
                    snapshot.Ghosts != null && ghostIndex < snapshot.Ghosts.Length
                        ? snapshot.Ghosts[ghostIndex].Direction
                        : Direction.None;
                Vector2 ghostVelocity = EncodeVelocity(ghostDirection);
                input[writeIndex++] = ghostVelocity.x;
                input[writeIndex++] = ghostVelocity.y;
            }

            int currentIndex = _context.Graph != null ? _context.Graph.GetNodeIndex(snapshot.PlayerTile) : -1;
            int recentVisitCount = _context.GetRecentVisitCount(currentIndex);
            int recentRepeatAge = _context.GetRecentRepeatAge(currentIndex);
            float recentUniqueRatio = _context.GetRecentTileWindowSize() > 0
                ? Mathf.Clamp01(_context.GetRecentUniqueTileCount() / (float)_context.GetRecentTileWindowSize())
                : 0f;
            float noProgressPressure = Mathf.Clamp01(_context.GetNoProgressDecisionStreak() / 24f);
            float lowPelletNoProgressPressure = Mathf.Clamp01(_context.GetLowPelletNoProgressStreak() / 24f);
            float sameDirectionPressure = Mathf.Clamp01(_context.GetSameDirectionStreak() / 8f);
            float repeatAgeAffinity = DistanceAffinity(recentRepeatAge, _context.GetRecentTileWindowSize());
            float revisitPressure = Mathf.Clamp01(recentVisitCount / 6f);
            float tightLoopPressure = revisitPressure * repeatAgeAffinity * Mathf.Clamp01((1f - recentUniqueRatio) + lowPelletNoProgressPressure);

            input[writeIndex++] = revisitPressure;
            input[writeIndex++] = repeatAgeAffinity;
            input[writeIndex++] = recentUniqueRatio;
            input[writeIndex++] = noProgressPressure;
            input[writeIndex++] = lowPelletNoProgressPressure;
            input[writeIndex++] = sameDirectionPressure;
            input[writeIndex++] = _context.GetLastDirectionWasReversal() ? 1f : 0f;
            input[writeIndex++] = tightLoopPressure;

            return new PolicyFeatureFrame
            {
                Input = input,
                DirectionScores = directionScores,
                LegalMask = legalMask
            };
        }

        public float[] BuildModelInput(PolicyFeatureFrame featureFrame, int targetInputSize)
        {
            if (featureFrame == null || featureFrame.Input == null)
                return Array.Empty<float>();

            if (targetInputSize == InputSize)
                return featureFrame.Input;

            if (targetInputSize == LegacyInputSizeValue)
            {
                float[] legacyInput = new float[LegacyInputSizeValue];
                Array.Copy(featureFrame.Input, 0, legacyInput, 0, BaseSectionSize);
                Array.Copy(featureFrame.Input, BaseSectionSize + EnhancedGlobalFeatureCount, legacyInput, BaseSectionSize, TemporalFeatureCount);
                return legacyInput;
            }

            throw new ArgumentOutOfRangeException(nameof(targetInputSize), $"Unsupported model input size {targetInputSize}.");
        }

        private void EncodeDirection(
            GameStateSnapshot snapshot,
            Direction direction,
            float[] input,
            ref int writeIndex,
            float[] directionScores,
            bool[] legalMask)
        {
            Vector2Int nextTile = snapshot.PlayerTile + DirectionHelper.ToVector(direction);
            if (nextTile.x < 0)
                nextTile.x = MazeData.Width - 1;
            else if (nextTile.x >= MazeData.Width)
                nextTile.x = 0;

            bool legal = _context.Graph.GetNodeIndex(nextTile) >= 0 &&
                _context.Graph.GetNeighbor(_context.Graph.GetNodeIndex(snapshot.PlayerTile), direction) >= 0;

            legalMask[(int)direction] = legal;

            TileForecast immediateForecast = legal
                ? _context.ForecastEngine.EvaluateTile(snapshot, nextTile, direction, 0)
                : default;
            TileForecast futureForecast = legal
                ? _context.ForecastEngine.EvaluateTile(snapshot, nextTile, direction, 2)
                : default;

            int nextIndex = legal ? _context.Graph.GetNodeIndex(nextTile) : -1;
            int nearestPellet = legal ? _context.Graph.FindNearestPelletDistance(nextTile, _context.Pellets) : 999;
            int nearestEnergizer = legal ? _context.Graph.FindNearestEnergizerDistance(nextTile, _context.Pellets) : 999;
            int pelletsAhead = legal ? _context.Graph.CountPelletsInDirection(snapshot.PlayerTile, direction, _context.Pellets, 8) : 0;
            int localPellets = legal ? _context.Graph.CountPelletsWithin(nextIndex, _context.Pellets, 6) : 0;
            int nearestThreat = immediateForecast.NearestThreatDistance >= 999 ? 999 : immediateForecast.NearestThreatDistance;
            int nearestOpportunity = immediateForecast.NearestOpportunityDistance >= 999 ? 999 : immediateForecast.NearestOpportunityDistance;

            input[writeIndex++] = legal ? 1f : 0f;
            input[writeIndex++] = snapshot.PlayerDirection == direction ? 1f : 0f;
            input[writeIndex++] = snapshot.PlayerDirection != Direction.None && direction == DirectionHelper.Opposite(snapshot.PlayerDirection) ? 1f : 0f;
            input[writeIndex++] = legal && _context.Pellets.HasPellet(nextTile.x, nextTile.y) ? 1f : 0f;
            input[writeIndex++] = legal && MazeData.GetTile(nextTile.x, nextTile.y) == MazeTile.Energizer && _context.Pellets.HasPellet(nextTile.x, nextTile.y) ? 1f : 0f;
            input[writeIndex++] = legal && snapshot.FruitActive && nextTile == snapshot.FruitTile ? 1f : 0f;
            input[writeIndex++] = DistanceAffinity(nearestPellet, 16);
            input[writeIndex++] = DistanceAffinity(nearestEnergizer, 16);
            input[writeIndex++] = DistanceAffinity(nearestOpportunity, 12);
            input[writeIndex++] = DistanceAffinity(nearestThreat, 12);
            input[writeIndex++] = Mathf.Clamp01(immediateForecast.Danger / 320f);
            input[writeIndex++] = Mathf.Clamp01(futureForecast.Danger / 320f);
            input[writeIndex++] = Mathf.Clamp01(immediateForecast.Opportunity / 180f);
            input[writeIndex++] = Mathf.Clamp01(pelletsAhead / 12f);
            input[writeIndex++] = legal ? Mathf.Clamp01(_context.Graph.GetDegree(nextIndex) / 4f) : 0f;
            input[writeIndex++] = legal ? Mathf.Clamp01(_context.Graph.GetDeadEndDepth(nextIndex) / 6f) : 0f;
            input[writeIndex++] = legal && _context.Graph.IsTunnel(nextIndex) ? 1f : 0f;
            input[writeIndex++] = Mathf.Clamp01(localPellets / 12f);

            directionScores[(int)direction] =
                input[writeIndex - 15] * 0.65f +
                input[writeIndex - 12] * 0.45f +
                input[writeIndex - 6] * 0.30f -
                input[writeIndex - 8] * 0.85f -
                input[writeIndex - 7] * 0.55f;
        }

        private static float DistanceAffinity(int distance, int cap)
        {
            if (distance >= 999)
                return 0f;

            return 1f - Mathf.Clamp01(distance / (float)Mathf.Max(1, cap));
        }

        private static Vector2 EncodeVelocity(Direction direction)
        {
            Vector2Int directionVector = DirectionHelper.ToVector(direction);
            return new Vector2(directionVector.x, directionVector.y);
        }
    }
}
