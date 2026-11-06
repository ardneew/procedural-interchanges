using System.Collections.Generic;
using Colossal.Mathematics;
using ProceduralInterchanges.Core.Math;
using ProceduralInterchanges.Core.Models;
using Unity.Entities;
using Unity.Mathematics;

namespace ProceduralInterchanges.Core.Generators
{
    public struct LegPortals
    {
        public float3 DivergeNode; // d_in: where inbound traffic splits into the interchange network
        public float3 MergeNode;   // m_out: where outbound traffic merges before leaving
        public float3 InboundDir;  // Tangent pointing into interchange at DivergeNode
        public float3 OutboundDir; // Tangent pointing into interchange at MergeNode (so -OutboundDir leaves)
    }

    public static class LegPortalGeometry
    {
        /// <summary>
        /// Generates the approach and departure geometry connecting a highway leg to the interchange network.
        /// Handles both separated dual-carriageway highways and single two-way highways with exact G1 tangency.
        /// </summary>
        public static LegPortals EmitLegApproaches(
            HighwayLeg leg,
            float3 center,
            float footprintRadius,
            float splitFraction,
            float sigma,
            List<PlannedNetCourse> outCourses,
            string idPrefix)
        {
            float3 outward = leg.LegAxisDirection;

            // Compute exact highway road tangent at cut point
            float3 tRoadIn;
            if (leg.Inbound != null && math.lengthsq(leg.Inbound.FlowDirection.xz) > 1e-4f)
            {
                float3 tan = CubicBezier3D.Tangent(leg.Inbound.OriginalCurve, leg.Inbound.CutCurveT);
                float3 hTan = math.normalize(new float3(tan.x, 0f, tan.z));
                tRoadIn = math.dot(hTan, center - leg.Inbound.PortalPosition) > 0f ? hTan : -hTan;
            }
            else if (math.lengthsq(leg.PortalInwardTangent.xz) > 1e-4f)
            {
                tRoadIn = leg.PortalInwardTangent;
            }
            else
            {
                tRoadIn = -outward;
            }

            float3 tRoadOut;
            if (leg.Outbound != null && math.lengthsq(leg.Outbound.FlowDirection.xz) > 1e-4f)
            {
                float3 tan = CubicBezier3D.Tangent(leg.Outbound.OriginalCurve, leg.Outbound.CutCurveT);
                float3 hTan = math.normalize(new float3(tan.x, 0f, tan.z));
                tRoadOut = math.dot(hTan, leg.Outbound.PortalPosition - center) > 0f ? hTan : -hTan;
            }
            else
            {
                tRoadOut = -tRoadIn;
            }

            if (leg.IsSeparatedDualCarriageway)
            {
                // Separated dual one-way carriageways
                float3 pIn = leg.Inbound != null ? leg.Inbound.PortalPosition : GetFallbackPortal(leg, true, center, footprintRadius);
                float3 pOut = leg.Outbound != null ? leg.Outbound.PortalPosition : GetFallbackPortal(leg, false, center, footprintRadius);

                float runoutDist = math.clamp(footprintRadius * splitFraction, 12f, 45f);
                float3 dIn = pIn + tRoadIn * runoutDist;
                float3 mOut = pOut + (-tRoadOut) * runoutDist; // tRoadOut points outwards, so -tRoadOut points inwards

                Entity inAttach = Entity.Null;
                float inSplit = 0f;
                bool inAttachIsNode = false;
                if (leg.Inbound != null)
                {
                    inAttach = leg.Inbound.AttachToNode ? leg.Inbound.AttachNodeEntity : leg.Inbound.SourceEdge;
                    inSplit = leg.Inbound.CutCurveT;
                    inAttachIsNode = leg.Inbound.AttachToNode;
                }

                Entity outAttach = Entity.Null;
                float outSplit = 0f;
                bool outAttachIsNode = false;
                if (leg.Outbound != null)
                {
                    outAttach = leg.Outbound.AttachToNode ? leg.Outbound.AttachNodeEntity : leg.Outbound.SourceEdge;
                    outSplit = leg.Outbound.CutCurveT;
                    outAttachIsNode = leg.Outbound.AttachToNode;
                }

                // Inbound approach (pIn -> dIn): perfectly collinear with highway tangent
                outCourses.Add(new PlannedNetCourse
                {
                    Id = $"{idPrefix}_app",
                    Role = NetRole.MainlineThrough,
                    LegIndex = leg.LegIndex,
                    TargetLegIndex = -1,
                    Curve = BiArcFit.FitSmoothCurve(pIn, tRoadIn, dIn, tRoadIn),
                    StackLevel = 0,
                    IsOneWay = true,
                    Lanes = leg.Inbound?.LaneCount ?? 2,
                    ResolvedPrefabEntity = leg.Inbound?.SourcePrefab ?? Entity.Null,
                    ResolvedPrefabName = leg.Inbound?.PrefabName ?? "Highway",
                    StartAttachEntity = inAttach,
                    StartSplitPosition = inSplit,
                    StartAttachIsNode = inAttachIsNode
                });

                // Outbound departure (mOut -> pOut): perfectly collinear with highway tangent
                outCourses.Add(new PlannedNetCourse
                {
                    Id = $"{idPrefix}_dep",
                    Role = NetRole.MainlineThrough,
                    LegIndex = -1,
                    TargetLegIndex = leg.LegIndex,
                    Curve = BiArcFit.FitSmoothCurve(mOut, tRoadOut, pOut, tRoadOut),
                    StackLevel = 0,
                    IsOneWay = true,
                    Lanes = leg.Outbound?.LaneCount ?? (leg.Inbound?.LaneCount ?? 2),
                    ResolvedPrefabEntity = leg.Outbound?.SourcePrefab ?? (leg.Inbound?.SourcePrefab ?? Entity.Null),
                    ResolvedPrefabName = leg.Outbound?.PrefabName ?? (leg.Inbound?.PrefabName ?? "Highway"),
                    EndAttachEntity = outAttach,
                    EndSplitPosition = outSplit,
                    EndAttachIsNode = outAttachIsNode
                });

                return new LegPortals
                {
                    DivergeNode = dIn,
                    MergeNode = mOut,
                    InboundDir = tRoadIn,
                    OutboundDir = -tRoadOut
                };
            }
            else
            {
                // Single two-way highway leg: zero offset in the grass!
                float3 pCenter = leg.PortalCenterPosition;
                if (math.lengthsq(pCenter) < 1f && leg.Inbound != null)
                {
                    pCenter = leg.Inbound.PortalPosition;
                }
                if (math.lengthsq(pCenter) < 1f)
                {
                    pCenter = center + outward * footprintRadius;
                }

                float3 t0 = tRoadIn;
                float3 rightNormal = math.normalize(new float3(t0.z * sigma, 0f, -t0.x * sigma));

                float flareDist = math.clamp(footprintRadius * math.max(0.20f, splitFraction), 45f, 85f);
                float leadIn = 12.0f;
                float3 pLead = pCenter + t0 * leadIn;

                float halfSep = math.clamp(footprintRadius * 0.032f, 4.5f, 7.0f);

                float3 dIn = pLead + t0 * (flareDist - leadIn) + rightNormal * halfSep;
                float3 mOut = pLead + t0 * (flareDist - leadIn) - rightNormal * halfSep;

                int lanes = leg.Inbound?.LaneCount ?? 2;

                Entity twoWayAttach = Entity.Null;
                float twoWaySplit = 0f;
                bool twoWayAttachIsNode = false;
                if (leg.Inbound != null)
                {
                    twoWayAttach = leg.Inbound.AttachToNode ? leg.Inbound.AttachNodeEntity : leg.Inbound.SourceEdge;
                    twoWaySplit = leg.Inbound.CutCurveT;
                    twoWayAttachIsNode = leg.Inbound.AttachToNode;
                }

                // 1. Inbound flare: pCenter -> dIn along natural highway tangent
                outCourses.Add(new PlannedNetCourse
                {
                    Id = $"{idPrefix}_inbound_flare",
                    Role = NetRole.MainlineThrough,
                    LegIndex = leg.LegIndex,
                    TargetLegIndex = -1,
                    Curve = BiArcFit.FitSmoothCurve(pCenter, t0, dIn, t0),
                    StackLevel = 0,
                    IsOneWay = true,
                    Lanes = lanes,
                    StartAttachEntity = twoWayAttach,
                    StartSplitPosition = twoWaySplit,
                    StartAttachIsNode = twoWayAttachIsNode
                });

                // 2. Outbound flare: mOut -> pCenter along natural highway tangent
                outCourses.Add(new PlannedNetCourse
                {
                    Id = $"{idPrefix}_outbound_flare",
                    Role = NetRole.MainlineThrough,
                    LegIndex = -1,
                    TargetLegIndex = leg.LegIndex,
                    Curve = BiArcFit.FitSmoothCurve(mOut, -t0, pCenter, -t0),
                    StackLevel = 0,
                    IsOneWay = true,
                    Lanes = lanes,
                    EndAttachEntity = twoWayAttach,
                    EndSplitPosition = twoWaySplit,
                    EndAttachIsNode = twoWayAttachIsNode
                });

                return new LegPortals
                {
                    DivergeNode = dIn,
                    MergeNode = mOut,
                    InboundDir = t0,
                    OutboundDir = t0
                };
            }
        }

        public static float3 GetPortal(HighwayLeg leg, bool isInbound, float3 center, float radius)
        {
            if (leg.IsSeparatedDualCarriageway)
            {
                if (isInbound && leg.Inbound != null) return leg.Inbound.PortalPosition;
                if (!isInbound && leg.Outbound != null) return leg.Outbound.PortalPosition;
            }
            else
            {
                if (leg.Inbound != null) return leg.Inbound.PortalPosition;
                if (math.lengthsq(leg.PortalCenterPosition) > 1f) return leg.PortalCenterPosition;
            }

            return GetFallbackPortal(leg, isInbound, center, radius);
        }

        private static float3 GetFallbackPortal(HighwayLeg leg, bool isInbound, float3 center, float radius)
        {
            float3 outward = leg.LegAxisDirection;
            float3 inward = -outward;
            if (leg.IsSeparatedDualCarriageway)
            {
                float side = (isInbound ? 1f : -1f) * (leg.CarriagewaySeparation > 0 ? leg.CarriagewaySeparation * 0.5f : 6f);
                float3 rightOfInward = new float3(inward.z, 0f, -inward.x);
                return center + outward * radius + rightOfInward * side;
            }
            return center + outward * radius;
        }
    }
}
