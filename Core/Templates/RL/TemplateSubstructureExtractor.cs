using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Colossal.Mathematics;
using ProceduralInterchanges.Core.Models;
using Unity.Mathematics;

namespace ProceduralInterchanges.Core.Templates.RL
{
    public enum TemplateNodeRole : byte
    {
        TerminalPort = 0,     // Degree-1 boundary node connected to host highway
        MainlineGore = 1,     // Degree >= 3 diverge/merge node on a through corridor
        LoopInterior = 2,     // Degree-2 node strictly inside a high-curvature loop cluster
        CoreOverpass = 3,     // Interior node near center (r < 0.45 * CanonicalRadius) not in a loop
        ConnectorInterior = 4 // Remaining degree-2 ramp/mainline nodes
    }

    public sealed class LoopClusterDef
    {
        public int ClusterIndex;              // 0 .. K-1 (max 4)
        public int CanonicalSectorIndex;      // Sector m in 0 .. RequiredLegs-1 containing CanonicalCenterXZ
        public float2 CanonicalCenterXZ;      // Centroid of loop nodes in canonical (LHT-mirrored) XZ space
        public float CanonicalMeanRadius;     // Mean distance of loop nodes from CanonicalCenterXZ
        public float CumulativeTurnRad;       // Total unsigned turning angle of the chain (rad)
        public bool IsClosedOr270Loop;        // true if CumulativeTurnRad >= 2.6f (~150 deg)
        public int EntryNodeIndex;            // First node index of chain (typically a gore node)
        public int ExitNodeIndex;             // Last node index of chain (typically a gore node)
        public int[] InteriorNodeIndices;     // Degree-2 nodes strictly inside the chain
        public int[] SegmentIndices;          // Ordered segment indices forming the loop chain
        public bool[] SegmentForwardInChain;  // true if segment's Start->End matches chain Entry->Exit order
    }

    public sealed class GoreNodeDef
    {
        public int GoreIndex;                 // 0 .. G-1 (max 8)
        public int NodeIndex;                 // Index in template.Nodes
        public int CanonicalSectorIndex;      // Nearest canonical leg/sector index
        public float2 CanonicalPosXZ;         // Sanitized canonical node position
        public float2 MainlineTangentXZ;      // Unit tangent along the primary mainline passing through this gore
        public float2 MainlineNormalXZ;       // Left-hand normal (-MainlineTangentXZ.y, MainlineTangentXZ.x)
        public float CanonicalRadialDist;     // Distance from origin in canonical space
    }

    public sealed class TemplateSubstructureMap
    {
        public InterchangeArchetype Archetype;
        public bool LeftHandTraffic;
        public float3[] SanitizedNodes;              // [Nodes.Length] Mainline-prioritized canonical node coords (LHT-mirrored)
        public float2[] CanonicalNodeTangentsXZ;     // [Nodes.Length] Dominant unit tangent at each node in canonical space
        public float3[] SegmentCanonVectorA;         // [Segments.Length] (BezierB - BezierA) in LHT-mirrored canonical space
        public float3[] SegmentCanonVectorD;         // [Segments.Length] (BezierC - BezierD) in LHT-mirrored canonical space
        public TemplateNodeRole[] NodeRoles;         // [Nodes.Length]
        public int[] NodeToLoopCluster;              // [Nodes.Length] -1 if not in LoopInterior, else 0..K-1
        public int[] SegmentToLoopCluster;           // [Segments.Length] -1 if not in a LoopCluster, else 0..K-1
        public int[] NodeToGoreIndex;                // [Nodes.Length] -1 if not a GoreNode, else 0..G-1
        public LoopClusterDef[] LoopClusters;        // Up to 4 primary curved/loop chains
        public GoreNodeDef[] GoreNodes;              // Up to 8 primary diverge/merge gore nodes
    }

    public static class TemplateSubstructureExtractor
    {
        private static readonly ConcurrentDictionary<(InterchangeArchetype, bool), TemplateSubstructureMap> s_Cache
            = new ConcurrentDictionary<(InterchangeArchetype, bool), TemplateSubstructureMap>();

        public static TemplateSubstructureMap GetOrExtract(WorkshopTemplateDef template, bool leftHandTraffic)
        {
            if (template == null) return null;
            var key = (template.Archetype, leftHandTraffic);
            if (s_Cache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var result = Extract(template, leftHandTraffic);
            s_Cache[key] = result;
            return result;
        }

        public static void ClearCache()
        {
            s_Cache.Clear();
        }

        private static TemplateSubstructureMap Extract(WorkshopTemplateDef template, bool leftHandTraffic)
        {
            int numNodes = template.Nodes != null ? template.Nodes.Length : 0;
            int numSegments = template.Segments != null ? template.Segments.Length : 0;

            var map = new TemplateSubstructureMap
            {
                Archetype = template.Archetype,
                LeftHandTraffic = leftHandTraffic,
                SanitizedNodes = new float3[numNodes],
                CanonicalNodeTangentsXZ = new float2[numNodes],
                SegmentCanonVectorA = new float3[numSegments],
                SegmentCanonVectorD = new float3[numSegments],
                NodeRoles = new TemplateNodeRole[numNodes],
                NodeToLoopCluster = new int[numNodes],
                SegmentToLoopCluster = new int[numSegments],
                NodeToGoreIndex = new int[numNodes]
            };

            for (int i = 0; i < numNodes; i++)
            {
                map.NodeToLoopCluster[i] = -1;
                map.NodeToGoreIndex[i] = -1;
            }
            for (int i = 0; i < numSegments; i++)
            {
                map.SegmentToLoopCluster[i] = -1;
            }

            float sigmaZ = leftHandTraffic ? -1.0f : 1.0f;

            // Step 1: LHT Mirroring & Intrinsic Handle Extraction
            for (int s = 0; s < numSegments; s++)
            {
                var seg = template.Segments[s];
                float3 a = new float3(seg.BezierA.x, seg.BezierA.y, sigmaZ * seg.BezierA.z);
                float3 b = new float3(seg.BezierB.x, seg.BezierB.y, sigmaZ * seg.BezierB.z);
                float3 c = new float3(seg.BezierC.x, seg.BezierC.y, sigmaZ * seg.BezierC.z);
                float3 d = new float3(seg.BezierD.x, seg.BezierD.y, sigmaZ * seg.BezierD.z);

                map.SegmentCanonVectorA[s] = b - a;
                map.SegmentCanonVectorD[s] = c - d;
            }

            // Step 2: Canonical Node Position Sanitization (Eliminating ObjectSubNets Lane-Offset Jumps)
            var incidentSegments = new List<int>[numNodes];
            for (int i = 0; i < numNodes; i++)
            {
                incidentSegments[i] = new List<int>();
            }

            for (int s = 0; s < numSegments; s++)
            {
                int u = template.Segments[s].StartNodeIndex;
                int v = template.Segments[s].EndNodeIndex;
                if (u >= 0 && u < numNodes) incidentSegments[u].Add(s);
                if (v >= 0 && v < numNodes) incidentSegments[v].Add(s);
            }

            for (int u = 0; u < numNodes; u++)
            {
                var inc = incidentSegments[u];
                if (inc.Count == 0)
                {
                    map.SanitizedNodes[u] = new float3(template.Nodes[u].x, template.Nodes[u].y, sigmaZ * template.Nodes[u].z);
                    map.CanonicalNodeTangentsXZ[u] = new float2(1f, 0f);
                    continue;
                }

                float3 sumPos = float3.zero;
                for (int i = 0; i < inc.Count; i++)
                {
                    int s = inc[i];
                    var seg = template.Segments[s];
                    float3 endPt = (seg.StartNodeIndex == u)
                        ? new float3(seg.BezierA.x, seg.BezierA.y, sigmaZ * seg.BezierA.z)
                        : new float3(seg.BezierD.x, seg.BezierD.y, sigmaZ * seg.BezierD.z);
                    sumPos += endPt;
                }
                map.SanitizedNodes[u] = sumPos / inc.Count;

                float2 sumTan = float2.zero;
                if (inc.Count == 2)
                {
                    // Degree-2 node: strict equal-weight average along continuous flow direction
                    for (int i = 0; i < 2; i++)
                    {
                        int s = inc[i];
                        var seg = template.Segments[s];
                        float2 tan = (seg.StartNodeIndex == u)
                            ? map.SegmentCanonVectorA[s].xz
                            : -map.SegmentCanonVectorD[s].xz;
                        if (math.lengthsq(tan) > 1e-4f)
                        {
                            tan = math.normalize(tan);
                            if (i == 0 || math.dot(sumTan, tan) >= 0f)
                                sumTan += tan;
                            else
                                sumTan -= tan;
                        }
                    }
                }
                else
                {
                    // Degree >= 3 node (gore split/merge): weight tangents by WidthClass
                    for (int i = 0; i < inc.Count; i++)
                    {
                        int s = inc[i];
                        var seg = template.Segments[s];
                        float w = math.max(1f, (float)seg.WidthClass);
                        float2 tan = (seg.StartNodeIndex == u)
                            ? map.SegmentCanonVectorA[s].xz
                            : -map.SegmentCanonVectorD[s].xz;
                        if (math.lengthsq(tan) > 1e-4f)
                        {
                            tan = math.normalize(tan);
                            if (math.lengthsq(sumTan) < 1e-4f || math.dot(sumTan, tan) >= 0f)
                                sumTan += tan * w;
                            else
                                sumTan -= tan * w;
                        }
                    }
                }

                map.CanonicalNodeTangentsXZ[u] = math.lengthsq(sumTan) > 1e-4f ? math.normalize(sumTan) : new float2(1f, 0f);
            }

            // Step 3: Chain Tracing & Loop Cluster Identification
            int[] degrees = new int[numNodes];
            for (int u = 0; u < numNodes; u++)
            {
                degrees[u] = incidentSegments[u].Count;
            }

            var visitedSegmentDirections = new HashSet<(int segIndex, bool forward)>();
            var candidateChains = new List<LoopClusterDef>();

            // Only archetypes with real 270-deg loop ramps (Cloverleaf, ParcloA4, Trumpet) should detect loop clusters.
            // Directional flyovers, stacks, turbines, and diamonds must preserve global smooth diffeomorphism.
            bool archetypeHasLoops = (template.Archetype == InterchangeArchetype.Cloverleaf ||
                                      template.Archetype == InterchangeArchetype.ParcloA4 ||
                                      template.Archetype == InterchangeArchetype.Trumpet);

            float GetSegmentTurn(int s)
            {
                var sDef = template.Segments[s];
                float3 sa = new float3(sDef.BezierA.x, sDef.BezierA.y, sigmaZ * sDef.BezierA.z);
                float3 sb = new float3(sDef.BezierB.x, sDef.BezierB.y, sigmaZ * sDef.BezierB.z);
                float3 sc = new float3(sDef.BezierC.x, sDef.BezierC.y, sigmaZ * sDef.BezierC.z);
                float3 sd = new float3(sDef.BezierD.x, sDef.BezierD.y, sigmaZ * sDef.BezierD.z);
                float prevTh = math.atan2(sb.z - sa.z, sb.x - sa.x);
                float sTurn = 0f;
                for (int r = 1; r <= 4; r++)
                {
                    float t = r / 4.0f;
                    float t1 = 1f - t;
                    float3 tan = 3f * t1 * t1 * (sb - sa) + 6f * t1 * t * (sc - sb) + 3f * t * t * (sd - sc);
                    float curTh = math.atan2(tan.z, tan.x);
                    float diff = math.atan2(math.sin(curTh - prevTh), math.cos(curTh - prevTh));
                    sTurn += math.abs(diff);
                    prevTh = curTh;
                }
                return sTurn;
            }

            // Helper to get connected other node of segment
            int GetOtherNode(int s, int fromNode)
            {
                var seg = template.Segments[s];
                return seg.StartNodeIndex == fromNode ? seg.EndNodeIndex : seg.StartNodeIndex;
            }

            if (archetypeHasLoops)
            {
                // Trace paths from any node with degree != 2
                for (int u0 = 0; u0 < numNodes; u0++)
                {
                    if (degrees[u0] == 2) continue;

                    foreach (int s0 in incidentSegments[u0])
                    {
                        bool fwd0 = (template.Segments[s0].StartNodeIndex == u0);
                        if (visitedSegmentDirections.Contains((s0, fwd0))) continue;

                        var chainNodes = new List<int> { u0 };
                        var chainSegments = new List<int> { s0 };
                        var chainFwd = new List<bool> { fwd0 };
                        visitedSegmentDirections.Add((s0, fwd0));

                        int curPrev = u0;
                        int cur = GetOtherNode(s0, u0);
                        chainNodes.Add(cur);

                        while (cur >= 0 && cur < numNodes && degrees[cur] == 2)
                        {
                            int nextSeg = -1;
                            foreach (int sc in incidentSegments[cur])
                            {
                                if (sc != chainSegments[chainSegments.Count - 1])
                                {
                                    nextSeg = sc;
                                    break;
                                }
                            }
                            if (nextSeg < 0) break;

                            // Stop if next segment is a straight approach/departure tangent (< 20 degrees turn)
                            if (GetSegmentTurn(nextSeg) < 0.35f) break;

                            bool nextFwd = (template.Segments[nextSeg].StartNodeIndex == cur);
                            visitedSegmentDirections.Add((nextSeg, nextFwd));
                            chainSegments.Add(nextSeg);
                            chainFwd.Add(nextFwd);

                            int nxt = GetOtherNode(nextSeg, cur);
                            curPrev = cur;
                            cur = nxt;
                            chainNodes.Add(cur);
                            if (cur == u0) break; // closed loop
                        }

                        int uLast = chainNodes[chainNodes.Count - 1];
                        // Also mark reverse directions as visited
                        for (int i = 0; i < chainSegments.Count; i++)
                        {
                            visitedSegmentDirections.Add((chainSegments[i], !chainFwd[i]));
                        }

                        // A loop cluster should NOT connect directly to a degree-1 terminal port
                        bool touchesTerminalPort = (degrees[u0] == 1 || degrees[uLast] == 1);
                        if (touchesTerminalPort) continue;

                        // Compute cumulative turning angle along the chain
                        float totalTurn = 0f;
                        for (int cs = 0; cs < chainSegments.Count; cs++)
                        {
                            int segIdx = chainSegments[cs];
                            totalTurn += GetSegmentTurn(segIdx);
                        }

                        // Candidate loop if cumulative turn >= 2.8 rad (160 deg) - 180-deg and 270-deg loops!
                        if (totalTurn >= 2.8f)
                        {
                            var interiorNodes = new List<int>();
                            for (int inIdx = 1; inIdx < chainNodes.Count - 1; inIdx++)
                            {
                                interiorNodes.Add(chainNodes[inIdx]);
                            }

                            float2 centerXZ = float2.zero;
                            for (int ni = 0; ni < chainNodes.Count; ni++)
                            {
                                centerXZ += map.SanitizedNodes[chainNodes[ni]].xz;
                            }
                            centerXZ /= chainNodes.Count;

                            float meanR = 0f;
                            for (int ni = 0; ni < chainNodes.Count; ni++)
                            {
                                meanR += math.distance(map.SanitizedNodes[chainNodes[ni]].xz, centerXZ);
                            }
                            meanR /= chainNodes.Count;

                            candidateChains.Add(new LoopClusterDef
                            {
                                EntryNodeIndex = u0,
                                ExitNodeIndex = uLast,
                                CumulativeTurnRad = totalTurn,
                                IsClosedOr270Loop = totalTurn >= 3.5f,
                                InteriorNodeIndices = interiorNodes.ToArray(),
                                SegmentIndices = chainSegments.ToArray(),
                                SegmentForwardInChain = chainFwd.ToArray(),
                                CanonicalCenterXZ = centerXZ,
                                CanonicalMeanRadius = meanR,
                                CanonicalSectorIndex = GetSectorIndex(centerXZ, template.CanonicalLegAzimuthsRad)
                            });
                        }
                    }
                }
            }

            // Sort candidates descending by cumulative turning angle and pick top 4
            candidateChains.Sort((a, b) => b.CumulativeTurnRad.CompareTo(a.CumulativeTurnRad));
            int numClusters = math.min(4, candidateChains.Count);
            map.LoopClusters = new LoopClusterDef[numClusters];

            for (int k = 0; k < numClusters; k++)
            {
                var cl = candidateChains[k];
                cl.ClusterIndex = k;
                map.LoopClusters[k] = cl;

                for (int i = 0; i < cl.InteriorNodeIndices.Length; i++)
                {
                    int v = cl.InteriorNodeIndices[i];
                    map.NodeToLoopCluster[v] = k;
                    map.NodeRoles[v] = TemplateNodeRole.LoopInterior;
                }
                for (int s = 0; s < cl.SegmentIndices.Length; s++)
                {
                    map.SegmentToLoopCluster[cl.SegmentIndices[s]] = k;
                }
            }

            // Step 4: Gore Nodes & Role Assignment
            var candidateGores = new List<int>();
            for (int u = 0; u < numNodes; u++)
            {
                if (degrees[u] >= 3)
                {
                    candidateGores.Add(u);
                }
                else if (degrees[u] == 1)
                {
                    map.NodeRoles[u] = TemplateNodeRole.TerminalPort;
                }
            }

            // Sort gores by radial distance descending (major mainline splits usually sit further out)
            candidateGores.Sort((a, b) => math.lengthsq(map.SanitizedNodes[b].xz).CompareTo(math.lengthsq(map.SanitizedNodes[a].xz)));
            int numGores = math.min(8, candidateGores.Count);
            map.GoreNodes = new GoreNodeDef[numGores];

            for (int g = 0; g < numGores; g++)
            {
                int u = candidateGores[g];
                map.NodeRoles[u] = TemplateNodeRole.MainlineGore;
                map.NodeToGoreIndex[u] = g;

                float2 tan = map.CanonicalNodeTangentsXZ[u];
                float2 norm = new float2(-tan.y, tan.x);

                map.GoreNodes[g] = new GoreNodeDef
                {
                    GoreIndex = g,
                    NodeIndex = u,
                    CanonicalPosXZ = map.SanitizedNodes[u].xz,
                    CanonicalSectorIndex = GetSectorIndex(map.SanitizedNodes[u].xz, template.CanonicalLegAzimuthsRad),
                    MainlineTangentXZ = tan,
                    MainlineNormalXZ = norm,
                    CanonicalRadialDist = math.length(map.SanitizedNodes[u].xz)
                };
            }

            // Step 5: Assign CoreOverpass vs ConnectorInterior for remaining nodes
            float coreRadiusThreshold = 0.45f * template.CanonicalRadius;
            for (int u = 0; u < numNodes; u++)
            {
                if (map.NodeRoles[u] == 0 && degrees[u] == 2)
                {
                    if (math.length(map.SanitizedNodes[u].xz) < coreRadiusThreshold)
                    {
                        map.NodeRoles[u] = TemplateNodeRole.CoreOverpass;
                    }
                    else
                    {
                        map.NodeRoles[u] = TemplateNodeRole.ConnectorInterior;
                    }
                }
            }

            return map;
        }

        private static int GetSectorIndex(float2 posXZ, float[] canonicalLegAzimuths)
        {
            if (canonicalLegAzimuths == null || canonicalLegAzimuths.Length == 0) return 0;
            float angle = math.atan2(posXZ.y, posXZ.x);
            int n = canonicalLegAzimuths.Length;

            for (int i = 0; i < n; i++)
            {
                float a0 = canonicalLegAzimuths[i];
                float a1 = canonicalLegAzimuths[(i + 1) % n];
                float span = math.atan2(math.sin(a1 - a0), math.cos(a1 - a0));
                if (span < 0f) span += 2f * math.PI;

                float diff = math.atan2(math.sin(angle - a0), math.cos(angle - a0));
                if (diff < 0f) diff += 2f * math.PI;

                if (diff <= span) return i;
            }
            return 0;
        }
    }
}
