using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ProceduralInterchanges.Core.Models;
using ProceduralInterchanges.Core.Templates.RL;
using Unity.Mathematics;

namespace ProceduralInterchanges.Lab.RL
{
    public sealed class PairwisePreferenceRecord
    {
        public string TimestampUtc { get; set; } = DateTime.UtcNow.ToString("o");
        public InterchangeArchetype Archetype { get; set; }
        public float[] State28 { get; set; } = new float[28];
        public float[] WinnerAction32 { get; set; } = new float[32];
        public float[] LoserAction32 { get; set; } = new float[32];
        public float[] WinnerLossComponents8 { get; set; } = new float[8];
        public float[] LoserLossComponents8 { get; set; } = new float[8];
    }

    public sealed class ExpertDemonstrationRecord
    {
        public string TimestampUtc { get; set; } = DateTime.UtcNow.ToString("o");
        public InterchangeArchetype Archetype { get; set; }
        public float[] State28 { get; set; } = new float[28];
        public float[] ExpertAction32 { get; set; } = new float[32];
        public float UserRating1To5 { get; set; } = 5.0f;
    }

    public sealed class RlhfDataset
    {
        public float[] LearnedRewardWeights { get; set; } = new float[8] { 25f, 30f, 20f, 20f, 35f, 30f, 12f, 2.5f };
        public List<PairwisePreferenceRecord> Preferences { get; set; } = new List<PairwisePreferenceRecord>();
        public List<ExpertDemonstrationRecord> Demonstrations { get; set; } = new List<ExpertDemonstrationRecord>();
    }

    public static class RlhfPreferenceLearner
    {
        public static RlhfDataset Dataset { get; private set; } = new RlhfDataset();

        public static void Load(string jsonPath = "docs/rlhf-preferences.json")
        {
            try
            {
                if (File.Exists(jsonPath))
                {
                    string json = File.ReadAllText(jsonPath);
                    var loaded = JsonSerializer.Deserialize<RlhfDataset>(json);
                    if (loaded != null)
                    {
                        Dataset = loaded;
                        LearnedPolicyWeights.ActiveRewardWeights.FromArray(Dataset.LearnedRewardWeights);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RLHF] Error loading dataset: {ex.Message}");
            }
            Dataset = new RlhfDataset();
        }

        public static void Save(string jsonPath = "docs/rlhf-preferences.json")
        {
            try
            {
                string dir = Path.GetDirectoryName(jsonPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(Dataset, options);
                File.WriteAllText(jsonPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RLHF] Error saving dataset: {ex.Message}");
            }
        }

        public static void RecordPreference(
            InterchangeArchetype archetype,
            float[] state28,
            float[] winnerAction32,
            float[] loserAction32,
            float[] winnerLosses8,
            float[] loserLosses8,
            string jsonPath = "docs/rlhf-preferences.json")
        {
            var record = new PairwisePreferenceRecord
            {
                Archetype = archetype,
                State28 = (float[])state28.Clone(),
                WinnerAction32 = (float[])winnerAction32.Clone(),
                LoserAction32 = (float[])loserAction32.Clone(),
                WinnerLossComponents8 = (float[])winnerLosses8.Clone(),
                LoserLossComponents8 = (float[])loserLosses8.Clone()
            };

            Dataset.Preferences.Add(record);
            UpdateRewardWeights();
            Save(jsonPath);
        }

        public static void RecordDemonstration(
            InterchangeArchetype archetype,
            float[] state28,
            float[] expertAction32,
            float rating,
            string jsonPath = "docs/rlhf-preferences.json")
        {
            var record = new ExpertDemonstrationRecord
            {
                Archetype = archetype,
                State28 = (float[])state28.Clone(),
                ExpertAction32 = (float[])expertAction32.Clone(),
                UserRating1To5 = rating
            };

            Dataset.Demonstrations.Add(record);
            Save(jsonPath);
        }

        public static void UpdateRewardWeights()
        {
            if (Dataset.Preferences == null || Dataset.Preferences.Count == 0) return;

            int pCount = Dataset.Preferences.Count;
            float[] wPrior = new float[8] { 25f, 30f, 20f, 20f, 35f, 30f, 12f, 2.5f };
            float[] w = (float[])Dataset.LearnedRewardWeights.Clone();

            // Run 150 iterations of Bradley-Terry projected gradient descent
            const float lr = 0.5f;
            for (int iter = 0; iter < 150; iter++)
            {
                float[] grad = new float[8];

                for (int p = 0; p < pCount; p++)
                {
                    var pref = Dataset.Preferences[p];
                    // Delta = Loser - Winner
                    float wDotDelta = 0f;
                    float[] delta = new float[8];
                    for (int i = 0; i < 8; i++)
                    {
                        delta[i] = pref.LoserLossComponents8[i] - pref.WinnerLossComponents8[i];
                        wDotDelta += w[i] * delta[i];
                    }

                    // Sigmoid probability of winner over loser
                    float pWinner = 1.0f / (1.0f + math.exp(-math.clamp(wDotDelta, -15f, 15f)));
                    float coeff = -(1.0f - pWinner) / pCount;

                    for (int i = 0; i < 8; i++)
                    {
                        grad[i] += coeff * delta[i];
                    }
                }

                // Prior regularization + gradient step
                for (int i = 0; i < 8; i++)
                {
                    grad[i] += 0.01f * (w[i] - wPrior[i]);
                    w[i] = math.clamp(w[i] - lr * grad[i], 2.0f, 120.0f);
                }
            }

            Dataset.LearnedRewardWeights = w;
            LearnedPolicyWeights.ActiveRewardWeights.FromArray(w);
        }
    }
}
