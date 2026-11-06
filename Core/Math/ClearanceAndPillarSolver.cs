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
            public int StackLevel;
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

            float effectiveClearance = math.max(8.8f, config.MinBridgeClearanceMeters + 0.8f);
            float tunnelDepth = 9.0f;

            // Step 0: Promote any colliding same-level crossings before subdivision
            PromoteCollidingCrossings(courses);

            // Step 1: Upfront subdivision of all courses into spans <= 31m (avoids pillar encroachment & double-splitting)
            PreSubdivideAllCourses(courses);

            // Step 2: Cluster course endpoints into shared topological junctions
            var junctions = new List<SharedJunction>();
            for (int i = 0; i < courses.Count; i++)
            {
                var c = courses[i];
                AddEndpointToJunctions(junctions, c.Curve.a.xz, i, true, courses);
                AddEndpointToJunctions(junctions, c.Curve.d.xz, i, false, courses);
            }

            // Step 3: Determine fixed boundary elevations (portals and surface junctions)
            for (int j = 0; j < junctions.Count; j++)
            {
                var junc = junctions[j];
                float terrY = sampleTerrainHeight(junc.XZ);

                // 3a. Host road portal attachment
                bool pinnedToPortal = false;
                for (int e = 0; e < junc.IncidentEndpoints.Count; e++)
                {
                    var (cIdx, isStart) = junc.IncidentEndpoints[e];
                    var crs = courses[cIdx];
                    var attachEnt = isStart ? crs.StartAttachEntity : crs.EndAttachEntity;
                    if (attachEnt != Unity.Entities.Entity.Null)
                    {
                        float rawY = isStart ? crs.Curve.a.y : crs.Curve.d.y;
                        junc.CanonicalY = math.abs(rawY) > 0.01f ? rawY : terrY;
                        junc.HasFixedY = true;
                        pinnedToPortal = true;
                        break;
                    }
                }
                if (pinnedToPortal) continue;

                // 3b. Degree-2 interior span joint between two spans of the same role & stack level
                if (junc.IncidentEndpoints.Count == 2)
                {
                    var (idx0, _) = junc.IncidentEndpoints[0];
                    var (idx1, _) = junc.IncidentEndpoints[1];
                    var c0 = courses[idx0];
                    var c1 = courses[idx1];
                    if (c0.Role == c1.Role && c0.StackLevel == c1.StackLevel)
                    {
                        junc.CanonicalY = terrY;
                        junc.HasFixedY = false;
                        continue;
                    }
                }

                // 3c. Check through corridors meeting at this junction
                bool hasThrough = false;
                int bestThroughLevel = 0;
                for (int e = 0; e < junc.IncidentEndpoints.Count; e++)
                {
                    var (cIdx, _) = junc.IncidentEndpoints[e];
                    var crs = courses[cIdx];
                    if (IsThroughRole(crs.Role))
                    {
                        if (!hasThrough)
                        {
                            hasThrough = true;
                            bestThroughLevel = crs.StackLevel;
                        }
                        else if (math.abs(crs.StackLevel) < math.abs(bestThroughLevel))
                        {
                            bestThroughLevel = crs.StackLevel;
                        }
                    }
                }

                if (hasThrough)
                {
                    if (bestThroughLevel == 0)
                        junc.CanonicalY = terrY;
                    else if (bestThroughLevel > 0)
                        junc.CanonicalY = terrY + effectiveClearance * bestThroughLevel;
                    else
                        junc.CanonicalY = terrY - tunnelDepth * math.abs(bestThroughLevel);

                    junc.HasFixedY = bestThroughLevel == 0;
                }
                else
                {
                    junc.CanonicalY = terrY;
                    junc.HasFixedY = false;
                }
            }

            // Initialize all courses to terrain-conforming linear elevations
            for (int i = 0; i < courses.Count; i++)
            {
                var c = courses[i];
                var cv = c.Curve;
                cv.a.y = sampleTerrainHeight(cv.a.xz);
                cv.d.y = sampleTerrainHeight(cv.d.xz);
                cv.b.y = math.lerp(cv.a.y, cv.d.y, 1f / 3f);
                cv.c.y = math.lerp(cv.a.y, cv.d.y, 2f / 3f);
                c.Curve = cv;
            }

            for (int j = 0; j < junctions.Count; j++)
            {
                var junc = junctions[j];
                if (!junc.HasFixedY) continue;
                ApplyJunctionElevation(junc, courses);
            }

            var solvedCourses = new HashSet<int>();

            // Step 4: Solve all StackLevel == 0 Through Corridors on terrain first
            for (int i = 0; i < courses.Count; i++)
            {
                var c = courses[i];
                if (IsThroughRole(c.Role) && c.StackLevel == 0 && !c.ForceElevated && !c.ForceTunnel)
                {
                    var cv = c.Curve;
                    SetLinearVerticalControlPoints(ref cv);
                    c.Curve = cv;
                    solvedCourses.Add(i);
                }
            }

            // Sync fixed junctions from solved level-0 through corridors
            SyncJunctionsFromSolved(junctions, courses, solvedCourses);

            // Step 5: Build and solve elevated/tunnel Through Corridor chains
            var throughChains = BuildChains(courses, c => IsThroughRole(c.Role) && !solvedCourses.Contains(courses.IndexOf(c)));
            throughChains.Sort((a, b) => math.abs(GetChainStackLevel(a, courses)).CompareTo(math.abs(GetChainStackLevel(b, courses))));

            for (int chIdx = 0; chIdx < throughChains.Count; chIdx++)
            {
                var chain = throughChains[chIdx];
                SolveSingleChain(chain, courses, solvedCourses, sampleTerrainHeight, effectiveClearance, tunnelDepth);
                SyncJunctionsFromSolved(junctions, courses, solvedCourses);
            }

            // Handle any single-span unchained through courses
            for (int i = 0; i < courses.Count; i++)
            {
                if (!solvedCourses.Contains(i) && IsThroughRole(courses[i].Role))
                {
                    SolveSingleChain(new List<int> { i }, courses, solvedCourses, sampleTerrainHeight, effectiveClearance, tunnelDepth);
                    SyncJunctionsFromSolved(junctions, courses, solvedCourses);
                }
            }

            // Step 6: Build and solve Ramp chains in ascending StackLevel order
            var rampChains = BuildRampChains(courses, solvedCourses, junctions);
            rampChains.Sort((a, b) => GetChainStackLevel(a, courses).CompareTo(GetChainStackLevel(b, courses)));

            for (int chIdx = 0; chIdx < rampChains.Count; chIdx++)
            {
                var chain = rampChains[chIdx];
                SolveSingleChain(chain, courses, solvedCourses, sampleTerrainHeight, effectiveClearance, tunnelDepth);
                SyncJunctionsFromSolved(junctions, courses, solvedCourses);
            }

            // Solve any remaining unchained courses
            for (int i = 0; i < courses.Count; i++)
            {
                if (!solvedCourses.Contains(i))
                {
                    SolveSingleChain(new List<int> { i }, courses, solvedCourses, sampleTerrainHeight, effectiveClearance, tunnelDepth);
                    SyncJunctionsFromSolved(junctions, courses, solvedCourses);
                }
            }

            // Step 7: Finalize StartElevation / EndElevation metadata and telemetry
            for (int i = 0; i < courses.Count; i++)
            {
                var c = courses[i];
                float startH = c.Curve.a.y - sampleTerrainHeight(c.Curve.a.xz);
                float endH = c.Curve.d.y - sampleTerrainHeight(c.Curve.d.xz);
                float midH = CubicBezier3D.Position(c.Curve, 0.5f).y - sampleTerrainHeight(CubicBezier3D.Position(c.Curve, 0.5f).xz);

                bool isTunnel = c.StackLevel < 0 || c.ForceTunnel || startH < -1.5f || endH < -1.5f || midH < -1.5f;
                bool isElev = !isTunnel && (c.StackLevel > 0 || c.ForceElevated || startH > 1.5f || endH > 1.5f || midH > 1.5f);

                c.ForceTunnel = isTunnel;
                c.ForceElevated = isElev;
                c.StartElevation = new float2(isElev ? math.max(0f, startH) : (isTunnel ? startH : 0f));
                c.EndElevation = new float2(isElev ? math.max(0f, endH) : (isTunnel ? endH : 0f));
            }

            ComputeTelemetryAndCompliance(courses, sampleTerrainHeight, config);
        }

        private static void PreSubdivideAllCourses(List<PlannedNetCourse> courses)
        {
            for (int i = 0; i < courses.Count; i++)
            {
                var course = courses[i];
                float len = CubicBezier3D.Length(course.Curve);
                if (len > 34.0f)
                {
                    int pieceCount = (int)math.ceil(len / 31.0f);
                    if (pieceCount < 2) pieceCount = 2;

                    var subPieces = new List<Bezier4x3>();
                    Bezier4x3 remaining = course.Curve;

                    for (int p = 0; p < pieceCount - 1; p++)
                    {
                        float remLen = CubicBezier3D.Length(remaining);
                        int remainingPieces = pieceCount - p;
                        float fractionRemaining = 1f / remainingPieces;
                        float candidateT = fractionRemaining;

                        float bestCandT = candidateT;
                        float bestMinDist = -1f;
                        float[] deltaCandidates = { 0f, -0.05f, 0.05f, -0.10f, 0.10f };

                        for (int dIdx = 0; dIdx < deltaCandidates.Length; dIdx++)
                        {
                            float testT = math.clamp(candidateT + deltaCandidates[dIdx], 0.15f, 0.85f);
                            if (remLen * testT > 34f || (remLen * (1f - testT) / (remainingPieces - 1)) > 34f)
                                continue;

                            float3 testPt = CubicBezier3D.Position(remaining, testT);
                            float minDist = float.MaxValue;
                            for (int j = 0; j < courses.Count; j++)
                            {
                                var other = courses[j];
                                if (other.StackLevel != course.StackLevel)
                                {
                                    float dist = CubicBezier3D.DistanceXZ(other.Curve, testPt.xz, out _);
                                    if (dist < minDist) minDist = dist;
                                }
                            }

                            if (minDist > bestMinDist)
                            {
                                bestMinDist = minDist;
                                bestCandT = testT;
                            }
                        }

                        CubicBezier3D.Split(remaining, bestCandT, out var piece, out remaining);
                        subPieces.Add(piece);
                    }
                    subPieces.Add(remaining);

                    var origEndAttach = course.EndAttachEntity;
                    var origEndSplit = course.EndSplitPosition;
                    var origEndIsNode = course.EndAttachIsNode;

                    course.Curve = subPieces[0];
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
            }
        }

        private static void PromoteCollidingCrossings(List<PlannedNetCourse> courses)
        {
            for (int i = 0; i < courses.Count; i++)
            {
                var cA = courses[i];
                for (int j = i + 1; j < courses.Count; j++)
                {
                    var cB = courses[j];

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

        private static void AddEndpointToJunctions(List<SharedJunction> junctions, float2 xz, int courseIndex, bool isStart, List<PlannedNetCourse> courses)
        {
            var crs = courses[courseIndex];
            int level = crs.StackLevel;

            for (int i = 0; i < junctions.Count; i++)
            {
                var junc = junctions[i];
                if (math.distance(junc.XZ, xz) < 0.6f)
                {
                    // Throughways at different stack levels never share a junction
                    if (IsThroughRole(crs.Role))
                    {
                        bool conflict = false;
                        for (int e = 0; e < junc.IncidentEndpoints.Count; e++)
                        {
                            var other = courses[junc.IncidentEndpoints[e].courseIndex];
                            if (IsThroughRole(other.Role) && other.StackLevel != level)
                            {
                                conflict = true;
                                break;
                            }
                        }
                        if (conflict) continue;
                    }

                    if (junc.StackLevel == level || (!IsThroughRole(crs.Role) && junc.StackLevel == 0))
                    {
                        junc.IncidentEndpoints.Add((courseIndex, isStart));
                        return;
                    }
                }
            }

            var newJunction = new SharedJunction
            {
                XZ = xz,
                StackLevel = level,
                CanonicalY = 0f,
                HasFixedY = false
            };
            newJunction.IncidentEndpoints.Add((courseIndex, isStart));
            junctions.Add(newJunction);
        }

        private static void ApplyJunctionElevation(SharedJunction junc, List<PlannedNetCourse> courses)
        {
            for (int e = 0; e < junc.IncidentEndpoints.Count; e++)
            {
                var (cIdx, isStart) = junc.IncidentEndpoints[e];
                var crs = courses[cIdx];
                var cv = crs.Curve;
                if (isStart)
                    cv.a.y = junc.CanonicalY;
                else
                    cv.d.y = junc.CanonicalY;
                SetLinearVerticalControlPoints(ref cv);
                crs.Curve = cv;
            }
        }

        private static void SyncJunctionsFromSolved(
            List<SharedJunction> junctions,
            List<PlannedNetCourse> courses,
            HashSet<int> solvedCourses)
        {
            for (int j = 0; j < junctions.Count; j++)
            {
                var junc = junctions[j];
                bool foundSolved = false;
                float solvedY = 0f;

                for (int e = 0; e < junc.IncidentEndpoints.Count; e++)
                {
                    var (cIdx, isStart) = junc.IncidentEndpoints[e];
                    if (solvedCourses.Contains(cIdx))
                    {
                        var crs = courses[cIdx];
                        solvedY = isStart ? crs.Curve.a.y : crs.Curve.d.y;
                        foundSolved = true;
                        break;
                    }
                }

                if (foundSolved)
                {
                    junc.CanonicalY = solvedY;
                    junc.HasFixedY = true;
                    ApplyJunctionElevation(junc, courses);
                }
            }
        }

        private static void SetLinearVerticalControlPoints(ref Bezier4x3 cv)
        {
            cv.b.y = math.lerp(cv.a.y, cv.d.y, 1f / 3f);
            cv.c.y = math.lerp(cv.a.y, cv.d.y, 2f / 3f);
        }

        private static List<List<int>> BuildChains(List<PlannedNetCourse> courses, Func<PlannedNetCourse, bool> predicate)
        {
            int count = courses.Count;
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
                if (!predicate(cA)) continue;

                for (int j = 0; j < count; j++)
                {
                    if (i == j) continue;
                    var cB = courses[j];
                    if (!predicate(cB)) continue;

                    if (math.distance(cA.Curve.d.xz, cB.Curve.a.xz) < 1.0f)
                    {
                        nextCourse[i] = j;
                        prevCourse[j] = i;
                        break;
                    }
                }
            }

            var chains = new List<List<int>>();
            var visited = new bool[count];

            for (int i = 0; i < count; i++)
            {
                if (visited[i] || !predicate(courses[i])) continue;
                if (prevCourse[i] != -1) continue;

                var chain = new List<int>();
                int curr = i;
                while (curr != -1 && !visited[curr])
                {
                    visited[curr] = true;
                    chain.Add(curr);
                    curr = nextCourse[curr];
                }
                chains.Add(chain);
            }

            return chains;
        }

        private static List<List<int>> BuildRampChains(
            List<PlannedNetCourse> courses,
            HashSet<int> solvedCourses,
            List<SharedJunction> junctions)
        {
            int count = courses.Count;
            var nextCourse = new int[count];
            var prevCourse = new int[count];
            for (int i = 0; i < count; i++)
            {
                nextCourse[i] = -1;
                prevCourse[i] = -1;
            }

            for (int i = 0; i < count; i++)
            {
                if (solvedCourses.Contains(i)) continue;
                var cA = courses[i];
                if (!IsRampRole(cA.Role)) continue;

                for (int j = 0; j < count; j++)
                {
                    if (i == j || solvedCourses.Contains(j)) continue;
                    var cB = courses[j];
                    if (!IsRampRole(cB.Role)) continue;

                    if (math.distance(cA.Curve.d.xz, cB.Curve.a.xz) < 1.0f)
                    {
                        nextCourse[i] = j;
                        prevCourse[j] = i;
                        break;
                    }
                }
            }

            var chains = new List<List<int>>();
            var visited = new bool[count];

            for (int i = 0; i < count; i++)
            {
                if (visited[i] || solvedCourses.Contains(i) || !IsRampRole(courses[i].Role)) continue;
                if (prevCourse[i] != -1) continue;

                var chain = new List<int>();
                int curr = i;
                while (curr != -1 && !visited[curr])
                {
                    visited[curr] = true;
                    chain.Add(curr);
                    curr = nextCourse[curr];
                }
                chains.Add(chain);
            }

            return chains;
        }

        private static int GetChainStackLevel(List<int> chain, List<PlannedNetCourse> courses)
        {
            int maxLevel = int.MinValue;
            for (int i = 0; i < chain.Count; i++)
            {
                int lvl = courses[chain[i]].StackLevel;
                if (lvl > maxLevel) maxLevel = lvl;
            }
            return maxLevel == int.MinValue ? 0 : maxLevel;
        }

        private static void SolveSingleChain(
            List<int> chain,
            List<PlannedNetCourse> courses,
            HashSet<int> solvedCourses,
            Func<float2, float> sampleTerrainHeight,
            float effectiveClearance,
            float tunnelDepth)
        {
            if (chain == null || chain.Count == 0) return;

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

            float yStart = courses[chain[0]].Curve.a.y;
            float yEnd = courses[chain[chain.Count - 1]].Curve.d.y;
            int chainLevel = GetChainStackLevel(chain, courses);

            // Detect all crossings against solved courses or lower-stack courses
            var chainCrossings = new List<(float s, float otherY, float terrY)>();

            float2 chainStartXZ = courses[chain[0]].Curve.a.xz;
            float2 chainEndXZ = courses[chain[chain.Count - 1]].Curve.d.xz;

            if (chainLevel == 0)
            {
                // Surface chains remain at grade on terrain; they do not climb or dive
            }
            else
            {
                for (int s = 0; s < chain.Count; s++)
                {
                    int cIdx = chain[s];
                    var crs = courses[cIdx];
                    float sBase = segStartDist[s];
                    float segLen = segLengths[s];

                    for (int o = 0; o < courses.Count; o++)
                    {
                        if (chain.Contains(o)) continue;
                        var other = courses[o];

                        if (chainLevel > 0)
                        {
                            // Elevated chain: only climb over courses with lower stack levels that are at or above grade
                            if (other.StackLevel >= chainLevel || other.StackLevel < 0 || other.ForceTunnel) continue;
                        }
                        else // chainLevel < 0
                        {
                            // Tunnel chain: dive under courses with higher stack level (surface or elevated)
                            if (other.StackLevel <= chainLevel) continue;
                        }

                        // Skip gore merge/diverge areas where chain connects to adjacent corridor at a shared junction
                        bool shareStartJunc = math.distance(chainStartXZ, other.Curve.a.xz) < 5.0f || math.distance(chainStartXZ, other.Curve.d.xz) < 5.0f;
                        bool shareEndJunc = math.distance(chainEndXZ, other.Curve.a.xz) < 5.0f || math.distance(chainEndXZ, other.Curve.d.xz) < 5.0f;
                        bool nearChainStart = shareStartJunc && math.distance(crs.Curve.a.xz, chainStartXZ) < 40.0f;
                        bool nearChainEnd = shareEndJunc && math.distance(crs.Curve.d.xz, chainEndXZ) < 40.0f;
                        if (nearChainStart || nearChainEnd) continue;

                        // Subspans of same corridor
                        if (crs.Id.Contains("_span_") && other.Id.Contains("_span_"))
                        {
                            string baseA = crs.Id.Substring(0, crs.Id.IndexOf("_span_"));
                            string baseB = other.Id.Substring(0, other.Id.IndexOf("_span_"));
                            if (baseA == baseB) continue;
                        }

                        if (FindIntersectionXZ(other.Curve, crs.Curve, out float tOther, out float tCrs, out float3 iPt))
                        {
                            float sCross = sBase + tCrs * segLen;
                            float oY = CubicBezier3D.Position(other.Curve, tOther).y;
                            float tY = sampleTerrainHeight(iPt.xz);
                            chainCrossings.Add((sCross, oY, tY));
                        }
                        else
                        {
                            for (int u = 1; u <= 9; u++)
                            {
                                float t1 = u * 0.1f;
                                float3 p1 = CubicBezier3D.Position(crs.Curve, t1);
                                if (math.distance(p1.xz, other.Curve.a.xz) < 8.0f || math.distance(p1.xz, other.Curve.d.xz) < 8.0f) continue;

                                for (int v = 1; v <= 9; v++)
                                {
                                    float t2 = v * 0.1f;
                                    float3 p2 = CubicBezier3D.Position(other.Curve, t2);
                                    if (math.distance(p2.xz, crs.Curve.a.xz) < 8.0f || math.distance(p2.xz, crs.Curve.d.xz) < 8.0f) continue;

                                    if (math.distance(p1.xz, p2.xz) < 5.4f)
                                    {
                                        float sCross = sBase + t1 * segLen;
                                        float oY = p2.y;
                                        float tY = sampleTerrainHeight(p1.xz);
                                        chainCrossings.Add((sCross, oY, tY));
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            if (chainLevel == 0)
            {
                // Surface chain: strictly conforms to terrain at grade
                for (int s = 0; s < chain.Count; s++)
                {
                    int cIdx = chain[s];
                    var crs = courses[cIdx];
                    var cv = crs.Curve;

                    float aY = s == 0 ? yStart : sampleTerrainHeight(cv.a.xz);
                    float dY = s == chain.Count - 1 ? yEnd : sampleTerrainHeight(cv.d.xz);

                    cv.a.y = aY;
                    cv.d.y = dY;
                    SetLinearVerticalControlPoints(ref cv);
                    crs.Curve = cv;
                    crs.StackLevel = 0;
                    crs.ForceElevated = false;
                    crs.ForceTunnel = false;
                    solvedCourses.Add(cIdx);
                }
                return;
            }

            if (chainLevel < 0)
            {
                // Tunnel chain: dive below surface & crossing corridors
                float midTerrain = sampleTerrainHeight(CubicBezier3D.Position(courses[chain[chain.Count / 2]].Curve, 0.5f).xz);
                float yTarget = midTerrain - tunnelDepth * math.max(1, math.abs(chainLevel));

                var waypoints = new List<(float s, float y)>();
                waypoints.Add((0f, yStart));

                float minCrossS = float.MaxValue;
                float maxCrossS = float.MinValue;
                for (int c = 0; c < chainCrossings.Count; c++)
                {
                    float maxAllowedY = math.min(chainCrossings[c].otherY, chainCrossings[c].terrY) - effectiveClearance;
                    if (maxAllowedY < yTarget) yTarget = maxAllowedY;
                    if (chainCrossings[c].s < minCrossS) minCrossS = chainCrossings[c].s;
                    if (chainCrossings[c].s > maxCrossS) maxCrossS = chainCrossings[c].s;
                    waypoints.Add((chainCrossings[c].s, maxAllowedY));
                }

                if (minCrossS == float.MaxValue)
                {
                    minCrossS = 0.35f * totalLength;
                    maxCrossS = 0.65f * totalLength;
                }

                float sPlatStart = math.clamp(minCrossS - 8f, 15f, math.max(15f, totalLength - 20f));
                float sPlatEnd = math.clamp(maxCrossS + 8f, sPlatStart, math.max(sPlatStart, totalLength - 15f));
                waypoints.Add((sPlatStart, yTarget));
                waypoints.Add((sPlatEnd, yTarget));
                waypoints.Add((totalLength, yEnd));
                waypoints.Sort((w1, w2) => w1.s.CompareTo(w2.s));

                // Deduplicate waypoints
                var cleanWaypoints = new List<(float s, float y)>();
                cleanWaypoints.Add(waypoints[0]);
                for (int w = 1; w < waypoints.Count; w++)
                {
                    var curr = waypoints[w];
                    var prev = cleanWaypoints[cleanWaypoints.Count - 1];
                    if (curr.s <= prev.s + 1.0f)
                    {
                        cleanWaypoints[cleanWaypoints.Count - 1] = (prev.s, math.min(prev.y, curr.y));
                    }
                    else
                    {
                        cleanWaypoints.Add(curr);
                    }
                }

                // Forward and backward dive propagation
                const float maxTunnelSlope = 0.12f;
                for (int w = 0; w < cleanWaypoints.Count - 1; w++)
                {
                    float ds = cleanWaypoints[w + 1].s - cleanWaypoints[w].s;
                    float maxDy = maxTunnelSlope * ds;
                    cleanWaypoints[w + 1] = (cleanWaypoints[w + 1].s, math.min(cleanWaypoints[w + 1].y, cleanWaypoints[w].y + maxDy));
                }
                for (int w = cleanWaypoints.Count - 1; w > 0; w--)
                {
                    float ds = cleanWaypoints[w].s - cleanWaypoints[w - 1].s;
                    float maxDy = maxTunnelSlope * ds;
                    cleanWaypoints[w - 1] = (cleanWaypoints[w - 1].s, math.min(cleanWaypoints[w - 1].y, cleanWaypoints[w].y + maxDy));
                }
                for (int w = 0; w < cleanWaypoints.Count - 1; w++)
                {
                    float ds = cleanWaypoints[w + 1].s - cleanWaypoints[w].s;
                    float maxDy = maxTunnelSlope * ds;
                    float currY = cleanWaypoints[w].y;
                    float nextY = cleanWaypoints[w + 1].y;
                    if (nextY - currY > maxDy) cleanWaypoints[w + 1] = (cleanWaypoints[w + 1].s, currY + maxDy);
                    else if (currY - nextY > maxDy) cleanWaypoints[w + 1] = (cleanWaypoints[w + 1].s, currY - maxDy);
                }

                float EvalTunnelY(float s)
                {
                    if (s <= cleanWaypoints[0].s) return cleanWaypoints[0].y;
                    if (s >= cleanWaypoints[cleanWaypoints.Count - 1].s) return cleanWaypoints[cleanWaypoints.Count - 1].y;
                    for (int w = 0; w < cleanWaypoints.Count - 1; w++)
                    {
                        if (s <= cleanWaypoints[w + 1].s)
                        {
                            float ds = cleanWaypoints[w + 1].s - cleanWaypoints[w].s;
                            float u = ds > 1e-4f ? (s - cleanWaypoints[w].s) / ds : 0f;
                            return math.lerp(cleanWaypoints[w].y, cleanWaypoints[w + 1].y, u);
                        }
                    }
                    return cleanWaypoints[cleanWaypoints.Count - 1].y;
                }

                ApplyProfileToChain(chain, courses, segStartDist, EvalTunnelY, sampleTerrainHeight, solvedCourses);
                return;
            }

            // Elevated chain
            float midTerr = sampleTerrainHeight(CubicBezier3D.Position(courses[chain[chain.Count / 2]].Curve, 0.5f).xz);
            float nominalPeakElev = midTerr + effectiveClearance * math.max(1, chainLevel);
            float yPeakElev = nominalPeakElev;

            var elevWaypoints = new List<(float s, float y)>();
            elevWaypoints.Add((0f, yStart));

            float minCrossSElev = float.MaxValue;
            float maxCrossSElev = float.MinValue;
            for (int c = 0; c < chainCrossings.Count; c++)
            {
                float reqY = math.max(chainCrossings[c].otherY, chainCrossings[c].terrY) + effectiveClearance;
                if (reqY > yPeakElev) yPeakElev = reqY;
                if (chainCrossings[c].s < minCrossSElev) minCrossSElev = chainCrossings[c].s;
                if (chainCrossings[c].s > maxCrossSElev) maxCrossSElev = chainCrossings[c].s;
                elevWaypoints.Add((chainCrossings[c].s, reqY));
            }

            const float maxBridgeSlope = 0.16f;

            if (chainCrossings.Count > 0)
            {
                float reqClimbDist = math.max(0f, yPeakElev - yStart) / maxBridgeSlope;
                float reqDescDist = math.max(0f, yPeakElev - yEnd) / maxBridgeSlope;

                float sPlatStartElev = math.clamp(minCrossSElev - 8f, reqClimbDist, math.max(reqClimbDist, totalLength - reqDescDist));
                float sPlatEndElev = math.clamp(maxCrossSElev + 8f, sPlatStartElev, math.max(sPlatStartElev, totalLength - reqDescDist));

                elevWaypoints.Add((sPlatStartElev, yPeakElev));
                elevWaypoints.Add((sPlatEndElev, yPeakElev));
            }
            else if (chainLevel > 0 && yStart <= midTerr + 2f && yEnd <= midTerr + 2f)
            {
                // Pure elevated flyover with no recorded lower crossings: apex in center
                float reqClimbDist = math.max(0f, yPeakElev - yStart) / maxBridgeSlope;
                float reqDescDist = math.max(0f, yPeakElev - yEnd) / maxBridgeSlope;
                float sMidStart = math.clamp(0.40f * totalLength, reqClimbDist, math.max(reqClimbDist, totalLength - reqDescDist));
                float sMidEnd = math.clamp(0.60f * totalLength, sMidStart, math.max(sMidStart, totalLength - reqDescDist));
                elevWaypoints.Add((sMidStart, yPeakElev));
                elevWaypoints.Add((sMidEnd, yPeakElev));
            }

            elevWaypoints.Add((totalLength, yEnd));
            elevWaypoints.Sort((w1, w2) => w1.s.CompareTo(w2.s));

            // Deduplicate waypoints
            var cleanElevWaypoints = new List<(float s, float y)>();
            cleanElevWaypoints.Add(elevWaypoints[0]);
            for (int w = 1; w < elevWaypoints.Count; w++)
            {
                var curr = elevWaypoints[w];
                var prev = cleanElevWaypoints[cleanElevWaypoints.Count - 1];
                if (curr.s <= prev.s + 1.0f)
                {
                    cleanElevWaypoints[cleanElevWaypoints.Count - 1] = (prev.s, math.max(prev.y, curr.y));
                }
                else
                {
                    cleanElevWaypoints.Add(curr);
                }
            }

            // Forward and backward climb propagation (Lipschitz cone)
            for (int w = 0; w < cleanElevWaypoints.Count - 1; w++)
            {
                float ds = cleanElevWaypoints[w + 1].s - cleanElevWaypoints[w].s;
                float maxDy = maxBridgeSlope * ds;
                cleanElevWaypoints[w + 1] = (cleanElevWaypoints[w + 1].s, math.max(cleanElevWaypoints[w + 1].y, cleanElevWaypoints[w].y - maxDy));
            }
            for (int w = cleanElevWaypoints.Count - 1; w > 0; w--)
            {
                float ds = cleanElevWaypoints[w].s - cleanElevWaypoints[w - 1].s;
                float maxDy = maxBridgeSlope * ds;
                cleanElevWaypoints[w - 1] = (cleanElevWaypoints[w - 1].s, math.max(cleanElevWaypoints[w - 1].y, cleanElevWaypoints[w].y - maxDy));
            }
            // Slope clamping pass ensuring grade <= 14% across all subsegments
            for (int w = 0; w < cleanElevWaypoints.Count - 1; w++)
            {
                float ds = cleanElevWaypoints[w + 1].s - cleanElevWaypoints[w].s;
                float maxDy = maxBridgeSlope * ds;
                float currY = cleanElevWaypoints[w].y;
                float nextY = cleanElevWaypoints[w + 1].y;
                if (nextY - currY > maxDy) cleanElevWaypoints[w + 1] = (cleanElevWaypoints[w + 1].s, currY + maxDy);
                else if (currY - nextY > maxDy) cleanElevWaypoints[w + 1] = (cleanElevWaypoints[w + 1].s, currY - maxDy);
            }

            float EvalElevY(float s)
            {
                if (s <= cleanElevWaypoints[0].s) return cleanElevWaypoints[0].y;
                if (s >= cleanElevWaypoints[cleanElevWaypoints.Count - 1].s) return cleanElevWaypoints[cleanElevWaypoints.Count - 1].y;
                for (int w = 0; w < cleanElevWaypoints.Count - 1; w++)
                {
                    if (s <= cleanElevWaypoints[w + 1].s)
                    {
                        float ds = cleanElevWaypoints[w + 1].s - cleanElevWaypoints[w].s;
                        float u = ds > 1e-4f ? (s - cleanElevWaypoints[w].s) / ds : 0f;
                        return math.lerp(cleanElevWaypoints[w].y, cleanElevWaypoints[w + 1].y, u);
                    }
                }
                return cleanElevWaypoints[cleanElevWaypoints.Count - 1].y;
            }

            ApplyProfileToChain(chain, courses, segStartDist, EvalElevY, sampleTerrainHeight, solvedCourses);
        }

        private static void ApplyProfileToChain(
            List<int> chain,
            List<PlannedNetCourse> courses,
            float[] segStartDist,
            Func<float, float> evalY,
            Func<float2, float> sampleTerrainHeight,
            HashSet<int> solvedCourses)
        {
            for (int s = 0; s < chain.Count; s++)
            {
                int cIdx = chain[s];
                var crs = courses[cIdx];
                float s0 = segStartDist[s];
                float s1 = segStartDist[s + 1];

                float aY = evalY(s0);
                float dY = evalY(s1);
                float bY = evalY(math.lerp(s0, s1, 1f / 3f));
                float cY = evalY(math.lerp(s0, s1, 2f / 3f));

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
                else if (midY <= terrMid - 4.5f || aY <= sampleTerrainHeight(cv.a.xz) - 4.5f || dY <= sampleTerrainHeight(cv.d.xz) - 4.5f)
                {
                    crs.StackLevel = math.min(-1, crs.StackLevel);
                    crs.ForceTunnel = true;
                }

                solvedCourses.Add(cIdx);
            }
        }

        private static bool IsThroughRole(NetRole role)
        {
            return role == NetRole.MainlineThrough || role == NetRole.CrossStreet ||
                   role == NetRole.CollectorDistributor;
        }

        private static bool IsRampRole(NetRole role)
        {
            return role == NetRole.LoopRamp || role == NetRole.DirectionalRamp ||
                   role == NetRole.SlipRamp || role == NetRole.CollectorDistributor ||
                   role == NetRole.TransitionTaper;
        }

        private static bool FindIntersectionXZ(in Bezier4x3 c1, in Bezier4x3 c2, out float t1, out float t2, out float3 intersectPt)
        {
            t1 = 0.5f;
            t2 = 0.5f;
            intersectPt = float3.zero;

            const int kSamples = 24;
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

            float step = 1f / kSamples;
            for (int iter = 0; iter < 5; iter++)
            {
                step *= 0.5f;
                float[] dt1 = { -step, 0f, step };
                float[] dt2 = { -step, 0f, step };

                for (int i = 0; i < 3; i++)
                {
                    float testT1 = math.clamp(bestT1 + dt1[i], 0f, 1f);
                    float3 p1 = CubicBezier3D.Position(c1, testT1);

                    for (int j = 0; j < 3; j++)
                    {
                        float testT2 = math.clamp(bestT2 + dt2[j], 0f, 1f);
                        float3 p2 = CubicBezier3D.Position(c2, testT2);

                        float distSq = math.distancesq(p1.xz, p2.xz);
                        if (distSq < bestDistSq)
                        {
                            bestDistSq = distSq;
                            bestT1 = testT1;
                            bestT2 = testT2;
                        }
                    }
                }
            }

            if (bestDistSq < 12.25f)
            {
                t1 = bestT1;
                t2 = bestT2;
                intersectPt = (CubicBezier3D.Position(c1, t1) + CubicBezier3D.Position(c2, t2)) * 0.5f;
                return true;
            }

            return false;
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

                bool clearanceViolation = c.StackLevel > 0 && c.MinClearanceMeters < 7.8f;

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
