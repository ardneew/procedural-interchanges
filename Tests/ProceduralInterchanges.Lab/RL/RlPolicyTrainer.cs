using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Colossal.Mathematics;
using ProceduralInterchanges.Core.Integration;
using ProceduralInterchanges.Core.Math;
using ProceduralInterchanges.Core.Models;
using ProceduralInterchanges.Core.Templates;
using ProceduralInterchanges.Core.Templates.RL;
using Unity.Mathematics;

namespace ProceduralInterchanges.Lab.RL
{
    public sealed class CurriculumScenario
    {
        public WorkshopTemplateDef Template;
        public InterchangeConfig Config;
        public List<HighwayLeg> Legs;
        public int BestS;
        public float[] State28;
        public float ImportanceWeight = 1.0f;
    }

    public static class RlPolicyTrainer
    {
        public static void TrainAllArchetypes(
            string outputPath = "Core/Templates/RL/LearnedPolicyWeights.cs",
            Action<string> log = null,
            Func<bool> shouldContinue = null)
        {
            log = log ?? Console.WriteLine;
            log("[RL-Trainer] Starting offline CEM Teacher + AdamW Policy Distillation...");

            var rewardWeights = LearnedPolicyWeights.ActiveRewardWeights;

            foreach (InterchangeArchetype arch in Enum.GetValues(typeof(InterchangeArchetype)))
            {
                if (shouldContinue != null && !shouldContinue()) break;

                var template = WorkshopTemplateLibrary.GetTemplate(arch);
                if (template == null) continue;

                log($"[RL-Trainer] Generating curriculum for archetype: {arch}...");
                var scenarios = BuildCurriculum(template);

                var weights = LearnedPolicyWeights.GetWeights(arch);
                var optimalActions = new float[scenarios.Count][];

                log($"[RL-Trainer] Running Parallel CEM Teacher across {scenarios.Count} scenarios for {arch}...");
                Parallel.For(0, scenarios.Count, b =>
                {
                    optimalActions[b] = FindOptimalActionCEM(scenarios[b], weights, rewardWeights, seed: (uint)(b * 7919 + 1));
                });

                if (shouldContinue != null && !shouldContinue()) break;

                log($"[RL-Trainer] Distilling into MLP via analytical AdamW backpropagation (250 epochs)...");
                FitMlpToTargets(weights, scenarios, optimalActions, epochs: 250);

                LearnedPolicyWeights.SetWeights(arch, weights);
                log($"[RL-Trainer] Completed training for {arch}.");
            }

            if (!string.IsNullOrEmpty(outputPath) && (shouldContinue == null || shouldContinue()))
            {
                ExportWeightsToCSharp(outputPath);
                log($"[RL-Trainer] Successfully exported trained weights to: {outputPath}");
            }
        }

        public static float[] FindOptimalActionCEM(
            CurriculumScenario scenario,
            PolicyModelWeights currentWeights,
            RewardWeightVector rewardWeights,
            uint seed)
        {
            int actionDim = InterchangeRlStateExtractor.kActionDim;
            float[] mu = new float[actionDim];
            float[] sigma = new float[actionDim];
            for (int i = 0; i < actionDim; i++) sigma[i] = 0.35f;

            // Initialize mu with current policy prediction
            InterchangePolicyNetwork.EvaluatePolicy(currentWeights, scenario.State28, mu);

            // Cold-start heuristic check: skew-counteracting loop scale
            for (int k = 0; k < 4; k++)
            {
                float skew = scenario.State28[k];
                mu[4 * k + 3] = math.clamp(-0.5f * skew, -0.6f, 0.4f);
            }

            float[] bestAction = (float[])mu.Clone();
            var subMap = TemplateSubstructureExtractor.GetOrExtract(scenario.Template, scenario.Config.LeftHandTraffic);
            var initialCourses = TemplateMorphSolver.GenerateMorphedCourses(
                scenario.Template, subMap, scenario.Config, scenario.Legs, scenario.BestS, bestAction);
            float bestR = InterchangeRewardEvaluator.EvaluateFast(initialCourses, scenario.Config, scenario.Legs, rewardWeights);

            const int kIters = 4;
            const int kSamples = 48;
            const int kElite = 8;

            uint rng = seed == 0 ? 1234567u : seed;
            uint NextRng()
            {
                rng ^= rng << 13;
                rng ^= rng >> 17;
                rng ^= rng << 5;
                return rng;
            }

            float NextGaussian()
            {
                float u1 = math.max(1e-7f, (NextRng() & 0xFFFFFF) / 16777216.0f);
                float u2 = (NextRng() & 0xFFFFFF) / 16777216.0f;
                return math.sqrt(-2.0f * math.log(u1)) * math.cos(2.0f * math.PI * u2);
            }

            for (int iter = 0; iter < kIters; iter++)
            {
                var candidates = new (float[] action, float reward)[kSamples];

                for (int m = 0; m < kSamples; m++)
                {
                    float[] a = new float[actionDim];
                    for (int i = 0; i < actionDim; i++)
                    {
                        a[i] = math.clamp(mu[i] + sigma[i] * NextGaussian(), -1f, 1f);
                    }

                    var courses = TemplateMorphSolver.GenerateMorphedCourses(
                        scenario.Template, subMap, scenario.Config, scenario.Legs, scenario.BestS, a);
                    float r = InterchangeRewardEvaluator.EvaluateFast(courses, scenario.Config, scenario.Legs, rewardWeights);
                    candidates[m] = (a, r);

                    if (r > bestR)
                    {
                        bestR = r;
                        bestAction = (float[])a.Clone();
                    }
                }

                Array.Sort(candidates, (x, y) => y.reward.CompareTo(x.reward));

                // Update mu and sigma from elites
                float[] newMu = new float[actionDim];
                for (int e = 0; e < kElite; e++)
                {
                    for (int i = 0; i < actionDim; i++)
                    {
                        newMu[i] += candidates[e].action[i];
                    }
                }
                for (int i = 0; i < actionDim; i++) newMu[i] /= kElite;

                float[] newSigma = new float[actionDim];
                for (int e = 0; e < kElite; e++)
                {
                    for (int i = 0; i < actionDim; i++)
                    {
                        float diff = candidates[e].action[i] - newMu[i];
                        newSigma[i] += diff * diff;
                    }
                }
                for (int i = 0; i < actionDim; i++)
                {
                    float variance = (newSigma[i] / kElite) + 0.01f;
                    sigma[i] = 0.7f * sigma[i] + 0.3f * math.sqrt(variance);
                    mu[i] = newMu[i];
                }
            }

            return bestAction;
        }

        public static void FitMlpToTargets(
            PolicyModelWeights weights,
            List<CurriculumScenario> scenarios,
            float[][] optimalActions,
            int epochs = 250)
        {
            int nSamples = scenarios.Count;
            if (nSamples == 0) return;

            // AdamW optimizer state buffers
            float[] mW1 = new float[64 * 28], vW1 = new float[64 * 28];
            float[] mB1 = new float[64], vB1 = new float[64];
            float[] mW2 = new float[64 * 64], vW2 = new float[64 * 64];
            float[] mB2 = new float[64], vB2 = new float[64];
            float[] mW3 = new float[32 * 64], vW3 = new float[32 * 64];
            float[] mB3 = new float[32], vB3 = new float[32];

            const float beta1 = 0.9f;
            const float beta2 = 0.999f;
            const float eps = 1e-8f;
            const float weightDecay = 1e-4f;
            const float baseLr = 0.004f;

            float beta1T = 1.0f;
            float beta2T = 1.0f;

            for (int ep = 0; ep < epochs; ep++)
            {
                // Cosine learning rate decay
                float lr = baseLr * 0.5f * (1.0f + math.cos(math.PI * ep / epochs));
                beta1T *= beta1;
                beta2T *= beta2;
                float correction1 = 1.0f - beta1T;
                float correction2 = 1.0f - beta2T;

                // Gradient accumulators
                float[] gW1 = new float[64 * 28];
                float[] gB1 = new float[64];
                float[] gW2 = new float[64 * 64];
                float[] gB2 = new float[64];
                float[] gW3 = new float[32 * 64];
                float[] gB3 = new float[32];

                for (int s = 0; s < nSamples; s++)
                {
                    float[] x = scenarios[s].State28;
                    float[] yTarget = optimalActions[s];
                    float omega = scenarios[s].ImportanceWeight;

                    // Forward pass with activations retained
                    float[] z1 = new float[64];
                    float[] h1 = new float[64];
                    for (int i = 0; i < 64; i++)
                    {
                        float sum = weights.B1[i];
                        int off = i * 28;
                        for (int j = 0; j < 28; j++) sum += weights.W1[off + j] * x[j];
                        z1[i] = sum;
                        h1[i] = sum / (1.0f + math.exp(-math.clamp(sum, -20f, 20f)));
                    }

                    float[] z2 = new float[64];
                    float[] h2 = new float[64];
                    for (int i = 0; i < 64; i++)
                    {
                        float sum = weights.B2[i];
                        int off = i * 64;
                        for (int j = 0; j < 64; j++) sum += weights.W2[off + j] * h1[j];
                        z2[i] = sum;
                        h2[i] = sum / (1.0f + math.exp(-math.clamp(sum, -20f, 20f)));
                    }

                    float[] z3 = new float[32];
                    float[] a = new float[32];
                    for (int i = 0; i < 32; i++)
                    {
                        float sum = weights.B3[i];
                        int off = i * 64;
                        for (int j = 0; j < 64; j++) sum += weights.W3[off + j] * h2[j];
                        z3[i] = sum;
                        a[i] = (float)Math.Tanh(math.clamp(sum, -15f, 15f));
                    }

                    // Analytical Backpropagation Equations (Section 4.1 of Blueprint)
                    // 1. Output Layer Gradient: delta3 = omega * (a - yTarget) * (1 - a^2)
                    float[] delta3 = new float[32];
                    for (int i = 0; i < 32; i++)
                    {
                        delta3[i] = omega * (a[i] - yTarget[i]) * (1.0f - a[i] * a[i]);
                        gB3[i] += delta3[i];
                        int off = i * 64;
                        for (int j = 0; j < 64; j++)
                        {
                            gW3[off + j] += delta3[i] * h2[j];
                        }
                    }

                    // 2. Hidden Layer 2 Gradient: delta2 = (W3^T * delta3) * SiLU'(z2)
                    float[] delta2 = new float[64];
                    for (int j = 0; j < 64; j++)
                    {
                        float sum = 0f;
                        for (int i = 0; i < 32; i++)
                        {
                            sum += weights.W3[i * 64 + j] * delta3[i];
                        }
                        float sig = 1.0f / (1.0f + math.exp(-math.clamp(z2[j], -20f, 20f)));
                        float dSiLU = sig * (1.0f + z2[j] * (1.0f - sig));
                        delta2[j] = sum * dSiLU;
                        gB2[j] += delta2[j];
                        int off = j * 64;
                        for (int k = 0; k < 64; k++)
                        {
                            gW2[off + k] += delta2[j] * h1[k];
                        }
                    }

                    // 3. Hidden Layer 1 Gradient: delta1 = (W2^T * delta2) * SiLU'(z1)
                    float[] delta1 = new float[64];
                    for (int k = 0; k < 64; k++)
                    {
                        float sum = 0f;
                        for (int j = 0; j < 64; j++)
                        {
                            sum += weights.W2[j * 64 + k] * delta2[j];
                        }
                        float sig = 1.0f / (1.0f + math.exp(-math.clamp(z1[k], -20f, 20f)));
                        float dSiLU = sig * (1.0f + z1[k] * (1.0f - sig));
                        delta1[k] = sum * dSiLU;
                        gB1[k] += delta1[k];
                        int off = k * 28;
                        for (int l = 0; l < 28; l++)
                        {
                            gW1[off + l] += delta1[k] * x[l];
                        }
                    }
                }

                // AdamW update step helper
                void AdamWStep(float[] param, float[] grad, float[] m, float[] v)
                {
                    float scale = 1.0f / nSamples;
                    for (int i = 0; i < param.Length; i++)
                    {
                        float g = grad[i] * scale;
                        m[i] = beta1 * m[i] + (1.0f - beta1) * g;
                        v[i] = beta2 * v[i] + (1.0f - beta2) * g * g;

                        float mHat = m[i] / correction1;
                        float vHat = v[i] / correction2;

                        param[i] = param[i] - lr * (mHat / (math.sqrt(vHat) + eps) + weightDecay * param[i]);
                    }
                }

                AdamWStep(weights.W1, gW1, mW1, vW1);
                AdamWStep(weights.B1, gB1, mB1, vB1);
                AdamWStep(weights.W2, gW2, mW2, vW2);
                AdamWStep(weights.B2, gB2, mB2, vB2);
                AdamWStep(weights.W3, gW3, mW3, vW3);
                AdamWStep(weights.B3, gB3, mB3, vB3);
            }
        }

        public static List<CurriculumScenario> BuildCurriculum(WorkshopTemplateDef template)
        {
            var scenarios = new List<CurriculumScenario>();
            int n = template.RequiredLegs;
            float rCanon = template.CanonicalRadius;

            // Helper to build synthetic HighwayLegs
            List<HighwayLeg> MakeLegs(float[] legAnglesRad, float radius, float stagger = 0f, float tangentCurveDeg = 0f, bool lht = false)
            {
                var legList = new List<HighwayLeg>();
                for (int i = 0; i < legAnglesRad.Length; i++)
                {
                    float angle = legAnglesRad[i];
                    float2 outward = new float2(math.cos(angle), math.sin(angle));
                    float3 center = float3.zero;

                    float3 pIn = new float3(outward.x * radius, 0f, outward.y * radius);
                    float3 pOut = new float3(outward.x * (radius + stagger), 0f, outward.y * (radius + stagger));

                    // Carriageway separation normal
                    float2 norm = new float2(-outward.y, outward.x);
                    if (lht) norm = -norm;

                    float sep = 28.0f;
                    float3 pInSep = pIn + new float3(norm.x * sep * 0.5f, 0f, norm.y * sep * 0.5f);
                    float3 pOutSep = pOut - new float3(norm.x * sep * 0.5f, 0f, norm.y * sep * 0.5f);

                    float3 inTan = new float3(-outward.x, 0f, -outward.y);
                    if (tangentCurveDeg != 0f)
                    {
                        float rad = math.radians(tangentCurveDeg);
                        float c = math.cos(rad), s = math.sin(rad);
                        inTan = new float3(c * inTan.x - s * inTan.z, 0f, s * inTan.x + c * inTan.z);
                    }

                    var leg = new HighwayLeg
                    {
                        LegIndex = i,
                        AzimuthRad = angle,
                        LegAxisDirection = new float3(outward.x, 0f, outward.y),
                        PortalCenterPosition = (pInSep + pOutSep) * 0.5f,
                        PortalInwardTangent = inTan,
                        IsSeparatedDualCarriageway = true,
                        CarriagewaySeparation = sep,
                        LongitudinalStaggerMeters = stagger,
                        Inbound = new CarriagewayEndpoint
                        {
                            PortalPosition = pInSep,
                            InwardTangent = inTan,
                            OriginalCurve = new Bezier4x3(
                                pInSep + inTan * 60f,
                                pInSep + inTan * 30f,
                                pInSep + inTan * 10f,
                                pInSep
                            ),
                            CutCurveT = 0.5f,
                            IsOneWay = true,
                            IsInboundToCenter = true,
                            LaneCount = 2
                        },
                        Outbound = new CarriagewayEndpoint
                        {
                            PortalPosition = pOutSep,
                            InwardTangent = inTan,
                            OriginalCurve = new Bezier4x3(
                                pOutSep,
                                pOutSep - inTan * 10f,
                                pOutSep - inTan * 30f,
                                pOutSep - inTan * 60f
                            ),
                            CutCurveT = 0.5f,
                            IsOneWay = true,
                            IsInboundToCenter = false,
                            LaneCount = 2
                        }
                    };

                    HighwayPairingSolver.AlignLegToCommonPerpendicularPlane(default, leg, center);
                    legList.Add(leg);
                }
                return legList;
            }

            // 1. 30 Canonical & Near-Canonical Scenarios
            for (int s = 0; s < 30; s++)
            {
                float radius = math.lerp(160f, 320f, s / 29.0f);
                bool lht = (s % 2 == 1);
                float[] angles = new float[n];
                for (int i = 0; i < n; i++)
                {
                    float jitter = math.radians((s * 3 % 11) - 5);
                    angles[i] = template.CanonicalLegAzimuthsRad[i] + jitter;
                }

                var cfg = new InterchangeConfig
                {
                    Archetype = template.Archetype,
                    FootprintRadius = radius,
                    LeftHandTraffic = lht,
                    Center = float3.zero,
                    MinBridgeClearanceMeters = 9.5f
                };
                var legs = MakeLegs(angles, radius, lht: lht);
                var state = InterchangeRlStateExtractor.ExtractState(template, cfg, legs, 0, null);

                scenarios.Add(new CurriculumScenario
                {
                    Template = template,
                    Config = cfg,
                    Legs = legs,
                    BestS = 0,
                    State28 = state,
                    ImportanceWeight = 1.0f
                });
            }

            // 2. 90 Acute & Asymmetric Skew Scenarios (down to 45°, 52°, 60°, 68°, 75°)
            float[] skewAngles = new float[] { 45f, 52f, 60f, 68f, 75f, 82f, 98f, 110f, 120f, 135f };
            for (int s = 0; s < 90; s++)
            {
                float skewDeg = skewAngles[s % skewAngles.Length];
                float radius = math.lerp(180f, 300f, (s % 7) / 6.0f);
                bool lht = (s % 3 == 0);

                float[] angles;
                if (n == 3)
                {
                    float stemAngle = -math.clamp(math.radians(skewDeg), math.radians(35f), math.radians(145f));
                    angles = new[] { stemAngle, 0f, math.PI };
                }
                else
                {
                    angles = new[] { 0f, math.radians(skewDeg), math.PI, math.PI + math.radians(skewDeg) };
                }
                Array.Sort(angles);

                var cfg = new InterchangeConfig
                {
                    Archetype = template.Archetype,
                    FootprintRadius = radius,
                    LeftHandTraffic = lht,
                    Center = float3.zero,
                    MinBridgeClearanceMeters = 9.5f
                };
                var legs = MakeLegs(angles, radius, lht: lht);
                var state = InterchangeRlStateExtractor.ExtractState(template, cfg, legs, 0, null);

                scenarios.Add(new CurriculumScenario
                {
                    Template = template,
                    Config = cfg,
                    Legs = legs,
                    BestS = 0,
                    State28 = state,
                    ImportanceWeight = 1.2f
                });
            }

            // 3. 60 Curved / Staggered / Topographic Scenarios
            for (int s = 0; s < 60; s++)
            {
                float radius = 220f;
                float stagger = (s % 5) * 8.0f; // 0m to 32m stagger
                float curveTan = (s % 3 == 1) ? 12.0f : (s % 3 == 2 ? -12.0f : 0f);
                bool lht = (s % 2 == 1);

                float[] angles = new float[n];
                for (int i = 0; i < n; i++)
                {
                    angles[i] = template.CanonicalLegAzimuthsRad[i];
                }

                float hillH = (s % 4 == 1) ? 8.0f : (s % 4 == 2 ? -6.0f : 0f);
                Func<float2, float> topo = p => hillH * math.exp(-math.lengthsq(p) / (120f * 120f));

                var cfg = new InterchangeConfig
                {
                    Archetype = template.Archetype,
                    FootprintRadius = radius,
                    LeftHandTraffic = lht,
                    Center = float3.zero,
                    MinBridgeClearanceMeters = 9.5f
                };
                var legs = MakeLegs(angles, radius, stagger: stagger, tangentCurveDeg: curveTan, lht: lht);
                var state = InterchangeRlStateExtractor.ExtractState(template, cfg, legs, 0, topo);

                scenarios.Add(new CurriculumScenario
                {
                    Template = template,
                    Config = cfg,
                    Legs = legs,
                    BestS = 0,
                    State28 = state,
                    ImportanceWeight = 1.5f
                });
            }

            // 4. Merge RLHF Expert Demonstrations if available
            if (RlhfPreferenceLearner.Dataset != null && RlhfPreferenceLearner.Dataset.Demonstrations != null)
            {
                foreach (var demo in RlhfPreferenceLearner.Dataset.Demonstrations)
                {
                    if (demo.Archetype == template.Archetype)
                    {
                        var cfg = new InterchangeConfig { Archetype = template.Archetype, FootprintRadius = 220f };
                        var legs = MakeLegs(template.CanonicalLegAzimuthsRad, 220f);
                        scenarios.Add(new CurriculumScenario
                        {
                            Template = template,
                            Config = cfg,
                            Legs = legs,
                            BestS = 0,
                            State28 = (float[])demo.State28.Clone(),
                            ImportanceWeight = 5.0f // Heavy expert demonstration weight
                        });
                    }
                }
            }

            return scenarios;
        }

        public static void ExportWeightsToCSharp(string outputPath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated>");
            sb.AppendLine("// Generated by RlPolicyTrainer in ProceduralInterchanges.Lab");
            sb.AppendLine("// </auto-generated>");
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using ProceduralInterchanges.Core.Models;");
            sb.AppendLine();
            sb.AppendLine("namespace ProceduralInterchanges.Core.Templates.RL");
            sb.AppendLine("{");
            sb.AppendLine("    public sealed class PolicyModelWeights");
            sb.AppendLine("    {");
            sb.AppendLine("        public InterchangeArchetype Archetype;");
            sb.AppendLine("        public float[] W1 = new float[64 * 28];");
            sb.AppendLine("        public float[] B1 = new float[64];");
            sb.AppendLine("        public float[] W2 = new float[64 * 64];");
            sb.AppendLine("        public float[] B2 = new float[64];");
            sb.AppendLine("        public float[] W3 = new float[32 * 64];");
            sb.AppendLine("        public float[] B3 = new float[32];");
            sb.AppendLine();
            sb.AppendLine("        public PolicyModelWeights(InterchangeArchetype archetype)");
            sb.AppendLine("        {");
            sb.AppendLine("            Archetype = archetype;");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    public static class LearnedPolicyWeights");
            sb.AppendLine("    {");
            sb.AppendLine("        private static readonly Dictionary<InterchangeArchetype, PolicyModelWeights> s_Weights");
            sb.AppendLine("            = new Dictionary<InterchangeArchetype, PolicyModelWeights>();");
            sb.AppendLine();
            sb.AppendLine("        public static readonly RewardWeightVector ActiveRewardWeights = new RewardWeightVector();");
            sb.AppendLine();
            sb.AppendLine("        static LearnedPolicyWeights()");
            sb.AppendLine("        {");

            // Format learned reward weights
            var rw = LearnedPolicyWeights.ActiveRewardWeights;
            sb.AppendLine($"            ActiveRewardWeights.WClearance = {rw.WClearance:F2}f;");
            sb.AppendLine($"            ActiveRewardWeights.WCurvature = {rw.WCurvature:F2}f;");
            sb.AppendLine($"            ActiveRewardWeights.WGrade = {rw.WGrade:F2}f;");
            sb.AppendLine($"            ActiveRewardWeights.WKink = {rw.WKink:F2}f;");
            sb.AppendLine($"            ActiveRewardWeights.WOvershoot = {rw.WOvershoot:F2}f;");
            sb.AppendLine($"            ActiveRewardWeights.WDistortion = {rw.WDistortion:F2}f;");
            sb.AppendLine($"            ActiveRewardWeights.WInflection = {rw.WInflection:F2}f;");
            sb.AppendLine($"            ActiveRewardWeights.WLength = {rw.WLength:F2}f;");
            sb.AppendLine();

            foreach (InterchangeArchetype arch in Enum.GetValues(typeof(InterchangeArchetype)))
            {
                var w = LearnedPolicyWeights.GetWeights(arch);
                sb.AppendLine($"            // Archetype: {arch}");
                sb.AppendLine($"            var m_{arch} = new PolicyModelWeights(InterchangeArchetype.{arch});");

                void AppendArray(string name, float[] arr)
                {
                    sb.Append($"            m_{arch}.{name} = new float[] {{ ");
                    for (int i = 0; i < arr.Length; i++)
                    {
                        sb.Append(arr[i].ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture));
                        sb.Append("f");
                        if (i < arr.Length - 1) sb.Append(", ");
                    }
                    sb.AppendLine(" };");
                }

                AppendArray("W1", w.W1);
                AppendArray("B1", w.B1);
                AppendArray("W2", w.W2);
                AppendArray("B2", w.B2);
                AppendArray("W3", w.W3);
                AppendArray("B3", w.B3);

                sb.AppendLine($"            s_Weights[InterchangeArchetype.{arch}] = m_{arch};");
                sb.AppendLine();
            }

            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        public static PolicyModelWeights GetWeights(InterchangeArchetype archetype)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (s_Weights.TryGetValue(archetype, out var weights)) return weights;");
            sb.AppendLine("            var fallback = new PolicyModelWeights(archetype);");
            sb.AppendLine("            s_Weights[archetype] = fallback;");
            sb.AppendLine("            return fallback;");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        public static void SetWeights(InterchangeArchetype archetype, PolicyModelWeights weights)");
            sb.AppendLine("        {");
            sb.AppendLine("            s_Weights[archetype] = weights;");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            string dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(outputPath, sb.ToString());
        }
    }
}
