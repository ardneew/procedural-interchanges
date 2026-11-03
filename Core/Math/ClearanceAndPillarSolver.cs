using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using ProceduralInterchanges.Core.Models;
using Unity.Mathematics;

namespace ProceduralInterchanges.Core.Math
{
    public static class ClearanceAndPillarSolver
    {
        public struct CrossingPoint
        {
            public PlannedNetCourse LowerCourse;
            public PlannedNetCourse UpperCourse;
            public float LowerT;
            public float UpperT;
            public float3 Point;
            public float RequiredDeckHeight;
        }

        private class SharedJunction
        {
            public float2 XZ;
            public float CanonicalY;
            public bool HasFixedY;
            public readonly List<(int courseIndex, bool isStart)> IncidentEndpoints = new List<(int, bool)>();
        }

        public static void SolveElevationsAndSubdivide(
            List<PlannedNetCourse> courses,
            Func<float2, float> sampleTerrainHeight,
            InterchangeConfig config)
        {
            if (courses == null || courses.Count == 0) return;

            float effectiveClearance = math.max(8.0f, config.MinBridgeClearanceMeters);

            // Step 1: Cluster course endpoints into shared topological junctions
            var junctions = new List<SharedJunction>();
            for (int i = 0; i < courses.Count; i++)
            {
                var c = courses[i];
                AddEndpointToJunctions(junctions, c.Curve.a.xz, i, true);
                AddEndpointToJunctions(junctions, c.Curve.d.xz, i, false);
            }

            // Determine canonical 3D elevation for each shared junction
            for (int j = 0; j < junctions.Count; j++)
            {
                var junc = junctions[j];

                // 1. Check if any incident endpoint attaches to an existing network road (portal cut elevation)
                bool pinnedToPortal = false;
                for (int e = 0; e < junc.IncidentEndpoints.Count; e++)
                {
                    var (cIdx, isStart) = junc.IncidentEndpoints[e];
                    var crs = courses[cIdx];
                    var attachEnt = isStart ? crs.StartAttachEntity : crs.EndAttachEntity;
                    if (attachEnt != Unity.Entities.Entity.Null)
                    {
                        junc.CanonicalY = isStart ? crs.Curve.a.y : crs.Curve.d.y;
                        junc.HasFixedY = true;
                        pinnedToPortal = true;
                        break;
                    }
                }
                if (pinnedToPortal) continue;

                // 2. Check through corridors (MainlineThrough, CrossStreet, RoundaboutRing, CollectorDistributor)
                // The junction elevation is determined by the through corridor passing through it, NOT by diverging/merging flyover ramps.
                bool hasThroughCorridor = false;
                int throughLevel = 0;
                bool throughForceElevated = false;

                for (int e = 0; e < junc.IncidentEndpoints.Count; e++)
                {
                    var (cIdx, _) = junc.IncidentEndpoints[e];
                    var crs = courses[cIdx];
                    if (crs.Role == NetRole.MainlineThrough || crs.Role == NetRole.CrossStreet ||
                        crs.Role == NetRole.RoundaboutRing || crs.Role == NetRole.CollectorDistributor)
                    {
                        if (!hasThroughCorridor)
                        {
                            hasThroughCorridor = true;
                            throughLevel = crs.StackLevel;
                            throughForceElevated = crs.ForceElevated;
                        }
                        else
                        {
                            // If multiple through corridors meet, select the primary/base level
                            throughLevel = math.min(throughLevel, crs.StackLevel);
                            throughForceElevated = throughForceElevated || crs.ForceElevated;
                        }
                    }
                }

                if (hasThroughCorridor)
                {
                    if (throughLevel == 0 && !throughForceElevated)
                    {
                        junc.CanonicalY = sampleTerrainHeight(junc.XZ);
                    }
                    else
                    {
                        junc.CanonicalY = sampleTerrainHeight(junc.XZ) + effectiveClearance * math.max(1, throughLevel);
                    }
                    junc.HasFixedY = true;
                    continue;
                }

                // 3. Pure ramp-to-ramp interior junction (e.g. Turbine waypoint or multi-span flyover)
                int maxStack = 0;
                for (int e = 0; e < junc.IncidentEndpoints.Count; e++)
                {
                    var (cIdx, _) = junc.IncidentEndpoints[e];
                    maxStack = math.max(maxStack, courses[cIdx].StackLevel);
                }

                // If this is an interior node along a ramp chain (degree 2 and both courses are ramps),
                // do not pin to full effectiveClearance * maxStack now; leave HasFixedY = false so SolveConnectedRampChains can solve smooth Y(s).
                bool isRampChainNode = junc.IncidentEndpoints.Count == 2;
                if (isRampChainNode)
                {
                    for (int e = 0; e < junc.IncidentEndpoints.Count; e++)
                    {
                        var role = courses[junc.IncidentEndpoints[e].Item1].Role;
                        if (!IsRampRole(role))
                        {
                            isRampChainNode = false;
                            break;
                        }
                    }
                }

                if (isRampChainNode)
                {
                    junc.CanonicalY = sampleTerrainHeight(junc.XZ);
                    junc.HasFixedY = false;
                }
                else if (maxStack > 0)
                {
                    junc.CanonicalY = sampleTerrainHeight(junc.XZ) + effectiveClearance * maxStack;
                    junc.HasFixedY = true;
                }
                else
                {
                    junc.CanonicalY = sampleTerrainHeight(junc.XZ);
                    junc.HasFixedY = true;
                }
            }

            // Write canonical elevation into all courses incident to each fixed junction
            for (int j = 0; j < junctions.Count; j++)
            {
                var junc = junctions[j];
                if (!junc.HasFixedY) continue;
                for (int e = 0; e < junc.IncidentEndpoints.Count; e++)
                {
                    var (cIdx, isStart) = junc.IncidentEndpoints[e];
                    var crs = courses[cIdx];
                    var curve = crs.Curve;
                    if (isStart)
                    {
                        curve.a.y = junc.CanonicalY;
                    }
                    else
                    {
                        curve.d.y = junc.CanonicalY;
                    }
                    crs.Curve = curve;
                }
            }

            // Step 2: Automatic 2D grade crossing collision resolution
            PromoteCollidingCrossings(courses);

            // Step 3: Fit ground courses to terrain first so ground carriageway 3D heights are settled
            for (int i = 0; i < courses.Count; i++)
            {
                var course = courses[i];
                if (course.StackLevel == 0 && !course.ForceElevated)
                {
                    var subdivided = new List<Bezier4x3>();
                    TopographySolver.FitGroundCourse(
                        course.Curve,
                        sampleTerrainHeight,
                        pinStart: true,
                        pinEnd: true,
                        subdivided);

                    if (subdivided.Count > 0)
                    {
                        var origEndAttach = course.EndAttachEntity;
                        var origEndSplit = course.EndSplitPosition;
                        var origEndIsNode = course.EndAttachIsNode;

                        course.Curve = subdivided[0];
                        if (subdivided.Count > 1)
                        {
                            course.EndAttachEntity = Unity.Entities.Entity.Null;
                            course.EndSplitPosition = 0f;
                            course.EndAttachIsNode = false;
                        }

                        for (int s = 1; s < subdivided.Count; s++)
                        {
                            var extra = course.Clone();
                            extra.Id = $"{course.Id}_sub_{s}";
                            extra.Curve = subdivided[s];
                            extra.StartAttachEntity = Unity.Entities.Entity.Null;
                            extra.StartSplitPosition = 0f;
                            extra.StartAttachIsNode = false;

                            if (s == subdivided.Count - 1)
                            {
                                extra.EndAttachEntity = origEndAttach;
                                extra.EndSplitPosition = origEndSplit;
                                extra.EndAttachIsNode = origEndIsNode;
                            }
                            else
                            {
                                extra.EndAttachEntity = Unity.Entities.Entity.Null;
                                extra.EndSplitPosition = 0f;
                                extra.EndAttachIsNode = false;
                            }

                            courses.Insert(i + s, extra);
                        }
                        i += subdivided.Count - 1;
                    }
                }
            }

            // Step 4: Detect all grade crossings with accurate 3D ground course heights
            var crossings = DetectCrossings(courses, sampleTerrainHeight, config.MinBridgeClearanceMeters);

            // Step 4.5: Solve continuous multi-segment ramp chains (e.g. Trumpet loops, flyovers, slip ramps)
            var solvedRampCourses = new HashSet<int>();
            SolveConnectedRampChains(courses, crossings, sampleTerrainHeight, config, solvedRampCourses);

            // Step 5: Solve vertical elevations for any remaining single elevated courses
            for (int i = 0; i < courses.Count; i++)
            {
                if (solvedRampCourses.Contains(i)) continue;
                var course = courses[i];
                if (course.StackLevel > 0 || course.ForceElevated)
                {
                    SolveElevatedProfile(course, i, crossings, sampleTerrainHeight, config);
                }
            }

            // Step 6: Subdivide elevated bridge spans and shift pillar nodes away from lower carriageways
            SubdivideAndShiftPillars(courses, sampleTerrainHeight, config);

            // Step 7: Compute final telemetry (MaxGradePercent, MinRadiusMeters, MinClearance, Compliance)
            ComputeTelemetryAndCompliance(courses, sampleTerrainHeight, config);
        }

        private static void PromoteCollidingCrossings(List<PlannedNetCourse> courses)
        {
            for (int i = 0; i < courses.Count; i++)
            {
                var cA = courses[i];
                for (int j = i + 1; j < courses.Count; j++)
                {
                    var cB = courses[j];

                    // Check if endpoints are shared (skip legitimate junctions)
                    float dAA = math.distance(cA.Curve.a.xz, cB.Curve.a.xz);
                    float dAD = math.distance(cA.Curve.a.xz, cB.Curve.d.xz);
                    float dDA = math.distance(cA.Curve.d.xz, cB.Curve.a.xz);
                    float dDD = math.distance(cA.Curve.d.xz, cB.Curve.d.xz);
                    if (dAA < 4.0f || dAD < 4.0f || dDA < 4.0f || dDD < 4.0f) continue;

                    if (FindIntersectionXZ(cA.Curve, cB.Curve, out float tA, out float tB, out _))
                    {
                        if (tA > 0.08f && tA < 0.92f && tB > 0.08f && tB < 0.92f)
                        {
                            if (cA.StackLevel == cB.StackLevel)
                            {
                                bool aIsMain = cA.Role == NetRole.MainlineThrough;
                                bool bIsMain = cB.Role == NetRole.MainlineThrough;

                                if (aIsMain && !bIsMain)
                                {
                                    cB.StackLevel = cA.StackLevel + 1;
                                    cB.ForceElevated = true;
                                }
                                else if (!aIsMain && bIsMain)
                                {
                                    cA.StackLevel = cB.StackLevel + 1;
                                    cA.ForceElevated = true;
                                }
                                else
                                {
                                    cB.StackLevel = cA.StackLevel + 1;
                                    cB.ForceElevated = true;
                                }
                            }
                        }
                    }
                }
            }
        }

        private static void AddEndpointToJunctions(List<SharedJunction> junctions, float2 xz, int courseIndex, bool isStart)
        {
            for (int i = 0; i < junctions.Count; i++)
            {
                if (math.distance(junctions[i].XZ, xz) < 0.5f)
                {
                    junctions[i].IncidentEndpoints.Add((courseIndex, isStart));
                    return;
                }
            }

            var newJunction = new SharedJunction
            {
                XZ = xz,
                CanonicalY = 0f,
                HasFixedY = false
            };
            newJunction.IncidentEndpoints.Add((courseIndex, isStart));
            junctions.Add(newJunction);
        }

        private static List<CrossingPoint> DetectCrossings(
            List<PlannedNetCourse> courses,
            Func<float2, float> sampleTerrainHeight,
            float minClearanceMeters)
        {
            var crossings = new List<CrossingPoint>();
            int count = courses.Count;

            for (int i = 0; i < count; i++)
            {
                var cA = courses[i];
                for (int j = i + 1; j < count; j++)
                {
                    var cB = courses[j];
                    if (cA.StackLevel == cB.StackLevel) continue;

                    int lowerIdx = cA.StackLevel < cB.StackLevel ? i : j;
                    int upperIdx = cA.StackLevel < cB.StackLevel ? j : i;
                    var cLower = courses[lowerIdx];
                    var cUpper = courses[upperIdx];

                    // Check if endpoints are shared (skip legitimate junctions)
                    float dAA = math.distance(cLower.Curve.a.xz, cUpper.Curve.a.xz);
                    float dAD = math.distance(cLower.Curve.a.xz, cUpper.Curve.d.xz);
                    float dDA = math.distance(cLower.Curve.d.xz, cUpper.Curve.a.xz);
                    float dDD = math.distance(cLower.Curve.d.xz, cUpper.Curve.d.xz);
                    if (dAA < 4.0f || dAD < 4.0f || dDA < 4.0f || dDD < 4.0f) continue;

                    // Find approximate 2D intersection
                    if (FindIntersectionXZ(cLower.Curve, cUpper.Curve, out float tLower, out float tUpper, out float3 pt))
                    {
                        float lowerY = CubicBezier3D.Position(cLower.Curve, tLower).y;
                        float terrainY = sampleTerrainHeight(pt.xz);
                        int levelDelta = cUpper.StackLevel - cLower.StackLevel;
                        float effectiveClearance = math.max(9.5f, minClearanceMeters);
                        float reqHeight = math.max(lowerY, terrainY) + effectiveClearance * levelDelta;

                        crossings.Add(new CrossingPoint
                        {
                            LowerCourse = cLower,
                            UpperCourse = cUpper,
                            LowerT = tLower,
                            UpperT = tUpper,
                            Point = pt,
                            RequiredDeckHeight = reqHeight
                        });
                    }
                }
            }

            return crossings;
        }

        private static bool FindIntersectionXZ(in Bezier4x3 c1, in Bezier4x3 c2, out float t1, out float t2, out float3 intersectPt)
        {
            t1 = 0.5f;
            t2 = 0.5f;
            intersectPt = float3.zero;

            const int kSamples = 16;
            float bestDistSq = float.MaxValue;
            float bestT1 = 0.5f;
            float bestT2 = 0.5f;

            for (int i = 0; i <= kSamples; i++)
            {
                float sampleT1 = (float)i / kSamples;
                float3 p1 = CubicBezier3D.Position(c1, sampleT1);

                for (int j = 0; j <= kSamples; j++)
                {
                    float sampleT2 = (float)j / kSamples;
                    float3 p2 = CubicBezier3D.Position(c2, sampleT2);

                    float distSq = math.distancesq(p1.xz, p2.xz);
                    if (distSq < bestDistSq)
                    {
                        bestDistSq = distSq;
                        bestT1 = sampleT1;
                        bestT2 = sampleT2;
                    }
                }
            }

            // If segments pass within 12 meters in XZ, consider it a crossing
            if (bestDistSq < 144f)
            {
                t1 = bestT1;
                t2 = bestT2;
                intersectPt = (CubicBezier3D.Position(c1, t1) + CubicBezier3D.Position(c2, t2)) * 0.5f;
                return true;
            }

            return false;
        }

        private static void SolveElevatedProfile(
            PlannedNetCourse course,
            int courseIndex,
            List<CrossingPoint> crossings,
            Func<float2, float> sampleTerrainHeight,
            InterchangeConfig config)
        {
            float effectiveClearance = math.max(9.5f, config.MinBridgeClearanceMeters);
            float baseClearance = effectiveClearance * math.max(1, course.StackLevel);

            // Endpoints are strictly pinned from shared junction analysis
            float yStart = course.Curve.a.y;
            float yEnd = course.Curve.d.y;

            // Check if this course has any crossing requirements
            float maxCrossingReqY = 0f;
            float crossingPeakT = 0.5f;
            bool hasCrossing = false;

            for (int x = 0; x < crossings.Count; x++)
            {
                var crossing = crossings[x];
                if (crossing.UpperCourse == course)
                {
                    hasCrossing = true;
                    if (crossing.RequiredDeckHeight > maxCrossingReqY)
                    {
                        maxCrossingReqY = crossing.RequiredDeckHeight;
                        crossingPeakT = crossing.UpperT;
                    }
                }
            }

            float y1_3 = math.lerp(yStart, yEnd, 1f / 3f);
            float y2_3 = math.lerp(yStart, yEnd, 2f / 3f);

            if (course.Role == NetRole.RoundaboutRing && course.StackLevel > 0)
            {
                y1_3 = sampleTerrainHeight(CubicBezier3D.Position(course.Curve, 1f / 3f).xz) + baseClearance;
                y2_3 = sampleTerrainHeight(CubicBezier3D.Position(course.Curve, 2f / 3f).xz) + baseClearance;
            }
            else if (hasCrossing)
            {
                float targetPeakY = math.max(maxCrossingReqY, math.max(yStart, yEnd) + effectiveClearance);
                if (crossingPeakT <= 0.4f)
                {
                    y1_3 = targetPeakY;
                    y2_3 = math.lerp(targetPeakY, yEnd, 0.5f);
                }
                else if (crossingPeakT >= 0.6f)
                {
                    y1_3 = math.lerp(yStart, targetPeakY, 0.5f);
                    y2_3 = targetPeakY;
                }
                else
                {
                    y1_3 = targetPeakY;
                    y2_3 = targetPeakY;
                }
            }
            else if (course.StackLevel > 0)
            {
                float midTerrain = sampleTerrainHeight(CubicBezier3D.Position(course.Curve, 0.5f).xz);
                float elevatedDeckY = midTerrain + baseClearance;
                y1_3 = math.max(y1_3, elevatedDeckY);
                y2_3 = math.max(y2_3, elevatedDeckY);
            }

            // Exact 2x2 linear system solution for cubic Bézier control points:
            // B(1/3)_y = (8 y0 + 12 by + 6 cy + y3) / 27 = y1_3
            // B(2/3)_y = (y0 + 6 by + 12 cy + 8 y3) / 27 = y2_3
            float r1 = 27f * y1_3 - 8f * yStart - yEnd;
            float r2 = 27f * y2_3 - yStart - 8f * yEnd;
            float cy = (2f * r2 - r1) / 18f;
            float by = (r1 - 6f * cy) / 12f;

            // Enforce maximum steepness (gradient <= 18%, within game hard limit of 20%) when no crossing constraint
            if (!hasCrossing)
            {
                float horizLen = math.max(5f, math.distance(course.Curve.a.xz, course.Curve.d.xz));
                float maxRiseThird = (horizLen / 3f) * 0.18f;
                by = math.clamp(by, yStart - maxRiseThird * 2f, yStart + maxRiseThird * 2f);
                cy = math.clamp(cy, yEnd - maxRiseThird * 2f, yEnd + maxRiseThird * 2f);
            }

            float3 a = new float3(course.Curve.a.x, yStart, course.Curve.a.z);
            float3 b = new float3(course.Curve.b.x, by, course.Curve.b.z);
            float3 c = new float3(course.Curve.c.x, cy, course.Curve.c.z);
            float3 d = new float3(course.Curve.d.x, yEnd, course.Curve.d.z);

            course.Curve = new Bezier4x3(a, b, c, d);
        }

        private static void SubdivideAndShiftPillars(
            List<PlannedNetCourse> courses,
            Func<float2, float> sampleTerrainHeight,
            InterchangeConfig config)
        {
            // Subdivide long elevated courses into pieces of 14m..26m (bridge deck span)
            // ensuring split points don't land within 4m of lower carriageways
            for (int i = 0; i < courses.Count; i++)
            {
                var course = courses[i];
                float len = CubicBezier3D.Length(course.Curve);

                float startH = course.Curve.a.y - sampleTerrainHeight(course.Curve.a.xz);
                float endH = course.Curve.d.y - sampleTerrainHeight(course.Curve.d.xz);
                float midH = CubicBezier3D.Position(course.Curve, 0.5f).y - sampleTerrainHeight(CubicBezier3D.Position(course.Curve, 0.5f).xz);
                bool isElevated = course.StackLevel > 0 || course.ForceElevated || startH > 1.0f || endH > 1.0f || midH > 1.5f;

                // Any elevated span or ramp climbing to an overpass is subdivided into pieces of ~38m
                // so that CS2 places piers at natural viaduct spans (~38m) rather than clustered every 18m
                bool needsSubdivision = (isElevated && len > 42.0f) || (midH > math.max(startH, endH) + 2.0f && len > 38.0f);

                if (needsSubdivision)
                {
                    int pieceCount = (int)math.ceil(len / 38f);
                    if (pieceCount < 2) pieceCount = 2;

                    var subPieces = new List<Bezier4x3>();
                    Bezier4x3 remaining = course.Curve;

                    for (int p = 0; p < pieceCount - 1; p++)
                    {
                        float fractionRemaining = 1f / (pieceCount - p);
                        float candidateT = fractionRemaining;

                        // Check proximity to all lower courses and choose best offset to maximize clearance
                        float bestCandT = candidateT;
                        float bestMinDist = -1f;
                        float[] deltaCandidates = { 0f, -0.08f, 0.08f, -0.15f, 0.15f, -0.22f, 0.22f };

                        for (int dIdx = 0; dIdx < deltaCandidates.Length; dIdx++)
                        {
                            float testT = math.clamp(candidateT + deltaCandidates[dIdx], 0.15f, 0.85f);
                            float3 testPillar = CubicBezier3D.Position(remaining, testT);
                            float minDist = float.MaxValue;

                            for (int j = 0; j < courses.Count; j++)
                            {
                                var other = courses[j];
                                if (other.StackLevel < course.StackLevel)
                                {
                                    float dist = CubicBezier3D.DistanceXZ(other.Curve, testPillar.xz, out _);
                                    if (dist < minDist) minDist = dist;
                                }
                            }

                            if (minDist > bestMinDist)
                            {
                                bestMinDist = minDist;
                                bestCandT = testT;
                            }
                        }

                        candidateT = bestCandT;

                        CubicBezier3D.Split(remaining, candidateT, out var piece, out remaining);
                        subPieces.Add(piece);
                    }
                    subPieces.Add(remaining);

                    var origEndAttach = course.EndAttachEntity;
                    var origEndSplit = course.EndSplitPosition;
                    var origEndIsNode = course.EndAttachIsNode;

                    course.Curve = subPieces[0];
                    course.StartElevation = new float2(math.max(0f, course.Curve.a.y - sampleTerrainHeight(course.Curve.a.xz)));
                    course.EndElevation = new float2(math.max(0f, course.Curve.d.y - sampleTerrainHeight(course.Curve.d.xz)));
                    course.ForceElevated = isElevated;

                    if (subPieces.Count > 1)
                    {
                        course.EndAttachEntity = Unity.Entities.Entity.Null;
                        course.EndSplitPosition = 0f;
                        course.EndAttachIsNode = false;
                    }

                    for (int s = 1; s < subPieces.Count; s++)
                    {
                        var extra = course.Clone();
                        extra.Id = $"{course.Id}_span_{s}";
                        extra.Curve = subPieces[s];
                        extra.StartElevation = new float2(math.max(0f, extra.Curve.a.y - sampleTerrainHeight(extra.Curve.a.xz)));
                        extra.EndElevation = new float2(math.max(0f, extra.Curve.d.y - sampleTerrainHeight(extra.Curve.d.xz)));
                        extra.ForceElevated = isElevated;

                        extra.StartAttachEntity = Unity.Entities.Entity.Null;
                        extra.StartSplitPosition = 0f;
                        extra.StartAttachIsNode = false;

                        if (s == subPieces.Count - 1)
                        {
                            extra.EndAttachEntity = origEndAttach;
                            extra.EndSplitPosition = origEndSplit;
                            extra.EndAttachIsNode = origEndIsNode;
                        }
                        else
                        {
                            extra.EndAttachEntity = Unity.Entities.Entity.Null;
                            extra.EndSplitPosition = 0f;
                            extra.EndAttachIsNode = false;
                        }

                        courses.Insert(i + s, extra);
                    }

                    i += subPieces.Count - 1;
                }
                else
                {
                    course.StartElevation = new float2(math.max(0f, startH));
                    course.EndElevation = new float2(math.max(0f, endH));
                    course.ForceElevated = isElevated;
                }
            }
        }

        private static void SolveConnectedRampChains(
            List<PlannedNetCourse> courses,
            List<CrossingPoint> crossings,
            Func<float2, float> sampleTerrainHeight,
            InterchangeConfig config,
            HashSet<int> solvedRampCourses)
        {
            float effectiveClearance = math.max(9.5f, config.MinBridgeClearanceMeters);
            int count = courses.Count;

            // Map each course to its next connected ramp course
            var nextCourse = new int[count];
            var prevCourse = new int[count];
            for (int i = 0; i < count; i++)
            {
                nextCourse[i] = -1;
                prevCourse[i] = -1;
            }

            for (int i = 0; i < count; i++)
            {
                var cA = courses[i];
                if (!IsRampRole(cA.Role)) continue;

                for (int j = 0; j < count; j++)
                {
                    if (i == j) continue;
                    var cB = courses[j];
                    if (!IsRampRole(cB.Role)) continue;

                    if (math.distance(cA.Curve.d.xz, cB.Curve.a.xz) < 2.0f)
                    {
                        nextCourse[i] = j;
                        prevCourse[j] = i;
                        break;
                    }
                }
            }

            var visited = new bool[count];

            for (int i = 0; i < count; i++)
            {
                if (visited[i] || !IsRampRole(courses[i].Role)) continue;

                // Only start a chain from a head node (no predecessor ramp course)
                if (prevCourse[i] != -1) continue;

                var chain = new List<int>();
                int curr = i;
                while (curr != -1 && !visited[curr])
                {
                    visited[curr] = true;
                    chain.Add(curr);
                    curr = nextCourse[curr];
                }

                if (chain.Count < 2) continue;

                // Compute cumulative arc lengths along the chain
                float totalLength = 0f;
                var segLengths = new float[chain.Count];
                var segStartDist = new float[chain.Count + 1];
                segStartDist[0] = 0f;

                for (int s = 0; s < chain.Count; s++)
                {
                    float l = CubicBezier3D.Length(courses[chain[s]].Curve);
                    segLengths[s] = l;
                    totalLength += l;
                    segStartDist[s + 1] = totalLength;
                }

                if (totalLength < 10f) continue;

                float yStart = courses[chain[0]].Curve.a.y;
                float yEnd = courses[chain[chain.Count - 1]].Curve.d.y;

                // Find all crossings on any segment of this chain
                var chainCrossings = new List<(float s, float reqY)>();
                for (int s = 0; s < chain.Count; s++)
                {
                    int cIdx = chain[s];
                    var crs = courses[cIdx];
                    for (int x = 0; x < crossings.Count; x++)
                    {
                        var cr = crossings[x];
                        if (cr.UpperCourse == crs)
                        {
                            float sCross = segStartDist[s] + cr.UpperT * segLengths[s];
                            chainCrossings.Add((sCross, cr.RequiredDeckHeight));
                        }
                    }
                }

                bool hasCrossings = chainCrossings.Count > 0;
                bool isElevatedChain = hasCrossings;
                for (int s = 0; s < chain.Count; s++)
                {
                    if (courses[chain[s]].StackLevel > 0 || courses[chain[s]].ForceElevated)
                    {
                        isElevatedChain = true;
                        break;
                    }
                }

                if (!isElevatedChain) continue;

                float yPeak;
                float sPlatStart;
                float sPlatEnd;

                if (hasCrossings)
                {
                    yPeak = 0f;
                    float minCrossS = float.MaxValue;
                    float maxCrossS = float.MinValue;
                    for (int c = 0; c < chainCrossings.Count; c++)
                    {
                        if (chainCrossings[c].reqY > yPeak) yPeak = chainCrossings[c].reqY;
                        if (chainCrossings[c].s < minCrossS) minCrossS = chainCrossings[c].s;
                        if (chainCrossings[c].s > maxCrossS) maxCrossS = chainCrossings[c].s;
                    }
                    yPeak = math.max(yPeak, math.max(yStart, yEnd) + effectiveClearance);

                    sPlatStart = math.clamp(minCrossS - 18f, 0.18f * totalLength, 0.78f * totalLength);
                    sPlatEnd = math.clamp(maxCrossS + 18f, sPlatStart + 6f, 0.82f * totalLength);
                }
                else
                {
                    // No direct crossing detected, but marked elevated: smooth hump
                    int midSeg = chain[chain.Count / 2];
                    float midTerrain = sampleTerrainHeight(CubicBezier3D.Position(courses[midSeg].Curve, 0.5f).xz);
                    yPeak = math.max(yStart, yEnd) + effectiveClearance;
                    yPeak = math.max(yPeak, midTerrain + effectiveClearance);

                    sPlatStart = 0.35f * totalLength;
                    sPlatEnd = 0.65f * totalLength;
                }

                // Smooth Hermite profile functions
                float EvalY(float s)
                {
                    if (s <= 0f) return yStart;
                    if (s >= totalLength) return yEnd;
                    if (s < sPlatStart)
                    {
                        float u = s / sPlatStart;
                        float smoothU = u * u * (3f - 2f * u);
                        return math.lerp(yStart, yPeak, smoothU);
                    }
                    if (s <= sPlatEnd)
                    {
                        return yPeak;
                    }
                    else
                    {
                        float u = (s - sPlatEnd) / (totalLength - sPlatEnd);
                        float smoothU = u * u * (3f - 2f * u);
                        return math.lerp(yPeak, yEnd, smoothU);
                    }
                }

                float EvalDerivY(float s)
                {
                    if (s <= 0f || s >= totalLength) return 0f;
                    if (s < sPlatStart)
                    {
                        float u = s / sPlatStart;
                        float dSmooth = 6f * u * (1f - u);
                        return (yPeak - yStart) * (dSmooth / sPlatStart);
                    }
                    if (s <= sPlatEnd)
                    {
                        return 0f;
                    }
                    else
                    {
                        float u = (s - sPlatEnd) / (totalLength - sPlatEnd);
                        float dSmooth = 6f * u * (1f - u);
                        return (yEnd - yPeak) * (dSmooth / (totalLength - sPlatEnd));
                    }
                }

                // Apply to each segment in the chain
                for (int s = 0; s < chain.Count; s++)
                {
                    int cIdx = chain[s];
                    var crs = courses[cIdx];
                    float s0 = segStartDist[s];
                    float s1 = segStartDist[s + 1];
                    float dS = segLengths[s];

                    float aY = EvalY(s0);
                    float dY = EvalY(s1);
                    float dY0 = EvalDerivY(s0);
                    float dY1 = EvalDerivY(s1);

                    float bY = aY + (dS / 3f) * dY0;
                    float cY = dY - (dS / 3f) * dY1;

                    var cv = crs.Curve;
                    cv.a.y = aY;
                    cv.b.y = bY;
                    cv.c.y = cY;
                    cv.d.y = dY;
                    crs.Curve = cv;

                    float midY = (aY + dY) * 0.5f;
                    float terrMid = sampleTerrainHeight(CubicBezier3D.Position(cv, 0.5f).xz);
                    if (midY >= terrMid + 4.5f || aY >= sampleTerrainHeight(cv.a.xz) + 4.5f || dY >= sampleTerrainHeight(cv.d.xz) + 4.5f)
                    {
                        crs.StackLevel = math.max(1, crs.StackLevel);
                        crs.ForceElevated = true;
                    }

                    solvedRampCourses.Add(cIdx);
                }
            }
        }

        private static bool IsRampRole(NetRole role)
        {
            return role == NetRole.LoopRamp || role == NetRole.DirectionalRamp || role == NetRole.CollectorDistributor || role == NetRole.TransitionTaper;
        }

        private static void ComputeTelemetryAndCompliance(
            List<PlannedNetCourse> courses,
            Func<float2, float> sampleTerrainHeight,
            InterchangeConfig config)
        {
            float maxMainlineGrade = config.MaxMainlineGradePercent;
            float maxRampGrade = config.MaxRampGradePercent;

            float minMainlineRadius = math.clamp(config.DesignSpeedKmh * config.DesignSpeedKmh / 90f, 90f, 250f);
            float minRampRadius = 45f;
            float minLoopRadius = 28f;

            for (int i = 0; i < courses.Count; i++)
            {
                var c = courses[i];
                c.LengthMeters = CubicBezier3D.Length(c.Curve);
                c.MinRadiusMeters = CubicBezier3D.MinRadiusXZ(c.Curve);
                c.MaxGradePercent = CubicBezier3D.MaxSlopeGrade(c.Curve);

                float allowedMaxGrade = c.Role == NetRole.MainlineThrough ? maxMainlineGrade : maxRampGrade;
                float reqRadius = c.Role switch
                {
                    NetRole.MainlineThrough => minMainlineRadius,
                    NetRole.LoopRamp => minLoopRadius,
                    _ => minRampRadius
                };

                // Check bridge clearance if elevated
                c.MinClearanceMeters = 99f;
                if (c.StackLevel > 0)
                {
                    for (int j = 0; j < courses.Count; j++)
                    {
                        var lower = courses[j];
                        if (lower.StackLevel < c.StackLevel)
                        {
                            if (FindIntersectionXZ(lower.Curve, c.Curve, out float tL, out float tU, out _))
                            {
                                float yUpper = CubicBezier3D.Position(c.Curve, tU).y;
                                float yLower = CubicBezier3D.Position(lower.Curve, tL).y;
                                float clr = yUpper - yLower;
                                if (clr < c.MinClearanceMeters)
                                {
                                    c.MinClearanceMeters = clr;
                                }
                            }
                        }
                    }
                }

                // Evaluate compliance level
                bool gradeViolation = c.MaxGradePercent > allowedMaxGrade + 1.5f || c.MaxGradePercent > 18.0f;
                bool gradeMarginal = c.MaxGradePercent > allowedMaxGrade && !gradeViolation;

                bool radiusViolation = c.MinRadiusMeters < reqRadius * 0.70f;
                bool radiusMarginal = c.MinRadiusMeters < reqRadius && !radiusViolation;

                bool clearanceViolation = c.StackLevel > 0 && c.MinClearanceMeters < 8.0f;

                if (gradeViolation || radiusViolation || clearanceViolation)
                {
                    c.Compliance = ComplianceLevel.Violation;
                    c.ComplianceMessage = gradeViolation ? $"Slope {c.MaxGradePercent:F1}% exceeds maximum" :
                                          radiusViolation ? $"Radius {c.MinRadiusMeters:F0}m too sharp" :
                                          $"Clearance {c.MinClearanceMeters:F1}m below minimum 8.0m";
                }
                else if (gradeMarginal || radiusMarginal)
                {
                    c.Compliance = ComplianceLevel.Marginal;
                    c.ComplianceMessage = gradeMarginal ? $"Grade {c.MaxGradePercent:F1}% is near limit" :
                                          $"Radius {c.MinRadiusMeters:F0}m is tight";
                }
                else
                {
                    c.Compliance = ComplianceLevel.Optimal;
                    c.ComplianceMessage = "Optimal engineering parameters";
                }
            }
        }
    }
}
