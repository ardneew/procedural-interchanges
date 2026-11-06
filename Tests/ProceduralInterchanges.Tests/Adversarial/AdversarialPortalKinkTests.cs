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
    public class AdversarialPortalKinkTests
    {
        [Fact]
        public void Trumpet_ThreeWay_TightSpacing_DetectsHairpinKink()
        {
            // Reproduces user photo scenario: 3-way highway junction with constrained radius (120m).
            var legs = SyntheticHighwaySites.CreateConstrained3WayJunction(radius: 120f, angleDeg: 90f);
            var config = new InterchangeConfig
            {
                Center = new float3(0f, 0f, 0f),
                FootprintRadius = 120f,
                MinBridgeClearanceMeters = 8.0f,
                Archetype = InterchangeArchetype.Trumpet
            };

            var template = WorkshopTemplateLibrary.GetTemplate(InterchangeArchetype.Trumpet);
            var plan = new InterchangePlan { Config = config, Legs = legs };
            TemplateMorphSolver.GenerateMorphedPlan(template, config, legs, null, plan);

            var verdict = InterchangeAudit.RunAudit(InterchangeArchetype.Trumpet, legs, plan.Courses);

            // Audit MUST verify monotonic progress and portal overshoot (zero reverse hooks or overshoots)
            var fatalIssues = verdict.Issues.FindAll(i => i.Category == AuditCategory.MonotonicProgress || i.Category == AuditCategory.PortalOvershoot);
            Assert.True(fatalIssues.Count == 0, $"Monotonic progress or portal overshoot detected: {string.Join("; ", fatalIssues)}");
        }

        [Fact]
        public void Trumpet_ThreeWay_SingleTwoWayHighway_TightSpacing_NoHairpinKink()
        {
            // Reproduces exact user in-game setup: 3 single two-way highways at R=130m.
            var legs = SyntheticHighwaySites.CreateSingleTwoWay3WayJunction(radius: 130f, angleDeg: 90f);
            var config = new InterchangeConfig
            {
                Center = new float3(0f, 0f, 0f),
                FootprintRadius = 130f,
                MinBridgeClearanceMeters = 8.0f,
                Archetype = InterchangeArchetype.Trumpet
            };

            var template = WorkshopTemplateLibrary.GetTemplate(InterchangeArchetype.Trumpet);
            var plan = new InterchangePlan { Config = config, Legs = legs };
            TemplateMorphSolver.GenerateMorphedPlan(template, config, legs, null, plan);

            var verdict = InterchangeAudit.RunAudit(InterchangeArchetype.Trumpet, legs, plan.Courses);

            var fatalIssues = verdict.Issues.FindAll(i => i.Category == AuditCategory.MonotonicProgress || i.Category == AuditCategory.PortalOvershoot);
            Assert.True(fatalIssues.Count == 0, $"Single two-way Trumpet violated monotonic progress or overshoot: {string.Join("; ", fatalIssues)}");
        }

        [Fact]
        public void Trumpet_StemAsymmetry_DetectsLateralWarpAndKink()
        {
            // Reproduces user photo scenario: stem highway skewed at 105 degrees with asymmetric leg distances.
            var legs = SyntheticHighwaySites.CreateAsymmetricRadii3Way(rA: 140f, rB: 180f, rStem: 110f, stemAngleDeg: 105f);
            var config = new InterchangeConfig
            {
                Center = new float3(0f, 0f, 0f),
                FootprintRadius = 150f,
                MinBridgeClearanceMeters = 8.0f,
                Archetype = InterchangeArchetype.Trumpet
            };

            var template = WorkshopTemplateLibrary.GetTemplate(InterchangeArchetype.Trumpet);
            var plan = new InterchangePlan { Config = config, Legs = legs };
            TemplateMorphSolver.GenerateMorphedPlan(template, config, legs, null, plan);

            var verdict = InterchangeAudit.RunAudit(InterchangeArchetype.Trumpet, legs, plan.Courses);

            var fatalIssues = verdict.Issues.FindAll(i => i.Category == AuditCategory.MonotonicProgress || i.Category == AuditCategory.PortalOvershoot);
            Assert.True(fatalIssues.Count == 0, $"Monotonic progress or portal overshoot detected on asymmetric Trumpet: {string.Join("; ", fatalIssues)}");
        }

        [Theory]
        [InlineData(InterchangeArchetype.Trumpet, 120f)]
        [InlineData(InterchangeArchetype.Diamond, 140f)]
        [InlineData(InterchangeArchetype.Cloverleaf, 160f)]
        public void AllTemplates_MorphedPlan_MustMaintainMonotonicProgress(InterchangeArchetype archetype, float radius)
        {
            var template = WorkshopTemplateLibrary.GetTemplate(archetype);
            var legs = template.RequiredLegs == 3
                ? SyntheticHighwaySites.CreateConstrained3WayJunction(radius: radius, angleDeg: 90f)
                : SyntheticHighwaySites.CreateOrthogonal4Way(radius: radius);

            var config = new InterchangeConfig
            {
                Center = new float3(0f, 0f, 0f),
                FootprintRadius = radius,
                MinBridgeClearanceMeters = 8.0f,
                Archetype = archetype
            };

            var plan = new InterchangePlan { Config = config, Legs = legs };
            TemplateMorphSolver.GenerateMorphedPlan(template, config, legs, null, plan);

            var verdict = InterchangeAudit.RunAudit(archetype, legs, plan.Courses);

            var fatalIssues = verdict.Issues.FindAll(i => i.Category == AuditCategory.MonotonicProgress || i.Category == AuditCategory.PortalOvershoot);
            Assert.True(fatalIssues.Count == 0, $"Archetype {archetype} at R={radius}m violated monotonic progress or overshoot: {string.Join("; ", fatalIssues)}");
        }
    }
}
