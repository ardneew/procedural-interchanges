using System.Collections.Generic;
using ProceduralInterchanges.Core.Generators;
using ProceduralInterchanges.Core.Math;
using ProceduralInterchanges.Core.Models;
using ProceduralInterchanges.Core.Templates;
using ProceduralInterchanges.Tests.Audit;
using ProceduralInterchanges.Tests.Fixtures;
using Unity.Mathematics;
using Xunit;

namespace ProceduralInterchanges.Tests.Adversarial
{
    public class AdversarialCurvatureAndSmoothnessTests
    {
        [Theory]
        [InlineData(InterchangeArchetype.Trumpet)]
        [InlineData(InterchangeArchetype.Diamond)]
        public void ThroughCorridors_HaveZeroInflectionS_Bends(InterchangeArchetype archetype)
        {
            var template = WorkshopTemplateLibrary.GetTemplate(archetype);
            var legs = template.RequiredLegs == 3
                ? SyntheticHighwaySites.Create3WayJunction(angleDeg: 90f, radius: 180f)
                : SyntheticHighwaySites.CreateOrthogonal4Way(radius: 180f);

            var config = new InterchangeConfig
            {
                Center = new float3(0f, 0f, 0f),
                FootprintRadius = 180f,
                Archetype = archetype
            };

            var plan = new InterchangePlan { Config = config, Legs = legs };
            TemplateMorphSolver.GenerateMorphedPlan(template, config, legs, null, plan);

            var verdict = InterchangeAudit.RunAudit(archetype, legs, plan.Courses);

            var curvatureIssues = verdict.Issues.FindAll(i => i.Category == AuditCategory.ExtraneousCurvature);
            Assert.True(curvatureIssues.Count == 0,
                $"Unwanted S-bends or inflection oscillations detected in {archetype}: {string.Join("; ", curvatureIssues)}");
        }

        [Fact]
        public void TerminalRunout_MaintainsCollinearLeadIn()
        {
            var legs = SyntheticHighwaySites.Create3WayJunction(angleDeg: 90f, radius: 180f);
            var config = new InterchangeConfig
            {
                Center = new float3(0f, 0f, 0f),
                FootprintRadius = 180f,
                Archetype = InterchangeArchetype.Trumpet
            };

            var template = WorkshopTemplateLibrary.GetTemplate(InterchangeArchetype.Trumpet);
            var plan = new InterchangePlan { Config = config, Legs = legs };
            TemplateMorphSolver.GenerateMorphedPlan(template, config, legs, null, plan);

            for (int l = 0; l < legs.Count; l++)
            {
                var leg = legs[l];
                // Check inbound terminal lead-in
                var inCourse = plan.Courses.Find(c => math.distance(c.Curve.a.xz, leg.Inbound.PortalPosition.xz) < 1.0f);
                Assert.NotNull(inCourse);

                float3 tHwyIn = math.normalize(leg.Inbound.InwardTangent);
                float3 normalIn = new float3(-tHwyIn.z, 0f, tHwyIn.x);

                // Sample position 20m along course
                float3 p20 = CubicBezier3D.Position(inCourse.Curve, 0.2f);
                float lateralOffset = math.abs(math.dot(p20.xz - leg.Inbound.PortalPosition.xz, normalIn.xz));

                Assert.True(lateralOffset < 1.0f,
                    $"Terminal for leg {leg.LegIndex} has lateral offset {lateralOffset:F2}m at 20m from portal (must remain strictly co-axial with highway).");
            }
        }
    }
}
