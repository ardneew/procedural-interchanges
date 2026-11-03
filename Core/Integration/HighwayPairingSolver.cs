using System;
using System.Collections.Generic;
using Colossal.Collections;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using ProceduralInterchanges.Core.Math;
using ProceduralInterchanges.Core.Models;
using ProceduralInterchanges.Systems;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace ProceduralInterchanges.Core.Integration
{
    public static class HighwayPairingSolver
    {
        public static List<HighwayLeg> ScanAndPairNearbyHighways(
            EntityManager em,
            SearchSystem netSearch,
            float3 center,
            float footprintRadius,
            bool leftHandTraffic,
            PrefabSystem prefabSystem = null)
        {
            var detectedLegs = new List<HighwayLeg>();
            var visitedEdges = new HashSet<Entity>();

            // Query roads crossing the footprint boundary ring
            Bounds3 queryBounds = new Bounds3(
                center - new float3(footprintRadius * 1.5f, 50f, footprintRadius * 1.5f),
                center + new float3(footprintRadius * 1.5f, 50f, footprintRadius * 1.5f));

            var results = new NativeList<Entity>(Allocator.Temp);
            var iterator = new NetSearchTreeIterator(results, queryBounds);
            netSearch.GetNetSearchTree(false, out var searchTree).Iterate(ref iterator);

            for (int i = 0; i < results.Length; i++)
            {
                Entity edgeEntity = results[i];
                if (visitedEdges.Contains(edgeEntity) || !em.HasComponent<Edge>(edgeEntity) || !em.HasComponent<Curve>(edgeEntity))
                {
                    continue;
                }

                if (!em.HasComponent<Road>(edgeEntity))
                {
                    continue;
                }

                var legs = PickAndPairThroughHighway(em, netSearch, edgeEntity, center, footprintRadius, leftHandTraffic, autoPairTwin: true, prefabSystem);
                if (legs != null && legs.Count > 0)
                {
                    for (int l = 0; l < legs.Count; l++)
                    {
                        var leg = legs[l];
                        if (leg.Inbound?.SourceEdge != Entity.Null) visitedEdges.Add(leg.Inbound.SourceEdge);
                        if (leg.Outbound?.SourceEdge != Entity.Null) visitedEdges.Add(leg.Outbound.SourceEdge);
                        for (int e = 0; e < leg.InteriorEdgesToDelete.Count; e++)
                        {
                            visitedEdges.Add(leg.InteriorEdgesToDelete[e]);
                        }

                        // Check if a leg with close azimuth already exists
                        bool exists = false;
                        for (int d = 0; d < detectedLegs.Count; d++)
                        {
                            if (math.abs(math.acos(math.clamp(math.dot(detectedLegs[d].LegAxisDirection.xz, leg.LegAxisDirection.xz), -1f, 1f))) < 0.25f)
                            {
                                exists = true;
                                break;
                            }
                        }
                        if (!exists)
                        {
                            detectedLegs.Add(leg);
                        }
                    }
                }
            }

            results.Dispose();

            // Sort legs counter-clockwise by azimuth angle
            detectedLegs.Sort((a, b) => a.AzimuthRad.CompareTo(b.AzimuthRad));
            for (int i = 0; i < detectedLegs.Count; i++)
            {
                detectedLegs[i].LegIndex = i;
            }

            return detectedLegs;
        }

        private struct CrossingInfo
        {
            public Entity Edge;
            public float CutT;
        }

        public static void RefreshLegsForRadius(
            EntityManager em,
            SearchSystem netSearch,
            List<HighwayLeg> legs,
            float3 center,
            float footprintRadius,
            bool leftHandTraffic,
            PrefabSystem prefabSystem)
        {
            if (legs == null || legs.Count == 0) return;

            for (int i = 0; i < legs.Count; i++)
            {
                var leg = legs[i];
                Entity rootEdge = leg.Inbound?.SourceEdge != Entity.Null ? leg.Inbound.SourceEdge : (leg.Outbound?.SourceEdge ?? Entity.Null);
                if (rootEdge == Entity.Null || !em.Exists(rootEdge)) continue;

                var crossings = new List<CrossingInfo>();
                var interiorEdges = new List<Entity>();
                FindCorridorCrossingsAndInterior(em, rootEdge, center, footprintRadius, crossings, interiorEdges);

                CrossingInfo bestCrossing = default;
                float bestAngleDiff = float.MaxValue;
                bool found = false;

                for (int c = 0; c < crossings.Count; c++)
                {
                    var cr = crossings[c];
                    if (!em.Exists(cr.Edge) || !em.HasComponent<Curve>(cr.Edge)) continue;
                    var curve = em.GetComponentData<Curve>(cr.Edge).m_Bezier;
                    float3 pos = CubicBezier3D.Position(curve, cr.CutT);
                    float3 outward = math.normalize(new float3(pos.x - center.x, 0f, pos.z - center.z));
                    float az = math.atan2(outward.z, outward.x);
                    float diff = math.abs(math.atan2(math.sin(az - leg.AzimuthRad), math.cos(az - leg.AzimuthRad)));
                    if (diff < bestAngleDiff)
                    {
                        bestAngleDiff = diff;
                        bestCrossing = cr;
                        found = true;
                    }
                }

                if (found && bestAngleDiff < math.radians(60f))
                {
                    var newLeg = CreateLegFromRoot(em, netSearch, bestCrossing.Edge, bestCrossing.CutT, center, footprintRadius, leftHandTraffic, autoPairTwin: leg.IsSeparatedDualCarriageway, prefabSystem);
                    if (newLeg != null)
                    {
                        leg.AzimuthRad = newLeg.AzimuthRad;
                        leg.LegAxisDirection = newLeg.LegAxisDirection;
                        leg.PortalCenterPosition = newLeg.PortalCenterPosition;
                        leg.PortalInwardTangent = newLeg.PortalInwardTangent;
                        leg.IsSeparatedDualCarriageway = newLeg.IsSeparatedDualCarriageway;
                        leg.CarriagewaySeparation = newLeg.CarriagewaySeparation;
                        leg.Inbound = newLeg.Inbound;
                        leg.Outbound = newLeg.Outbound;
                        leg.InteriorEdgesToDelete = new List<Entity>(interiorEdges);
                    }
                }
            }
        }

        public static List<HighwayLeg> PickAndPairThroughHighway(
            EntityManager em,
            SearchSystem netSearch,
            Entity clickedEdge,
            float3 center,
            ref float footprintRadius,
            bool leftHandTraffic,
            bool autoPairTwin,
            PrefabSystem prefabSystem = null)
        {
            var legs = new List<HighwayLeg>();
            if (!em.HasComponent<Edge>(clickedEdge) || !em.HasComponent<Curve>(clickedEdge))
            {
                return legs;
            }

            var crossings = new List<CrossingInfo>();
            var interiorEdges = new List<Entity>();
            FindCorridorCrossingsAndInterior(em, clickedEdge, center, footprintRadius, crossings, interiorEdges);

            if (crossings.Count == 0)
            {
                // Road might be outside the current footprint radius. Walk corridor up to 500m to find closest point to center.
                var curve = em.GetComponentData<Curve>(clickedEdge).m_Bezier;
                float dA = math.distance(curve.a.xz, center.xz);
                float dD = math.distance(curve.d.xz, center.xz);
                float dMid = math.distance(CubicBezier3D.Position(curve, 0.5f).xz, center.xz);
                float dMin = math.min(dA, math.min(dD, dMid));

                if (dMin > footprintRadius && dMin <= 500f)
                {
                    footprintRadius = math.clamp(dMin + 30f, 120f, 500f);
                    FindCorridorCrossingsAndInterior(em, clickedEdge, center, footprintRadius, crossings, interiorEdges);
                }
            }

            for (int c = 0; c < crossings.Count; c++)
            {
                var info = crossings[c];
                var leg = CreateLegFromRoot(em, netSearch, info.Edge, info.CutT, center, footprintRadius, leftHandTraffic, autoPairTwin, prefabSystem);
                if (leg != null)
                {
                    leg.InteriorEdgesToDelete = new List<Entity>(interiorEdges);

                    bool dup = false;
                    for (int l = 0; l < legs.Count; l++)
                    {
                        if (math.abs(math.acos(math.clamp(math.dot(legs[l].LegAxisDirection.xz, leg.LegAxisDirection.xz), -1f, 1f))) < 0.25f)
                        {
                            dup = true;
                            break;
                        }
                    }
                    if (!dup)
                    {
                        legs.Add(leg);
                    }
                }
            }

            return legs;
        }

        public static List<HighwayLeg> PickAndPairThroughHighway(
            EntityManager em,
            SearchSystem netSearch,
            Entity clickedEdge,
            float3 center,
            float footprintRadius,
            bool leftHandTraffic,
            bool autoPairTwin,
            PrefabSystem prefabSystem = null)
        {
            float r = footprintRadius;
            return PickAndPairThroughHighway(em, netSearch, clickedEdge, center, ref r, leftHandTraffic, autoPairTwin, prefabSystem);
        }

        public static HighwayLeg PickAndPairHighwayFromEdge(
            EntityManager em,
            SearchSystem netSearch,
            Entity clickedEdge,
            float3 center,
            float footprintRadius,
            bool leftHandTraffic,
            bool autoPairTwin,
            PrefabSystem prefabSystem = null)
        {
            var list = PickAndPairThroughHighway(em, netSearch, clickedEdge, center, footprintRadius, leftHandTraffic, autoPairTwin, prefabSystem);
            return (list != null && list.Count > 0) ? list[0] : null;
        }

        private static HighwayLeg CreateLegFromRoot(
            EntityManager em,
            SearchSystem netSearch,
            Entity edgeEntity,
            float cutT,
            float3 center,
            float footprintRadius,
            bool leftHandTraffic,
            bool autoPairTwin,
            PrefabSystem prefabSystem)
        {
            var curveComp = em.GetComponentData<Curve>(edgeEntity);
            var edgeComp = em.GetComponentData<Edge>(edgeEntity);
            var prefabRef = em.GetComponentData<PrefabRef>(edgeEntity);

            var endpoint = CreateCarriagewayEndpoint(em, edgeEntity, curveComp.m_Bezier, edgeComp, prefabRef, center, footprintRadius, cutT, prefabSystem);
            if (endpoint == null)
            {
                return null;
            }

            var leg = new HighwayLeg();
            float3 outwardAxis = math.normalize(new float3(endpoint.PortalPosition.x - center.x, 0f, endpoint.PortalPosition.z - center.z));
            leg.LegAxisDirection = outwardAxis;
            leg.AzimuthRad = math.atan2(outwardAxis.z, outwardAxis.x);

            CarriagewayEndpoint twinEndpoint = null;
            if (autoPairTwin && endpoint.IsOneWay)
            {
                twinEndpoint = FindTwinCarriageway(em, netSearch, endpoint, center, outwardAxis, footprintRadius, leftHandTraffic, prefabSystem);
            }

            if (twinEndpoint != null)
            {
                leg.IsSeparatedDualCarriageway = true;
                leg.CarriagewaySeparation = math.distance(endpoint.PortalPosition.xz, twinEndpoint.PortalPosition.xz);
                leg.PortalCenterPosition = (endpoint.PortalPosition + twinEndpoint.PortalPosition) * 0.5f;

                if (endpoint.IsInboundToCenter)
                {
                    leg.Inbound = endpoint;
                    leg.Outbound = twinEndpoint;
                }
                else
                {
                    leg.Inbound = twinEndpoint;
                    leg.Outbound = endpoint;
                }
            }
            else
            {
                leg.IsSeparatedDualCarriageway = false;
                leg.CarriagewaySeparation = 0f;
                leg.PortalCenterPosition = endpoint.PortalPosition;

                if (!endpoint.IsOneWay)
                {
                    // Two-way single highway leg: carries both inbound and outbound traffic
                    endpoint.IsInboundToCenter = true;
                    leg.Inbound = endpoint;
                    leg.Outbound = null;
                }
                else if (endpoint.IsInboundToCenter)
                {
                    leg.Inbound = endpoint;
                    leg.Outbound = null;
                }
                else
                {
                    leg.Inbound = null;
                    leg.Outbound = endpoint;
                }
            }

            if (leg.Inbound != null && math.lengthsq(leg.Inbound.FlowDirection.xz) > 1e-4f)
            {
                float3 flow = leg.Inbound.FlowDirection;
                leg.PortalInwardTangent = math.normalize(new float3(flow.x, 0f, flow.z));
            }
            else if (leg.Outbound != null && math.lengthsq(leg.Outbound.FlowDirection.xz) > 1e-4f)
            {
                float3 flow = -leg.Outbound.FlowDirection;
                leg.PortalInwardTangent = math.normalize(new float3(flow.x, 0f, flow.z));
            }
            else
            {
                leg.PortalInwardTangent = -leg.LegAxisDirection;
            }

            return leg;
        }

        private static void FindCorridorCrossingsAndInterior(
            EntityManager em,
            Entity startEdge,
            float3 center,
            float radius,
            List<CrossingInfo> outCrossings,
            List<Entity> outInteriorEdges)
        {
            var visitedEdges = new HashSet<Entity>();
            var queue = new Queue<Entity>();

            queue.Enqueue(startEdge);
            visitedEdges.Add(startEdge);

            while (queue.Count > 0)
            {
                Entity curEdge = queue.Dequeue();
                if (!em.Exists(curEdge) || !em.HasComponent<Edge>(curEdge) || !em.HasComponent<Curve>(curEdge)) continue;

                var curveComp = em.GetComponentData<Curve>(curEdge);
                var edgeComp = em.GetComponentData<Edge>(curEdge);

                var roots = new List<float>();
                SolveCircleIntersectionsT(curveComp.m_Bezier, center.xz, radius, roots);

                for (int r = 0; r < roots.Count; r++)
                {
                    float rootT = roots[r];
                    bool dup = false;
                    for (int c = 0; c < outCrossings.Count; c++)
                    {
                        if (outCrossings[c].Edge == curEdge && math.abs(outCrossings[c].CutT - rootT) < 0.05f)
                        {
                            dup = true;
                            break;
                        }
                    }
                    if (!dup)
                    {
                        outCrossings.Add(new CrossingInfo { Edge = curEdge, CutT = rootT });
                    }
                }

                float distA = math.distance(curveComp.m_Bezier.a.xz, center.xz);
                float distD = math.distance(curveComp.m_Bezier.d.xz, center.xz);

                if (distA < radius && distD < radius)
                {
                    if (!outInteriorEdges.Contains(curEdge))
                    {
                        outInteriorEdges.Add(curEdge);
                    }
                }

                // Walk connected collinear edges if either end is within search horizon (up to 500m)
                if (distA < 500f || distD < 500f)
                {
                    WalkConnectedCollinear(em, curEdge, edgeComp.m_Start, curveComp.m_Bezier, true, visitedEdges, queue);
                    WalkConnectedCollinear(em, curEdge, edgeComp.m_End, curveComp.m_Bezier, false, visitedEdges, queue);
                }
            }
        }

        private static void WalkConnectedCollinear(
            EntityManager em,
            Entity curEdge,
            Entity node,
            Bezier4x3 curCurve,
            bool isStart,
            HashSet<Entity> visited,
            Queue<Entity> queue)
        {
            if (node == Entity.Null || !em.Exists(node) || !em.HasBuffer<ConnectedEdge>(node)) return;

            float3 curTangent = CubicBezier3D.Tangent(curCurve, isStart ? 0f : 1f);
            float3 curDirLeavingNode = isStart ? -math.normalize(curTangent) : math.normalize(curTangent);

            var connBuf = em.GetBuffer<ConnectedEdge>(node);
            for (int i = 0; i < connBuf.Length; i++)
            {
                Entity nextEdge = connBuf[i].m_Edge;
                if (nextEdge == curEdge || nextEdge == Entity.Null || visited.Contains(nextEdge)) continue;
                if (!em.Exists(nextEdge) || !em.HasComponent<Edge>(nextEdge) || !em.HasComponent<Curve>(nextEdge)) continue;

                var nextEdgeComp = em.GetComponentData<Edge>(nextEdge);
                var nextCurve = em.GetComponentData<Curve>(nextEdge).m_Bezier;

                bool nextStartsAtNode = nextEdgeComp.m_Start == node;
                float3 nextTangent = CubicBezier3D.Tangent(nextCurve, nextStartsAtNode ? 0f : 1f);
                float3 nextDirLeavingNode = nextStartsAtNode ? math.normalize(nextTangent) : -math.normalize(nextTangent);

                // Collinear continuation through node means opposite directions leaving node (dot < -0.65f)
                if (math.dot(curDirLeavingNode, nextDirLeavingNode) < -0.65f)
                {
                    visited.Add(nextEdge);
                    queue.Enqueue(nextEdge);
                }
            }
        }

        private static CarriagewayEndpoint CreateCarriagewayEndpoint(
            EntityManager em,
            Entity edgeEntity,
            Bezier4x3 curve,
            Edge edgeComp,
            PrefabRef prefabRef,
            float3 center,
            float footprintRadius,
            float cutT,
            PrefabSystem prefabSystem)
        {
            float len = CubicBezier3D.Length(curve);
            bool attachToNode = false;
            Entity attachNode = Entity.Null;

            if (cutT * len < 8f)
            {
                cutT = 0f;
                attachToNode = true;
                attachNode = edgeComp.m_Start;
            }
            else if ((1f - cutT) * len < 8f)
            {
                cutT = 1f;
                attachToNode = true;
                attachNode = edgeComp.m_End;
            }

            float3 portalPos = CubicBezier3D.Position(curve, cutT);
            float3 tangent = math.normalize(CubicBezier3D.Tangent(curve, cutT));

            // Inbound means tangent points towards center
            float3 toCenter = center - portalPos;
            bool isInbound = math.dot(tangent, toCenter) > 0f;

            // Extract lane count, one-way flag, and width
            int laneCount = 2;
            bool isOneWay = true;
            float width = 16f;
            float speedLimit = 27.7f;
            string prefabName = "Highway";

            if (prefabSystem != null && prefabSystem.TryGetPrefab<PrefabBase>(prefabRef.m_Prefab, out var pb))
            {
                prefabName = pb.name;
            }

            if (em.HasComponent<RoadData>(prefabRef.m_Prefab))
            {
                var roadData = em.GetComponentData<RoadData>(prefabRef.m_Prefab);
                speedLimit = roadData.m_SpeedLimit;
            }

            if (em.HasComponent<NetGeometryData>(prefabRef.m_Prefab))
            {
                var geom = em.GetComponentData<NetGeometryData>(prefabRef.m_Prefab);
                width = geom.m_DefaultWidth;
            }

            // Inspect lanes authoritatively using composition buffers
            bool inspected = false;
            if (em.HasComponent<Composition>(edgeEntity))
            {
                Entity compEdge = em.GetComponentData<Composition>(edgeEntity).m_Edge;
                if (compEdge != Entity.Null && em.Exists(compEdge) && em.HasBuffer<NetCompositionLane>(compEdge))
                {
                    var lanes = em.GetBuffer<NetCompositionLane>(compEdge);
                    int fLanes = 0;
                    int bLanes = 0;
                    for (int l = 0; l < lanes.Length; l++)
                    {
                        var lane = lanes[l];
                        if ((lane.m_Flags & (LaneFlags.Master | LaneFlags.Road)) == LaneFlags.Road && (lane.m_Flags & LaneFlags.Slave) == 0)
                        {
                            if ((lane.m_Flags & LaneFlags.Invert) == 0)
                                fLanes++;
                            else
                                bLanes++;
                        }
                    }
                    if (fLanes > 0 || bLanes > 0)
                    {
                        laneCount = math.max(1, fLanes);
                        isOneWay = (bLanes == 0);
                        inspected = true;
                    }
                }
            }

            if (!inspected)
            {
                HighwayPrefabCatalogSystem.InspectRoadPrefabLanes(em, prefabRef.m_Prefab, prefabName, out int fLanes, out int bLanes, out isOneWay);
                laneCount = math.max(1, fLanes);
            }

            return new CarriagewayEndpoint
            {
                SourceEdge = edgeEntity,
                SourcePrefab = prefabRef.m_Prefab,
                PrefabName = prefabName,
                OriginalCurve = curve,
                CutCurveT = cutT,
                PortalPosition = portalPos,
                FlowDirection = isInbound ? tangent : -tangent,
                IsInboundToCenter = isInbound,
                IsOneWay = isOneWay,
                LaneCount = laneCount,
                Width = width,
                SpeedLimitMs = speedLimit,
                AttachToNode = attachToNode,
                AttachNodeEntity = attachNode
            };
        }

        private static CarriagewayEndpoint FindTwinCarriageway(
            EntityManager em,
            SearchSystem netSearch,
            CarriagewayEndpoint primary,
            float3 center,
            float3 outwardAxis,
            float footprintRadius,
            bool leftHandTraffic,
            PrefabSystem prefabSystem)
        {
            Bounds3 searchBounds = new Bounds3(
                primary.PortalPosition - new float3(55f, 20f, 55f),
                primary.PortalPosition + new float3(55f, 20f, 55f));

            var candidateList = new NativeList<Entity>(Allocator.Temp);
            var it = new NetSearchTreeIterator(candidateList, searchBounds);
            netSearch.GetNetSearchTree(false, out var tree).Iterate(ref it);

            CarriagewayEndpoint bestTwin = null;
            float bestScore = float.MaxValue;
            float handednessSign = leftHandTraffic ? -1f : 1f;

            // In primary flow direction, normal to left
            float3 leftNormal = new float3(-primary.FlowDirection.z, 0f, primary.FlowDirection.x);

            for (int i = 0; i < candidateList.Length; i++)
            {
                Entity candidate = candidateList[i];
                if (candidate == primary.SourceEdge || !em.HasComponent<Edge>(candidate) || !em.HasComponent<Curve>(candidate))
                {
                    continue;
                }

                var curve = em.GetComponentData<Curve>(candidate).m_Bezier;
                var edge = em.GetComponentData<Edge>(candidate);
                var prefab = em.GetComponentData<PrefabRef>(candidate);

                var roots = new List<float>();
                SolveCircleIntersectionsT(curve, center.xz, footprintRadius, roots);
                if (roots.Count == 0) continue;

                for (int r = 0; r < roots.Count; r++)
                {
                    var ep = CreateCarriagewayEndpoint(em, candidate, curve, edge, prefab, center, footprintRadius, roots[r], prefabSystem);
                    if (ep == null) continue;

                    // Twin must be one-way
                    if (!ep.IsOneWay) continue;

                    // Anti-parallel flow check
                    float flowDot = math.dot(primary.FlowDirection, ep.FlowDirection);
                    if (flowDot > -0.70f) continue; // Must flow in opposite direction

                    // Lateral separation check
                    float3 delta = ep.PortalPosition - primary.PortalPosition;
                    float latDist = math.abs(math.dot(delta, new float3(outwardAxis.z, 0f, -outwardAxis.x)));
                    float longDist = math.abs(math.dot(delta, outwardAxis));

                    // Verify lateral offset matches traffic handedness
                    // For RHT, opposing carriageway must be on the left (dot(delta, leftNormal) > 0)
                    // For LHT, opposing carriageway must be on the right (dot(delta, leftNormal) < 0)
                    if (math.dot(delta, leftNormal) * handednessSign <= 0f)
                    {
                        continue;
                    }

                    if (latDist >= 2.0f && latDist <= 48.0f && longDist <= 30.0f)
                    {
                        float score = latDist + 10f * (1f + flowDot);
                        if (score < bestScore)
                        {
                            bestScore = score;
                            bestTwin = ep;
                        }
                    }
                }
            }

            candidateList.Dispose();
            return bestTwin;
        }

        public static void SolveCircleIntersectionsT(in Bezier4x3 curve, float2 centerXZ, float radius, List<float> outRoots)
        {
            float rSq = radius * radius;
            const int kSamples = 40;
            float prevT = 0f;
            float prevVal = math.distancesq(curve.a.xz, centerXZ) - rSq;

            for (int i = 1; i <= kSamples; i++)
            {
                float t = (float)i / kSamples;
                float3 pos = CubicBezier3D.Position(curve, t);
                float val = math.distancesq(pos.xz, centerXZ) - rSq;

                if (prevVal * val <= 0f)
                {
                    float tLow = prevT;
                    float tHigh = t;
                    for (int iter = 0; iter < 8; iter++)
                    {
                        float tMid = (tLow + tHigh) * 0.5f;
                        float valMid = math.distancesq(CubicBezier3D.Position(curve, tMid).xz, centerXZ) - rSq;
                        if (prevVal * valMid <= 0f)
                        {
                            tHigh = tMid;
                        }
                        else
                        {
                            tLow = tMid;
                            prevVal = valMid;
                        }
                    }
                    float root = (tLow + tHigh) * 0.5f;
                    bool dup = false;
                    for (int r = 0; r < outRoots.Count; r++)
                    {
                        if (math.abs(outRoots[r] - root) < 0.02f) { dup = true; break; }
                    }
                    if (!dup) outRoots.Add(root);
                }

                prevT = t;
                prevVal = val;
            }
        }
    }

    public struct NetSearchTreeIterator : INativeQuadTreeIterator<Entity, QuadTreeBoundsXZ>
    {
        private NativeList<Entity> m_Results;
        private Bounds3 m_Bounds;

        public NetSearchTreeIterator(NativeList<Entity> results, Bounds3 bounds)
        {
            m_Results = results;
            m_Bounds = bounds;
        }

        public bool Intersect(QuadTreeBoundsXZ bounds)
        {
            return MathUtils.Intersect(bounds.m_Bounds.xz, m_Bounds.xz);
        }

        public void Iterate(QuadTreeBoundsXZ bounds, Entity item)
        {
            if (MathUtils.Intersect(bounds.m_Bounds, m_Bounds))
            {
                m_Results.Add(item);
            }
        }
    }
}
