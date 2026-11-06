using System;
using System.Collections.Generic;
using ProceduralInterchanges.Core.Models;
using Unity.Mathematics;

namespace ProceduralInterchanges.Core.Templates.RL
{
    public static class InterchangePolicyNetwork
    {
        public static void EvaluatePolicy(PolicyModelWeights weights, float[] state28, float[] outAction32)
        {
            if (weights == null || state28 == null || outAction32 == null) return;

            float[] h1 = new float[64];
            float[] h2 = new float[64];

            // Layer 1: 28 -> 64 with SiLU
            for (int i = 0; i < 64; i++)
            {
                float z = weights.B1[i];
                int offset = i * 28;
                for (int j = 0; j < 28; j++)
                {
                    z += weights.W1[offset + j] * state28[j];
                }
                // SiLU(z) = z / (1 + exp(-z))
                h1[i] = z / (1.0f + math.exp(-math.clamp(z, -20f, 20f)));
            }

            // Layer 2: 64 -> 64 with SiLU
            for (int i = 0; i < 64; i++)
            {
                float z = weights.B2[i];
                int offset = i * 64;
                for (int j = 0; j < 64; j++)
                {
                    z += weights.W2[offset + j] * h1[j];
                }
                h2[i] = z / (1.0f + math.exp(-math.clamp(z, -20f, 20f)));
            }

            // Layer 3: 64 -> 32 with Tanh
            for (int i = 0; i < 32; i++)
            {
                float z = weights.B3[i];
                int offset = i * 64;
                for (int j = 0; j < 64; j++)
                {
                    z += weights.W3[offset + j] * h2[j];
                }
                outAction32[i] = (float)System.Math.Tanh(math.clamp(z, -15f, 15f));
            }
        }

        public static float[] PredictAndPolishAction(
            WorkshopTemplateDef template,
            TemplateSubstructureMap subMap,
            InterchangeConfig config,
            IReadOnlyList<HighwayLeg> legs,
            int bestS,
            Func<float2, float> sampleTerrainHeight,
            RewardWeightVector rewardWeights = null)
        {
            float[] action32 = new float[InterchangeRlStateExtractor.kActionDim];
            if (template == null) return action32;

            rewardWeights = rewardWeights ?? LearnedPolicyWeights.ActiveRewardWeights;
            float[] state28 = InterchangeRlStateExtractor.ExtractState(template, config, legs, bestS, sampleTerrainHeight);
            var weights = LearnedPolicyWeights.GetWeights(template.Archetype);

            EvaluatePolicy(weights, state28, action32);

            // 8-Step Runtime Coordinate Micro-Polish (<1.1ms)
            // Evaluates fast reward and accepts positive directional probes
            var courses = TemplateMorphSolver.GenerateMorphedCourses(template, subMap, config, legs, bestS, action32, sampleTerrainHeight);
            float bestR = InterchangeRewardEvaluator.EvaluateFast(courses, config, legs, rewardWeights);

            void Probe(int actIdx, float delta)
            {
                float orig = action32[actIdx];
                action32[actIdx] = math.clamp(orig + delta, -1f, 1f);
                var testCourses = TemplateMorphSolver.GenerateMorphedCourses(template, subMap, config, legs, bestS, action32, sampleTerrainHeight);
                float rPlus = InterchangeRewardEvaluator.EvaluateFast(testCourses, config, legs, rewardWeights);

                if (rPlus > bestR + 0.05f)
                {
                    bestR = rPlus;
                    return;
                }

                action32[actIdx] = math.clamp(orig - delta, -1f, 1f);
                testCourses = TemplateMorphSolver.GenerateMorphedCourses(template, subMap, config, legs, bestS, action32, sampleTerrainHeight);
                float rMinus = InterchangeRewardEvaluator.EvaluateFast(testCourses, config, legs, rewardWeights);

                if (rMinus > bestR + 0.05f)
                {
                    bestR = rMinus;
                    return;
                }

                action32[actIdx] = orig; // Revert
            }

            int numClusters = subMap.LoopClusters != null ? subMap.LoopClusters.Length : 0;
            // Probes 0..3: Loop cluster scale and radial shift
            for (int k = 0; k < numClusters && k < 4; k++)
            {
                Probe(4 * k + 3, 0.14f); // Loop scale
                Probe(4 * k + 0, 0.16f); // Radial loop translation
            }

            // Probe 4: Central overpass core rotation
            Probe(26, 0.15f);

            // Probe 5: Central overpass core scale
            Probe(27, 0.15f);

            // Probes 6..7: Terminal handle scale on primary legs
            Probe(28, 0.12f);
            Probe(29, 0.12f);

            return action32;
        }
    }
}
