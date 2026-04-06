using System;
using System.Collections.Generic;
using UnityEngine;
using MazeChase.AI;
using MazeChase.Game;

namespace MazeChase.AI.Autoplay
{
    public enum AutoplayMode
    {
        NeuralPolicy = 0,
        ResearchPlanner = 1,
        ExpertLegacy = 2,
        Attract = 3
    }

    public enum AutoplayObjective
    {
        Survive = 0,
        ClearPellets = 1,
        SeekEnergizer = 2,
        ChaseFrightened = 3,
        CollectFruit = 4,
        EscapeTrap = 5,
        AttractRoute = 6
    }

    public interface IAutoplayBot
    {
        string Name { get; }
        void Initialize(AutoplayContext context);
        void ResetForRound();
        BotDecision Evaluate(GameStateSnapshot snapshot);
    }

    [Serializable]
    public sealed class PolicyFeatureFrame
    {
        public float[] Input;
        public float[] DirectionScores;
        public bool[] LegalMask;
    }

    public sealed class BotDecision
    {
        public static BotDecision None => new BotDecision(Direction.None, 0f, 0f, AutoplayObjective.Survive, "No move");

        public BotDecision(
            Direction direction,
            float confidence,
            float valueEstimate,
            AutoplayObjective objective,
            string debugLabel)
        {
            Direction = direction;
            Confidence = confidence;
            ValueEstimate = valueEstimate;
            Objective = objective;
            DebugLabel = debugLabel ?? string.Empty;
            DirectionScores = new float[5];
        }

        public Direction Direction { get; set; }
        public float Confidence { get; set; }
        public float ValueEstimate { get; set; }
        public float DeathRiskEstimate { get; set; }
        public AutoplayObjective Objective { get; set; }
        public string DebugLabel { get; set; }
        public string Source { get; set; }
        public bool UsedFallback { get; set; }
        public float[] DirectionScores { get; set; }
        public PolicyFeatureFrame FeatureFrame { get; set; }

        public float GetScore(Direction direction)
        {
            int index = (int)direction;
            if (DirectionScores == null || index < 0 || index >= DirectionScores.Length)
                return 0f;

            return DirectionScores[index];
        }
    }

    public struct GhostSnapshot
    {
        public int GhostIndex;
        public Vector2Int Tile;
        public Direction Direction;
        public GhostState State;
    }

    public sealed class GameStateSnapshot
    {
        public int FrameIndex { get; set; }
        public int Round { get; set; }
        public int Lives { get; set; }
        public int Score { get; set; }
        public int RemainingPellets { get; set; }
        public int TotalPellets { get; set; }
        public Vector2Int PlayerTile { get; set; }
        public Direction PlayerDirection { get; set; }
        public GhostState GlobalGhostMode { get; set; }
        public float ModeTimeRemaining { get; set; }
        public bool FruitActive { get; set; }
        public Vector2Int FruitTile { get; set; }
        public GhostSnapshot[] Ghosts { get; set; }

        public float PelletProgress
        {
            get
            {
                if (TotalPellets <= 0)
                    return 0f;

                return 1f - (RemainingPellets / (float)TotalPellets);
            }
        }
    }

    public sealed class AutoplayContext
    {
        private const int RecentTileWindow = 48;
        private const int LowPelletThreshold = 24;
        private readonly Queue<int> _recentTileOrder = new Queue<int>(RecentTileWindow);
        private readonly Dictionary<int, int> _recentTileCounts = new Dictionary<int, int>(RecentTileWindow);
        private readonly Dictionary<int, int> _recentTileLastSeenStep = new Dictionary<int, int>(RecentTileWindow);
        private readonly Dictionary<int, int> _recentTilePreviousSeenStep = new Dictionary<int, int>(RecentTileWindow);
        private int _recentTileStep;
        private Direction _lastObservedDirection = Direction.None;
        private bool _lastDirectionWasReversal;
        private int _sameDirectionStreak;
        private int _lastObservedRound = -1;
        private int _lastObservedLives = -1;
        private int _lastObservedScore = -1;
        private int _lastObservedRemainingPellets = -1;
        private int _noProgressDecisions;
        private int _lowPelletNoProgressDecisions;

        public PlayerController Player { get; private set; }
        public Ghost[] Ghosts { get; private set; }
        public PelletManager Pellets { get; private set; }
        public FruitSpawner FruitSpawner { get; private set; }
        public GhostModeTimer GhostModeTimer { get; private set; }
        public MazeGraph Graph { get; private set; }
        public GhostForecastEngine ForecastEngine { get; private set; }
        public AIDatasetRecorder DatasetRecorder { get; private set; }

        public void Initialize(
            PlayerController player,
            Ghost[] ghosts,
            PelletManager pellets,
            FruitSpawner fruitSpawner,
            GhostModeTimer ghostModeTimer,
            MazeGraph graph,
            GhostForecastEngine forecastEngine,
            AIDatasetRecorder datasetRecorder)
        {
            Player = player;
            Ghosts = ghosts;
            Pellets = pellets;
            FruitSpawner = fruitSpawner;
            GhostModeTimer = ghostModeTimer;
            Graph = graph;
            ForecastEngine = forecastEngine;
            DatasetRecorder = datasetRecorder;
        }

        public void ResetRuntimeMemory()
        {
            _recentTileOrder.Clear();
            _recentTileCounts.Clear();
            _recentTileLastSeenStep.Clear();
            _recentTilePreviousSeenStep.Clear();
            _recentTileStep = 0;
            _lastObservedDirection = Direction.None;
            _lastDirectionWasReversal = false;
            _sameDirectionStreak = 0;
            _lastObservedRound = -1;
            _lastObservedLives = -1;
            _lastObservedScore = -1;
            _lastObservedRemainingPellets = -1;
            _noProgressDecisions = 0;
            _lowPelletNoProgressDecisions = 0;
        }

        public void RememberVisitedTile(Vector2Int tile)
        {
            if (Graph == null)
                return;

            int nodeIndex = Graph.GetNodeIndex(tile);
            if (nodeIndex < 0)
                return;

            _recentTileStep++;
            _recentTileOrder.Enqueue(nodeIndex);

            if (_recentTileLastSeenStep.TryGetValue(nodeIndex, out int previousLastSeenStep))
                _recentTilePreviousSeenStep[nodeIndex] = previousLastSeenStep;

            if (_recentTileCounts.TryGetValue(nodeIndex, out int currentCount))
                _recentTileCounts[nodeIndex] = currentCount + 1;
            else
                _recentTileCounts[nodeIndex] = 1;

            _recentTileLastSeenStep[nodeIndex] = _recentTileStep;

            while (_recentTileOrder.Count > RecentTileWindow)
            {
                int expired = _recentTileOrder.Dequeue();
                if (!_recentTileCounts.TryGetValue(expired, out int expiredCount))
                    continue;

                if (expiredCount <= 1)
                    _recentTileCounts.Remove(expired);
                else
                    _recentTileCounts[expired] = expiredCount - 1;
            }
        }

        public int GetRecentVisitCount(Vector2Int tile)
        {
            if (Graph == null)
                return 0;

            int nodeIndex = Graph.GetNodeIndex(tile);
            if (nodeIndex < 0)
                return 0;

            return GetRecentVisitCount(nodeIndex);
        }

        public int GetRecentVisitCount(int nodeIndex)
        {
            if (nodeIndex < 0)
                return 0;

            return _recentTileCounts.TryGetValue(nodeIndex, out int count) ? count : 0;
        }

        public int GetRecentVisitAge(Vector2Int tile)
        {
            if (Graph == null)
                return int.MaxValue;

            int nodeIndex = Graph.GetNodeIndex(tile);
            if (nodeIndex < 0)
                return int.MaxValue;

            return GetRecentVisitAge(nodeIndex);
        }

        public int GetRecentVisitAge(int nodeIndex)
        {
            if (nodeIndex < 0)
                return int.MaxValue;

            return _recentTileLastSeenStep.TryGetValue(nodeIndex, out int lastSeenStep)
                ? _recentTileStep - lastSeenStep
                : int.MaxValue;
        }

        public int GetRecentRepeatAge(Vector2Int tile)
        {
            if (Graph == null)
                return int.MaxValue;

            int nodeIndex = Graph.GetNodeIndex(tile);
            if (nodeIndex < 0)
                return int.MaxValue;

            return GetRecentRepeatAge(nodeIndex);
        }

        public int GetRecentRepeatAge(int nodeIndex)
        {
            if (nodeIndex < 0)
                return int.MaxValue;

            return _recentTilePreviousSeenStep.TryGetValue(nodeIndex, out int previousSeenStep)
                ? _recentTileStep - previousSeenStep
                : int.MaxValue;
        }

        public int GetRecentUniqueTileCount()
        {
            return _recentTileCounts.Count;
        }

        public int GetRecentTileWindowSize()
        {
            return RecentTileWindow;
        }

        public int GetNoProgressDecisionStreak()
        {
            return _noProgressDecisions;
        }

        public int GetLowPelletNoProgressStreak()
        {
            return _lowPelletNoProgressDecisions;
        }

        public int GetSameDirectionStreak()
        {
            return _sameDirectionStreak;
        }

        public bool GetLastDirectionWasReversal()
        {
            return _lastDirectionWasReversal;
        }

        public void ObserveDecisionSnapshot(GameStateSnapshot snapshot)
        {
            if (snapshot == null)
            {
                ResetRuntimeMemory();
                return;
            }

            UpdateDirectionMemory(snapshot.PlayerDirection);
            UpdateProgressMemory(snapshot);
        }

        private void UpdateDirectionMemory(Direction direction)
        {
            if (direction == Direction.None)
            {
                _lastDirectionWasReversal = false;
                _sameDirectionStreak = 0;
                return;
            }

            _lastDirectionWasReversal =
                _lastObservedDirection != Direction.None &&
                direction == DirectionHelper.Opposite(_lastObservedDirection);

            if (direction == _lastObservedDirection)
                _sameDirectionStreak++;
            else
                _sameDirectionStreak = 1;

            _lastObservedDirection = direction;
        }

        private void UpdateProgressMemory(GameStateSnapshot snapshot)
        {
            bool needsReset =
                _lastObservedRound < 0 ||
                snapshot.Round != _lastObservedRound ||
                snapshot.Lives != _lastObservedLives ||
                snapshot.RemainingPellets > _lastObservedRemainingPellets ||
                snapshot.Score < _lastObservedScore;

            if (needsReset)
            {
                _noProgressDecisions = 0;
                _lowPelletNoProgressDecisions = 0;
            }
            else
            {
                bool madeProgress =
                    snapshot.Score != _lastObservedScore ||
                    snapshot.RemainingPellets != _lastObservedRemainingPellets;

                if (madeProgress)
                    _noProgressDecisions = 0;
                else
                    _noProgressDecisions++;

                if (snapshot.RemainingPellets <= LowPelletThreshold)
                {
                    if (madeProgress)
                        _lowPelletNoProgressDecisions = 0;
                    else
                        _lowPelletNoProgressDecisions++;
                }
                else
                {
                    _lowPelletNoProgressDecisions = 0;
                }
            }

            _lastObservedRound = snapshot.Round;
            _lastObservedLives = snapshot.Lives;
            _lastObservedScore = snapshot.Score;
            _lastObservedRemainingPellets = snapshot.RemainingPellets;
        }
    }
}
