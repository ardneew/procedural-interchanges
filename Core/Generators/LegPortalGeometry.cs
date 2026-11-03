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

                float3 tInward = tRoadIn;
                float3 dIn = math.lerp(pIn, center, splitFraction);
                float3 mOut = math.lerp(pOut, center, splitFraction);

                // Inbound approach (pIn -> dIn)
                outCourses.Add(new PlannedNetCourse
                {
                    Id = $"{idPrefix}_app",
                    Role = NetRole.MainlineThrough,
                    LegIndex = leg.LegIndex,
                    TargetLegIndex = -1,
                    Curve = BiArcFit.FitSmoothCurve(pIn, tRoadIn, dIn, tInward),
                    StackLevel = 0,
                    IsOneWay = true,
                    Lanes = leg.Inbound?.LaneCount ?? 2,
                    ResolvedPrefabEntity = leg.Inbound?.SourcePrefab ?? Entity.Null,
                    ResolvedPrefabName = leg.Inbound?.PrefabName ?? "Highway"
                });

                // Outbound departure (mOut -> pOut)
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
                    ResolvedPrefabName = leg.Outbound?.PrefabName ?? (leg.Inbound?.PrefabName ?? "Highway")
                });

                return new LegPortals
                {
                    DivergeNode = dIn,
                    MergeNode = mOut,
                    InboundDir = tInward,
                    OutboundDir = tInward
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
                float3 rightNormal = math.normalize(new float3(-t0.z * sigma, 0f, t0.x * sigma));

                float flareDist = math.clamp(footprintRadius * math.max(0.24f, splitFraction), 25f, 65f);
                float halfSep = math.clamp(footprintRadius * 0.045f, 6.0f, 9.0f);

                float3 dIn = pCenter + t0 * flareDist + rightNormal * halfSep;
                float3 mOut = pCenter + t0 * flareDist - rightNormal * halfSep;

                int lanes = leg.Inbound?.LaneCount ?? 2;

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
                    Lanes = lanes
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
                    Lanes = lanes
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
            if (leg.IsSeparatedDualCarriageway)
            {
                float side = (isInbound ? -1f : 1f) * (leg.CarriagewaySeparation > 0 ? leg.CarriagewaySeparation * 0.5f : 6f);
                float3 normal = new float3(-outward.z, 0f, outward.x);
                return center + outward * radius + normal * side;
            }
            return center + outward * radius;
        }
    }
}
