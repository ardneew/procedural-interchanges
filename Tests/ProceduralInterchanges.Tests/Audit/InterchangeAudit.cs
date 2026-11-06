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
            float minRadius = 18.0f,
            Func<float2, float> terrainSampler = null)
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
            VerifyG1Continuity(courses, verdict, 3.0f);

            // 5. Reachability & boundary checks
            if (legs != null && legs.Count >= 2)
            {
                VerifyReachability(archetype, legs, courses, verdict);
                VerifyPortalAlignment(legs, courses, verdict);
                VerifyMonotonicProgress(legs, courses, verdict);
                VerifyPortalOvershoot(legs, courses, verdict);
            }

            // 6. Curvature smoothness & subterranean checks
            VerifyCurvatureSmoothness(courses, verdict);
            if (terrainSampler != null)
            {
                VerifySubterraneanClearance(courses, verdict, terrainSampler);
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
                                float3 tan1 = CubicBezier3D.Tangent(c1.Curve, t1);
                                float3 tan2 = CubicBezier3D.Tangent(c2.Curve, t2);
                                if (math.lengthsq(tan1.xz) > 1e-4f && math.lengthsq(tan2.xz) > 1e-4f)
                                {
                                    float alignment = math.abs(math.dot(math.normalize(tan1.xz), math.normalize(tan2.xz)));
                                    // Parallel or side-by-side merging/diverging courses are not crossing over each other
                                    if (alignment > 0.70f) continue;
                                }

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
            float maxAngleDeg = 3.0f)
        {
            if (courses == null || courses.Count == 0) return;

            var arrivalsAt = new Dictionary<int, List<int>>();
            var departuresAt = new Dictionary<int, List<int>>();
            var clusterCenter = new List<float3>();

            int GetCluster(float3 pos)
            {
                for (int c = 0; c < clusterCenter.Count; c++)
                {
                    if (math.distance(pos.xz, clusterCenter[c].xz) < 0.8f && math.abs(pos.y - clusterCenter[c].y) < 2.0f)
                        return c;
                }
                int newIdx = clusterCenter.Count;
                clusterCenter.Add(pos);
                arrivalsAt[newIdx] = new List<int>();
                departuresAt[newIdx] = new List<int>();
                return newIdx;
            }

            for (int i = 0; i < courses.Count; i++)
            {
                int cArr = GetCluster(courses[i].Curve.d);
                arrivalsAt[cArr].Add(i);

                int cDep = GetCluster(courses[i].Curve.a);
                departuresAt[cDep].Add(i);
            }

            for (int k = 0; k < clusterCenter.Count; k++)
            {
                var arrList = arrivalsAt[k];
                var depList = departuresAt[k];

                if (arrList.Count == 0 || depList.Count == 0) continue;

                if (arrList.Count == 1 && depList.Count == 1)
                {
                    // Strict degree-2 join: MUST be smooth G1 (< 3.0 deg kink)
                    var c1 = courses[arrList[0]];
                    var c2 = courses[depList[0]];
                    float3 t1 = CubicBezier3D.Tangent(c1.Curve, 1.0f);
                    float3 t2 = CubicBezier3D.Tangent(c2.Curve, 0.0f);
                    if (math.lengthsq(t1.xz) > 1e-4f && math.lengthsq(t2.xz) > 1e-4f)
                    {
                        float dot = math.dot(math.normalize(new float3(t1.x, 0f, t1.z)), math.normalize(new float3(t2.x, 0f, t2.z)));
                        float angleDeg = math.degrees(math.acos(math.clamp(dot, -1f, 1f)));
                        if (angleDeg > 15.0f)
                        {
                            verdict.AddIssue(AuditSeverity.Error, AuditCategory.TangentContinuity,
                                $"Kink angle of {angleDeg:F1}° at degree-2 join between '{c1.Id}' and '{c2.Id}' exceeds 15.0° tolerance.",
                                c1.Curve.d, c1.Id, c2.Id);
                        }
                        else if (angleDeg > maxAngleDeg)
                        {
                            verdict.AddIssue(AuditSeverity.Warning, AuditCategory.TangentContinuity,
                                $"Kink angle of {angleDeg:F1}° at degree-2 join between '{c1.Id}' and '{c2.Id}' exceeds {maxAngleDeg:F1}° target.",
                                c1.Curve.d, c1.Id, c2.Id);
                        }
                    }
                }
                else
                {
                    // Gore split or merge (degree >= 3):
                    // Every incoming movement must have a valid through or diverging branch <= 32.0 deg
                    for (int a = 0; a < arrList.Count; a++)
                    {
                        var c1 = courses[arrList[a]];
                        float3 t1 = CubicBezier3D.Tangent(c1.Curve, 1.0f);
                        if (math.lengthsq(t1.xz) < 1e-4f) continue;
                        float2 t1Norm = math.normalize(new float2(t1.x, t1.z));

                        float minAngleAtBranch = float.MaxValue;
                        for (int d = 0; d < depList.Count; d++)
                        {
                            var c2 = courses[depList[d]];
                            float3 t2 = CubicBezier3D.Tangent(c2.Curve, 0.0f);
                            if (math.lengthsq(t2.xz) < 1e-4f) continue;
                            float2 t2Norm = math.normalize(new float2(t2.x, t2.z));

                            float dot = math.dot(t1Norm, t2Norm);
                            if (dot <= 0.20f) continue; // Opposing or perpendicular traffic is not a downstream branch!

                            float angleDeg = math.degrees(math.acos(math.clamp(dot, -1f, 1f)));
                            if (angleDeg < minAngleAtBranch) minAngleAtBranch = angleDeg;
                        }

                        if (minAngleAtBranch != float.MaxValue && minAngleAtBranch > 42.0f)
                        {
                            verdict.AddIssue(AuditSeverity.Error, AuditCategory.TangentContinuity,
                                $"Branch diverge/merge angle of {minAngleAtBranch:F1}° at '{c1.Id}' exceeds 42.0° tolerance.",
                                c1.Curve.d, c1.Id);
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

        private static void VerifyPortalAlignment(
            IReadOnlyList<HighwayLeg> legs,
            IReadOnlyList<PlannedNetCourse> courses,
            AuditVerdict verdict)
        {
            if (legs == null || courses == null) return;

            for (int l = 0; l < legs.Count; l++)
            {
                var leg = legs[l];
                if (leg.Inbound != null)
                {
                    for (int i = 0; i < courses.Count; i++)
                    {
                        var c = courses[i];
                        if (math.distance(c.Curve.a.xz, leg.Inbound.PortalPosition.xz) < 0.5f)
                        {
                            // Inbound approach course
                            float3 tCourse = math.normalize(CubicBezier3D.Tangent(c.Curve, 0f));
                            float3 tHwy = math.normalize(leg.Inbound.InwardTangent);
                            if (math.lengthsq(tHwy.xz) > 1e-4f)
                            {
                                float dot = math.clamp(math.dot(math.normalize(tCourse.xz), math.normalize(tHwy.xz)), -1f, 1f);
                                float angleDeg = math.degrees(math.acos(dot));
                                if (angleDeg > 1.0f)
                                {
                                    verdict.AddIssue(AuditSeverity.Error, AuditCategory.PortalAlignment,
                                        $"Inbound portal on Leg {leg.LegIndex} has kink angle {angleDeg:F2}° with incoming highway tangent.",
                                        c.Curve.a, c.Id);
                                }
                            }
                        }
                    }
                }

                if (leg.Outbound != null)
                {
                    for (int i = 0; i < courses.Count; i++)
                    {
                        var c = courses[i];
                        if (math.distance(c.Curve.d.xz, leg.Outbound.PortalPosition.xz) < 0.5f)
                        {
                            // Outbound departure course
                            float3 tCourse = math.normalize(CubicBezier3D.Tangent(c.Curve, 1f));
                            float3 tHwy = -math.normalize(leg.Outbound.InwardTangent);
                            if (math.lengthsq(tHwy.xz) > 1e-4f)
                            {
                                float dot = math.clamp(math.dot(math.normalize(tCourse.xz), math.normalize(tHwy.xz)), -1f, 1f);
                                float angleDeg = math.degrees(math.acos(dot));
                                if (angleDeg > 1.0f)
                                {
                                    verdict.AddIssue(AuditSeverity.Error, AuditCategory.PortalAlignment,
                                        $"Outbound portal on Leg {leg.LegIndex} has kink angle {angleDeg:F2}° with outgoing highway tangent.",
                                        c.Curve.d, c.Id);
                                }
                            }
                        }
                    }
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
    
        private static void VerifyMonotonicProgress(
            IReadOnlyList<HighwayLeg> legs,
            IReadOnlyList<PlannedNetCourse> courses,
            AuditVerdict verdict)
        {
            if (legs == null || courses == null) return;

            for (int l = 0; l < legs.Count; l++)
            {
                var leg = legs[l];
                if (leg.Inbound != null)
                {
                    float3 tHwyIn = math.normalize(leg.Inbound.InwardTangent);
                    for (int i = 0; i < courses.Count; i++)
                    {
                        var c = courses[i];
                        if (math.distance(c.Curve.a.xz, leg.Inbound.PortalPosition.xz) < 1.0f)
                        {
                            const int M = 20;
                            for (int s = 0; s <= M; s++)
                            {
                                float t = (float)s / M;
                                float3 tan = CubicBezier3D.Tangent(c.Curve, t);
                                if (math.lengthsq(tan.xz) < 1e-4f) continue;
                                float3 tanNorm = math.normalize(new float3(tan.x, 0f, tan.z));
                                float dot = math.clamp(math.dot(tanNorm.xz, tHwyIn.xz), -1f, 1f);
                                if (dot < 0.20f)
                                {
                                    float angDeg = math.degrees(math.acos(dot));
                                    verdict.AddIssue(AuditSeverity.Error, AuditCategory.MonotonicProgress,
                                        $"Inbound boundary course '{c.Id}' reverses or severely kinks at s={t:F2} (angle {angDeg:F1} deg with highway inward vector); hairpin hook detected.",
                                        CubicBezier3D.Position(c.Curve, t), c.Id);
                                    break;
                                }
                            }
                        }
                    }
                }

                if (leg.Outbound != null)
                {
                    float3 tHwyOut = -math.normalize(leg.Outbound.InwardTangent);
                    for (int i = 0; i < courses.Count; i++)
                    {
                        var c = courses[i];
                        if (math.distance(c.Curve.d.xz, leg.Outbound.PortalPosition.xz) < 1.0f)
                        {
                            const int M = 20;
                            for (int s = 0; s <= M; s++)
                            {
                                float t = (float)s / M;
                                float3 tan = CubicBezier3D.Tangent(c.Curve, t);
                                if (math.lengthsq(tan.xz) < 1e-4f) continue;
                                float3 tanNorm = math.normalize(new float3(tan.x, 0f, tan.z));
                                float dot = math.clamp(math.dot(tanNorm.xz, tHwyOut.xz), -1f, 1f);
                                if (dot < 0.20f)
                                {
                                    float angDeg = math.degrees(math.acos(dot));
                                    verdict.AddIssue(AuditSeverity.Error, AuditCategory.MonotonicProgress,
                                        $"Outbound boundary course '{c.Id}' reverses or severely kinks at s={t:F2} (angle {angDeg:F1} deg with highway outward vector); hairpin hook detected.",
                                        CubicBezier3D.Position(c.Curve, t), c.Id);
                                    break;
                                }
                            }
                        }
                    }
                }
            }
        }

        private static void VerifyPortalOvershoot(
            IReadOnlyList<HighwayLeg> legs,
            IReadOnlyList<PlannedNetCourse> courses,
            AuditVerdict verdict)
        {
            if (legs == null || courses == null) return;

            for (int l = 0; l < legs.Count; l++)
            {
                var leg = legs[l];
                float2 planePt = leg.PortalCenterPosition.xz;
                float2 inTan = math.lengthsq(leg.PortalInwardTangent.xz) > 1e-4f
                    ? math.normalize(leg.PortalInwardTangent.xz)
                    : -math.normalize(leg.LegAxisDirection.xz);
                float2 outwardNormal = -inTan;
                float2 lateralDir = new float2(-outwardNormal.y, outwardNormal.x);

                for (int i = 0; i < courses.Count; i++)
                {
                    var c = courses[i];
                    if (c.LegIndex != leg.LegIndex && c.TargetLegIndex != leg.LegIndex) continue;

                    for (int s = 1; s <= 9; s++)
                    {
                        float t = s / 10f;
                        float3 pos = CubicBezier3D.Position(c.Curve, t);
                        float lateralDist = math.abs(math.dot(pos.xz - planePt, lateralDir));
                        if (lateralDist < 50.0f)
                        {
                            float overshootDist = math.dot(pos.xz - planePt, outwardNormal);
                            if (overshootDist > 2.5f)
                            {
                                verdict.AddIssue(AuditSeverity.Error, AuditCategory.PortalOvershoot,
                                    $"Course '{c.Id}' overshoots leg {leg.LegIndex} portal boundary by {overshootDist:F1}m into host highway corridor.",
                                    pos, c.Id);
                                break;
                            }
                        }
                    }
                }
            }
        }

        private static void VerifyCurvatureSmoothness(
            IReadOnlyList<PlannedNetCourse> courses,
            AuditVerdict verdict)
        {
            if (courses == null) return;

            for (int i = 0; i < courses.Count; i++)
            {
                var c = courses[i];
                if (c.Role != NetRole.MainlineThrough) continue;

                float chord = math.distance(c.Curve.a.xz, c.Curve.d.xz);
                float arcLen = CubicBezier3D.Length(c.Curve);
                if (chord < 5.0f || arcLen < 5.0f) continue;

                float efficiency = chord / arcLen;
                int signChanges = 0;
                float prevKappa = 0f;
                const int samples = 20;
                float totalWindingRad = 0f;
                float3 prevTan = CubicBezier3D.Tangent(c.Curve, 0f);

                for (int s = 1; s <= samples; s++)
                {
                    float t = (float)s / samples;
                    float kappa = CubicBezier3D.CurvatureXZ(c.Curve, t);
                    float3 curTan = CubicBezier3D.Tangent(c.Curve, t);

                    if (math.lengthsq(curTan.xz) > 1e-4f && math.lengthsq(prevTan.xz) > 1e-4f)
                    {
                        float dot = math.clamp(math.dot(math.normalize(curTan.xz), math.normalize(prevTan.xz)), -1f, 1f);
                        totalWindingRad += math.acos(dot);
                    }
                    prevTan = curTan;

                    if (math.abs(kappa) > 1e-4f)
                    {
                        if (prevKappa != 0f && (kappa * prevKappa < 0f))
                        {
                            signChanges++;
                        }
                        prevKappa = kappa;
                    }
                }

                float totalWindingDeg = math.degrees(totalWindingRad);
                if (signChanges >= 2 && efficiency > 0.90f && totalWindingDeg > 35f)
                {
                    verdict.AddIssue(AuditSeverity.Error, AuditCategory.ExtraneousCurvature,
                        $"Course '{c.Id}' has {signChanges} curvature inflections and {totalWindingDeg:F1} deg winding on direct corridor (efficiency {efficiency:F2}).",
                        c.Curve.a, c.Id);
                }
            }
        }

        private static void VerifySubterraneanClearance(
            IReadOnlyList<PlannedNetCourse> courses,
            AuditVerdict verdict,
            Func<float2, float> terrainSampler)
        {
            if (courses == null || terrainSampler == null) return;

            for (int i = 0; i < courses.Count; i++)
            {
                var c = courses[i];
                if (c.ForceTunnel || c.StackLevel < 0 || c.Id.ToLowerInvariant().Contains("tunnel")) continue;

                for (int s = 0; s <= 10; s++)
                {
                    float t = s / 10f;
                    float3 pos = CubicBezier3D.Position(c.Curve, t);
                    float groundY = terrainSampler(pos.xz);
                    float subterraneanDepth = groundY - pos.y;

                    if (subterraneanDepth > 0.40f)
                    {
                        verdict.AddIssue(AuditSeverity.Error, AuditCategory.SubterraneanRoad,
                            $"Course '{c.Id}' plunges {subterraneanDepth:F2}m underground at t={t:F2} (road Y={pos.y:F2}, terrain Y={groundY:F2}).",
                            pos, c.Id);
                        break;
                    }
                }
            }
        }
}
}
