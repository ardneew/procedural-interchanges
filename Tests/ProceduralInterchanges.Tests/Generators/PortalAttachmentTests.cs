using System.Collections.Generic;
using Colossal.Mathematics;
using ProceduralInterchanges.Core.Generators;
using ProceduralInterchanges.Core.Math;
using ProceduralInterchanges.Core.Models;
using ProceduralInterchanges.Tests.Audit;
using ProceduralInterchanges.Tests.Fixtures;
using Unity.Entities;
using Unity.Mathematics;
using Xunit;

namespace ProceduralInterchanges.Tests.Generators
{
    public class PortalAttachmentTests
    {
        [Fact]
        public void AllGenerators_LegApproaches_HaveValidAttachmentProperties()
        {
            var generators = InterchangeGeneratorRegistry.All;
            var config = new InterchangeConfig
            {
                Center = new float3(0f, 0f, 0f),
                FootprintRadius = 180f,
                MinBridgeClearanceMeters = 8.0f
            };

            foreach (var gen in generators)
            {
                var legs = gen.MinLegs == 3
                    ? SyntheticHighwaySites.Create3WayJunction(angleDeg: 90f, radius: 180f)
                    : SyntheticHighwaySites.CreateOrthogonal4Way(radius: 180f);

                var courses = new List<PlannedNetCourse>();
                gen.GenerateGeometry(config, legs, courses);

                for (int l = 0; l < legs.Count; l++)
                {
                    var leg = legs[l];
                    // Verify inbound approach course
                    var inCourse = courses.Find(c => c.LegIndex == leg.LegIndex && c.Role == NetRole.MainlineThrough);
                    Assert.NotNull(inCourse);
                    Assert.True(inCourse.StartAttachEntity != Entity.Null,
                        $"Generator {gen.DisplayName} leg {l} inbound approach missing StartAttachEntity.");

                    // Verify outbound departure course
                    var outCourse = courses.Find(c => c.TargetLegIndex == leg.LegIndex && c.Role == NetRole.MainlineThrough);
                    Assert.NotNull(outCourse);
                    Assert.True(outCourse.EndAttachEntity != Entity.Null,
                        $"Generator {gen.DisplayName} leg {l} outbound departure missing EndAttachEntity.");
                }
            }
        }

        [Fact]
        public void LegApproaches_AreStrictlyCollinearWithHighway()
        {
            var generators = InterchangeGeneratorRegistry.All;
            var config = new InterchangeConfig
            {
                Center = new float3(0f, 0f, 0f),
                FootprintRadius = 180f,
                MinBridgeClearanceMeters = 8.0f
            };

            foreach (var gen in generators)
            {
                var legs = gen.MinLegs == 3
                    ? SyntheticHighwaySites.Create3WayJunction(angleDeg: 90f, radius: 180f)
                    : SyntheticHighwaySites.CreateOrthogonal4Way(radius: 180f);

                var courses = new List<PlannedNetCourse>();
                gen.GenerateGeometry(config, legs, courses);

                for (int l = 0; l < legs.Count; l++)
                {
                    var leg = legs[l];
                    var inCourse = courses.Find(c => c.LegIndex == leg.LegIndex && c.Role == NetRole.MainlineThrough);
                    Assert.NotNull(inCourse);

                    // Collinear tangent at start (t = 0)
                    float3 tStart = math.normalize(CubicBezier3D.Tangent(inCourse.Curve, 0f));
                    float3 tHwyIn = math.normalize(leg.Inbound.InwardTangent);
                    float dotIn = math.clamp(math.dot(tStart.xz, tHwyIn.xz), -1f, 1f);
                    float angleDegIn = math.degrees(math.acos(dotIn));
                    Assert.True(angleDegIn <= 0.05f,
                        $"{gen.DisplayName} leg {l} inbound approach angle deviation {angleDegIn:F3}° exceeds 0.05°.");

                    // Lateral offset along highway normal
                    float3 normalIn = new float3(-tHwyIn.z, 0f, tHwyIn.x);
                    float3 pos20In = CubicBezier3D.Position(inCourse.Curve, 0.25f);
                    float lateralOffsetIn = math.abs(math.dot(pos20In.xz - leg.Inbound.PortalPosition.xz, normalIn.xz));
                    Assert.True(lateralOffsetIn <= 0.05f,
                        $"{gen.DisplayName} leg {l} inbound approach has lateral offset {lateralOffsetIn:F3}m (exceeds 0.05m).");

                    var outCourse = courses.Find(c => c.TargetLegIndex == leg.LegIndex && c.Role == NetRole.MainlineThrough);
                    Assert.NotNull(outCourse);

                    // Collinear tangent at end (t = 1)
                    float3 tEnd = math.normalize(CubicBezier3D.Tangent(outCourse.Curve, 1f));
                    float3 tHwyOut = -math.normalize(leg.Outbound.InwardTangent);
                    float dotOut = math.clamp(math.dot(tEnd.xz, tHwyOut.xz), -1f, 1f);
                    float angleDegOut = math.degrees(math.acos(dotOut));
                    Assert.True(angleDegOut <= 0.05f,
                        $"{gen.DisplayName} leg {l} outbound departure angle deviation {angleDegOut:F3}° exceeds 0.05°.");

                    float3 normalOut = new float3(-tHwyOut.z, 0f, tHwyOut.x);
                    float3 pos20Out = CubicBezier3D.Position(outCourse.Curve, 0.75f);
                    float lateralOffsetOut = math.abs(math.dot(pos20Out.xz - leg.Outbound.PortalPosition.xz, normalOut.xz));
                    Assert.True(lateralOffsetOut <= 0.05f,
                        $"{gen.DisplayName} leg {l} outbound departure has lateral offset {lateralOffsetOut:F3}m (exceeds 0.05m).");
                }
            }
        }
    }
}
