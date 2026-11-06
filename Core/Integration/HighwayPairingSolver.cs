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

            if (leg.IsSeparatedDualCarriageway)
            {
                AlignLegToCommonPerpendicularPlane(em, leg, center);
            }

            return leg;
        }

        public static void AlignLegToCommonPerpendicularPlane(EntityManager em, HighwayLeg leg, float3 center)
        {
            if (leg == null) return;
            if (!leg.IsSeparatedDualCarriageway || leg.Inbound == null || leg.Outbound == null)
            {
                leg.LongitudinalStaggerMeters = 0f;
                leg.TangentConvergenceRad = 0f;
                return;
            }

            var inEp = leg.Inbound;
            var outEp = leg.Outbound;

            // Inward tangents
            float2 inTanXZ = math.lengthsq(inEp.InwardTangent.xz) > 1e-4f ? math.normalize(inEp.InwardTangent.xz) : -math.normalize(leg.LegAxisDirection.xz);
            float2 outTanXZ = math.lengthsq(outEp.InwardTangent.xz) > 1e-4f ? math.normalize(outEp.InwardTangent.xz) : -math.normalize(leg.LegAxisDirection.xz);

            // Compute convergence angle
            float cross = inTanXZ.x * outTanXZ.y - inTanXZ.y * outTanXZ.x;
            leg.TangentConvergenceRad = math.asin(math.clamp(cross, -1f, 1f));

            float dIn = math.distance(inEp.PortalPosition.xz, center.xz);
            float dOut = math.distance(outEp.PortalPosition.xz, center.xz);

            CarriagewayEndpoint farEp = dIn >= dOut ? inEp : outEp;
            CarriagewayEndpoint nearEp = dIn >= dOut ? outEp : inEp;

            float2 approachAxis = math.lengthsq(leg.LegAxisDirection.xz) > 1e-4f ? math.normalize(leg.LegAxisDirection.xz) : math.normalize(farEp.PortalPosition.xz - center.xz);
            leg.LongitudinalStaggerMeters = math.abs(math.dot((nearEp.PortalPosition - farEp.PortalPosition).xz, approachAxis));

            if (leg.LongitudinalStaggerMeters < 1.0f)
            {
                return; // Already flush within 1m
            }

            // Boundary plane anchored at farEp.PortalPosition
            // Outward normal points outward along leg axis / opposite to inward tangent
            float2 planePt = farEp.PortalPosition.xz;
            float2 farInward = math.lengthsq(farEp.InwardTangent.xz) > 1e-4f ? math.normalize(farEp.InwardTangent.xz) : -approachAxis;
            float2 planeNormal = -farInward; // Points outward away from center

            // Find intersection between nearEp.OriginalCurve and plane (x - planePt) . planeNormal = 0
            var curve = nearEp.OriginalCurve;
            float bestT = -1f;
            float bestDist = float.MaxValue;

            // Sample along curve to find sign change of dot(B(t).xz - planePt, planeNormal)
            const int kSamples = 32;
            float prevT = 0f;
            float prevVal = math.dot(CubicBezier3D.Position(curve, 0f).xz - planePt, planeNormal);

            for (int s = 1; s <= kSamples; s++)
            {
                float t = (float)s / kSamples;
                float val = math.dot(CubicBezier3D.Position(curve, t).xz - planePt, planeNormal);
                if ((prevVal <= 0f && val >= 0f) || (prevVal >= 0f && val <= 0f))
                {
                    // Root is in [prevT, t]
                    float t0 = prevT;
                    float t1 = t;
                    for (int iter = 0; iter < 12; iter++)
                    {
                        float midT = 0.5f * (t0 + t1);
                        float midVal = math.dot(CubicBezier3D.Position(curve, midT).xz - planePt, planeNormal);
                        if (math.abs(midVal) < 1e-3f) { t0 = midT; break; }
                        if ((prevVal <= 0f && midVal >= 0f) || (prevVal >= 0f && midVal <= 0f))
                        {
                            t1 = midT;
                        }
                        else
                        {
                            t0 = midT;
                            prevVal = midVal;
                        }
                    }
                    float candidateT = t0;
                    float3 pos = CubicBezier3D.Position(curve, candidateT);
                    float dCenter = math.distance(pos.xz, center.xz);
                    // We want root close to farEp's distance
                    float dDiff = math.abs(dCenter - math.distance(planePt, center.xz));
                    if (dDiff < bestDist)
                    {
                        bestDist = dDiff;
                        bestT = candidateT;
                    }
                }
                prevT = t;
                prevVal = val;
            }

            if (bestT >= 0f && bestT <= 1f)
            {
                nearEp.CutCurveT = bestT;
                nearEp.PortalPosition = CubicBezier3D.Position(curve, bestT);
                float3 tan = CubicBezier3D.Tangent(curve, bestT);
                if (math.lengthsq(tan.xz) > 1e-4f)
                {
                    tan = math.normalize(new float3(tan.x, 0f, tan.z));
                    if (math.dot(tan.xz, nearEp.InwardTangent.xz) < 0f) tan = -tan;
                    nearEp.InwardTangent = tan;
                }
                nearEp.RequiresOuterTrimReplacement = true;

                // Determine whether start or end is outer
                bool startOutside = math.distance(curve.a.xz, center.xz) > math.distance(curve.d.xz, center.xz);
                nearEp.KeepStartPortionOnTrim = startOutside;

                leg.PortalCenterPosition = (inEp.PortalPosition + outEp.PortalPosition) * 0.5f;
                leg.CarriagewaySeparation = math.distance(inEp.PortalPosition.xz, outEp.PortalPosition.xz);
            }
            else
            {
                // Fallback: If root was not found within [0, 1] (e.g. host edge ends just before/after plane),
                // project the nearest endpoint along its tangent onto plane
                float3 pEnd = CubicBezier3D.Position(curve, 1f);
                float3 pStart = CubicBezier3D.Position(curve, 0f);
                float val0 = math.dot(pStart.xz - planePt, planeNormal);
                float val1 = math.dot(pEnd.xz - planePt, planeNormal);

                float extT = math.abs(val0) <= math.abs(val1) ? 0f : 1f;
                float3 extPt = extT == 0f ? pStart : pEnd;
                float3 extTan = CubicBezier3D.Tangent(curve, extT);
                float2 extTanXZ = math.lengthsq(extTan.xz) > 1e-4f ? math.normalize(extTan.xz) : -planeNormal;
                float denom = math.dot(extTanXZ, planeNormal);

                if (math.abs(denom) > 1e-3f)
                {
                    float sProj = -math.dot(extPt.xz - planePt, planeNormal) / denom;
                    if (math.abs(sProj) < 60f)
                    {
                        nearEp.PortalPosition = extPt + new float3(extTanXZ.x, 0f, extTanXZ.y) * sProj;
                        nearEp.InwardTangent = math.dot(extTanXZ, nearEp.InwardTangent.xz) >= 0f
                            ? new float3(extTanXZ.x, 0f, extTanXZ.y)
                            : new float3(-extTanXZ.x, 0f, -extTanXZ.y);
                        nearEp.RequiresOuterTrimReplacement = true;
                        nearEp.KeepStartPortionOnTrim = math.distance(curve.a.xz, center.xz) > math.distance(curve.d.xz, center.xz);
                        leg.PortalCenterPosition = (inEp.PortalPosition + outEp.PortalPosition) * 0.5f;
                        leg.CarriagewaySeparation = math.distance(inEp.PortalPosition.xz, outEp.PortalPosition.xz);
                    }
                }
            }
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
        private static int CountActiveConnectedEdges(EntityManager em, Entity node)
        {
            if (node == Entity.Null || !em.Exists(node) || !em.HasBuffer<ConnectedEdge>(node)) return 0;
            var buf = em.GetBuffer<ConnectedEdge>(node);
            int count = 0;
            for (int i = 0; i < buf.Length; i++)
            {
                Entity e = buf[i].m_Edge;
                if (e != Entity.Null && em.Exists(e) && !em.HasComponent<Deleted>(e))
                {
                    count++;
                }
            }
            return count;
        }

        public static HighwayLeg PickHighwayEndpointAtClick(
            EntityManager em,
            SearchSystem netSearchSystem,
            PrefabSystem prefabSystem,
            Entity clickedEdge,
            float3 clickWorldPos,
            bool leftHandTraffic,
            bool autoPairTwin)
        {
            if (clickedEdge == Entity.Null || !em.Exists(clickedEdge) || !em.HasComponent<Edge>(clickedEdge) || !em.HasComponent<Curve>(clickedEdge))
            {
                return null;
            }

            var edge = em.GetComponentData<Edge>(clickedEdge);
            var curve = em.GetComponentData<Curve>(clickedEdge).m_Bezier;
            var prefabRef = em.GetComponentData<PrefabRef>(clickedEdge);

            // Find closest t on curve to clickWorldPos.xz
            float bestT = 0f;
            float bestDistSq = float.MaxValue;
            for (int i = 0; i <= 64; i++)
            {
                float t = i / 64f;
                float3 p = CubicBezier3D.Position(curve, t);
                float dSq = math.distancesq(p.xz, clickWorldPos.xz);
                if (dSq < bestDistSq)
                {
                    bestDistSq = dSq;
                    bestT = t;
                }
            }

            // Ternary refinement
            float tMin = math.max(0f, bestT - 0.04f);
            float tMax = math.min(1f, bestT + 0.04f);
            for (int iter = 0; iter < 6; iter++)
            {
                float t1 = math.lerp(tMin, tMax, 0.33333334f);
                float t2 = math.lerp(tMin, tMax, 0.6666667f);
                float d1 = math.distancesq(CubicBezier3D.Position(curve, t1).xz, clickWorldPos.xz);
                float d2 = math.distancesq(CubicBezier3D.Position(curve, t2).xz, clickWorldPos.xz);
                if (d1 < d2) tMax = t2;
                else tMin = t1;
            }
            float tClick = (tMin + tMax) * 0.5f;
            float curveLen = CubicBezier3D.Length(curve);
            float dStart = tClick * curveLen;
            float dEnd = (1f - tClick) * curveLen;

            float cutT;
            bool attachToNode;
            Entity attachNode;
            float3 portalPos;

            if (CountActiveConnectedEdges(em, edge.m_Start) == 1 && dStart <= 28f)
            {
                cutT = 0f;
                attachToNode = true;
                attachNode = edge.m_Start;
                portalPos = em.GetComponentData<Node>(edge.m_Start).m_Position;
            }
            else if (CountActiveConnectedEdges(em, edge.m_End) == 1 && dEnd <= 28f)
            {
                cutT = 1f;
                attachToNode = true;
                attachNode = edge.m_End;
                portalPos = em.GetComponentData<Node>(edge.m_End).m_Position;
            }
            else
            {
                cutT = math.clamp(tClick, 0.05f, 0.95f);
                attachToNode = false;
                attachNode = Entity.Null;
                portalPos = CubicBezier3D.Position(curve, cutT);
            }

            // Road metadata
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
                speedLimit = em.GetComponentData<RoadData>(prefabRef.m_Prefab).m_SpeedLimit;
            }
            if (em.HasComponent<NetGeometryData>(prefabRef.m_Prefab))
            {
                width = em.GetComponentData<NetGeometryData>(prefabRef.m_Prefab).m_DefaultWidth;
            }

            bool inspected = false;
            if (em.HasComponent<Composition>(clickedEdge))
            {
                Entity compEdge = em.GetComponentData<Composition>(clickedEdge).m_Edge;
                if (compEdge != Entity.Null && em.Exists(compEdge) && em.HasBuffer<NetCompositionLane>(compEdge))
                {
                    var lanes = em.GetBuffer<NetCompositionLane>(compEdge);
                    int fLanes = 0, bLanes = 0;
                    for (int l = 0; l < lanes.Length; l++)
                    {
                        var lane = lanes[l];
                        if ((lane.m_Flags & (LaneFlags.Master | LaneFlags.Road)) == LaneFlags.Road && (lane.m_Flags & LaneFlags.Slave) == 0)
                        {
                            if ((lane.m_Flags & LaneFlags.Invert) == 0) fLanes++;
                            else bLanes++;
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

            float3 tangent = math.normalize(CubicBezier3D.Tangent(curve, cutT));

            var endpoint = new CarriagewayEndpoint
            {
                SourceEdge = clickedEdge,
                SourcePrefab = prefabRef.m_Prefab,
                PrefabName = prefabName,
                OriginalCurve = curve,
                CutCurveT = cutT,
                PortalPosition = portalPos,
                FlowDirection = tangent,
                InwardTangent = tangent,
                Direction = CarriagewayDirection.Inbound,
                IsInboundToCenter = true,
                IsOneWay = isOneWay,
                LaneCount = laneCount,
                Width = width,
                SpeedLimitMs = speedLimit,
                AttachToNode = attachToNode,
                AttachNodeEntity = attachNode
            };

            CarriagewayEndpoint twinEndpoint = null;
            if (autoPairTwin && isOneWay && netSearchSystem != null)
            {
                Bounds3 searchBounds = new Bounds3(portalPos - new float3(48f, 20f, 48f), portalPos + new float3(48f, 20f, 48f));
                var candidateList = new NativeList<Entity>(Allocator.Temp);
                var it = new NetSearchTreeIterator(candidateList, searchBounds);
                netSearchSystem.GetNetSearchTree(false, out var tree).Iterate(ref it);

                Entity bestTwinEdge = Entity.Null;
                float bestScore = float.MaxValue;
                Bezier4x3 bestTwinCurve = default;
                Edge bestTwinEdgeComp = default;

                for (int i = 0; i < candidateList.Length; i++)
                {
                    Entity cand = candidateList[i];
                    if (cand == clickedEdge || !em.HasComponent<Edge>(cand) || !em.HasComponent<Curve>(cand)) continue;

                    var candEdge = em.GetComponentData<Edge>(cand);
                    var candCurve = em.GetComponentData<Curve>(cand).m_Bezier;
                    float3 candTan = math.normalize(candCurve.d - candCurve.a);

                    float dot = math.dot(tangent.xz, candTan.xz);
                    if (dot > -0.70f) continue;

                    float candMinDistSq = float.MaxValue;
                    for (int s = 0; s <= 32; s++)
                    {
                        float st = s / 32f;
                        float dSq = math.distancesq(CubicBezier3D.Position(candCurve, st).xz, portalPos.xz);
                        if (dSq < candMinDistSq)
                        {
                            candMinDistSq = dSq;
                        }
                    }

                    float latSep = math.sqrt(candMinDistSq);
                    if (latSep <= 42f)
                    {
                        float score = latSep + 10f * (1f + dot);
                        if (score < bestScore)
                        {
                            bestScore = score;
                            bestTwinEdge = cand;
                            bestTwinCurve = candCurve;
                            bestTwinEdgeComp = candEdge;
                        }
                    }
                }
                candidateList.Dispose();

                if (bestTwinEdge != Entity.Null)
                {
                    float twinCurveLen = CubicBezier3D.Length(bestTwinCurve);
                    float candBestT = 0.5f;
                    float candMinDistSq = float.MaxValue;
                    for (int s = 0; s <= 64; s++)
                    {
                        float st = s / 64f;
                        float dSq = math.distancesq(CubicBezier3D.Position(bestTwinCurve, st).xz, portalPos.xz);
                        if (dSq < candMinDistSq) { candMinDistSq = dSq; candBestT = st; }
                    }

                    float twinCutT;
                    bool twinAttachToNode;
                    Entity twinAttachNode;
                    float3 twinPortalPos;

                    if (CountActiveConnectedEdges(em, bestTwinEdgeComp.m_Start) == 1 && candBestT * twinCurveLen <= 28f)
                    {
                        twinCutT = 0f;
                        twinAttachToNode = true;
                        twinAttachNode = bestTwinEdgeComp.m_Start;
                        twinPortalPos = em.GetComponentData<Node>(bestTwinEdgeComp.m_Start).m_Position;
                    }
                    else if (CountActiveConnectedEdges(em, bestTwinEdgeComp.m_End) == 1 && (1f - candBestT) * twinCurveLen <= 28f)
                    {
                        twinCutT = 1f;
                        twinAttachToNode = true;
                        twinAttachNode = bestTwinEdgeComp.m_End;
                        twinPortalPos = em.GetComponentData<Node>(bestTwinEdgeComp.m_End).m_Position;
                    }
                    else
                    {
                        twinCutT = math.clamp(candBestT, 0.05f, 0.95f);
                        twinAttachToNode = false;
                        twinAttachNode = Entity.Null;
                        twinPortalPos = CubicBezier3D.Position(bestTwinCurve, twinCutT);
                    }

                    var twinPrefabRef = em.GetComponentData<PrefabRef>(bestTwinEdge);
                    string twinPrefabName = prefabName;
                    if (prefabSystem != null && prefabSystem.TryGetPrefab<PrefabBase>(twinPrefabRef.m_Prefab, out var tpb))
                    {
                        twinPrefabName = tpb.name;
                    }

                    float3 twinTan = math.normalize(CubicBezier3D.Tangent(bestTwinCurve, twinCutT));

                    twinEndpoint = new CarriagewayEndpoint
                    {
                        SourceEdge = bestTwinEdge,
                        SourcePrefab = twinPrefabRef.m_Prefab,
                        PrefabName = twinPrefabName,
                        OriginalCurve = bestTwinCurve,
                        CutCurveT = twinCutT,
                        PortalPosition = twinPortalPos,
                        FlowDirection = twinTan,
                        InwardTangent = twinTan,
                        Direction = CarriagewayDirection.Outbound,
                        IsInboundToCenter = false,
                        IsOneWay = true,
                        LaneCount = laneCount,
                        Width = width,
                        SpeedLimitMs = speedLimit,
                        AttachToNode = twinAttachToNode,
                        AttachNodeEntity = twinAttachNode
                    };
                }
            }

            var leg = new HighwayLeg
            {
                IsSeparatedDualCarriageway = (twinEndpoint != null),
                Inbound = endpoint,
                Outbound = twinEndpoint
            };

            if (twinEndpoint != null)
            {
                leg.PortalCenterPosition = (endpoint.PortalPosition + twinEndpoint.PortalPosition) * 0.5f;
                leg.CarriagewaySeparation = math.distance(endpoint.PortalPosition.xz, twinEndpoint.PortalPosition.xz);
            }
            else
            {
                leg.PortalCenterPosition = endpoint.PortalPosition;
                leg.CarriagewaySeparation = 0f;
            }

            return leg;
        }

        public static void RecomputeCenterAndSortLegs(
            EntityManager em,
            List<HighwayLeg> legs,
            InterchangeConfig config,
            float baseTerrainY,
            Func<float2, float> sampleTerrainHeight = null)
        {
            if (legs == null || legs.Count == 0) return;

            int N = legs.Count;
            float2 sumM = float2.zero;
            float2[] midpoints = new float2[N];

            for (int i = 0; i < N; i++)
            {
                var leg = legs[i];
                float3 mPos = leg.IsSeparatedDualCarriageway && leg.Outbound != null
                    ? (leg.Inbound.PortalPosition + leg.Outbound.PortalPosition) * 0.5f
                    : leg.Inbound.PortalPosition;
                leg.PortalCenterPosition = mPos;
                midpoints[i] = mPos.xz;
                sumM += mPos.xz;
            }

            float2 centroid = sumM / N;
            float2 centerXZ = centroid;

            if (N >= 2)
            {
                float2x2 A = float2x2.zero;
                float2 b = float2.zero;

                for (int i = 0; i < N; i++)
                {
                    var leg = legs[i];
                    var curve = leg.Inbound.OriginalCurve;
                    float3 tan3 = math.normalize(CubicBezier3D.Tangent(curve, leg.Inbound.CutCurveT));
                    float2 u = math.normalize(tan3.xz);
                    if (math.lengthsq(u) < 1e-4f) u = new float2(1f, 0f);

                    float2x2 P = new float2x2(1f - u.x * u.x, -u.x * u.y, -u.x * u.y, 1f - u.y * u.y);
                    A.c0 += P.c0;
                    A.c1 += P.c1;
                    b += math.mul(P, midpoints[i]);
                }

                float det = A.c0.x * A.c1.y - A.c0.y * A.c1.x;
                if (math.abs(det) > 0.05f)
                {
                    float2x2 invA = new float2x2(A.c1.y, -A.c0.y, -A.c1.x, A.c0.x) * (1f / det);
                    float2 lsqCenter = math.mul(invA, b);
                    if (math.distance(lsqCenter, centroid) <= 260f)
                    {
                        centerXZ = lsqCenter;
                    }
                }
            }

            if (sampleTerrainHeight != null)
            {
                baseTerrainY = sampleTerrainHeight(centerXZ);
            }

            config.Center = new float3(centerXZ.x, baseTerrainY, centerXZ.y);

            float maxDist = 0f;
            for (int i = 0; i < N; i++)
            {
                float d = math.distance(midpoints[i], centerXZ);
                if (d > maxDist) maxDist = d;
            }
            if (config.FootprintRadius <= 0f)
            {
                config.FootprintRadius = math.clamp(maxDist, 140f, 350f);
            }

            // Orient endpoints relative to config.Center
            for (int i = 0; i < N; i++)
            {
                var leg = legs[i];
                float2 outward = midpoints[i] - centerXZ;
                float lenOut = math.length(outward);
                float2 a_i = lenOut > 1e-4f ? outward / lenOut : new float2(1f, 0f);
                leg.LegAxisDirection = new float3(a_i.x, 0f, a_i.y);
                leg.AzimuthRad = math.atan2(a_i.y, a_i.x);

                void OrientEndpoint(CarriagewayEndpoint ep)
                {
                    if (ep == null) return;
                    var c = ep.OriginalCurve;
                    if (ep.AttachToNode)
                    {
                        if (ep.CutCurveT < 0.5f)
                        {
                            ep.InwardTangent = -math.normalize(CubicBezier3D.Tangent(c, 0f));
                        }
                        else
                        {
                            ep.InwardTangent = math.normalize(CubicBezier3D.Tangent(c, 1f));
                        }
                    }
                    else
                    {
                        float3 t = math.normalize(CubicBezier3D.Tangent(c, ep.CutCurveT));
                        if (math.dot(t.xz, -a_i) < 0f) t = -t;
                        ep.InwardTangent = t;
                    }
                }

                OrientEndpoint(leg.Inbound);
                OrientEndpoint(leg.Outbound);

                if (leg.IsSeparatedDualCarriageway && leg.Outbound != null)
                {
                    var epA = leg.Inbound;
                    var epB = leg.Outbound;
                    // FlowDirection holds the authoritative vehicle traffic heading
                    float flowDotA = math.dot(epA.FlowDirection.xz, -a_i);
                    float flowDotB = math.dot(epB.FlowDirection.xz, -a_i);

                    if (flowDotA >= flowDotB)
                    {
                        leg.Inbound = epA;
                        leg.Outbound = epB;
                    }
                    else
                    {
                        leg.Inbound = epB;
                        leg.Outbound = epA;
                    }

                    leg.Inbound.Direction = CarriagewayDirection.Inbound;
                    leg.Inbound.IsInboundToCenter = true;
                    leg.Outbound.Direction = CarriagewayDirection.Outbound;
                    leg.Outbound.IsInboundToCenter = false;
                }
                else
                {
                    leg.Inbound.Direction = CarriagewayDirection.TwoWay;
                }
            }

            legs.Sort((a, b) => a.AzimuthRad.CompareTo(b.AzimuthRad));
            for (int i = 0; i < legs.Count; i++)
            {
                legs[i].LegIndex = i;
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
