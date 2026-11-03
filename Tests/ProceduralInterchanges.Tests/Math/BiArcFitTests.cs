using System.Collections.Generic;
using Colossal.Mathematics;
using ProceduralInterchanges.Core.Math;
using Unity.Mathematics;
using Xunit;

namespace ProceduralInterchanges.Tests.Math
{
    public class BiArcFitTests
    {
        [Fact]
        public void FitSmoothCurve_CollinearPoses_ProducesStraightSegment()
        {
            var p0 = new float3(0f, 0f, 0f);
            var d0 = new float3(1f, 0f, 0f);
            var p1 = new float3(100f, 0f, 0f);
            var d1 = new float3(1f, 0f, 0f);

            var curve = BiArcFit.FitSmoothCurve(p0, d0, p1, d1);

            Assert.Equal(p0, curve.a);
            Assert.Equal(p1, curve.d);
            Assert.Equal(0f, curve.b.z, 4);
            Assert.Equal(0f, curve.c.z, 4);

            float3 tanMid = math.normalize(CubicBezier3D.Tangent(curve, 0.5f));
            Assert.Equal(1f, tanMid.x, 3);
            Assert.Equal(0f, tanMid.z, 3);
        }

        [Fact]
        public void FitSmoothCurve_OrthogonalTurn_ProducesG1ContinuityAtPoses()
        {
            var p0 = new float3(0f, 0f, 0f);
            var d0 = new float3(1f, 0f, 0f); // East
            var p1 = new float3(50f, 0f, 50f);
            var d1 = new float3(0f, 0f, 1f); // North

            var curve = BiArcFit.FitSmoothCurve(p0, d0, p1, d1);

            float3 tan0 = math.normalize(CubicBezier3D.Tangent(curve, 0.0f));
            float3 tan1 = math.normalize(CubicBezier3D.Tangent(curve, 1.0f));

            Assert.True(math.dot(tan0, d0) > 0.999f, "Start tangent must match pose direction");
            Assert.True(math.dot(tan1, d1) > 0.999f, "End tangent must match pose direction");
        }

        [Fact]
        public void FitCircularArc_90DegreeSweep_ProducesExactEndpointsAndTangents()
        {
            var center = new float3(0f, 0f, 0f);
            float radius = 40f;
            float startAngle = 0f;
            float sweepAngle = math.PI * 0.5f; // 90 deg CCW

            var arc = BiArcFit.FitCircularArc(center, radius, startAngle, sweepAngle, 0f, 5f);

            // Start should be at (40, 0, 0)
            Assert.Equal(40f, arc.a.x, 2);
            Assert.Equal(0f, arc.a.z, 2);
            Assert.Equal(0f, arc.a.y, 2);

            // End should be at (0, 5, 40)
            Assert.Equal(0f, arc.d.x, 2);
            Assert.Equal(40f, arc.d.z, 2);
            Assert.Equal(5f, arc.d.y, 2);

            // Midpoint radius check
            float3 mid = CubicBezier3D.Position(arc, 0.5f);
            float midRadius = math.length(mid.xz);
            Assert.Equal(radius, midRadius, 1);
        }

        [Fact]
        public void FitLoop270_GeneratesThreeContinuousArcs()
        {
            var loopCenter = new float3(100f, 0f, 100f);
            float radius = 35f;
            float entryAngle = 0f;
            var arcs = new List<Bezier4x3>();

            BiArcFit.FitLoop270(loopCenter, radius, entryAngle, 1, 0f, 8.5f, 0f, arcs);

            Assert.Equal(3, arcs.Count);

            // Verify continuous joins between the 3 arcs
            Assert.True(math.distance(arcs[0].d, arcs[1].a) < 1e-4f, "Arc 0 and Arc 1 must connect bit-identically");
            Assert.True(math.distance(arcs[1].d, arcs[2].a) < 1e-4f, "Arc 1 and Arc 2 must connect bit-identically");

            // Verify tangent continuity across arc joins
            float3 tan0End = math.normalize(CubicBezier3D.Tangent(arcs[0], 1.0f));
            float3 tan1Start = math.normalize(CubicBezier3D.Tangent(arcs[1], 0.0f));
            Assert.True(math.dot(tan0End, tan1Start) > 0.999f, "Tangents between Arc 0 and 1 must be collinear");

            float3 tan1End = math.normalize(CubicBezier3D.Tangent(arcs[1], 1.0f));
            float3 tan2Start = math.normalize(CubicBezier3D.Tangent(arcs[2], 0.0f));
            Assert.True(math.dot(math.normalize(tan1End.xz), math.normalize(tan2Start.xz)) > 0.999f, "Horizontal tangents between Arc 1 and 2 must be collinear");
        }
    }
}
