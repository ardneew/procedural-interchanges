using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Unity.Mathematics;

namespace ProceduralInterchanges.Core.Math
{
    public static class TopographySolver
    {
        public static void FitGroundCourse(
            Bezier4x3 curve,
            Func<float2, float> sampleTerrainHeight,
            bool pinStart,
            bool pinEnd,
            List<Bezier4x3> outSubdividedCourses,
            float maxDeviation = 0.30f,
            int maxDepth = 4)
        {
            float len = CubicBezier3D.Length(curve);
            if (len < 12f || maxDepth <= 0)
            {
                // Fit single cubic segment
                outSubdividedCourses.Add(FitSingleSegment(curve, sampleTerrainHeight, pinStart, pinEnd));
                return;
            }

            // Check worst deviation
            var fitted = FitSingleSegment(curve, sampleTerrainHeight, pinStart, pinEnd);
            float maxGap = 0f;
            float worstT = 0.5f;

            for (int k = 1; k < 12; k++)
            {
                float t = (float)k / 12f;
                float3 pos = CubicBezier3D.Position(fitted, t);
                float terrainH = sampleTerrainHeight(pos.xz);
                float gap = math.abs(pos.y - terrainH);
                if (gap > maxGap)
                {
                    maxGap = gap;
                    worstT = t;
                }
            }

            if (maxGap > maxDeviation && len >= 16f)
            {
                float splitT = math.clamp(worstT, 0.35f, 0.65f);
                CubicBezier3D.Split(curve, splitT, out var left, out var right);

                FitGroundCourse(left, sampleTerrainHeight, pinStart, true, outSubdividedCourses, maxDeviation, maxDepth - 1);
                FitGroundCourse(right, sampleTerrainHeight, true, pinEnd, outSubdividedCourses, maxDeviation, maxDepth - 1);
            }
            else
            {
                outSubdividedCourses.Add(fitted);
            }
        }

        public static Bezier4x3 FitSingleSegment(
            Bezier4x3 curve,
            Func<float2, float> sampleTerrainHeight,
            bool pinStart,
            bool pinEnd)
        {
            float y0 = pinStart ? curve.a.y : sampleTerrainHeight(curve.a.xz);
            float y3 = pinEnd ? curve.d.y : sampleTerrainHeight(curve.d.xz);

            float3 p1_3 = CubicBezier3D.Position(curve, 1f / 3f);
            float3 p2_3 = CubicBezier3D.Position(curve, 2f / 3f);

            float g1 = sampleTerrainHeight(p1_3.xz);
            float g2 = sampleTerrainHeight(p2_3.xz);

            // Solve linear system for B1.y and B2.y
            // C(1/3) = (8 y0 + 12 y1 + 6 y2 + y3) / 27 = g1
            // C(2/3) = (y0 + 6 y1 + 12 y2 + 8 y3) / 27 = g2
            float r1 = 27f * g1 - 8f * y0 - y3;
            float r2 = 27f * g2 - y0 - 8f * y3;

            float y2 = (2f * r2 - r1) / 18f;
            float y1 = (r1 - 6f * y2) / 12f;

            float horizLen = math.max(5f, math.distance(curve.a.xz, curve.d.xz));
            float maxRise = horizLen * 0.18f; // Never exceed game engine hard limit (0.20f)

            // Only adjust unpinned endpoints if slope exceeds maximum rise
            if (!pinStart && !pinEnd && math.abs(y3 - y0) > maxRise)
            {
                y3 = y0 + math.sign(y3 - y0) * maxRise;
            }
            else if (pinStart && !pinEnd && math.abs(y3 - y0) > maxRise)
            {
                y3 = y0 + math.sign(y3 - y0) * maxRise;
            }
            else if (!pinStart && pinEnd && math.abs(y3 - y0) > maxRise)
            {
                y0 = y3 - math.sign(y3 - y0) * maxRise;
            }
            // If both are pinned, neither y0 nor y3 is mutated

            float maxRiseThird = (horizLen / 3f) * 0.18f;
            y1 = math.clamp(y1, y0 - maxRiseThird * 2f, y0 + maxRiseThird * 2f);
            y2 = math.clamp(y2, y3 - maxRiseThird * 2f, y3 + maxRiseThird * 2f);

            float3 a = new float3(curve.a.x, y0, curve.a.z);
            float3 b = new float3(curve.b.x, y1, curve.b.z);
            float3 c = new float3(curve.c.x, y2, curve.c.z);
            float3 d = new float3(curve.d.x, y3, curve.d.z);

            return new Bezier4x3(a, b, c, d);
        }
    }
}
