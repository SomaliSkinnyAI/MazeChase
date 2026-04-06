using System.IO;
using UnityEngine;
using MazeChase.Game;

namespace MazeChase.AI.Autoplay
{
    [System.Serializable]
    public sealed class GraphPolicyModelWeights
    {
        public string version = "1.0";
        public string source = "unknown";
        public int inputSize;
        public int hidden1Size;
        public int hidden2Size;
        public float[] w1;
        public float[] b1;
        public float[] w2;
        public float[] b2;
        public float[] policyW;
        public float[] policyB;
        public float[] valueW;
        public float[] valueB;
        public float[] riskW;
        public float[] riskB;
    }

    /// <summary>
    /// Lightweight MLP policy/value model executed entirely in C#.
    /// </summary>
    public sealed class GraphPolicyModel
    {
        private readonly GraphPolicyModelWeights _weights;

        private GraphPolicyModel(GraphPolicyModelWeights weights)
        {
            _weights = weights;
        }

        public string Source => _weights.source;
        public int InputSize => _weights.inputSize;

        public static bool TryLoadDefault(out GraphPolicyModel model, out string source, params int[] expectedInputSizes)
        {
            string persistentPath = Path.Combine(Application.persistentDataPath, "AI", "policy_model.json");
            if (TryLoadFromPath(persistentPath, out model, expectedInputSizes))
            {
                source = persistentPath;
                return true;
            }

            string streamingPath = Path.Combine(Application.streamingAssetsPath, "AI", "policy_model.json");
            if (TryLoadFromPath(streamingPath, out model, expectedInputSizes))
            {
                source = streamingPath;
                return true;
            }

            source = string.Empty;
            model = null;
            return false;
        }

        public static bool TryLoadFromPath(string path, out GraphPolicyModel model, params int[] expectedInputSizes)
        {
            model = null;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return false;

            try
            {
                string json = File.ReadAllText(path);
                GraphPolicyModelWeights weights = JsonUtility.FromJson<GraphPolicyModelWeights>(json);
                if (!Validate(weights, expectedInputSizes))
                    return false;

                model = new GraphPolicyModel(weights);
                return true;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[GraphPolicyModel] Failed to load model from {path}: {ex.Message}");
                return false;
            }
        }

        public void Evaluate(float[] input, bool[] legalMask, float[] logitsOut, out float valueOut, out float deathRiskOut)
        {
            float[] hidden1 = new float[_weights.hidden1Size];
            float[] hidden2 = new float[_weights.hidden2Size];

            for (int hidden = 0; hidden < _weights.hidden1Size; hidden++)
            {
                float sum = _weights.b1[hidden];
                int offset = hidden * _weights.inputSize;
                for (int inputIndex = 0; inputIndex < _weights.inputSize; inputIndex++)
                    sum += input[inputIndex] * _weights.w1[offset + inputIndex];

                hidden1[hidden] = Mathf.Max(0f, sum);
            }

            for (int hidden = 0; hidden < _weights.hidden2Size; hidden++)
            {
                float sum = _weights.b2[hidden];
                int offset = hidden * _weights.hidden1Size;
                for (int inputIndex = 0; inputIndex < _weights.hidden1Size; inputIndex++)
                    sum += hidden1[inputIndex] * _weights.w2[offset + inputIndex];

                hidden2[hidden] = Mathf.Max(0f, sum);
            }

            for (int action = 0; action < 4; action++)
            {
                float sum = _weights.policyB[action];
                int offset = action * _weights.hidden2Size;
                for (int hidden = 0; hidden < _weights.hidden2Size; hidden++)
                    sum += hidden2[hidden] * _weights.policyW[offset + hidden];

                Direction direction = action == 0
                    ? Direction.Up
                    : action == 1
                        ? Direction.Down
                        : action == 2
                            ? Direction.Left
                            : Direction.Right;

                logitsOut[(int)direction] = legalMask != null && !legalMask[(int)direction]
                    ? -1000000f
                    : sum;
            }

            float value = _weights.valueB[0];
            for (int hidden = 0; hidden < _weights.hidden2Size; hidden++)
                value += hidden2[hidden] * _weights.valueW[hidden];

            valueOut = (float)System.Math.Tanh(value);

            if (_weights.riskW != null && _weights.riskW.Length == _weights.hidden2Size && _weights.riskB != null && _weights.riskB.Length == 1)
            {
                float risk = _weights.riskB[0];
                for (int hidden = 0; hidden < _weights.hidden2Size; hidden++)
                    risk += hidden2[hidden] * _weights.riskW[hidden];

                deathRiskOut = 1f / (1f + Mathf.Exp(-risk));
            }
            else
            {
                deathRiskOut = 0.5f;
            }
        }

        public static float ComputeConfidence(float[] logits, Direction chosenDirection)
        {
            float maxLogit = float.NegativeInfinity;
            foreach (Direction direction in DirectionHelper.AllDirections)
                maxLogit = Mathf.Max(maxLogit, logits[(int)direction]);

            float sum = 0f;
            float chosen = 0f;
            foreach (Direction direction in DirectionHelper.AllDirections)
            {
                float exp = Mathf.Exp(logits[(int)direction] - maxLogit);
                sum += exp;
                if (direction == chosenDirection)
                    chosen = exp;
            }

            if (sum <= 0f)
                return 0f;

            return chosen / sum;
        }

        private static bool Validate(GraphPolicyModelWeights weights, int[] expectedInputSizes)
        {
            if (weights == null)
                return false;

            if (expectedInputSizes != null && expectedInputSizes.Length > 0 && System.Array.IndexOf(expectedInputSizes, weights.inputSize) < 0)
                return false;

            if (weights.hidden1Size <= 0 || weights.hidden2Size <= 0)
                return false;

            return
                weights.w1 != null && weights.w1.Length == weights.inputSize * weights.hidden1Size &&
                weights.b1 != null && weights.b1.Length == weights.hidden1Size &&
                weights.w2 != null && weights.w2.Length == weights.hidden1Size * weights.hidden2Size &&
                weights.b2 != null && weights.b2.Length == weights.hidden2Size &&
                weights.policyW != null && weights.policyW.Length == weights.hidden2Size * 4 &&
                weights.policyB != null && weights.policyB.Length == 4 &&
                weights.valueW != null && weights.valueW.Length == weights.hidden2Size &&
                weights.valueB != null && weights.valueB.Length == 1 &&
                (weights.riskW == null || weights.riskW.Length == weights.hidden2Size) &&
                (weights.riskB == null || weights.riskB.Length == 1);
        }
    }
}
