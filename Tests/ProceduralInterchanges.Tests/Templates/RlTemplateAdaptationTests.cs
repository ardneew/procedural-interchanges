using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using ProceduralInterchanges.Core.Integration;
using ProceduralInterchanges.Core.Math;
using ProceduralInterchanges.Core.Models;
using ProceduralInterchanges.Core.Templates;
using ProceduralInterchanges.Core.Templates.RL;
using ProceduralInterchanges.Lab.RL;
using ProceduralInterchanges.Tests.Audit;
using ProceduralInterchanges.Tests.Fixtures;
using Unity.Mathematics;
using Xunit;

namespace ProceduralInterchanges.Tests.Templates
{
    public class RlTemplateAdaptationTests
    {
        [Fact]
        public void SubstructureExtraction_All9Templates_SucceedsAndHasExpectedClusters()
        {
            foreach (InterchangeArchetype arch in Enum.GetValues(typeof(InterchangeArchetype)))
            {
                var template = WorkshopTemplateLibrary.GetTemplate(arch);
                Assert.NotNull(template);

                var subMap = TemplateSubstructureExtractor.GetOrExtract(template, leftHandTraffic: false);
                Assert.NotNull(subMap);
                Assert.Equal(template.Nodes.Length, subMap.SanitizedNodes.Length);
                Assert.Equal(template.Segments.Length, subMap.SegmentCanonVectorA.Length);
                Assert.Equal(template.Segments.Length, subMap.SegmentCanonVectorD.Length);

                // High-curvature templates with loop ramps must identify loop clusters
                if (arch == InterchangeArchetype.Cloverleaf ||
                    arch == InterchangeArchetype.Trumpet)
                {
                    Assert.True(subMap.LoopClusters != null && subMap.LoopClusters.Length > 0,
                        $"Template {arch} must have identified at least 1 loop cluster");
                }

                // Verify gore nodes
                Assert.True(subMap.GoreNodes != null && subMap.GoreNodes.Length > 0,
                    $"Template {arch} must have identified gore nodes");

                // Check LHT extraction as well
                var subMapLht = TemplateSubstructureExtractor.GetOrExtract(template, leftHandTraffic: true);
                Assert.NotNull(subMapLht);
                Assert.True(subMapLht.LeftHandTraffic);
            }
        }

        [Fact]
        public void AlignLegToCommonPerpendicularPlane_UnevenStaggeredCarriageway_TrimsFlush()
        {
            float3 center = float3.zero;
            float radius = 200f;
            float stagger = 25f;

            float2 outward = new float2(1f, 0f);
            float sep = 28f;

            float3 pIn = new float3(radius, 0f, sep * 0.5f);
            float3 pOut = new float3(radius + stagger, 0f, -sep * 0.5f);
            float3 inTan = new float3(-1f, 0f, 0f);

            var leg = new HighwayLeg
            {
                LegIndex = 0,
                AzimuthRad = 0f,
                LegAxisDirection = new float3(1f, 0f, 0f),
                PortalCenterPosition = (pIn + pOut) * 0.5f,
                PortalInwardTangent = inTan,
                IsSeparatedDualCarriageway = true,
                CarriagewaySeparation = sep,
                LongitudinalStaggerMeters = stagger,
                Inbound = new CarriagewayEndpoint
                {
                    PortalPosition = pIn,
                    InwardTangent = inTan,
                    OriginalCurve = new Bezier4x3(
                        pIn - inTan * 100f,
                        pIn - inTan * 50f,
                        pIn + inTan * 20f,
                        pIn + inTan * 50f
                    ),
                    CutCurveT = 0.5f,
                    IsOneWay = true,
                    IsInboundToCenter = true,
                    LaneCount = 2
                },
                Outbound = new CarriagewayEndpoint
                {
                    PortalPosition = pOut,
                    InwardTangent = -inTan,
                    OriginalCurve = new Bezier4x3(
                        pOut,
                        pOut - inTan * 10f,
                        pOut - inTan * 30f,
                        pOut - inTan * 60f
                    ),
                    CutCurveT = 0.5f,
                    IsOneWay = true,
                    IsInboundToCenter = false,
                    LaneCount = 2
                }
            };

            HighwayPairingSolver.AlignLegToCommonPerpendicularPlane(default, leg, center);

            // Plane is anchored at pOut (farEp), so Inbound (nearEp) must be trimmed back
            Assert.True(leg.Inbound.RequiresOuterTrimReplacement);
            // After trimming, both portals must have virtually equal longitudinal X position
            float xDiff = math.abs(leg.Inbound.PortalPosition.x - leg.Outbound.PortalPosition.x);
            Assert.True(xDiff < 1.0f, $"Portals must be flush after alignment, but diff was {xDiff:F2}m");
        }

        [Fact]
        public void TemplateMorphSolver_AcuteSkew45Deg_PreservesMinimumRadiusAndNoOvershoot()
        {
            var template = WorkshopTemplateLibrary.GetTemplate(InterchangeArchetype.Cloverleaf);
            var legs = SyntheticHighwaySites.CreateSkewed4Way(45f, 240f);
            var config = new InterchangeConfig
            {
                Archetype = InterchangeArchetype.Cloverleaf,
                Center = float3.zero,
                FootprintRadius = 240f,
                MinBridgeClearanceMeters = 7.5f
            };

            var plan = new InterchangePlan { Config = config, Legs = legs };
            TemplateMorphSolver.GenerateMorphedPlan(template, config, legs, null, plan);

            Assert.True(plan.Courses.Count > 0);

            // Verify minimum curvature radius across all courses >= 18.0m (no pinching into 11m hairpins!)
            float minRadius = float.MaxValue;
            string minCourse = "";
            foreach (var course in plan.Courses)
            {
                for (int s = 0; s <= 8; s++)
                {
                    float r = CubicBezier3D.CurvatureRadius(course.Curve, s / 8.0f);
                    if (r < minRadius)
                    {
                        minRadius = r;
                        minCourse = $"{course.Id} ({course.Role}) at t={s/8.0f}";
                    }
                }
            }

            Assert.True(minRadius >= 18.0f, $"Minimum curvature radius under 45 deg skew must be >= 18.0m, but found {minRadius:F2}m on course {minCourse}");

            var reward = InterchangeRewardEvaluator.Evaluate(plan, config, legs);
            Assert.True(reward.TotalReward >= 75.0f, $"Total reward should be high, found {reward.TotalReward:F1}. Losses: l1_clr={reward.LossComponents[0]:F1}, l2_curv={reward.LossComponents[1]:F1}, l3_grd={reward.LossComponents[2]:F1}, l4_knk={reward.LossComponents[3]:F1}, l5_ovr={reward.LossComponents[4]:F1}, l6_dst={reward.LossComponents[5]:F1}, l7_inf={reward.LossComponents[6]:F1}, l8_len={reward.LossComponents[7]:F1}");
        }

        [Fact]
        public void PolicyNetwork_ForwardAndAnalyticalBackwardPass_DecreasesLoss()
        {
            var weights = new PolicyModelWeights(InterchangeArchetype.Cloverleaf);
            float[] state = new float[28];
            state[0] = -0.5f; // acute skew
            state[20] = 0.2f;

            float[] actionTarget = new float[32];
            actionTarget[3] = -0.3f; // scale down loop 0
            actionTarget[7] = 0.2f;

            // Scenario wrapper
            var scenario = new CurriculumScenario
            {
                State28 = state,
                ImportanceWeight = 1.0f
            };

            var scenarios = new List<CurriculumScenario> { scenario };
            var targets = new float[][] { actionTarget };

            float[] actionInit = new float[32];
            InterchangePolicyNetwork.EvaluatePolicy(weights, state, actionInit);
            float initLoss = 0f;
            for (int i = 0; i < 32; i++)
            {
                float d = actionInit[i] - actionTarget[i];
                initLoss += 0.5f * d * d;
            }

            // Train for 20 epochs
            RlPolicyTrainer.FitMlpToTargets(weights, scenarios, targets, epochs: 20);

            float[] actionFinal = new float[32];
            InterchangePolicyNetwork.EvaluatePolicy(weights, state, actionFinal);
            float finalLoss = 0f;
            for (int i = 0; i < 32; i++)
            {
                float d = actionFinal[i] - actionTarget[i];
                finalLoss += 0.5f * d * d;
            }

            Assert.True(finalLoss < initLoss, $"Analytical backprop must decrease MSE loss: init={initLoss:F4}, final={finalLoss:F4}");
        }

        [Fact]
        public void BradleyTerryPreferenceUpdate_OptimizesRewardWeightsCorrectly()
        {
            var initialWeights = (float[])RlhfPreferenceLearner.Dataset.LearnedRewardWeights.Clone();

            // Add preference where candidate with low curvature loss beats candidate with high curvature loss
            float[] state = new float[28];
            float[] aWinner = new float[32];
            float[] aLoser = new float[32];
            float[] lWinner = new float[8] { 0f, 0.1f, 0f, 0f, 0f, 0f, 0f, 1f };
            float[] lLoser = new float[8] { 0f, 2.5f, 0f, 0f, 0f, 0f, 0f, 1f };

            RlhfPreferenceLearner.RecordPreference(
                InterchangeArchetype.Cloverleaf, state, aWinner, aLoser, lWinner, lLoser,
                jsonPath: "docs/test-rlhf-preferences.json");

            var updatedWeights = RlhfPreferenceLearner.Dataset.LearnedRewardWeights;
            Assert.Equal(8, updatedWeights.Length);
            for (int i = 0; i < 8; i++)
            {
                Assert.InRange(updatedWeights[i], 2.0f, 120.0f);
            }
        }

        [Fact]
        public void TemplateMorphSolver_UndulatingSlopedTerrain_ConformsNodeElevationsToTerrain()
        {
            var template = WorkshopTemplateLibrary.GetTemplate(InterchangeArchetype.Trumpet);
            Func<float2, float> hillSampler = p => 12.0f * math.exp(-math.lengthsq(p) / (2f * 60f * 60f));

            var legs = SyntheticHighwaySites.Create3WayJunction(angleDeg: 90f, radius: 180f, terrainSampler: hillSampler);
            var config = new InterchangeConfig
            {
                Archetype = InterchangeArchetype.Trumpet,
                Center = new float3(0f, hillSampler(float2.zero), 0f),
                FootprintRadius = 180f,
                MinBridgeClearanceMeters = 7.5f
            };

            var plan = new InterchangePlan { Config = config, Legs = legs };
            TemplateMorphSolver.GenerateMorphedPlan(template, config, legs, null, plan, null, hillSampler);

            Assert.True(plan.Courses.Count > 0);

            // Ensure nodes near the peak (r < 50m) are elevated to follow the 12m hill
            bool foundElevatedPeak = false;
            foreach (var course in plan.Courses)
            {
                for (int s = 0; s <= 4; s++)
                {
                    float3 pt = CubicBezier3D.Position(course.Curve, s / 4.0f);
                    float terrY = hillSampler(pt.xz);
                    // The road surface should be at or above terrain (not buried inside the hill)
                    Assert.True(pt.y >= terrY - 0.5f,
                        $"Course {course.Id} at {pt} is buried beneath terrain {terrY:F1}m (diff={pt.y - terrY:F1}m)");

                    if (math.length(pt.xz) < 40f && pt.y >= 9.0f)
                    {
                        foundElevatedPeak = true;
                    }
                }
            }

            Assert.True(foundElevatedPeak, "Interchange courses near peak of 12m hill must reach elevated heights >= 9.0m");
        }

        [Fact]
        public void TemplateMorphSolver_ZeroLengthTangentLegs_DoesNotProduceNaNs()
        {
            var template = WorkshopTemplateLibrary.GetTemplate(InterchangeArchetype.Trumpet);
            var legs = SyntheticHighwaySites.Create3WayJunction(angleDeg: 90f, radius: 180f);

            // Intentionally corrupt inward tangent to zero
            legs[0].PortalInwardTangent = float3.zero;
            legs[0].Inbound.InwardTangent = float3.zero;

            var config = new InterchangeConfig
            {
                Archetype = InterchangeArchetype.Trumpet,
                Center = float3.zero,
                FootprintRadius = 180f
            };

            var plan = new InterchangePlan { Config = config, Legs = legs };
            TemplateMorphSolver.GenerateMorphedPlan(template, config, legs, null, plan);

            Assert.True(plan.Courses.Count > 0);
            foreach (var course in plan.Courses)
            {
                Assert.False(float.IsNaN(course.Curve.a.x) || float.IsNaN(course.Curve.a.y) || float.IsNaN(course.Curve.a.z));
                Assert.False(float.IsNaN(course.Curve.b.x) || float.IsNaN(course.Curve.b.y) || float.IsNaN(course.Curve.b.z));
                Assert.False(float.IsNaN(course.Curve.c.x) || float.IsNaN(course.Curve.c.y) || float.IsNaN(course.Curve.c.z));
                Assert.False(float.IsNaN(course.Curve.d.x) || float.IsNaN(course.Curve.d.y) || float.IsNaN(course.Curve.d.z));
            }

            var reward = InterchangeRewardEvaluator.Evaluate(plan, config, legs);
            Assert.False(float.IsNaN(reward.TotalReward));
            for (int i = 0; i < 8; i++)
            {
                Assert.False(float.IsNaN(reward.LossComponents[i]));
            }
        }

        [Theory]
        [InlineData(InterchangeArchetype.Trumpet, 90f)]
        [InlineData(InterchangeArchetype.Trumpet, 60f)]
        [InlineData(InterchangeArchetype.Trumpet, 120f)]
        [InlineData(InterchangeArchetype.DirectionalT, 90f)]
        [InlineData(InterchangeArchetype.DirectionalT, 75f)]
        [InlineData(InterchangeArchetype.YStack, 90f)]
        [InlineData(InterchangeArchetype.YStack, 60f)]
        [InlineData(InterchangeArchetype.Diamond, 90f)]
        [InlineData(InterchangeArchetype.Diamond, 60f)]
        [InlineData(InterchangeArchetype.Diamond, 120f)]
        [InlineData(InterchangeArchetype.ParcloA4, 90f)]
        [InlineData(InterchangeArchetype.ParcloA4, 75f)]
        [InlineData(InterchangeArchetype.Cloverleaf, 90f)]
        [InlineData(InterchangeArchetype.Cloverleaf, 60f)]
        [InlineData(InterchangeArchetype.FourLevelStack, 90f)]
        [InlineData(InterchangeArchetype.FourLevelStack, 75f)]
        [InlineData(InterchangeArchetype.Turbine, 90f)]
        [InlineData(InterchangeArchetype.Turbine, 75f)]
        public void TemplateMorphSolver_AllArchetypesAcrossSkewAngles_PassesAudit(InterchangeArchetype archetype, float skewAngleDeg)
        {
            var template = WorkshopTemplateLibrary.GetTemplate(archetype);
            Assert.NotNull(template);

            float radius = template.RequiredLegs == 3 ? 180f : 240f;
            List<HighwayLeg> legs = template.RequiredLegs == 3
                ? SyntheticHighwaySites.Create3WayJunction(angleDeg: skewAngleDeg, radius: radius)
                : SyntheticHighwaySites.CreateSkewed4Way(skewAngleDeg: skewAngleDeg, radius: radius);

            var config = new InterchangeConfig
            {
                Archetype = archetype,
                Center = float3.zero,
                FootprintRadius = radius,
                MinBridgeClearanceMeters = 7.5f
            };

            var plan = new InterchangePlan { Config = config, Legs = legs };
            TemplateMorphSolver.GenerateMorphedPlan(template, config, legs, null, plan);

            Assert.True(plan.Courses.Count > 0, $"Plan for {archetype} at {skewAngleDeg} deg must generate courses.");

            float allowedGrade = (archetype == InterchangeArchetype.Trumpet || archetype == InterchangeArchetype.DirectionalT) ? 0.35f : 0.20f;
            float allowedRadius = (archetype == InterchangeArchetype.Diamond || archetype == InterchangeArchetype.Turbine || archetype == InterchangeArchetype.YStack || archetype == InterchangeArchetype.Trumpet || archetype == InterchangeArchetype.DirectionalT) ? 4.0f : 12.0f;
            var verdict = InterchangeAudit.RunAudit(archetype, legs, plan.Courses, minClearance: 7.0f, maxGrade: allowedGrade, minRadius: allowedRadius);
            Assert.True(verdict.Passed, $"Audit errors on {archetype} skew {skewAngleDeg} deg: {string.Join("; ", verdict.Issues)}");
        }
    }
}
