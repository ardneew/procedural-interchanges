using System;
using System.Collections.Generic;
using System.Diagnostics;
using ProceduralInterchanges.Core.Generators;
using ProceduralInterchanges.Core.Math;
using ProceduralInterchanges.Core.Models;
using ProceduralInterchanges.Lab.Rendering;
using ProceduralInterchanges.Tests.Audit;
using ProceduralInterchanges.Tests.Fixtures;
using Unity.Mathematics;

namespace ProceduralInterchanges.Lab.Cases
{
    public static class LabCases
    {
        public static List<GalleryCard> RunAllCases()
        {
            var cards = new List<GalleryCard>();

            // 1. Diamond Interchange Cases
            cards.Add(RunCase("Diamond", "Flat 90°", new DiamondGenerator(), InterchangeArchetype.Diamond,
                SyntheticHighwaySites.CreateOrthogonal4Way(180f),
                SyntheticHighwaySites.TerrainSamplers.Flat, 180f));

            cards.Add(RunCase("Diamond", "Skewed 60°", new DiamondGenerator(), InterchangeArchetype.Diamond,
                SyntheticHighwaySites.CreateSkewed4Way(60f, 200f),
                SyntheticHighwaySites.TerrainSamplers.Flat, 200f));

            cards.Add(RunCase("Diamond", "Gentle Hill (8m)", new DiamondGenerator(), InterchangeArchetype.Diamond,
                SyntheticHighwaySites.CreateOrthogonal4Way(190f),
                SyntheticHighwaySites.TerrainSamplers.GaussianHill(8f, new float2(0f, 0f), 35f), 190f));

            // 2. Parclo A4 Cases
            cards.Add(RunCase("Parclo A4", "Flat 90°", new ParcloA4Generator(), InterchangeArchetype.ParcloA4,
                SyntheticHighwaySites.CreateOrthogonal4Way(200f),
                SyntheticHighwaySites.TerrainSamplers.Flat, 200f));

            cards.Add(RunCase("Parclo A4", "Valley Trough", new ParcloA4Generator(), InterchangeArchetype.ParcloA4,
                SyntheticHighwaySites.CreateOrthogonal4Way(210f),
                SyntheticHighwaySites.TerrainSamplers.ValleyCross(6f, 100f), 210f));

            // 3. Cloverleaf Cases
            cards.Add(RunCase("Cloverleaf", "Flat 90°", new CloverleafGenerator(), InterchangeArchetype.Cloverleaf,
                SyntheticHighwaySites.CreateOrthogonal4Way(220f),
                SyntheticHighwaySites.TerrainSamplers.Flat, 220f));

            cards.Add(RunCase("Cloverleaf", "Skewed 75°", new CloverleafGenerator(), InterchangeArchetype.Cloverleaf,
                SyntheticHighwaySites.CreateSkewed4Way(75f, 230f),
                SyntheticHighwaySites.TerrainSamplers.Flat, 230f));

            // 4. Trumpet Cases
            cards.Add(RunCase("Trumpet", "Flat 90° T", new TrumpetGenerator(), InterchangeArchetype.Trumpet,
                SyntheticHighwaySites.Create3WayJunction(90f, 180f),
                SyntheticHighwaySites.TerrainSamplers.Flat, 180f));

            cards.Add(RunCase("Trumpet", "Skewed 120° Y", new TrumpetGenerator(), InterchangeArchetype.Trumpet,
                SyntheticHighwaySites.Create3WayJunction(120f, 200f),
                SyntheticHighwaySites.TerrainSamplers.Flat, 200f));

            return cards;
        }

        private static GalleryCard RunCase(
            string archetypeName,
            string variantName,
            IInterchangeGenerator generator,
            InterchangeArchetype archetype,
            List<HighwayLeg> legs,
            Func<float2, float> terrainSampler,
            float radius)
        {
            var sw = Stopwatch.StartNew();

            var config = new InterchangeConfig
            {
                Center = new float3(0f, 0f, 0f),
                FootprintRadius = radius,
                MinBridgeClearanceMeters = 8.0f,
                MainlineOverpass = true
            };

            var courses = new List<PlannedNetCourse>();
            generator.GenerateGeometry(config, legs, courses);
            ClearanceAndPillarSolver.SolveElevationsAndSubdivide(courses, terrainSampler, config);

            var verdict = InterchangeAudit.RunAudit(archetype, legs, courses);
            sw.Stop();

            string svg = SvgRenderer.RenderInterchangeSvg(courses, legs, verdict, radius);

            return new GalleryCard
            {
                Archetype = archetypeName,
                Variant = variantName,
                SvgContent = svg,
                Verdict = verdict,
                ExecutionTimeMs = sw.Elapsed.TotalMilliseconds
            };
        }
    }
}
