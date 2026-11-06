using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Unity.Mathematics;

namespace ProceduralInterchanges.Core.Math
{
    public static class BiArcFit
    {
        public const float kStandardGoreAngleRad = 0.104719755f; // 6 degrees

        public static Bezier4x3 FitSmoothCurve(float3 startPos, float3 startDir, float3 endPos, float3 endDir)
        {
            float3 t0 = math.normalize(startDir);
            float3 t1 = math.normalize(endDir);
            float chord = math.distance(startPos.xz, endPos.xz);

            if (chord < 1e-3f)
            {
                return new Bezier4x3(startPos, startPos, endPos, endPos);
            }

            // Ray intersection in 2D XZ
            float2 p0 = startPos.xz;
            float2 p1 = endPos.xz;
            float2 d0 = t0.xz;
            float2 d1 = t1.xz;

            float denom = d0.x * d1.y - d0.y * d1.x;
            float handleLength0 = chord * 0.38f;
            float handleLength1 = chord * 0.38f;

            if (math.abs(denom) > 1e-4f)
            {
                float2 delta = p1 - p0;
                float s = (delta.x * d1.y - delta.y * d1.x) / denom;
                float u = (delta.y * d0.x - delta.x * d0.y) / denom;

                if (s > 0f && u > 0f)
                {
                    float dot = math.clamp(math.dot(d0, d1), -1f, 1f);
                    float angle = math.acos(dot);
                    if (angle > 0.05f)
                    {
                        float tanQuarter = math.tan(angle * 0.25f);
                        float factor = (2f / 3f) * (1f - tanQuarter * tanQuarter);
                        handleLength0 = math.clamp(s * factor, chord * 0.15f, chord * 0.65f);
                        handleLength1 = math.clamp(u * factor, chord * 0.15f, chord * 0.65f);
                    }
                }
            }

            float3 b = startPos + t0 * handleLength0;
            float3 c = endPos - t1 * handleLength1;

            return new Bezier4x3(startPos, b, c, endPos);
        }

        public static Bezier4x3 FitCircularArc(float3 center, float radius, float startAngleRad, float sweepAngleRad, float startY, float endY)
        {
            radius = math.max(30.0f, radius);
            float alpha = (4f / 3f) * math.tan(sweepAngleRad / 4f);

            float cos0 = math.cos(startAngleRad);
            float sin0 = math.sin(startAngleRad);
            float endAngle = startAngleRad + sweepAngleRad;
            float cos1 = math.cos(endAngle);
            float sin1 = math.sin(endAngle);

            float3 p0 = new float3(center.x + radius * cos0, startY, center.z + radius * sin0);
            float3 p3 = new float3(center.x + radius * cos1, endY, center.z + radius * sin1);

            // Tangent vectors for circle
            float3 t0 = new float3(-sin0, 0f, cos0) * (sweepAngleRad >= 0 ? 1f : -1f);
            float3 t1 = new float3(-sin1, 0f, cos1) * (sweepAngleRad >= 0 ? 1f : -1f);

            float hLen = math.abs(radius * alpha);
            float3 p1 = p0 + t0 * hLen;
            float3 p2 = p3 - t1 * hLen;

            // Interpolate Y
            p1.y = math.lerp(startY, endY, 1f / 3f);
            p2.y = math.lerp(startY, endY, 2f / 3f);

            return new Bezier4x3(p0, p1, p2, p3);
        }

        public static void FitLoop270(float3 loopCenter, float radius, float entryAngleRad, int handednessSign, float startY, float peakY, float endY, List<Bezier4x3> outThreeArcs)
        {
            // 270 degree loop composed of 3 x 90 degree arcs
            // Negative sweep for RHT (clockwise turn) ensures velocity vector aligns with road tangent
            float sweep90 = -math.PI * 0.5f * (handednessSign >= 0 ? 1f : -1f);

            float a0 = entryAngleRad;
            float a1 = a0 + sweep90;
            float a2 = a1 + sweep90;
            float a3 = a2 + sweep90;

            float y1 = math.lerp(startY, peakY, 0.5f);
            float y2 = math.lerp(peakY, endY, 0.5f);

            outThreeArcs.Add(FitCircularArc(loopCenter, radius, a0, sweep90, startY, y1));
            outThreeArcs.Add(FitCircularArc(loopCenter, radius, a1, sweep90, y1, peakY));
            outThreeArcs.Add(FitCircularArc(loopCenter, radius, a2, sweep90, peakY, endY));
        }

        public static void FitConnectedLoop270(
            float3 startPos, float3 startDir,
            float3 endPos, float3 endDir,
            float radius, int handednessSign,
            float startY, float peakY, float endY,
            List<Bezier4x3> outArcs)
        {
            radius = math.max(28.0f, radius);
            float3 t0 = math.normalize(startDir);
            float3 t1 = math.normalize(endDir);
            float sigma = handednessSign >= 0 ? 1f : -1f;

            float2 T0_2d = t0.xz;
            float2 T1_2d = t1.xz;

            // Compute radial angles on circle where tangent matches T0 and T1
            float a0 = math.atan2(T0_2d.y, T0_2d.x) + sigma * (math.PI * 0.5f);
            float aExit = math.atan2(T1_2d.y, T1_2d.x) + sigma * (math.PI * 0.5f);

            // Compute total clockwise/counterclockwise sweep matching actual highway angle
            float deltaPhi = -sigma * (aExit - a0);
            while (deltaPhi <= 0f) deltaPhi += math.PI * 2f;
            while (deltaPhi > math.PI * 2f) deltaPhi -= math.PI * 2f;
            if (deltaPhi < 1.2f)
            {
                // Ensure loop sweeps the full around loop rather than a direct turn
                deltaPhi += math.PI * 2f;
            }

            float totalSweep = -sigma * deltaPhi;
            float sweepArc = totalSweep / 3f;

            float2 u0 = new float2(math.cos(a0), math.sin(a0));
            float2 u1 = new float2(math.cos(aExit), math.sin(aExit));

            // Solve for circle center C and lead distances L0, L1:
            // Pstart + T0 * L0 = C + R * u0
            // Pend - T1 * L1 = C + R * u1
            // => T0 * L0 + T1 * L1 = (Pend - Pstart) - R * (u1 - u0)
            float det = T0_2d.x * T1_2d.y - T0_2d.y * T1_2d.x;
            bool solved = false;
            float L0 = 12f;
            float L1 = 12f;

            if (math.abs(det) > 0.12f)
            {
                float2 dP = endPos.xz - startPos.xz;
                float2 dU = u1 - u0;

                float k0 = (dP.x * T1_2d.y - dP.y * T1_2d.x) / det;
                float m0 = (dU.x * T1_2d.y - dU.y * T1_2d.x) / det;

                float k1 = (T0_2d.x * dP.y - T0_2d.y * dP.x) / det;
                float m1 = (T0_2d.x * dU.y - T0_2d.y * dU.x) / det;

                // Try radii from requested radius down to 26m
                float[] rCandidates = { radius, radius * 0.9f, radius * 0.8f, radius * 0.7f, 32f, 28f, 26f };
                for (int rc = 0; rc < rCandidates.Length; rc++)
                {
                    float testR = rCandidates[rc];
                    float testL0 = k0 - m0 * testR;
                    float testL1 = k1 - m1 * testR;
                    if (testL0 >= 4f && testL1 >= 4f)
                    {
                        radius = testR;
                        L0 = testL0;
                        L1 = testL1;
                        solved = true;
                        break;
                    }
                }
            }

            float2 cXZ;
            float2 p0_2d;

            if (solved)
            {
                p0_2d = startPos.xz + T0_2d * L0;
                cXZ = p0_2d - u0 * radius;
            }
            else
            {
                // Fallback: place circle center in the quadrant offset from startPos and endPos
                float d0 = math.distance(startPos.xz, endPos.xz);
                radius = math.clamp(d0 * 0.35f, 26f, 40f);
                L0 = math.clamp(d0 * 0.25f, 10f, 30f);
                p0_2d = startPos.xz + T0_2d * L0;
                cXZ = p0_2d - u0 * radius;
            }

            float3 circleCenter = new float3(cXZ.x, (startY + endY) * 0.5f, cXZ.y);
            float3 p0_3d = new float3(p0_2d.x, startY, p0_2d.y);

            // 2. 3 circular arcs covering exact total sweep
            float arcA0 = a0;
            float arcA1 = arcA0 + sweepArc;
            float arcA2 = arcA1 + sweepArc;

            float y1 = math.lerp(startY, peakY, 0.5f);
            float y2 = math.lerp(peakY, endY, 0.5f);

            var arc0 = FitCircularArc(circleCenter, radius, arcA0, sweepArc, startY, y1);
            var arc1 = FitCircularArc(circleCenter, radius, arcA1, sweepArc, y1, peakY);
            var arc2 = FitCircularArc(circleCenter, radius, arcA2, sweepArc, peakY, endY);

            // 1. Lead-in if startPos is offset from circle entry p0
            float3 tEntry = math.normalize(arc0.b - arc0.a);
            if (math.distance(startPos.xz, p0_3d.xz) > 1.5f)
            {
                outArcs.Add(FitSmoothCurve(startPos, t0, p0_3d, tEntry));
            }

            outArcs.Add(arc0);
            outArcs.Add(arc1);
            outArcs.Add(arc2);

            // 3. Lead-out from circle exit p3 to endPos
            float3 p3_3d = arc2.d;
            float3 tExit = math.normalize(arc2.d - arc2.c);
            if (math.distance(p3_3d.xz, endPos.xz) > 1.5f)
            {
                outArcs.Add(FitSmoothCurve(p3_3d, tExit, endPos, t1));
            }
        }

        public static void ApplyGoreTaper(float3 splitPos, float3 mainlineDir, int sideSign, float goreAngleRad, out float3 rampStartDir)
        {
            float3 mDir = math.normalize(mainlineDir);
            float3 normal = new float3(mDir.z, 0f, -mDir.x) * (sideSign >= 0 ? 1f : -1f);

            float cosA = math.cos(goreAngleRad);
            float sinA = math.sin(goreAngleRad);

            rampStartDir = math.normalize(mDir * cosA + normal * sinA);
        }
    }
}
