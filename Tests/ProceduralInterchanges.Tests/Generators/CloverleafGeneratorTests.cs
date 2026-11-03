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
    public class CloverleafGeneratorTests
    {
        [Fact]
        public void Cloverleaf_OrthogonalFlat_GeneratesValidCourses()
        {
            var gen = new CloverleafGenerator();
            var legs = SyntheticHighwaySites.CreateOrthogonal4Way(radius: 220f);
            var config = new InterchangeConfig
            {
                Center = new float3(0f, 0f, 0f),
                FootprintRadius = 220f,
                MinBridgeClearanceMeters = 8.0f,
                MainlineOverpass = true
            };

            var courses = new List<PlannedNetCourse>();
            gen.GenerateGeometry(config, legs, courses);
            ClearanceAndPillarSolver.SolveElevationsAndSubdivide(courses, SyntheticHighwaySites.TerrainSamplers.Flat, config);

            var verdict = InterchangeAudit.RunAudit(InterchangeArchetype.Cloverleaf, legs, courses);

            Assert.True(verdict.Metrics.TotalCourses >= 16, $"Expected >= 16 courses, got {verdict.Metrics.TotalCourses}");
            Assert.True(verdict.Metrics.TotalLengthMeters > 3000f, "Expected total length > 3000m");
        }

        [Fact]
        public void Cloverleaf_ContainsFourLoopsAndFourOuterRamps()
        {
            var gen = new CloverleafGenerator();
            var legs = SyntheticHighwaySites.CreateOrthogonal4Way(radius: 220f);
            var config = new InterchangeConfig
            {
                Center = new float3(0f, 0f, 0f),
                FootprintRadius = 220f,
                MinBridgeClearanceMeters = 8.0f,
                MainlineOverpass = true
            };

            var courses = new List<PlannedNetCourse>();
            gen.GenerateGeometry(config, legs, courses);

            int loopCount = 0;
            int outerCount = 0;

            foreach (var c in courses)
            {
                if (c.Role == NetRole.LoopRamp || c.Id.Contains("loop")) loopCount++;
                if (c.Role == NetRole.DirectionalRamp || c.Id.Contains("outer")) outerCount++;
            }

            Assert.True(loopCount >= 4, $"Expected at least 4 loop segments, got {loopCount}");
            Assert.True(outerCount >= 4, $"Expected at least 4 outer ramp segments, got {outerCount}");
        }
    }
}
