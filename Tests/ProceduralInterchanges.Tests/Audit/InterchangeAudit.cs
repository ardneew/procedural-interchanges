using System;
using System.Collections.Generic;
using System.Diagnostics;
using Colossal.Mathematics;
using ProceduralInterchanges.Core.Math;
using ProceduralInterchanges.Core.Models;
using Unity.Mathematics;

namespace ProceduralInterchanges.Tests.Audit
{
    public static class InterchangeAudit
    {
        public static AuditVerdict RunAudit(
            InterchangeArchetype archetype,
            IReadOnlyList<HighwayLeg> legs,
            IReadOnlyList<PlannedNetCourse> courses,
            float minClearance = 7.5f,
            float maxGrade = 0.20f,
            float minRadius = 18.0f)
        {
            var sw = Stopwatch.StartNew();
            var verdict = new AuditVerdict();
            verdict.Metrics.TotalCourses = courses != null ? courses.Count : 0;

            if (courses == null || courses.Count == 0)
            {
                verdict.AddIssue(AuditSeverity.Error, AuditCategory.CurveDegeneracy, "No courses planned in interchange.");
                sw.Stop();
                verdict.Metrics.AuditTimeMs = sw.Elapsed.TotalMilliseconds;
                return verdict;
            }

            // 1. Calculate basic geometry metrics & verify single curve degeneracy
            float totalLen = 0f;
            float maxGradeFound = 0f;
            float minRadiusFound = float.MaxValue;

            for (int i = 0; i < courses.Count; i++)
            {
                var c = courses[i];
                float len = CubicBezier3D.Length(c.Curve);
                totalLen += len;

                if (len < 0.5f)
                {
                    verdict.AddIssue(AuditSeverity.Error, AuditCategory.CurveDegeneracy,
                        $"Course '{c.Id}' has near-zero length ({len:F2} m).", c.Curve.a, c.Id);
                }

                // Check grade
                float dxz = math.distance(c.Curve.a.xz, c.Curve.d.xz);
                if (dxz > 1.0f)
                {
                    float dy = math.abs(c.Curve.d.y - c.Curve.a.y);
                    float grade = dy / dxz;
                    if (grade > maxGradeFound) maxGradeFound = grade;

                    if (grade > maxGrade)
                    {
                        verdict.AddIssue(AuditSeverity.Error, AuditCategory.MaximumGrade,
                            $"Course '{c.Id}' grade {grade * 100f:F1}% exceeds maximum allowed {maxGrade * 100f:F1}%.",
                            c.Curve.a, c.Id);
                    }
                    else if (grade > 0.12f)
                    {
                        verdict.AddIssue(AuditSeverity.Warning, AuditCategory.MaximumGrade,
                            $"Course '{c.Id}' grade {grade * 100f:F1}% is steep (>12%).", c.Curve.a, c.Id);
                    }
                }

                // Check curvature / minimum radius
                for (int s = 1; s <= 5; s++)
                {
                    float t = s / 6f;
                    float kappa = CubicBezier3D.CurvatureXZ(c.Curve, t);
                    if (kappa > 1e-4f)
                    {
                        float rad = 1f / kappa;
                        if (rad < minRadiusFound) minRadiusFound = rad;

                        if (rad < minRadius)
                        {
                            verdict.AddIssue(AuditSeverity.Error, AuditCategory.MinimumTurningRadius,
                                $"Course '{c.Id}' has sharp turning radius {rad:F1} m at t={t:F2} (limit: {minRadius:F1} m).",
                                CubicBezier3D.Position(c.Curve, t), c.Id);
                            break;
                        }
                    }
                }
            }

            verdict.Metrics.TotalLengthMeters = totalLen;
            verdict.Metrics.MaxGradePercent = maxGradeFound * 100f;
            verdict.Metrics.MinTurningRadiusMeters = minRadiusFound == float.MaxValue ? 0f : minRadiusFound;

            // 2. Vertical clearance check at XZ crossings
            VerifyVerticalClearance(courses, verdict, minClearance);

            // 3. Pillar clearance check
            VerifyPillarClearance(courses, verdict);

            // 4. G1 tangent continuity check at junctions
            VerifyG1Continuity(courses, verdict);

            // 5. Reachability check
            if (legs != null && legs.Count >= 2)
            {
                VerifyReachability(archetype, legs, courses, verdict);
            }

            sw.Stop();
            verdict.Metrics.AuditTimeMs = sw.Elapsed.TotalMilliseconds;
            return verdict;
        }

        private static void VerifyVerticalClearance(
            IReadOnlyList<PlannedNetCourse> courses,
            AuditVerdict verdict,
            float minClearance)
        {
            int crossings = 0;
            float minClearanceFound = float.MaxValue;

            for (int i = 0; i < courses.Count; i++)
            {
                var c1 = courses[i];
                for (int j = i + 1; j < courses.Count; j++)
                {
                    var c2 = courses[j];

                    // Ignore courses that share endpoints in XZ (junctions, splits, joins)
                    if (AreConnectedEndpointsXZ(c1, c2)) continue;

                    // Ignore sub-spans of the same parent corridor
                    if (AreSubSpansOfSameCorridor(c1.Id, c2.Id)) continue;

                    // Check bounding box overlap in XZ first
                    if (!BoundingBoxesOverlapXZ(c1.Curve, c2.Curve)) continue;

                    // Sample interior parameter ranges only to avoid gore/diverge boundaries
                    const int samples = 10;
                    for (int s1 = 1; s1 < samples; s1++)
                    {
                        float t1 = (float)s1 / samples;
                        float3 p1 = CubicBezier3D.Position(c1.Curve, t1);

                        // If p1 is close to an endpoint of c2, it's a ramp branch area
                        if (math.distance(p1.xz, c2.Curve.a.xz) < 8.0f || math.distance(p1.xz, c2.Curve.d.xz) < 8.0f)
                            continue;

                        for (int s2 = 1; s2 < samples; s2++)
                        {
                            float t2 = (float)s2 / samples;
                            float3 p2 = CubicBezier3D.Position(c2.Curve, t2);

                            if (math.distance(p2.xz, c1.Curve.a.xz) < 8.0f || math.distance(p2.xz, c1.Curve.d.xz) < 8.0f)
                                continue;

                            float distXZ = math.distance(p1.xz, p2.xz);
                            float combinedHalfWidth = 5.0f; // Crossing road deck envelope

                            if (distXZ < combinedHalfWidth)
                            {
                                float deltaY = math.abs(p1.y - p2.y);
                                if (deltaY < minClearanceFound) minClearanceFound = deltaY;
                                crossings++;

                                if (deltaY < minClearance)
                                {
                                    verdict.AddIssue(AuditSeverity.Error, AuditCategory.VerticalClearance,
                                        $"Insufficient 3D clearance ({deltaY:F2} m < {minClearance:F1} m) between '{c1.Id}' and '{c2.Id}'.",
                                        (p1 + p2) * 0.5f, c1.Id, c2.Id);
                                }
                                break;
                            }
                        }
                    }
                }
            }

            verdict.Metrics.CrossingCount = crossings;
            verdict.Metrics.MinClearanceMeters = minClearanceFound == float.MaxValue ? 0f : minClearanceFound;
        }

        private static void VerifyPillarClearance(
            IReadOnlyList<PlannedNetCourse> courses,
            AuditVerdict verdict,
            float pillarBuffer = 3.5f)
        {
            int pillars = 0;
            for (int i = 0; i < courses.Count; i++)
            {
                var c = courses[i];
                if (c.ForceElevated || c.StackLevel > 0 || c.Curve.a.y > 4.5f || c.Curve.d.y > 4.5f)
                {
                    float3 mid = CubicBezier3D.Position(c.Curve, 0.5f);
                    if (mid.y > 5.0f && CubicBezier3D.Length(c.Curve) > 40f)
                    {
                        pillars++;
                        for (int j = 0; j < courses.Count; j++)
                        {
                            if (i == j) continue;
                            var lower = courses[j];
                            if (AreSubSpansOfSameCorridor(c.Id, lower.Id)) continue;

                            if (lower.Curve.a.y < mid.y - 4.0f)
                            {
                                float dist = math.distance(mid.xz, CubicBezier3D.Position(lower.Curve, 0.5f).xz);
                                if (dist < pillarBuffer)
                                {
                                    verdict.AddIssue(AuditSeverity.Error, AuditCategory.PillarEncroachment,
                                        $"Pillar support on '{c.Id}' encroaches into carriageway '{lower.Id}'.",
                                        mid, c.Id, lower.Id);
                                }
                            }
                        }
                    }
                }
            }
            verdict.Metrics.PillarCount = pillars;
        }

        private static void VerifyG1Continuity(
            IReadOnlyList<PlannedNetCourse> courses,
            AuditVerdict verdict,
            float maxAngleDeg = 20f)
        {
            float maxCos = math.cos(math.radians(maxAngleDeg));

            for (int i = 0; i < courses.Count; i++)
            {
                var c1 = courses[i];
                for (int j = i + 1; j < courses.Count; j++)
                {
                    var c2 = courses[j];

                    if (math.distance(c1.Curve.d, c2.Curve.a) < 0.2f)
                    {
                        float3 t1 = CubicBezier3D.Tangent(c1.Curve, 1.0f);
                        float3 t2 = CubicBezier3D.Tangent(c2.Curve, 0.0f);
                        if (math.lengthsq(t1) > 1e-4f && math.lengthsq(t2) > 1e-4f)
                        {
                            float dot = math.dot(math.normalize(t1), math.normalize(t2));
                            if (dot < maxCos && dot > -0.9f)
                            {
                                float angleDeg = math.degrees(math.acos(math.clamp(dot, -1f, 1f)));
                                verdict.AddIssue(AuditSeverity.Warning, AuditCategory.TangentContinuity,
                                    $"Kink angle of {angleDeg:F1}° at join between '{c1.Id}' and '{c2.Id}'.",
                                    c1.Curve.d, c1.Id, c2.Id);
                            }
                        }
                    }
                }
            }
        }

        private static void VerifyReachability(
            InterchangeArchetype archetype,
            IReadOnlyList<HighwayLeg> legs,
            IReadOnlyList<PlannedNetCourse> courses,
            AuditVerdict verdict)
        {
            for (int l = 0; l < legs.Count; l++)
            {
                var leg = legs[l];
                if (leg.Inbound == null || leg.Outbound == null) continue;

                bool hasEntry = false;
                bool hasExit = false;

                for (int i = 0; i < courses.Count; i++)
                {
                    if (math.distance(courses[i].Curve.a.xz, leg.Inbound.PortalPosition.xz) < 3.0f) hasEntry = true;
                    if (math.distance(courses[i].Curve.d.xz, leg.Outbound.PortalPosition.xz) < 3.0f) hasExit = true;
                }

                if (!hasEntry)
                {
                    verdict.AddIssue(AuditSeverity.Error, AuditCategory.TopologicalReachability,
                        $"Leg {leg.LegIndex} has no inbound entry course connecting to portal.", leg.Inbound.PortalPosition);
                }
                if (!hasExit)
                {
                    verdict.AddIssue(AuditSeverity.Error, AuditCategory.TopologicalReachability,
                        $"Leg {leg.LegIndex} has no outbound exit course connecting to portal.", leg.Outbound.PortalPosition);
                }
            }
        }

        private static bool AreConnectedEndpointsXZ(PlannedNetCourse c1, PlannedNetCourse c2)
        {
            const float threshold = 1.0f;
            return math.distance(c1.Curve.a.xz, c2.Curve.a.xz) < threshold ||
                   math.distance(c1.Curve.a.xz, c2.Curve.d.xz) < threshold ||
                   math.distance(c1.Curve.d.xz, c2.Curve.a.xz) < threshold ||
                   math.distance(c1.Curve.d.xz, c2.Curve.d.xz) < threshold;
        }

        private static bool AreSubSpansOfSameCorridor(string id1, string id2)
        {
            if (string.IsNullOrEmpty(id1) || string.IsNullOrEmpty(id2)) return false;
            string root1 = GetRootCorridorId(id1);
            string root2 = GetRootCorridorId(id2);
            return root1.Equals(root2, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetRootCorridorId(string id)
        {
            int idx = id.IndexOf("_sub_", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0) id = id.Substring(0, idx);

            idx = id.IndexOf("_span_", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0) id = id.Substring(0, idx);

            // Strip trailing _\d+ (e.g. trumpet_loop_0 -> trumpet_loop)
            int lastUnderscore = id.LastIndexOf('_');
            if (lastUnderscore > 0 && int.TryParse(id.Substring(lastUnderscore + 1), out _))
            {
                id = id.Substring(0, lastUnderscore);
            }

            return id;
        }

        private static bool BoundingBoxesOverlapXZ(Bezier4x3 b1, Bezier4x3 b2)
        {
            float minX1 = math.min(math.min(b1.a.x, b1.b.x), math.min(b1.c.x, b1.d.x));
            float maxX1 = math.max(math.max(b1.a.x, b1.b.x), math.max(b1.c.x, b1.d.x));
            float minZ1 = math.min(math.min(b1.a.z, b1.b.z), math.min(b1.c.z, b1.d.z));
            float maxZ1 = math.max(math.max(b1.a.z, b1.b.z), math.max(b1.c.z, b1.d.z));

            float minX2 = math.min(math.min(b2.a.x, b2.b.x), math.min(b2.c.x, b2.d.x));
            float maxX2 = math.max(math.max(b2.a.x, b2.b.x), math.max(b2.c.x, b2.d.x));
            float minZ2 = math.min(math.min(b2.a.z, b2.b.z), math.min(b2.c.z, b2.d.z));
            float maxZ2 = math.max(math.max(b2.a.z, b2.b.z), math.max(b2.c.z, b2.d.z));

            return !(maxX1 < minX2 || minX1 > maxX2 || maxZ1 < minZ2 || minZ1 > maxZ2);
        }
    }
}
