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
    public class DiamondGeneratorTests
    {
        [Fact]
        public void Diamond_OrthogonalFlat_PassesInterchangeAudit()
        {
            var gen = new DiamondGenerator();
            var legs = SyntheticHighwaySites.CreateOrthogonal4Way(radius: 180f);
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

            var verdict = InterchangeAudit.RunAudit(InterchangeArchetype.Diamond, legs, courses);

            Assert.True(verdict.Passed, $"Diamond audit failed with {verdict.Issues.Count} issues: {string.Join("; ", verdict.Issues)}");
            Assert.True(verdict.Metrics.TotalCourses >= 8, $"Expected >= 8 courses, got {verdict.Metrics.TotalCourses}");
        }

        [Fact]
        public void Diamond_Skewed60Deg_GeneratesValidCourses()
        {
            var gen = new DiamondGenerator();
            var legs = SyntheticHighwaySites.CreateSkewed4Way(60f, radius: 200f);
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

            var verdict = InterchangeAudit.RunAudit(InterchangeArchetype.Diamond, legs, courses);

            Assert.True(verdict.Metrics.TotalCourses >= 8, $"Expected >= 8 courses, got {verdict.Metrics.TotalCourses}");
            Assert.True(verdict.Metrics.TotalLengthMeters > 1500f, "Expected total length > 1500m");
        }

        [Fact]
        public void Diamond_LeftHandTraffic_GeneratesValidCourses()
        {
            var gen = new DiamondGenerator();
            var legs = SyntheticHighwaySites.CreateOrthogonal4Way(radius: 180f);
            var lhtLegs = SyntheticHighwaySites.ConvertToLeftHandTraffic(legs);
            var config = new InterchangeConfig
            {
                Center = new float3(0f, 0f, 0f),
                FootprintRadius = 180f,
                MinBridgeClearanceMeters = 8.0f,
                MainlineOverpass = true,
                LeftHandTraffic = true
            };

            var courses = new List<PlannedNetCourse>();
            gen.GenerateGeometry(config, lhtLegs, courses);
            ClearanceAndPillarSolver.SolveElevationsAndSubdivide(courses, SyntheticHighwaySites.TerrainSamplers.Flat, config);

            var verdict = InterchangeAudit.RunAudit(InterchangeArchetype.Diamond, lhtLegs, courses);

            Assert.True(verdict.Metrics.TotalCourses >= 8, $"Expected >= 8 courses, got {verdict.Metrics.TotalCourses}");
            Assert.True(verdict.Metrics.TotalLengthMeters > 1500f, "Expected total length > 1500m");
        }
    }
}
