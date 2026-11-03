using System.Collections.Generic;
using Colossal.Mathematics;
using ProceduralInterchanges.Core.Math;
using ProceduralInterchanges.Core.Models;
using ProceduralInterchanges.Tests.Fixtures;
using Unity.Mathematics;
using Xunit;

namespace ProceduralInterchanges.Tests.Clearance
{
    public class ClearanceAndPillarSolverTests
    {
        [Fact]
        public void SolveElevations_CrossingCourses_SeparatesUpperAndLowerByClearance()
        {
            var config = new InterchangeConfig
            {
                Center = new float3(0f, 0f, 0f),
                FootprintRadius = 150f,
                MinBridgeClearanceMeters = 8.0f,
                MainlineOverpass = true
            };

            // Lower course: East-West from (-50, 0, 0) to (50, 0, 0)
            var lowerCourse = new PlannedNetCourse
            {
                Id = "ground_mainline",
                Role = NetRole.MainlineThrough,
                StackLevel = 0,
                Curve = new Bezier4x3(
                    new float3(-60f, 0f, 0f),
                    new float3(-20f, 0f, 0f),
                    new float3(20f, 0f, 0f),
                    new float3(60f, 0f, 0f)),
                Lanes = 3,
                IsOneWay = true
            };

            // Upper course: North-South from (0, 0, -50) to (0, 0, 50) at StackLevel 1
            var upperCourse = new PlannedNetCourse
            {
                Id = "elevated_cross",
                Role = NetRole.CrossStreet,
                StackLevel = 1,
                Curve = new Bezier4x3(
                    new float3(0f, 0f, -60f),
                    new float3(0f, 0f, -20f),
                    new float3(0f, 0f, 20f),
                    new float3(0f, 0f, 60f)),
                Lanes = 2,
                IsOneWay = true
            };

            var courses = new List<PlannedNetCourse> { lowerCourse, upperCourse };

            ClearanceAndPillarSolver.SolveElevationsAndSubdivide(
                courses,
                SyntheticHighwaySites.TerrainSamplers.Flat,
                config);

            Assert.NotEmpty(courses);

            // Find the point where upper course crosses the origin (0, 0)
            float upperYAtOrigin = 0f;
            float lowerYAtOrigin = 0f;

            foreach (var c in courses)
            {
                if (c.Id.Contains("cross"))
                {
                    // Check near center (x ≈ 0, z ≈ 0)
                    for (int s = 0; s <= 10; s++)
                    {
                        float3 pos = CubicBezier3D.Position(c.Curve, s / 10f);
                        if (math.length(pos.xz) < 5f)
                        {
                            upperYAtOrigin = math.max(upperYAtOrigin, pos.y);
                        }
                    }
                }
                else if (c.Id.Contains("mainline"))
                {
                    for (int s = 0; s <= 10; s++)
                    {
                        float3 pos = CubicBezier3D.Position(c.Curve, s / 10f);
                        if (math.length(pos.xz) < 5f)
                        {
                            lowerYAtOrigin = pos.y;
                        }
                    }
                }
            }

            float verticalSeparation = upperYAtOrigin - lowerYAtOrigin;
            Assert.True(verticalSeparation >= 7.8f,
                $"Vertical clearance must meet 8m minimum, was {verticalSeparation:F2} m");
        }
    }
}
