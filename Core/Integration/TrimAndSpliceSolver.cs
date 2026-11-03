using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Net;
using Game.Prefabs;
using ProceduralInterchanges.Core.Math;
using ProceduralInterchanges.Core.Models;
using Unity.Entities;
using Unity.Mathematics;

namespace ProceduralInterchanges.Core.Integration
{
    public static class TrimAndSpliceSolver
    {
        public static void ComputeBoundaryCutsAndOuterReplacements(
            EntityManager em,
            IReadOnlyList<HighwayLeg> legs,
            float3 center,
            float footprintRadius,
            Func<float2, float> sampleTerrainHeight,
            List<PlannedNetCourse> outOuterReplacements,
            List<Entity> outEdgesToDelete,
            List<RestorableEdgeSnapshot> outSnapshots)
        {
            if (legs == null) return;

            var cutsByEdge = new Dictionary<Entity, List<CarriagewayEndpoint>>();
            var processedEdges = new HashSet<Entity>();

            void RegisterEndpoint(CarriagewayEndpoint ep)
            {
                if (ep == null || ep.SourceEdge == Entity.Null || !em.Exists(ep.SourceEdge)) return;
                if (!cutsByEdge.TryGetValue(ep.SourceEdge, out var list))
                {
                    list = new List<CarriagewayEndpoint>();
                    cutsByEdge[ep.SourceEdge] = list;
                }
                list.Add(ep);
            }

            for (int i = 0; i < legs.Count; i++)
            {
                var leg = legs[i];
                RegisterEndpoint(leg.Inbound);
                RegisterEndpoint(leg.Outbound);
            }

            foreach (var kvp in cutsByEdge)
            {
                Entity edgeEntity = kvp.Key;
                List<CarriagewayEndpoint> endpoints = kvp.Value;
                if (!em.Exists(edgeEntity) || !em.HasComponent<Edge>(edgeEntity) || !em.HasComponent<Curve>(edgeEntity)) continue;

                // Always snapshot all touched host edges so they can be identified and restored if split or modified
                RecordSnapshot(em, edgeEntity, outSnapshots);

                // When a SourceEdge is only referenced by a single leg, do NOT delete it.
                // Leave it intact and let CS2 natively weld/split at CutCurveT.
                if (endpoints.Count < 2)
                {
                    continue;
                }

                var origCurve = em.GetComponentData<Curve>(edgeEntity).m_Bezier;
                var edgeComp = em.GetComponentData<Edge>(edgeEntity);

                bool allAttachToNode = true;
                for (int e = 0; e < endpoints.Count; e++)
                {
                    if (!endpoints[e].AttachToNode)
                    {
                        allAttachToNode = false;
                        break;
                    }
                }

                bool startOutside = math.distance(origCurve.a.xz, center.xz) > footprintRadius + 1.0f;
                bool endOutside = math.distance(origCurve.d.xz, center.xz) > footprintRadius + 1.0f;

                if (allAttachToNode && (startOutside || endOutside))
                {
                    // Fully attached to existing node, no need to cut and replace
                    continue;
                }

                processedEdges.Add(edgeEntity);
                outEdgesToDelete.Add(edgeEntity);

                // Collect all cut parameters on this edge (from endpoints and circle intersections)
                var cutTs = new List<float>();
                for (int e = 0; e < endpoints.Count; e++)
                {
                    float t = endpoints[e].CutCurveT;
                    if (t > 0.02f && t < 0.98f && !cutTs.Contains(t))
                    {
                        cutTs.Add(t);
                    }
                }

                // Also check any geometric roots with the boundary circle
                var roots = new List<float>();
                HighwayPairingSolver.SolveCircleIntersectionsT(origCurve, center.xz, footprintRadius, roots);
                for (int r = 0; r < roots.Count; r++)
                {
                    float rt = roots[r];
                    if (rt > 0.02f && rt < 0.98f && !cutTs.Contains(rt))
                    {
                        cutTs.Add(rt);
                    }
                }

                cutTs.Sort();

                CarriagewayEndpoint primaryEp = endpoints[0];

                if (cutTs.Count == 0)
                {
                    // If neither start nor end is outside, the entire edge is inside and deleted
                    continue;
                }

                float tMin = cutTs[0];
                float tMax = cutTs[cutTs.Count - 1];

                // Check if the original edge is elevated above ground
                float terrStart = sampleTerrainHeight(origCurve.a.xz);
                float terrEnd = sampleTerrainHeight(origCurve.d.xz);
                bool isOrigElevated = (origCurve.a.y - terrStart > 3.0f) || (origCurve.d.y - terrEnd > 3.0f);

                // Find closest endpoint to tMin and tMax
                CarriagewayEndpoint epStart = endpoints[0];
                float bestDistStart = float.MaxValue;
                CarriagewayEndpoint epEnd = endpoints[0];
                float bestDistEnd = float.MaxValue;

                for (int e = 0; e < endpoints.Count; e++)
                {
                    float dS = math.abs(endpoints[e].CutCurveT - tMin);
                    if (dS < bestDistStart) { bestDistStart = dS; epStart = endpoints[e]; }
                    float dE = math.abs(endpoints[e].CutCurveT - tMax);
                    if (dE < bestDistEnd) { bestDistEnd = dE; epEnd = endpoints[e]; }
                }

                // 1. Left outer portion [0, tMin]
                if (tMin > 0.02f)
                {
                    CubicBezier3D.Split(origCurve, tMin, out var leftOuter, out _);
                    float3 endTan = math.normalize(leftOuter.d - leftOuter.c);
                    leftOuter.d = new float3(epStart.PortalPosition.x, center.y, epStart.PortalPosition.z);
                    leftOuter.c = leftOuter.d - endTan * math.distance(leftOuter.c, leftOuter.d);
                    epStart.PortalPosition = leftOuter.d;
                    float lenLeft = CubicBezier3D.Length(leftOuter);

                    if ((startOutside || lenLeft >= 2.0f) && lenLeft >= 1.5f)
                    {
                        var leftCourse = new PlannedNetCourse
                        {
                            Id = $"outer_repl_{edgeEntity.Index}_start",
                            Role = NetRole.TransitionTaper,
                            Curve = leftOuter,
                            StackLevel = isOrigElevated ? 1 : 0,
                            ForceElevated = isOrigElevated,
                            Lanes = epStart.LaneCount,
                            IsOneWay = epStart.IsOneWay,
                            ResolvedPrefabEntity = epStart.SourcePrefab,
                            ResolvedPrefabName = epStart.PrefabName,
                            StartAttachEntity = edgeComp.m_Start,
                            StartAttachIsNode = true,
                            EndAttachEntity = Entity.Null,
                            EndAttachIsNode = false
                        };

                        if (isOrigElevated)
                        {
                            leftCourse.StartElevation = new float2(math.max(0f, origCurve.a.y - terrStart));
                            leftCourse.EndElevation = new float2(math.max(0f, leftOuter.d.y - sampleTerrainHeight(leftOuter.d.xz)));
                        }

                        outOuterReplacements.Add(leftCourse);

                        // Snap any endpoint near tMin to leftOuter.d
                        for (int e = 0; e < endpoints.Count; e++)
                        {
                            if (math.abs(endpoints[e].CutCurveT - tMin) < 0.12f)
                            {
                                endpoints[e].PortalPosition = leftOuter.d;
                            }
                        }
                    }
                }

                // 2. Right outer portion [tMax, 1]
                if (tMax < 0.98f)
                {
                    CubicBezier3D.Split(origCurve, tMax, out _, out var rightOuter);
                    float3 startTan = math.normalize(rightOuter.b - rightOuter.a);
                    rightOuter.a = new float3(epEnd.PortalPosition.x, center.y, epEnd.PortalPosition.z);
                    rightOuter.b = rightOuter.a + startTan * math.distance(rightOuter.b, rightOuter.a);
                    epEnd.PortalPosition = rightOuter.a;
                    float lenRight = CubicBezier3D.Length(rightOuter);

                    if ((endOutside || lenRight >= 2.0f) && lenRight >= 1.5f)
                    {
                        var rightCourse = new PlannedNetCourse
                        {
                            Id = $"outer_repl_{edgeEntity.Index}_end",
                            Role = NetRole.TransitionTaper,
                            Curve = rightOuter,
                            StackLevel = isOrigElevated ? 1 : 0,
                            ForceElevated = isOrigElevated,
                            Lanes = epEnd.LaneCount,
                            IsOneWay = epEnd.IsOneWay,
                            ResolvedPrefabEntity = epEnd.SourcePrefab,
                            ResolvedPrefabName = epEnd.PrefabName,
                            StartAttachEntity = Entity.Null,
                            StartAttachIsNode = false,
                            EndAttachEntity = edgeComp.m_End,
                            EndAttachIsNode = true
                        };

                        if (isOrigElevated)
                        {
                            rightCourse.StartElevation = new float2(math.max(0f, rightOuter.a.y - sampleTerrainHeight(rightOuter.a.xz)));
                            rightCourse.EndElevation = new float2(math.max(0f, origCurve.d.y - terrEnd));
                        }

                        outOuterReplacements.Add(rightCourse);

                        // Snap any endpoint near tMax to rightOuter.a
                        for (int e = 0; e < endpoints.Count; e++)
                        {
                            if (math.abs(endpoints[e].CutCurveT - tMax) < 0.12f)
                            {
                                endpoints[e].PortalPosition = rightOuter.a;
                            }
                        }
                    }
                }

                // Per Phase 4 Step 4.2: edgeEntity is deleted, so clear attach references on its endpoints
                // so the interchange terminal courses weld via bit-for-bit m_Position equality at PortalPosition
                for (int e = 0; e < endpoints.Count; e++)
                {
                    endpoints[e].AttachToNode = false;
                    endpoints[e].AttachNodeEntity = Entity.Null;
                    endpoints[e].SourceEdge = Entity.Null;
                }
            }

            // Also add any recorded interior edges
            for (int i = 0; i < legs.Count; i++)
            {
                var leg = legs[i];
                if (leg.InteriorEdgesToDelete != null)
                {
                    for (int e = 0; e < leg.InteriorEdgesToDelete.Count; e++)
                    {
                        var intEdge = leg.InteriorEdgesToDelete[e];
                        if (processedEdges.Add(intEdge) && em.Exists(intEdge) && em.HasComponent<Edge>(intEdge))
                        {
                            outEdgesToDelete.Add(intEdge);
                            RecordSnapshot(em, intEdge, outSnapshots);
                        }
                    }
                }
            }
        }

        private static void RecordSnapshot(EntityManager em, Entity edgeEntity, List<RestorableEdgeSnapshot> outSnapshots)
        {
            if (!em.Exists(edgeEntity) || !em.HasComponent<Edge>(edgeEntity) || !em.HasComponent<Curve>(edgeEntity))
            {
                return;
            }

            var edge = em.GetComponentData<Edge>(edgeEntity);
            var curve = em.GetComponentData<Curve>(edgeEntity).m_Bezier;
            var prefab = em.GetComponentData<PrefabRef>(edgeEntity).m_Prefab;

            float2 startElev = float2.zero;
            float2 endElev = float2.zero;

            if (em.HasComponent<Elevation>(edge.m_Start))
            {
                startElev = em.GetComponentData<Elevation>(edge.m_Start).m_Elevation;
            }
            if (em.HasComponent<Elevation>(edge.m_End))
            {
                endElev = em.GetComponentData<Elevation>(edge.m_End).m_Elevation;
            }

            CompositionFlags upgrades = default;
            if (em.HasComponent<Upgraded>(edgeEntity))
            {
                upgrades = em.GetComponentData<Upgraded>(edgeEntity).m_Flags;
            }

            outSnapshots.Add(new RestorableEdgeSnapshot
            {
                OriginalEntity = edgeEntity,
                PrefabEntity = prefab,
                PrefabName = "Highway",
                Curve = curve,
                StartElevation = startElev,
                EndElevation = endElev,
                Upgrades = upgrades,
                StartNode = edge.m_Start,
                EndNode = edge.m_End
            });
        }
    }
}
