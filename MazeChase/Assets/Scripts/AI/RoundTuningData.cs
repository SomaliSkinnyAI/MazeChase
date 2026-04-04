using UnityEngine;

namespace MazeChase.AI
{
    /// <summary>
    /// Per-round tuning parameters that control game difficulty progression.
    /// All speed values are in tiles per second. Durations are in seconds.
    /// </summary>
    public struct RoundTuning
    {
        public float PlayerSpeed;
        public float GhostSpeed;
        public float FrightenedPlayerSpeed;
        public float FrightenedGhostSpeed;
        public float TunnelGhostSpeed;
        public float EatenGhostSpeed;
        public float FrightenedDuration;
        public int FrightenedFlashCount;
        public string FruitType;
        public int FruitScore;
    }

    /// <summary>
    /// Static class providing per-round tuning tables that mirror classic Pac-Man
    /// difficulty progression. Use GetTuning(round) to retrieve parameters for a
    /// given round (1-based).
    /// </summary>
    public static class RoundTuningData
    {
        // ── Frightened duration per round ──────────────────────────────
        // Round:  1   2   3   4   5   6   7   8   9  10  11  12  13  14  15  16  17  18  19+
        private static readonly float[] FrightenedDurations =
        {
            12f, 10f, 8f, 6f, 5f, 10f, 4f, 4f, 3f, 10f, 4f, 3f, 3f, 6f, 3f, 3f, 0f, 2f
            // Doubled from classic values for more satisfying ghost chasing.
        };

        // ── Frightened flash counts per round ─────────────────────────
        private static readonly int[] FrightenedFlashCounts =
        {
            5, 5, 5, 5, 5, 5, 5, 5, 3, 5, 5, 3, 3, 5, 3, 3, 0, 3
        };

        // ── Fruit types and scores ────────────────────────────────────
        private static readonly string[] FruitTypes =
        {
            "Cherry",      // Round 1
            "Strawberry",  // Round 2
            "Orange",      // Round 3-4
            "Orange",
            "Apple",       // Round 5-6
            "Apple",
            "Grape",       // Round 7-8
            "Grape",
            "Galaxian",    // Round 9-10
            "Galaxian",
            "Bell",        // Round 11-12
            "Bell",
            "Key"          // Round 13+
        };

        private static readonly int[] FruitScores =
        {
            100,   // Cherry
            300,   // Strawberry
            500,   // Orange
            500,
            700,   // Apple
            700,
            1000,  // Grape
            1000,
            2000,  // Galaxian
            2000,
            3000,  // Bell
            3000,
            5000   // Key
        };

        /// <summary>
        /// Returns the complete tuning parameters for the specified round.
        /// </summary>
        /// <param name="round">1-based round number. Values less than 1 are clamped to 1.</param>
        public static RoundTuning GetTuning(int round)
        {
            round = Mathf.Max(1, round);

            RoundTuning tuning;

            // ── Speeds ────────────────────────────────────────────────
            // Classic Pac-Man speed percentages mapped to tiles/sec.
            // Base speed reference: ~4.0 tiles/sec at 100%.
            if (round == 1)
            {
                tuning.PlayerSpeed = 3.8f;             // Player clearly faster
                tuning.GhostSpeed = 1.8f;              // Easy and approachable
                tuning.FrightenedPlayerSpeed = 4.2f;
                tuning.FrightenedGhostSpeed = 1.0f;    // Very slow when frightened
                tuning.TunnelGhostSpeed = 0.8f;
            }
            else if (round <= 3)
            {
                tuning.PlayerSpeed = 3.8f;
                tuning.GhostSpeed = 2.2f;              // Still comfortable
                tuning.FrightenedPlayerSpeed = 4.2f;
                tuning.FrightenedGhostSpeed = 1.2f;
                tuning.TunnelGhostSpeed = 1.0f;
            }
            else if (round <= 6)
            {
                tuning.PlayerSpeed = 4.0f;
                tuning.GhostSpeed = 2.6f;              // Starting to challenge
                tuning.FrightenedPlayerSpeed = 4.5f;
                tuning.FrightenedGhostSpeed = 1.5f;
                tuning.TunnelGhostSpeed = 1.2f;
            }
            else if (round <= 12)
            {
                tuning.PlayerSpeed = 4.0f;
                tuning.GhostSpeed = 3.0f;              // Getting hard
                tuning.FrightenedPlayerSpeed = 4.5f;
                tuning.FrightenedGhostSpeed = 1.8f;
                tuning.TunnelGhostSpeed = 1.5f;
            }
            else
            {
                tuning.PlayerSpeed = 4.2f;
                tuning.GhostSpeed = 3.4f;              // Expert level
                tuning.FrightenedPlayerSpeed = 4.8f;
                tuning.FrightenedGhostSpeed = 2.0f;
                tuning.TunnelGhostSpeed = 1.8f;
            }

            tuning.EatenGhostSpeed = 4.5f;             // Always fast when eaten

            // ── Frightened duration ───────────────────────────────────
            tuning.FrightenedDuration = GetFrightenedDuration(round);

            // ── Frightened flash count ────────────────────────────────
            tuning.FrightenedFlashCount = GetFrightenedFlashCount(round);

            // ── Fruit ─────────────────────────────────────────────────
            tuning.FruitType = GetFruitType(round);
            tuning.FruitScore = GetFruitScore(round);

            return tuning;
        }

        /// <summary>
        /// Duration of frightened mode in seconds for the given round.
        /// Returns 0 for rounds where ghosts cannot be frightened.
        /// </summary>
        public static float GetFrightenedDuration(int round)
        {
            round = Mathf.Max(1, round);
            if (round <= FrightenedDurations.Length)
                return FrightenedDurations[round - 1];
            return 0f; // Round 19+ has no frightened time
        }

        /// <summary>
        /// Number of white flashes before frightened mode ends.
        /// </summary>
        public static int GetFrightenedFlashCount(int round)
        {
            round = Mathf.Max(1, round);
            if (round <= FrightenedFlashCounts.Length)
                return FrightenedFlashCounts[round - 1];
            return 0;
        }

        /// <summary>
        /// Fruit type name for the given round.
        /// </summary>
        public static string GetFruitType(int round)
        {
            round = Mathf.Max(1, round);
            int index = Mathf.Min(round - 1, FruitTypes.Length - 1);
            return FruitTypes[index];
        }

        /// <summary>
        /// Score value for the fruit that appears on the given round.
        /// </summary>
        public static int GetFruitScore(int round)
        {
            round = Mathf.Max(1, round);
            int index = Mathf.Min(round - 1, FruitScores.Length - 1);
            return FruitScores[index];
        }
    }
}
