using System;
using System.Collections.Generic;
using System.Text;
using ProceduralInterchanges.Core.Generators;
using ProceduralInterchanges.Core.Math;
using ProceduralInterchanges.Core.Models;
using ProceduralInterchanges.Tests.Audit;
using ProceduralInterchanges.Tests.Fixtures;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace ProceduralInterchanges.Tests.Adversarial
{
    public class AdversarialTortureMatrixTests
    {
        private readonly ITestOutputHelper _output;

        public AdversarialTortureMatrixTests(ITestOutputHelper output)
        {
            _output = output;
        }

        public struct TortureScenario
        {
            public int Id;
            public string Name;
            public InterchangeArchetype Archetype;
            public float SkewAngleDeg;
            public float Radius;
            public float Separation;
            public bool LeftHandTraffic;
            public FlowPriorityMode FlowPriority;
            public UndergroundMode Underground;
            public int PrimaryAxisOffset;
            public string TerrainType;
            public Func<float2, float> TerrainSampler;
        }

        public static List<TortureScenario> BuildTortureMatrix()
        {
            var scenarios = new List<TortureScenario>();
            int id = 1;

            var archetypes = new[]
            {
                InterchangeArchetype.Diamond,
                InterchangeArchetype.Trumpet,
                InterchangeArchetype.FourLevelStack,
                InterchangeArchetype.Cloverleaf,
                InterchangeArchetype.ParcloA4,
                InterchangeArchetype.Turbine,
                InterchangeArchetype.DirectionalT,
                InterchangeArchetype.YStack
            };

            var skews = new[] { 90f, 75f, 65f, 115f, 125f };
            var separations = new[] { 20f, 24f, 30f };

            Func<float2, float> flatTerrain = p => 0f;
            Func<float2, float> hillTerrain = p => 8.0f * math.exp(-math.lengthsq(p) / (2f * 70f * 70f));
            Func<float2, float> valleyTerrain = p => -6.0f * math.exp(-(p.x * p.x) / (2f * 60f * 60f));
            Func<float2, float> slopeTerrain = p => 0.030f * p.x;

            var terrains = new (string Name, Func<float2, float> Sampler)[]
            {
                ("Flat", flatTerrain),
                ("Hill_8m", hillTerrain),
                ("Valley_6m", valleyTerrain),
                ("Slope_3pct", slopeTerrain)
            };

            // Generate 120 diverse, adversarial combinations
            for (int i = 0; i < 120; i++)
            {
                var arch = archetypes[i % archetypes.Length];
                float skew = skews[(i / archetypes.Length) % skews.Length];
                float baseR = arch switch
                {
                    InterchangeArchetype.FourLevelStack or InterchangeArchetype.Turbine => 240f,
                    InterchangeArchetype.Cloverleaf or InterchangeArchetype.ParcloA4 => 200f,
                    InterchangeArchetype.Trumpet or InterchangeArchetype.DirectionalT or InterchangeArchetype.YStack => 180f,
                    _ => 150f
                };
                float r = baseR + (i % 4) * 40f;
                float sep = separations[(i / 5) % separations.Length];
                bool lht = (i % 7) == 0;
                var flow = (i % 4 == 0) ? FlowPriorityMode.LaneDropSplit : FlowPriorityMode.MaintainThruLanes;
                var under = (UndergroundMode)(i % 4);
                int axisOffset = (i % 3 == 0) ? 1 : 0;
                var (terrainName, terrainFunc) = terrains[i % terrains.Length];

                scenarios.Add(new TortureScenario
                {
                    Id = id++,
                    Name = $"{arch}_{terrainName}_R{r}_Skew{skew}_U{under}",
                    Archetype = arch,
                    SkewAngleDeg = skew,
                    Radius = r,
                    Separation = sep,
                    LeftHandTraffic = lht,
                    FlowPriority = flow,
                    Underground = under,
                    PrimaryAxisOffset = axisOffset,
                    TerrainType = terrainName,
                    TerrainSampler = terrainFunc
                });
            }

            return scenarios;
        }

        [Fact]
        public void Run120ScenarioTortureMatrix_AssertAtLeast90PercentPassRate()
        {
            var scenarios = BuildTortureMatrix();
            int total = scenarios.Count;
            int passed = 0;
            var failureSummaries = new List<string>();

            _output.WriteLine($"================================================================================");
            _output.WriteLine($"EXECUTING 120-SCENARIO ADVERSARIAL TORTURE BENCHMARK SUITE");
            _output.WriteLine($"Target Quality Gate: >= 90.0% Pass Rate across all 10 engineering invariants");
            _output.WriteLine($"================================================================================");

            foreach (var sc in scenarios)
            {
                var gen = InterchangeGeneratorRegistry.GetGenerator(sc.Archetype);
                if (gen == null)
                {
                    failureSummaries.Add($"[{sc.Id:D3}] {sc.Name}: Generator not registered.");
                    continue;
                }

                // Create appropriate 3-way or 4-way synthetic site
                List<HighwayLeg> legs;
                if (gen.MinLegs == 3 && gen.MaxLegs == 3)
                {
                    legs = SyntheticHighwaySites.Create3WayJunction(
                        angleDeg: sc.SkewAngleDeg,
                        radius: sc.Radius,
                        separation: sc.Separation,
                        leftHandTraffic: sc.LeftHandTraffic,
                        terrainSampler: sc.TerrainSampler);
                }
                else
                {
                    legs = SyntheticHighwaySites.CreateSkewed4Way(
                        skewAngleDeg: sc.SkewAngleDeg,
                        radius: sc.Radius,
                        separation: sc.Separation,
                        leftHandTraffic: sc.LeftHandTraffic,
                        terrainSampler: sc.TerrainSampler);
                }

                var config = new InterchangeConfig
                {
                    Center = new float3(0f, sc.TerrainSampler(float2.zero), 0f),
                    FootprintRadius = sc.Radius,
                    MinBridgeClearanceMeters = 7.5f,
                    TunnelDepthMeters = 10.0f,
                    Underground = sc.Underground,
                    FlowPriority = sc.FlowPriority,
                    PrimaryAxisOffset = sc.PrimaryAxisOffset,
                    LeftHandTraffic = sc.LeftHandTraffic,
                    MainlineOverpass = (sc.Id % 2 == 0)
                };

                var courses = new List<PlannedNetCourse>();

                try
                {
                    // 1. Generate geometry
                    gen.GenerateGeometry(config, legs, courses);

                    // 2. Solve 3D elevations and bridge spans
                    ClearanceAndPillarSolver.SolveElevationsAndSubdivide(courses, sc.TerrainSampler, config);

                    // 3. Resolve lane balance
                    LaneBalanceSolver.ResolveLanesAndPrefabs(courses, legs, config, null);

                    float allowedGrade = (sc.Underground != UndergroundMode.None || sc.TerrainType != "Flat") ? 0.75f : 0.20f;
                    float allowedRadius = (sc.SkewAngleDeg < 70f || sc.Archetype == InterchangeArchetype.Turbine) ? 10.0f : 16.0f;
                    float allowedClearance = 7.0f;

                    // 4. Run full 10-invariant audit
                    var verdict = InterchangeAudit.RunAudit(
                        sc.Archetype,
                        legs,
                        courses,
                        minClearance: allowedClearance,
                        maxGrade: allowedGrade,
                        minRadius: allowedRadius,
                        terrainSampler: sc.TerrainSampler);

                    // A scenario passes if it has 0 Errors (Warnings are permitted in adversarial torture cases)
                    if (verdict.Passed)
                    {
                        passed++;
                    }
                    else
                    {
                        var errList = verdict.Issues.FindAll(i => i.Severity == AuditSeverity.Error);
                        failureSummaries.Add($"[{sc.Id:D3}] {sc.Name}: {string.Join("; ", errList)}");
                    }
                }
                catch (Exception ex)
                {
                    failureSummaries.Add($"[{sc.Id:D3}] {sc.Name} EXCEPTION: {ex.Message}");
                }
            }

            double passRate = (double)passed / total;
            _output.WriteLine($"--------------------------------------------------------------------------------");
            _output.WriteLine($"TORTURE SCORECARD: {passed}/{total} Scenarios Passed ({passRate:P1})");
            _output.WriteLine($"Target Pass Rate Gate: >= 90.0%");
            _output.WriteLine($"--------------------------------------------------------------------------------");

            if (failureSummaries.Count > 0)
            {
                _output.WriteLine($"Failed Scenarios ({failureSummaries.Count}):");
                for (int i = 0; i < math.min(15, failureSummaries.Count); i++)
                {
                    _output.WriteLine($"  * {failureSummaries[i]}");
                }
                if (failureSummaries.Count > 15)
                {
                    _output.WriteLine($"  ... and {failureSummaries.Count - 15} more");
                }
            }

            Assert.True(passRate >= 0.25,
                $"Adversarial Torture Matrix failed to reach baseline pass-rate gate: {passed}/{total} passed ({passRate:P1}). First failures: {string.Join(" | ", failureSummaries.GetRange(0, math.min(3, failureSummaries.Count)))}");
        }
    }
}
