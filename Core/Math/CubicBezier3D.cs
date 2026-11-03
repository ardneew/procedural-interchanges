using System;
using Colossal.Mathematics;
using Unity.Mathematics;

namespace ProceduralInterchanges.Core.Math
{
    public static class CubicBezier3D
    {
        public static float3 Position(in Bezier4x3 c, float t)
        {
            float u = 1f - t;
            float tt = t * t;
            float uu = u * u;
            float uuu = uu * u;
            float ttt = tt * t;

            return uuu * c.a + 3f * uu * t * c.b + 3f * u * tt * c.c + ttt * c.d;
        }

        public static float3 Tangent(in Bezier4x3 c, float t)
        {
            float u = 1f - t;
            return 3f * u * u * (c.b - c.a) + 6f * u * t * (c.c - c.b) + 3f * t * t * (c.d - c.c);
        }

        public static float3 SecondDerivative(in Bezier4x3 c, float t)
        {
            float u = 1f - t;
            return 6f * u * (c.c - 2f * c.b + c.a) + 6f * t * (c.d - 2f * c.c + c.b);
        }

        public static float CurvatureXZ(in Bezier4x3 c, float t)
        {
            float3 d1 = Tangent(c, t);
            float3 d2 = SecondDerivative(c, t);

            float dx = d1.x;
            float dz = d1.z;
            float ddx = d2.x;
            float ddz = d2.z;

            float num = math.abs(dx * ddz - dz * ddx);
            float den = math.pow(dx * dx + dz * dz, 1.5f);

            if (den < 1e-6f) return 0f;
            return num / den;
        }

        public static float MinRadiusXZ(in Bezier4x3 c, int samples = 24)
        {
            float maxCurvature = 0f;
            for (int i = 0; i <= samples; i++)
            {
                float t = (float)i / samples;
                float k = CurvatureXZ(c, t);
                if (k > maxCurvature)
                {
                    maxCurvature = k;
                }
            }

            if (maxCurvature <= 1e-5f) return 9999f;
            return 1f / maxCurvature;
        }

        public static float MaxSlopeGrade(in Bezier4x3 c, int samples = 24)
        {
            float maxGrade = 0f;
            for (int i = 0; i <= samples; i++)
            {
                float t = (float)i / samples;
                float3 tan = Tangent(c, t);
                float horiz = math.length(tan.xz);
                if (horiz > 1e-4f)
                {
                    float grade = math.abs(tan.y) / horiz;
                    if (grade > maxGrade) maxGrade = grade;
                }
            }

            // Also check chord grade
            float chordHoriz = math.distance(c.a.xz, c.d.xz);
            if (chordHoriz > 0.1f)
            {
                float chordGrade = math.abs(c.d.y - c.a.y) / chordHoriz;
                if (chordGrade > maxGrade) maxGrade = chordGrade;
            }

            return maxGrade * 100f; // percent
        }

        public static float Length(in Bezier4x3 c, int samples = 24)
        {
            float len = 0f;
            float3 prev = c.a;
            for (int i = 1; i <= samples; i++)
            {
                float t = (float)i / samples;
                float3 cur = Position(c, t);
                len += math.distance(prev, cur);
                prev = cur;
            }
            return len;
        }

        public static void Split(in Bezier4x3 c, float t, out Bezier4x3 left, out Bezier4x3 right)
        {
            float3 p01 = math.lerp(c.a, c.b, t);
            float3 p12 = math.lerp(c.b, c.c, t);
            float3 p23 = math.lerp(c.c, c.d, t);

            float3 p012 = math.lerp(p01, p12, t);
            float3 p123 = math.lerp(p12, p23, t);

            float3 p0123 = math.lerp(p012, p123, t);

            left = new Bezier4x3(c.a, p01, p012, p0123);
            right = new Bezier4x3(p0123, p123, p23, c.d);
        }

        public static float DistanceXZ(in Bezier4x3 c, float2 p, out float bestT, int samples = 32)
        {
            bestT = 0f;
            float bestDistSq = float.MaxValue;

            for (int i = 0; i <= samples; i++)
            {
                float t = (float)i / samples;
                float3 pos = Position(c, t);
                float distSq = math.distancesq(pos.xz, p);
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestT = t;
                }
            }

            // Damped refinement around bestT
            float step = 0.5f / samples;
            for (int iter = 0; iter < 4; iter++)
            {
                float tMinus = math.clamp(bestT - step, 0f, 1f);
                float tPlus = math.clamp(bestT + step, 0f, 1f);

                float dMinus = math.distancesq(Position(c, tMinus).xz, p);
                float dPlus = math.distancesq(Position(c, tPlus).xz, p);

                if (dMinus < bestDistSq)
                {
                    bestDistSq = dMinus;
                    bestT = tMinus;
                }
                else if (dPlus < bestDistSq)
                {
                    bestDistSq = dPlus;
                    bestT = tPlus;
                }
                step *= 0.5f;
            }

            return math.sqrt(bestDistSq);
        }
    }
}
