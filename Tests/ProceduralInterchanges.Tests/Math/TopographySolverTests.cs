using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using ProceduralInterchanges.Core.Math;
using ProceduralInterchanges.Tests.Fixtures;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace ProceduralInterchanges.Tests.Math
{
    public class TopographySolverTests
    {
        private readonly ITestOutputHelper _output;
        public TopographySolverTests(ITestOutputHelper output) { _output = output; }

        [Fact]
        public void FitGroundCourse_FlatTerrain_ProducesSingleUndividedSegment()
        {
            var curve = new Bezier4x3(
                new float3(0f, 0f, 0f),
                new float3(20f, 0f, 0f),
                new float3(40f, 0f, 0f),
                new float3(60f, 0f, 0f));

            var result = new List<Bezier4x3>();
            TopographySolver.FitGroundCourse(curve, SyntheticHighwaySites.TerrainSamplers.Flat, false, false, result);

            Assert.Single(result);
            Assert.Equal(0f, result[0].a.y, 4);
            Assert.Equal(0f, result[0].d.y, 4);
        }

        [Fact]
        public void FitGroundCourse_SteepHill_SubdividesAndFollowsTerrain()
        {
            // Segment across an incline from 0m to 12m
            var curve = new Bezier4x3(
                new float3(-40f, 0f, 0f),
                new float3(-13.3f, 0f, 0f),
                new float3(13.3f, 0f, 0f),
                new float3(40f, 0f, 0f));

            var hill = SyntheticHighwaySites.TerrainSamplers.GaussianHill(6f, new float2(0f, 0f), 25f);
            var result = new List<Bezier4x3>();

            TopographySolver.FitGroundCourse(curve, hill, false, false, result, maxDeviation: 0.10f, maxDepth: 4);

            _output.WriteLine($"Total pieces: {result.Count}");
            for (int i = 0; i < result.Count; i++)
            {
                var p = result[i];
                _output.WriteLine($"Piece {i}: A=({p.a.x:F1}, {p.a.y:F2}), D=({p.d.x:F1}, {p.d.y:F2}), MidY={CubicBezier3D.Position(p, 0.5f).y:F2}");
            }

            Assert.True(result.Count > 1, $"Expected multiple subdivisions for hill, got {result.Count}");

            float peakFound = 0f;
            foreach (var piece in result)
            {
                peakFound = math.max(peakFound, piece.a.y);
                peakFound = math.max(peakFound, piece.d.y);
                peakFound = math.max(peakFound, CubicBezier3D.Position(piece, 0.5f).y);
            }
            Assert.True(peakFound > 4.5f, $"Fitted courses should rise with the hill (found max {peakFound:F1} m)");
        }

        [Fact]
        public void FitGroundCourse_PinnedEnds_MaintainsExactSpecifiedPortalElevations()
        {
            float pinnedStartElevation = 12.5f;
            float pinnedEndElevation = 3.0f;

            var curve = new Bezier4x3(
                new float3(0f, pinnedStartElevation, 0f),
                new float3(20f, 10f, 0f),
                new float3(40f, 5f, 0f),
                new float3(60f, pinnedEndElevation, 0f));

            var result = new List<Bezier4x3>();
            TopographySolver.FitGroundCourse(curve, SyntheticHighwaySites.TerrainSamplers.Flat, true, true, result);

            Assert.NotEmpty(result);
            Assert.Equal(pinnedStartElevation, result[0].a.y, 3);
            Assert.Equal(pinnedEndElevation, result[result.Count - 1].d.y, 3);
        }
    }
}
