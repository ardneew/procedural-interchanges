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
    public class AdversarialFootprintScalingTests
    {
        [Theory]
        [InlineData(InterchangeArchetype.Trumpet)]
        [InlineData(InterchangeArchetype.Diamond)]
        public void FootprintRadius_SliderScaling_MonotonicallyScalesGeometry(InterchangeArchetype archetype)
        {
            var template = WorkshopTemplateLibrary.GetTemplate(archetype);
            var legs = template.RequiredLegs == 3
                ? SyntheticHighwaySites.Create3WayJunction(angleDeg: 90f, radius: 350f)
                : SyntheticHighwaySites.CreateOrthogonal4Way(radius: 350f);

            float[] radiiToTest = new float[] { 140f, 200f, 280f };
            float[] measuredMaxR = new float[radiiToTest.Length];

            for (int rIdx = 0; rIdx < radiiToTest.Length; rIdx++)
            {
                float R = radiiToTest[rIdx];
                var config = new InterchangeConfig
                {
                    Center = new float3(0f, 0f, 0f),
                    FootprintRadius = R,
                    Archetype = archetype
                };

                var plan = new InterchangePlan { Config = config, Legs = legs };
                TemplateMorphSolver.GenerateMorphedPlan(template, config, legs, null, plan);

                // Find maximum radial distance of interior nodes (excluding the fixed portals at 350m)
                float maxInnerR = 0f;
                for (int i = 0; i < plan.Courses.Count; i++)
                {
                    var c = plan.Courses[i];
                    // Skip terminal boundary segments that connect out to 350m
                    if (c.StartAttachEntity != Unity.Entities.Entity.Null || c.EndAttachEntity != Unity.Entities.Entity.Null)
                        continue;

                    for (int s = 0; s <= 5; s++)
                    {
                        float3 pt = CubicBezier3D.Position(c.Curve, s / 5f);
                        float dist = math.length(pt.xz);
                        if (dist > maxInnerR) maxInnerR = dist;
                    }
                }

                measuredMaxR[rIdx] = maxInnerR;
            }

            // Assert monotonic scaling: footprint radius slider must visibly scale interior geometry
            Assert.True(measuredMaxR[1] > measuredMaxR[0],
                $"{archetype}: Increasing footprint radius from {radiiToTest[0]}m to {radiiToTest[1]}m did not scale geometry ({measuredMaxR[0]:F1}m vs {measuredMaxR[1]:F1}m).");
            Assert.True(measuredMaxR[2] > measuredMaxR[1],
                $"{archetype}: Increasing footprint radius from {radiiToTest[1]}m to {radiiToTest[2]}m did not scale geometry ({measuredMaxR[1]:F1}m vs {measuredMaxR[2]:F1}m).");
        }

        [Fact]
        public void ConstrainedSpacing_AutoClampsOrWarns()
        {
            // When highway legs are placed at 80m (too tight for highway radii)
            var legs = SyntheticHighwaySites.Create3WayJunction(angleDeg: 90f, radius: 80f);
            var config = new InterchangeConfig
            {
                Center = new float3(0f, 0f, 0f),
                FootprintRadius = 80f,
                Archetype = InterchangeArchetype.Trumpet
            };

            var template = WorkshopTemplateLibrary.GetTemplate(InterchangeArchetype.Trumpet);
            var plan = new InterchangePlan { Config = config, Legs = legs };
            TemplateMorphSolver.GenerateMorphedPlan(template, config, legs, null, plan);

            // Audit will detect turning radius violations or hairpin kinks when compressed to 80m
            var verdict = InterchangeAudit.RunAudit(InterchangeArchetype.Trumpet, legs, plan.Courses);
            Assert.False(verdict.Passed, "Audit should detect violations when interchange is placed in an impossibly small 80m footprint.");
        }
    }
}
