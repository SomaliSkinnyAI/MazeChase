using UnityEngine;
using MazeChase.Game;

namespace MazeChase.AI.Autoplay
{
    /// <summary>
    /// Learned policy/value bot. When trained weights are not available it falls
    /// back to the planner teacher so autoplay remains strong and the project can
    /// still generate datasets for future training passes.
    /// </summary>
    public sealed class NeuralPolicyBot : IAutoplayBot
    {
        private const int RescuePelletThreshold = 12;
        private const int CriticalRescuePelletThreshold = 4;
        private const int RescueStallThreshold = 18;
        private const int CriticalRescueStallThreshold = 8;
        private const float ConfidenceFallbackThreshold = 0.45f;

        private readonly ResearchPlannerBot _fallbackPlanner = new ResearchPlannerBot();
        private readonly ExpertBot _confidenceFallbackBot = new ExpertBot();
        private AutoplayContext _context;
        private ObservationEncoder _encoder;
        private GraphPolicyModel _model;
        private string _modelSource = string.Empty;
        private bool _missingModelLogged;

        public string Name => "NeuralPolicy";
        public string ActiveModelName => _model != null ? _model.Source : "ResearchPlanner fallback (no neural weights)";
        public string ActiveModelPath => _modelSource;
        public bool HasLoadedModel => _model != null;

        public void Initialize(AutoplayContext context)
        {
            _context = context;
            _encoder = new ObservationEncoder(context);
            _fallbackPlanner.Initialize(context);
            _confidenceFallbackBot.Initialize(context);

            if (GraphPolicyModel.TryLoadDefault(out _model, out _modelSource, _encoder.StackedInputSize, _encoder.InputSize, _encoder.LegacyInputSize))
            {
                Debug.Log($"[NeuralPolicyBot] Loaded policy model from {_modelSource} (inputSize={_model.InputSize}, stackDepth={_encoder.FrameStackDepth})");
            }
        }

        public void ResetForRound()
        {
            _fallbackPlanner.ResetForRound();
            _confidenceFallbackBot.ResetForRound();
            _encoder?.ResetFrameBuffer();
        }

        public BotDecision Evaluate(GameStateSnapshot snapshot)
        {
            PolicyFeatureFrame featureFrame = _encoder.Encode(snapshot);

            if (_model == null)
            {
                if (!_missingModelLogged)
                {
                    Debug.LogWarning("[NeuralPolicyBot] No policy model found. Falling back to ResearchPlannerBot until weights are trained and exported.");
                    _missingModelLogged = true;
                }

                BotDecision fallback = _fallbackPlanner.Evaluate(snapshot);
                fallback.FeatureFrame = featureFrame;
                fallback.Source = "NeuralFallback/ResearchPlanner";
                fallback.UsedFallback = true;
                return fallback;
            }

            if (ShouldUsePlannerRescue(snapshot))
            {
                BotDecision rescue = _fallbackPlanner.Evaluate(snapshot);
                rescue.FeatureFrame = featureFrame;
                rescue.Source = "NeuralRescue/ResearchPlanner";
                rescue.UsedFallback = true;
                rescue.DebugLabel = $"Planner rescue stall={_context.GetNoProgressDecisionStreak()}";
                return rescue;
            }

            float[] modelInput = _encoder.BuildModelInput(featureFrame, _model.InputSize);
            float[] logits = new float[5];
            _model.Evaluate(modelInput, featureFrame.LegalMask, logits, out float valueEstimate, out float deathRiskEstimate);
            Direction rawPolicyDirection = GetBestDirection(logits, featureFrame.LegalMask);
            float rawPolicyConfidence = GraphPolicyModel.ComputeConfidence(logits, rawPolicyDirection);

            if (rawPolicyDirection == Direction.None || rawPolicyConfidence < ConfidenceFallbackThreshold)
            {
                return BuildConfidenceFallback(snapshot, featureFrame, valueEstimate, deathRiskEstimate, rawPolicyConfidence);
            }

            float[] rerankedScores = new float[5];
            Direction bestDirection = Direction.None;
            float bestScore = float.NegativeInfinity;

            foreach (Direction direction in DirectionHelper.AllDirections)
            {
                if (!featureFrame.LegalMask[(int)direction])
                {
                    rerankedScores[(int)direction] = float.NegativeInfinity;
                    continue;
                }

                float score = logits[(int)direction] + (featureFrame.DirectionScores[(int)direction] * 0.35f);

                GameStateSnapshot projected = BuildProjectedSnapshot(snapshot, direction);
                PolicyFeatureFrame projectedFeatures = _encoder.Encode(projected);
                float[] projectedModelInput = _encoder.BuildModelInputProjected(projectedFeatures, _model.InputSize);
                _model.Evaluate(projectedModelInput, projectedFeatures.LegalMask, new float[5], out float projectedValue, out float projectedRisk);
                score += projectedValue * 0.4f;
                score -= projectedRisk * 0.65f;
                score += ComputeLoopAndEndgameBias(snapshot, projected);

                rerankedScores[(int)direction] = score;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestDirection = direction;
                }
            }

            float confidence = GraphPolicyModel.ComputeConfidence(rerankedScores, bestDirection);
            AutoplayObjective objective = DetermineObjective(snapshot, bestDirection);

            var decision = new BotDecision(bestDirection, confidence, valueEstimate, objective, $"Policy {_model.Source} risk={deathRiskEstimate:0.00}")
            {
                Source = $"NeuralPolicy/{_modelSource}",
                DirectionScores = rerankedScores,
                FeatureFrame = featureFrame,
                DeathRiskEstimate = deathRiskEstimate
            };

            return decision;
        }

        private GameStateSnapshot BuildProjectedSnapshot(GameStateSnapshot snapshot, Direction direction)
        {
            Vector2Int nextTile = snapshot.PlayerTile + DirectionHelper.ToVector(direction);
            if (nextTile.x < 0)
                nextTile.x = MazeData.Width - 1;
            else if (nextTile.x >= MazeData.Width)
                nextTile.x = 0;

            bool consumesPellet = _context.Pellets != null && _context.Pellets.HasPellet(nextTile.x, nextTile.y);
            bool collectsFruit = snapshot.FruitActive && nextTile == snapshot.FruitTile;

            GhostSnapshot[] ghosts = new GhostSnapshot[snapshot.Ghosts.Length];
            for (int i = 0; i < ghosts.Length; i++)
                ghosts[i] = snapshot.Ghosts[i];

            return new GameStateSnapshot
            {
                FrameIndex = snapshot.FrameIndex,
                Round = snapshot.Round,
                Lives = snapshot.Lives,
                Score = snapshot.Score,
                RemainingPellets = consumesPellet ? Mathf.Max(0, snapshot.RemainingPellets - 1) : snapshot.RemainingPellets,
                TotalPellets = snapshot.TotalPellets,
                PlayerTile = nextTile,
                PlayerDirection = direction,
                GlobalGhostMode = snapshot.GlobalGhostMode,
                ModeTimeRemaining = snapshot.ModeTimeRemaining,
                FruitActive = collectsFruit ? false : snapshot.FruitActive,
                FruitTile = snapshot.FruitTile,
                Ghosts = ghosts
            };
        }

        private AutoplayObjective DetermineObjective(GameStateSnapshot snapshot, Direction direction)
        {
            if (direction == Direction.None)
                return AutoplayObjective.Survive;

            int currentIndex = _context.Graph.GetNodeIndex(snapshot.PlayerTile);
            if (currentIndex < 0)
                return AutoplayObjective.Survive;

            int nextIndex = _context.Graph.GetNeighbor(currentIndex, direction);
            if (nextIndex < 0)
                return AutoplayObjective.Survive;

            Vector2Int nextTile = _context.Graph.GetTile(nextIndex);
            TileForecast forecast = _context.ForecastEngine.EvaluateTile(snapshot, nextTile, direction, 0);

            if (forecast.Opportunity > 100f)
                return AutoplayObjective.ChaseFrightened;
            if (snapshot.FruitActive && nextTile == snapshot.FruitTile)
                return AutoplayObjective.CollectFruit;
            if (MazeData.GetTile(nextTile.x, nextTile.y) == MazeTile.Energizer && _context.Pellets.HasPellet(nextTile.x, nextTile.y))
                return AutoplayObjective.SeekEnergizer;
            if (forecast.Danger > 90f)
                return AutoplayObjective.EscapeTrap;

            return AutoplayObjective.ClearPellets;
        }

        private float ComputeLoopAndEndgameBias(GameStateSnapshot snapshot, GameStateSnapshot projected)
        {
            if (_context == null || _context.Graph == null || _context.Pellets == null)
                return 0f;

            float bias = 0f;
            float endgamePressure = Mathf.Clamp01((24f - snapshot.RemainingPellets) / 24f);
            float stallPressure = Mathf.Clamp01(_context.GetNoProgressDecisionStreak() / (float)RescueStallThreshold);
            Vector2Int nextTile = projected.PlayerTile;
            int nextIndex = _context.Graph.GetNodeIndex(nextTile);
            int recentVisitCount = _context.GetRecentVisitCount(nextIndex);
            int recentVisitAge = _context.GetRecentVisitAge(nextIndex);
            bool hasPellet = _context.Pellets.HasPellet(nextTile.x, nextTile.y);
            int nearestPellet = _context.Graph.FindNearestPelletDistance(nextTile, _context.Pellets);

            if (recentVisitCount > 0)
                bias -= recentVisitCount * Mathf.Lerp(0.4f, 1.5f, endgamePressure) * (1f + (stallPressure * 1.5f));

            if (recentVisitAge <= 8)
                bias -= (9 - recentVisitAge) * Mathf.Lerp(0.05f, 0.18f, endgamePressure) * (1f + stallPressure);

            if (hasPellet)
                bias += Mathf.Lerp(0.2f, 1.6f, endgamePressure) * (1f + (stallPressure * 1.2f));
            else if (endgamePressure > 0f && recentVisitCount > 0)
                bias -= 0.8f * endgamePressure * (1f + stallPressure);

            if (nearestPellet < 999)
                bias += Mathf.Max(0f, 8f - nearestPellet) * 0.15f * Mathf.Lerp(0.5f, 2.2f, endgamePressure);

            if (stallPressure > 0f && !hasPellet && nearestPellet < 999)
                bias += Mathf.Max(0f, 12f - nearestPellet) * 0.12f * stallPressure;

            if (stallPressure > 0f && recentVisitCount > 1)
                bias -= recentVisitCount * 0.9f * stallPressure;

            return bias;
        }

        private bool ShouldUsePlannerRescue(GameStateSnapshot snapshot)
        {
            if (snapshot == null || _context == null || _context.Graph == null)
                return false;

            int noProgressDecisions = _context.GetNoProgressDecisionStreak();

            if (snapshot.RemainingPellets <= CriticalRescuePelletThreshold && noProgressDecisions >= CriticalRescueStallThreshold)
                return true;

            if (snapshot.RemainingPellets > RescuePelletThreshold || noProgressDecisions < RescueStallThreshold)
                return false;

            int currentIndex = _context.Graph.GetNodeIndex(snapshot.PlayerTile);
            return _context.GetRecentVisitCount(currentIndex) >= 2;
        }

        private BotDecision BuildConfidenceFallback(
            GameStateSnapshot snapshot,
            PolicyFeatureFrame featureFrame,
            float valueEstimate,
            float deathRiskEstimate,
            float policyConfidence)
        {
            BotDecision fallback = _confidenceFallbackBot.Evaluate(snapshot);
            fallback.FeatureFrame = featureFrame;
            fallback.Source = "NeuralLowConfidence/ExpertLegacy";
            fallback.UsedFallback = true;
            fallback.ValueEstimate = valueEstimate;
            fallback.DeathRiskEstimate = deathRiskEstimate;
            fallback.Confidence = policyConfidence;
            fallback.DebugLabel = $"Low-confidence fallback conf={policyConfidence:0.00} risk={deathRiskEstimate:0.00}";
            return fallback;
        }

        private static Direction GetBestDirection(float[] scores, bool[] legalMask)
        {
            Direction bestDirection = Direction.None;
            float bestScore = float.NegativeInfinity;

            foreach (Direction direction in DirectionHelper.AllDirections)
            {
                if (legalMask != null && !legalMask[(int)direction])
                    continue;

                float score = scores[(int)direction];
                if (score > bestScore)
                {
                    bestScore = score;
                    bestDirection = direction;
                }
            }

            return bestDirection;
        }
    }
}
