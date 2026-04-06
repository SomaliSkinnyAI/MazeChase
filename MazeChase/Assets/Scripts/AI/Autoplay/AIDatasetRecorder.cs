using System;
using System.IO;
using UnityEngine;

namespace MazeChase.AI.Autoplay
{
    [Serializable]
    internal sealed class DatasetEntry
    {
        public string episodeId;
        public int frameIndex;
        public int round;
        public int score;
        public int lives;
        public int playerX;
        public int playerY;
        public int playerDirection;
        public int chosenDirection;
        public int objective;
        public int remainingPellets;
        public int totalPellets;
        public bool fruitActive;
        public bool usedFallback;
        public string source;
        public float confidence;
        public float valueEstimate;
        public float deathRiskEstimate;
        public int[] ghostX;
        public int[] ghostY;
        public int[] ghostDirections;
        public int[] ghostStates;
        public float[] input;
        public float[] directionScores;
        public bool[] legalMask;
    }

    /// <summary>
    /// Optional JSONL recorder used to capture training data from planner-backed
    /// autoplay runs. Each line is a committed tile decision.
    /// </summary>
    public sealed class AIDatasetRecorder : IDisposable
    {
        private StreamWriter _writer;
        private string _episodeId = Guid.NewGuid().ToString("N");

        public bool Enabled { get; private set; }
        public string OutputPath { get; private set; }

        public void Initialize(bool enabled)
        {
            Enabled = enabled;
            if (!Enabled)
                return;

            string directory = Path.Combine(Application.persistentDataPath, "AI", "datasets");
            Directory.CreateDirectory(directory);
            OutputPath = Path.Combine(directory, $"policy-dataset-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl");
            _writer = new StreamWriter(OutputPath, false) { AutoFlush = true };
            Debug.Log($"[AIDatasetRecorder] Recording dataset to {OutputPath}");
        }

        public void BeginEpisode()
        {
            if (!Enabled)
                return;

            _episodeId = Guid.NewGuid().ToString("N");
        }

        public void Record(GameStateSnapshot snapshot, BotDecision decision)
        {
            if (!Enabled || _writer == null || snapshot == null || decision == null || decision.FeatureFrame == null)
                return;

            var entry = new DatasetEntry
            {
                episodeId = _episodeId,
                frameIndex = snapshot.FrameIndex,
                round = snapshot.Round,
                score = snapshot.Score,
                lives = snapshot.Lives,
                playerX = snapshot.PlayerTile.x,
                playerY = snapshot.PlayerTile.y,
                playerDirection = (int)snapshot.PlayerDirection,
                chosenDirection = (int)decision.Direction,
                objective = (int)decision.Objective,
                remainingPellets = snapshot.RemainingPellets,
                totalPellets = snapshot.TotalPellets,
                fruitActive = snapshot.FruitActive,
                usedFallback = decision.UsedFallback,
                source = decision.Source ?? string.Empty,
                confidence = decision.Confidence,
                valueEstimate = decision.ValueEstimate,
                deathRiskEstimate = decision.DeathRiskEstimate,
                ghostX = GetGhostXs(snapshot),
                ghostY = GetGhostYs(snapshot),
                ghostDirections = GetGhostDirections(snapshot),
                ghostStates = GetGhostStates(snapshot),
                input = decision.FeatureFrame.Input,
                directionScores = decision.DirectionScores,
                legalMask = decision.FeatureFrame.LegalMask
            };

            _writer.WriteLine(JsonUtility.ToJson(entry));
        }

        public void Dispose()
        {
            _writer?.Dispose();
            _writer = null;
        }

        private static int[] GetGhostXs(GameStateSnapshot snapshot)
        {
            int count = snapshot.Ghosts != null ? snapshot.Ghosts.Length : 0;
            int[] values = new int[count];
            for (int i = 0; i < count; i++)
                values[i] = snapshot.Ghosts[i].Tile.x;

            return values;
        }

        private static int[] GetGhostYs(GameStateSnapshot snapshot)
        {
            int count = snapshot.Ghosts != null ? snapshot.Ghosts.Length : 0;
            int[] values = new int[count];
            for (int i = 0; i < count; i++)
                values[i] = snapshot.Ghosts[i].Tile.y;

            return values;
        }

        private static int[] GetGhostDirections(GameStateSnapshot snapshot)
        {
            int count = snapshot.Ghosts != null ? snapshot.Ghosts.Length : 0;
            int[] values = new int[count];
            for (int i = 0; i < count; i++)
                values[i] = (int)snapshot.Ghosts[i].Direction;

            return values;
        }

        private static int[] GetGhostStates(GameStateSnapshot snapshot)
        {
            int count = snapshot.Ghosts != null ? snapshot.Ghosts.Length : 0;
            int[] values = new int[count];
            for (int i = 0; i < count; i++)
                values[i] = (int)snapshot.Ghosts[i].State;

            return values;
        }
    }
}
