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
    public class ParcloA4GeneratorTests
    {
        [Fact]
        public void ParcloA4_OrthogonalFlat_GeneratesValidCourses()
        {
            var gen = new ParcloA4Generator();
            var legs = SyntheticHighwaySites.CreateOrthogonal4Way(radius: 200f);
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

            var verdict = InterchangeAudit.RunAudit(InterchangeArchetype.ParcloA4, legs, courses);

            Assert.True(verdict.Metrics.TotalCourses >= 10, $"Expected >= 10 courses, got {verdict.Metrics.TotalCourses}");
            Assert.True(verdict.Metrics.TotalLengthMeters > 2000f, "Expected total length > 2000m");
        }

        [Fact]
        public void ParcloA4_DiagonalQuadrants_ContainLoopRamps()
        {
            var gen = new ParcloA4Generator();
            var legs = SyntheticHighwaySites.CreateOrthogonal4Way(radius: 200f);
            var config = new InterchangeConfig
            {
                Center = new float3(0f, 0f, 0f),
                FootprintRadius = 200f,
                MinBridgeClearanceMeters = 8.0f,
                MainlineOverpass = true
            };

            var courses = new List<PlannedNetCourse>();
            gen.GenerateGeometry(config, legs, courses);

            int loopCount = 0;
            foreach (var c in courses)
            {
                if (c.Role == NetRole.LoopRamp || c.Id.Contains("loop"))
                {
                    loopCount++;
                }
            }

            Assert.True(loopCount >= 2, $"Parclo A4 must have at least 2 loop ramps, found {loopCount}");
        }
    }
}
