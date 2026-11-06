using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using ProceduralInterchanges.Core.Math;
using ProceduralInterchanges.Core.Models;
using Unity.Mathematics;

namespace ProceduralInterchanges.Core.Templates.RL
{
    public sealed class RewardBreakdown
    {
        public float TotalReward;
        public float[] LossComponents = new float[8]; // L1 .. L8
        public bool HardConstraintSatisfied;
        public float MinClearanceFound;
        public float MaxGradeFound;
        public float MinRadiusFound;
        public float MaxBoundaryKinkDeg;
    }

    public static class InterchangeRewardEvaluator
    {
        public static RewardBreakdown Evaluate(
            InterchangePlan plan,
            InterchangeConfig config,
            IReadOnlyList<HighwayLeg> legs,
            RewardWeightVector weights = null)
        {
            weights = weights ?? new RewardWeightVector();
            var result = new RewardBreakdown
            {
                MinClearanceFound = 999f,
                MaxGradeFound = 0f,
                MinRadiusFound = 999f,
                MaxBoundaryKinkDeg = 0f
            };

            if (plan == null || plan.Courses == null || plan.Courses.Count == 0)
            {
                result.TotalReward = 0f;
                return result;
            }

            var courses = plan.Courses;
            int nCourses = courses.Count;

            float l1_clearance = 0f;
            float l2_curvature = 0f;
            float l3_grade = 0f;
            float l4_kink = 0f;
            float l5_overshoot = 0f;
            float l6_distortion = 0f;
            float l7_inflection = 0f;
            float l8_length = 0f;

            // 1. Curvature, Grade, Length, and Inflections per course
            float totalLength = 0f;

            for (int i = 0; i < nCourses; i++)
            {
                var c = courses[i];
                var b = c.Curve;
                float len = CubicBezier3D.Length(b);
                totalLength += len;

                // Grade check
                float dy = math.abs(b.d.y - b.a.y);
                float hLen = math.max(1f, math.distance(b.a.xz, b.d.xz));
                float grade = dy / hLen;
                if (grade > result.MaxGradeFound) result.MaxGradeFound = grade;
                if (grade > 0.20f)
                {
                    l3_grade += (grade - 0.20f) * 8.0f;
                }

                // Curvature & Inflection check
                // Sample at 8 intervals
                float prevCurvSign = 0f;
                for (int s = 0; s <= 8; s++)
                {
                    float t = s / 8.0f;
                    float r = CubicBezier3D.CurvatureRadius(b, t);
                    if (r < result.MinRadiusFound) result.MinRadiusFound = r;
                    if (r < 18.0f)
                    {
                        l2_curvature += (18.0f - r) / 18.0f;
                    }

                    // Check signed 2D curvature for unwanted S-bends on loop ramps (loops must turn monotonically)
                    if (c.Role == NetRole.LoopRamp)
                    {
                        float3 d1 = CubicBezier3D.Tangent(b, t);
                        float3 d2 = CubicBezier3D.SecondDerivative(b, t);

                        float dx = d1.x;
                        float dz = d1.z;
                        float ddx = d2.x;
                        float ddz = d2.z;
                        float den = math.pow(dx * dx + dz * dz, 1.5f);

                        if (den > 1e-4f)
                        {
                            float signedCurv = (dx * ddz - dz * ddx) / den;
                            float thresh = 0.0125f; // R < 80m for loops
                            if (math.abs(signedCurv) > thresh)
                            {
                                float curSign = math.sign(signedCurv);
                                if (prevCurvSign != 0f && curSign != prevCurvSign)
                                {
                                    l7_inflection += 1.0f;
                                }
                                prevCurvSign = curSign;
                            }
                        }
                    }
                }
            }

            l8_length = totalLength / 1200.0f;

            // 2. Vertical Clearance Check between overlapping courses
            for (int i = 0; i < nCourses; i++)
            {
                var c1 = courses[i];
                for (int j = i + 1; j < nCourses; j++)
                {
                    var c2 = courses[j];

                    // Quick bounds overlap check in XZ
                    float minX1 = math.min(math.min(c1.Curve.a.x, c1.Curve.b.x), math.min(c1.Curve.c.x, c1.Curve.d.x));
                    float maxX1 = math.max(math.max(c1.Curve.a.x, c1.Curve.b.x), math.max(c1.Curve.c.x, c1.Curve.d.x));
                    float minZ1 = math.min(math.min(c1.Curve.a.z, c1.Curve.b.z), math.min(c1.Curve.c.z, c1.Curve.d.z));
                    float maxZ1 = math.max(math.max(c1.Curve.a.z, c1.Curve.b.z), math.max(c1.Curve.c.z, c1.Curve.d.z));

                    float minX2 = math.min(math.min(c2.Curve.a.x, c2.Curve.b.x), math.min(c2.Curve.c.x, c2.Curve.d.x));
                    float maxX2 = math.max(math.max(c2.Curve.a.x, c2.Curve.b.x), math.max(c2.Curve.c.x, c2.Curve.d.x));
                    float minZ2 = math.min(math.min(c2.Curve.a.z, c2.Curve.b.z), math.min(c2.Curve.c.z, c2.Curve.d.z));
                    float maxZ2 = math.max(math.max(c2.Curve.a.z, c2.Curve.b.z), math.max(c2.Curve.c.z, c2.Curve.d.z));

                    if (maxX1 < minX2 || maxX2 < minX1 || maxZ1 < minZ2 || maxZ2 < minZ1)
                    {
                        continue;
                    }

                    // Ignore contiguous roads sharing an endpoint (merge / diverge / continuous chains)
                    bool shareEndpoint = math.distance(c1.Curve.a.xz, c2.Curve.a.xz) < 5.0f ||
                                         math.distance(c1.Curve.a.xz, c2.Curve.d.xz) < 5.0f ||
                                         math.distance(c1.Curve.d.xz, c2.Curve.a.xz) < 5.0f ||
                                         math.distance(c1.Curve.d.xz, c2.Curve.d.xz) < 5.0f;
                    if (shareEndpoint)
                    {
                        continue;
                    }

                    // Check 2D polyline crossings in XZ plane for overpass bridge clearances
                    for (int s1 = 0; s1 < 6; s1++)
                    {
                        float3 a1 = CubicBezier3D.Position(c1.Curve, s1 / 6.0f);
                        float3 b1 = CubicBezier3D.Position(c1.Curve, (s1 + 1) / 6.0f);

                        for (int s2 = 0; s2 < 6; s2++)
                        {
                            float3 a2 = CubicBezier3D.Position(c2.Curve, s2 / 6.0f);
                            float3 b2 = CubicBezier3D.Position(c2.Curve, (s2 + 1) / 6.0f);

                            if (SegmentsIntersect2D(a1.xz, b1.xz, a2.xz, b2.xz, out float t12, out float t34))
                            {
                                float y1 = math.lerp(a1.y, b1.y, t12);
                                float y2 = math.lerp(a2.y, b2.y, t34);
                                float dy = math.abs(y1 - y2);

                                if (dy < result.MinClearanceFound) result.MinClearanceFound = dy;
                                float reqClearance = math.max(7.3f, config.MinBridgeClearanceMeters);
                                if (dy < reqClearance)
                                {
                                    l1_clearance += (reqClearance - dy) / reqClearance;
                                }
                            }
                        }
                    }
                }
            }

            // 3. Boundary Kink & Overshoot Checks
            if (legs != null && legs.Count > 0)
            {
                float maxAllowedRadius = config.FootprintRadius + 6.0f;
                for (int i = 0; i < nCourses; i++)
                {
                    var c = courses[i];
                    // Global radial footprint overshoot
                    float dRadA = math.length(c.Curve.a.xz - config.Center.xz) - maxAllowedRadius;
                    float dRadD = math.length(c.Curve.d.xz - config.Center.xz) - maxAllowedRadius;
                    if (dRadA > 0f) l5_overshoot += dRadA * 0.25f;
                    if (dRadD > 0f) l5_overshoot += dRadD * 0.25f;

                    for (int l = 0; l < legs.Count; l++)
                    {
                        var leg = legs[l];
                        float2 planePt = leg.PortalCenterPosition.xz;
                        float2 inTan = leg.PortalInwardTangent.xz;
                        float2 planeNormal = math.lengthsq(inTan) > 1e-4f ? -math.normalize(inTan) : -math.normalize(leg.LegAxisDirection.xz);
                        float2 inTanNorm = math.lengthsq(inTan) > 1e-4f ? math.normalize(inTan) : -math.normalize(leg.LegAxisDirection.xz);
                        float2 lateralDir = new float2(-planeNormal.y, planeNormal.x);

                        // Local boundary plane overshoot (only within corridor)
                        if (math.abs(math.dot(c.Curve.a.xz - planePt, lateralDir)) < 65.0f)
                        {
                            float dA = math.dot(c.Curve.a.xz - planePt, planeNormal);
                            if (dA > 2.0f) l5_overshoot += (dA - 2.0f) * 0.2f;
                        }
                        if (math.abs(math.dot(c.Curve.d.xz - planePt, lateralDir)) < 65.0f)
                        {
                            float dD = math.dot(c.Curve.d.xz - planePt, planeNormal);
                            if (dD > 2.0f) l5_overshoot += (dD - 2.0f) * 0.2f;
                        }

                        // Kink check for curves connecting to leg portals
                        float distInA = leg.Inbound != null ? math.distance(c.Curve.a.xz, leg.Inbound.PortalPosition.xz) : 999f;
                        float distInD = leg.Inbound != null ? math.distance(c.Curve.d.xz, leg.Inbound.PortalPosition.xz) : 999f;
                        float distOutA = leg.Outbound != null ? math.distance(c.Curve.a.xz, leg.Outbound.PortalPosition.xz) : 999f;
                        float distOutD = leg.Outbound != null ? math.distance(c.Curve.d.xz, leg.Outbound.PortalPosition.xz) : 999f;

                        bool touchesA = distInA < 8.0f || distOutA < 8.0f || math.distance(c.Curve.a.xz, planePt) < 18.0f;
                        bool touchesD = distInD < 8.0f || distOutD < 8.0f || math.distance(c.Curve.d.xz, planePt) < 18.0f;

                        if (touchesA)
                        {
                            float3 diffStart = c.Curve.b - c.Curve.a;
                            if (math.lengthsq(diffStart.xz) > 1e-4f)
                            {
                                float2 tanNorm = math.normalize(diffStart.xz);
                                float cosAngle = math.clamp(math.abs(math.dot(tanNorm, inTanNorm)), 0f, 1f);
                                float angle = math.degrees(math.acos(cosAngle));
                                if (angle > result.MaxBoundaryKinkDeg) result.MaxBoundaryKinkDeg = angle;
                                if (angle > 1.5f) l4_kink += (angle - 1.5f) / 10.0f;
                            }
                        }

                        if (touchesD)
                        {
                            float3 diffEnd = c.Curve.d - c.Curve.c;
                            if (math.lengthsq(diffEnd.xz) > 1e-4f)
                            {
                                float2 tanNorm = math.normalize(diffEnd.xz);
                                float cosAngle = math.clamp(math.abs(math.dot(tanNorm, inTanNorm)), 0f, 1f);
                                float angle = math.degrees(math.acos(cosAngle));
                                if (angle > result.MaxBoundaryKinkDeg) result.MaxBoundaryKinkDeg = angle;
                                if (angle > 1.5f) l4_kink += (angle - 1.5f) / 10.0f;
                            }
                        }
                    }
                }
            }

            // Internal degree-2 join kinks
            for (int i = 0; i < nCourses; i++)
            {
                var c1 = courses[i];
                for (int j = 0; j < nCourses; j++)
                {
                    if (i == j) continue;
                    var c2 = courses[j];
                    if (math.distance(c1.Curve.d.xz, c2.Curve.a.xz) < 0.5f)
                    {
                        float3 d1 = c1.Curve.d - c1.Curve.c;
                        float3 d2 = c2.Curve.b - c2.Curve.a;
                        if (math.lengthsq(d1.xz) > 1e-4f && math.lengthsq(d2.xz) > 1e-4f)
                        {
                            float2 t1 = math.normalize(d1.xz);
                            float2 t2 = math.normalize(d2.xz);
                            float dot = math.clamp(math.dot(t1, t2), -1f, 1f);
                            float angleDeg = math.degrees(math.acos(dot));
                            if (angleDeg > result.MaxBoundaryKinkDeg) result.MaxBoundaryKinkDeg = angleDeg;
                            if (angleDeg > 2.0f)
                            {
                                l4_kink += (angleDeg - 2.0f) / 10.0f;
                            }
                        }
                    }
                }
            }

            result.LossComponents[0] = l1_clearance;
            result.LossComponents[1] = l2_curvature;
            result.LossComponents[2] = l3_grade;
            result.LossComponents[3] = l4_kink;
            result.LossComponents[4] = l5_overshoot;
            result.LossComponents[5] = l6_distortion;
            result.LossComponents[6] = l7_inflection;
            result.LossComponents[7] = l8_length;

            float wDotL =
                weights.WClearance * l1_clearance +
                weights.WCurvature * l2_curvature +
                weights.WGrade * l3_grade +
                weights.WKink * l4_kink +
                weights.WOvershoot * l5_overshoot +
                weights.WDistortion * l6_distortion +
                weights.WInflection * l7_inflection +
                weights.WLength * l8_length;

            result.TotalReward = math.clamp(100.0f - wDotL, 0.0f, 100.0f);
            result.HardConstraintSatisfied = (result.MinRadiusFound >= 17.5f) &&
                                            (result.MaxGradeFound <= 0.205f) &&
                                            (result.MinClearanceFound >= 6.8f || l1_clearance == 0f);

            return result;
        }

        public static float EvaluateFast(
            IReadOnlyList<PlannedNetCourse> courses,
            InterchangeConfig config,
            IReadOnlyList<HighwayLeg> legs,
            RewardWeightVector weights = null)
        {
            weights = weights ?? new RewardWeightVector();
            if (courses == null || courses.Count == 0) return 0f;

            float lCurvature = 0f;
            float lGrade = 0f;
            float lKink = 0f;
            float lOvershoot = 0f;
            float totalLen = 0f;

            for (int i = 0; i < courses.Count; i++)
            {
                var c = courses[i];
                var b = c.Curve;
                float chord = math.distance(b.a.xz, b.d.xz);
                totalLen += chord;

                // Grade
                float dy = math.abs(b.d.y - b.a.y);
                float grade = dy / math.max(1f, chord);
                if (grade > 0.20f) lGrade += (grade - 0.20f) * 6.0f;

                // 3-point Curvature sampling
                float r0 = CubicBezier3D.CurvatureRadius(b, 0.15f);
                float r1 = CubicBezier3D.CurvatureRadius(b, 0.50f);
                float r2 = CubicBezier3D.CurvatureRadius(b, 0.85f);
                float minR = math.min(r0, math.min(r1, r2));
                if (minR < 18.0f)
                {
                    lCurvature += (18.0f - minR) / 18.0f;
                }
            }

            // Boundary overshoot & kink
            if (legs != null && legs.Count > 0)
            {
                float maxAllowedRadius = config.FootprintRadius + 6.0f;
                for (int i = 0; i < courses.Count; i++)
                {
                    var c = courses[i];
                    float dRadA = math.length(c.Curve.a.xz - config.Center.xz) - maxAllowedRadius;
                    float dRadD = math.length(c.Curve.d.xz - config.Center.xz) - maxAllowedRadius;
                    if (dRadA > 0f) lOvershoot += dRadA * 0.25f;
                    if (dRadD > 0f) lOvershoot += dRadD * 0.25f;

                    for (int l = 0; l < legs.Count; l++)
                    {
                        var leg = legs[l];
                        float2 planePt = leg.PortalCenterPosition.xz;
                        float2 inTan = leg.PortalInwardTangent.xz;
                        float2 normal = math.lengthsq(inTan) > 1e-4f ? -math.normalize(inTan) : -math.normalize(leg.LegAxisDirection.xz);
                        float2 inTanNorm = math.lengthsq(inTan) > 1e-4f ? math.normalize(inTan) : -math.normalize(leg.LegAxisDirection.xz);
                        float2 lateralDir = new float2(-normal.y, normal.x);

                        if (math.abs(math.dot(c.Curve.a.xz - planePt, lateralDir)) < 65.0f)
                        {
                            float dA = math.dot(c.Curve.a.xz - planePt, normal);
                            if (dA > 2.0f) lOvershoot += (dA - 2.0f) * 0.2f;
                        }
                        if (math.abs(math.dot(c.Curve.d.xz - planePt, lateralDir)) < 65.0f)
                        {
                            float dD = math.dot(c.Curve.d.xz - planePt, normal);
                            if (dD > 2.0f) lOvershoot += (dD - 2.0f) * 0.2f;
                        }

                        // Boundary kink
                        bool touchesA = math.distance(c.Curve.a.xz, planePt) < 20.0f;
                        bool touchesD = math.distance(c.Curve.d.xz, planePt) < 20.0f;

                        if (touchesA)
                        {
                            float3 diffStart = c.Curve.b - c.Curve.a;
                            if (math.lengthsq(diffStart.xz) > 1e-4f)
                            {
                                float2 tanNorm = math.normalize(diffStart.xz);
                                float cosAngle = math.clamp(math.abs(math.dot(tanNorm, inTanNorm)), 0f, 1f);
                                float angle = math.degrees(math.acos(cosAngle));
                                if (angle > 1.5f) lKink += (angle - 1.5f) / 10.0f;
                            }
                        }
                        if (touchesD)
                        {
                            float3 diffEnd = c.Curve.d - c.Curve.c;
                            if (math.lengthsq(diffEnd.xz) > 1e-4f)
                            {
                                float2 tanNorm = math.normalize(diffEnd.xz);
                                float cosAngle = math.clamp(math.abs(math.dot(tanNorm, inTanNorm)), 0f, 1f);
                                float angle = math.degrees(math.acos(cosAngle));
                                if (angle > 1.5f) lKink += (angle - 1.5f) / 10.0f;
                            }
                        }
                    }
                }
            }

            // Internal degree-2 join kinks
            for (int i = 0; i < courses.Count; i++)
            {
                var c1 = courses[i];
                for (int j = 0; j < courses.Count; j++)
                {
                    if (i == j) continue;
                    var c2 = courses[j];
                    if (math.distance(c1.Curve.d.xz, c2.Curve.a.xz) < 0.5f)
                    {
                        float3 d1 = c1.Curve.d - c1.Curve.c;
                        float3 d2 = c2.Curve.b - c2.Curve.a;
                        if (math.lengthsq(d1.xz) > 1e-4f && math.lengthsq(d2.xz) > 1e-4f)
                        {
                            float2 t1 = math.normalize(d1.xz);
                            float2 t2 = math.normalize(d2.xz);
                            float dot = math.clamp(math.dot(t1, t2), -1f, 1f);
                            float angleDeg = math.degrees(math.acos(dot));
                            if (angleDeg > 2.0f)
                            {
                                lKink += (angleDeg - 2.0f) / 10.0f;
                            }
                        }
                    }
                }
            }

            // 3D vertical clearance check at crossings
            float lClearance = 0f;
            for (int i = 0; i < courses.Count; i++)
            {
                var c1 = courses[i];
                for (int j = i + 1; j < courses.Count; j++)
                {
                    var c2 = courses[j];
                    float minX1 = math.min(math.min(c1.Curve.a.x, c1.Curve.b.x), math.min(c1.Curve.c.x, c1.Curve.d.x));
                    float maxX1 = math.max(math.max(c1.Curve.a.x, c1.Curve.b.x), math.max(c1.Curve.c.x, c1.Curve.d.x));
                    float minZ1 = math.min(math.min(c1.Curve.a.z, c1.Curve.b.z), math.min(c1.Curve.c.z, c1.Curve.d.z));
                    float maxZ1 = math.max(math.max(c1.Curve.a.z, c1.Curve.b.z), math.max(c1.Curve.c.z, c1.Curve.d.z));

                    float minX2 = math.min(math.min(c2.Curve.a.x, c2.Curve.b.x), math.min(c2.Curve.c.x, c2.Curve.d.x));
                    float maxX2 = math.max(math.max(c2.Curve.a.x, c2.Curve.b.x), math.max(c2.Curve.c.x, c2.Curve.d.x));
                    float minZ2 = math.min(math.min(c2.Curve.a.z, c2.Curve.b.z), math.min(c2.Curve.c.z, c2.Curve.d.z));
                    float maxZ2 = math.max(math.max(c2.Curve.a.z, c2.Curve.b.z), math.max(c2.Curve.c.z, c2.Curve.d.z));

                    if (maxX1 < minX2 || maxX2 < minX1 || maxZ1 < minZ2 || maxZ2 < minZ1) continue;

                    bool shareEndpoint = math.distance(c1.Curve.a.xz, c2.Curve.a.xz) < 3.0f ||
                                         math.distance(c1.Curve.a.xz, c2.Curve.d.xz) < 3.0f ||
                                         math.distance(c1.Curve.d.xz, c2.Curve.a.xz) < 3.0f ||
                                         math.distance(c1.Curve.d.xz, c2.Curve.d.xz) < 3.0f;
                    if (shareEndpoint) continue;

                    for (int s1 = 0; s1 < 4; s1++)
                    {
                        float3 a1 = CubicBezier3D.Position(c1.Curve, s1 / 4.0f);
                        float3 b1 = CubicBezier3D.Position(c1.Curve, (s1 + 1) / 4.0f);
                        for (int s2 = 0; s2 < 4; s2++)
                        {
                            float3 a2 = CubicBezier3D.Position(c2.Curve, s2 / 4.0f);
                            float3 b2 = CubicBezier3D.Position(c2.Curve, (s2 + 1) / 4.0f);
                            if (SegmentsIntersect2D(a1.xz, b1.xz, a2.xz, b2.xz, out float t12, out float t34))
                            {
                                float y1 = math.lerp(a1.y, b1.y, t12);
                                float y2 = math.lerp(a2.y, b2.y, t34);
                                float dy = math.abs(y1 - y2);
                                float req = math.max(7.3f, config.MinBridgeClearanceMeters);
                                if (dy < req)
                                {
                                    lClearance += (req - dy) * 5.0f;
                                }
                            }
                        }
                    }
                }
            }

            float lLength = totalLen / 1200.0f;

            float wDotL =
                weights.WClearance * lClearance +
                weights.WCurvature * lCurvature +
                weights.WGrade * lGrade +
                weights.WKink * lKink +
                weights.WOvershoot * lOvershoot +
                weights.WLength * lLength;

            return math.clamp(100.0f - wDotL, 0.0f, 100.0f);
        }

        private static bool SegmentsIntersect2D(float2 p1, float2 p2, float2 p3, float2 p4, out float t12, out float t34)
        {
            t12 = 0f;
            t34 = 0f;
            float2 d12 = p2 - p1;
            float2 d34 = p4 - p3;
            float denom = d12.x * d34.y - d12.y * d34.x;
            if (math.abs(denom) < 1e-5f) return false;

            float2 dp = p3 - p1;
            t12 = (dp.x * d34.y - dp.y * d34.x) / denom;
            t34 = (dp.x * d12.y - dp.y * d12.x) / denom;

            return t12 >= 0f && t12 <= 1f && t34 >= 0f && t34 <= 1f;
        }
    }
}
