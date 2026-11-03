using Colossal.Mathematics;
using ProceduralInterchanges.Core.Math;
using Unity.Mathematics;
using Xunit;

namespace ProceduralInterchanges.Tests.Math
{
    public class CubicBezier3DTests
    {
        [Fact]
        public void Length_StraightSegment_MatchesEuclideanDistance()
        {
            var p0 = new float3(10f, 5f, 20f);
            var p3 = new float3(110f, 5f, 20f);
            var curve = new Bezier4x3(
                p0,
                math.lerp(p0, p3, 1f / 3f),
                math.lerp(p0, p3, 2f / 3f),
                p3);

            float len = CubicBezier3D.Length(curve, 32);
            Assert.Equal(100f, len, 2);
        }

        [Fact]
        public void Split_PreservesEndpointsAndMidpointContinuity()
        {
            var curve = new Bezier4x3(
                new float3(0f, 0f, 0f),
                new float3(30f, 10f, 10f),
                new float3(70f, 15f, -10f),
                new float3(100f, 0f, 0f));

            float splitT = 0.45f;
            CubicBezier3D.Split(curve, splitT, out var left, out var right);

            // Left starts at curve.a, right ends at curve.d
            Assert.Equal(curve.a, left.a);
            Assert.Equal(curve.d, right.d);

            // Join point must match exactly
            float3 splitPos = CubicBezier3D.Position(curve, splitT);
            Assert.True(math.distance(left.d, right.a) < 1e-4f, "Split boundary must coincide");
            Assert.True(math.distance(left.d, splitPos) < 1e-4f, "Split point must match original curve at splitT");

            // Tangents at split boundary must have identical direction (G1 continuity)
            float3 leftTan = math.normalize(CubicBezier3D.Tangent(left, 1.0f));
            float3 rightTan = math.normalize(CubicBezier3D.Tangent(right, 0.0f));
            Assert.True(math.dot(leftTan, rightTan) > 0.9999f, "Tangents must align at split boundary");
        }

        [Fact]
        public void CurvatureXZ_CircularArcApproximation_MatchesReciprocalOfRadius()
        {
            // Approximate a 90-degree circular arc of radius R = 50 centered at (0, 50)
            float R = 50f;
            float k = 4f / 3f * (math.SQRT2 - 1f) * R; // Standard cubic bezier circle approximation constant

            var arc = new Bezier4x3(
                new float3(R, 0f, 0f),
                new float3(R, 0f, k),
                new float3(k, 0f, R),
                new float3(0f, 0f, R));

            float midCurvature = CubicBezier3D.CurvatureXZ(arc, 0.5f);
            float expectedCurvature = 1f / R; // 0.02

            Assert.Equal(expectedCurvature, midCurvature, 3);
            Assert.True(math.abs(R - (1f / midCurvature)) < 0.6f, "Curvature radius within 0.6m of 50m");
        }

        [Fact]
        public void MaxSlopeGrade_SlopedCurve_CalculatesCorrectGrade()
        {
            // 100m horizontal run, 8m vertical rise -> 8% grade
            var curve = new Bezier4x3(
                new float3(0f, 0f, 0f),
                new float3(33.3f, 2.67f, 0f),
                new float3(66.7f, 5.33f, 0f),
                new float3(100f, 8f, 0f));

            float gradePercent = CubicBezier3D.MaxSlopeGrade(curve, 24);
            Assert.Equal(8.0f, gradePercent, 1);
        }

        [Fact]
        public void DistanceXZ_PointNearCurve_FindsClosestPoint()
        {
            // Straight curve along X-axis
            var curve = new Bezier4x3(
                new float3(0f, 0f, 0f),
                new float3(33.3f, 0f, 0f),
                new float3(66.7f, 0f, 0f),
                new float3(100f, 0f, 0f));

            // Point at (50, 15)
            float dist = CubicBezier3D.DistanceXZ(curve, new float2(50f, 15f), out float bestT);

            Assert.Equal(15f, dist, 1);
            Assert.Equal(0.5f, bestT, 2);
        }
    }
}
