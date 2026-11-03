using System.Collections.Generic;
using ProceduralInterchanges.Core.Generators;
using ProceduralInterchanges.Core.Math;
using ProceduralInterchanges.Core.Models;
using ProceduralInterchanges.Tests.Audit;
using ProceduralInterchanges.Tests.Fixtures;
using Unity.Mathematics;
using Xunit;

namespace ProceduralInterchanges.Tests.Generators
{
    public class TrumpetGeneratorTests
    {
        [Fact]
        public void Trumpet_3WayFlat_PassesInterchangeAudit()
        {
            var gen = new TrumpetGenerator();
            var legs = SyntheticHighwaySites.Create3WayJunction(angleDeg: 90f, radius: 180f);
            var config = new InterchangeConfig
            {
                Center = new float3(0f, 0f, 0f),
                FootprintRadius = 180f,
                MinBridgeClearanceMeters = 8.0f,
                MainlineOverpass = true
            };

            var courses = new List<PlannedNetCourse>();
            gen.GenerateGeometry(config, legs, courses);
            ClearanceAndPillarSolver.SolveElevationsAndSubdivide(courses, SyntheticHighwaySites.TerrainSamplers.Flat, config);

            var verdict = InterchangeAudit.RunAudit(InterchangeArchetype.Trumpet, legs, courses);

            Assert.True(verdict.Passed, $"Trumpet audit failed: {string.Join("; ", verdict.Issues)}");
            Assert.True(verdict.Metrics.TotalCourses >= 6, $"Expected >= 6 courses, got {verdict.Metrics.TotalCourses}");
        }

        [Fact]
        public void Trumpet_SkewedStem_GeneratesValidCourses()
        {
            var gen = new TrumpetGenerator();
            var legs = SyntheticHighwaySites.Create3WayJunction(angleDeg: 120f, radius: 200f);
            var config = new InterchangeConfig
            {
                Center = new float3(0f, 0f, 0f),
                FootprintRadius = 200f,
                MinBridgeClearanceMeters = 8.0f,
                MainlineOverpass = true
            };

            var courses = new List<PlannedNetCourse>();
            gen.GenerateGeometry(config, legs, courses);
            ClearanceAndPillarSolver.SolveElevationsAndSubdivide(courses, SyntheticHighwaySites.TerrainSamplers.Flat, config);

            var verdict = InterchangeAudit.RunAudit(InterchangeArchetype.Trumpet, legs, courses);

            Assert.True(verdict.Metrics.TotalCourses >= 6, $"Expected >= 6 courses, got {verdict.Metrics.TotalCourses}");
            Assert.True(verdict.Metrics.TotalLengthMeters > 1500f, "Expected total length > 1500m");
        }
    }
}
