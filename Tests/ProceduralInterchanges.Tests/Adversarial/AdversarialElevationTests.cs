using System;
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
    public class AdversarialElevationTests
    {
        [Fact]
        public void SlopedAndUndulatingTerrain_NoGroundClipping()
        {
            // Undulating synthetic terrain sampler
            Func<float2, float> wavyTerrain = p => 4.0f * math.sin(p.x * 0.02f) + 2.0f * math.cos(p.y * 0.03f);

            var legs = SyntheticHighwaySites.Create3WayJunction(angleDeg: 90f, radius: 180f);
            var config = new InterchangeConfig
            {
                Center = new float3(0f, wavyTerrain(float2.zero), 0f),
                FootprintRadius = 180f,
                Archetype = InterchangeArchetype.Trumpet
            };

            var template = WorkshopTemplateLibrary.GetTemplate(InterchangeArchetype.Trumpet);
            var plan = new InterchangePlan { Config = config, Legs = legs };
            TemplateMorphSolver.GenerateMorphedPlan(template, config, legs, null, plan);

            ClearanceAndPillarSolver.SolveElevationsAndSubdivide(plan.Courses, wavyTerrain, config);

            var verdict = InterchangeAudit.RunAudit(InterchangeArchetype.Trumpet, legs, plan.Courses, terrainSampler: wavyTerrain);

            var subIssues = verdict.Issues.FindAll(i => i.Category == AuditCategory.SubterraneanRoad);
            Assert.True(subIssues.Count == 0,
                $"Road courses plunged underground on undulating terrain: {string.Join("; ", subIssues)}");
        }
    }
}
