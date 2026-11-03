using System.Collections.Generic;
using ProceduralInterchanges.Core.Math;
using ProceduralInterchanges.Core.Models;
using Unity.Mathematics;

namespace ProceduralInterchanges.Core.Generators
{
    public sealed class YStackGenerator : IInterchangeGenerator
    {
        public InterchangeArchetype Archetype => InterchangeArchetype.YStack;
        public string DisplayName => "3-Level Y-Stack";
        public string Description => "Fully directional 3-level interchange for three freeway branches meeting at Y angles.";
        public int MinLegs => 3;
        public int MaxLegs => 3;

        public bool SupportsConfiguration(IReadOnlyList<HighwayLeg> legs, out string reason)
        {
            if (legs == null || legs.Count != 3)
            {
                reason = "Requires exactly 3 connected highway legs.";
                return false;
            }
            reason = string.Empty;
            return true;
        }

        public void GenerateGeometry(InterchangeConfig config, IReadOnlyList<HighwayLeg> legs, List<PlannedNetCourse> outCourses)
        {
            float3 center = config.Center;
            float R = config.FootprintRadius;
            float sigma = config.LeftHandTraffic ? -1f : 1f;

            // Precompute portals, split nodes, merge nodes, directions for each leg
            float3[] dirIn = new float3[3];
            float3[] dirOut = new float3[3];
            float3[] dNodes = new float3[3];
            float3[] mNodes = new float3[3];

            for (int i = 0; i < 3; i++)
            {
                var portals = LegPortalGeometry.EmitLegApproaches(legs[i], center, R, 0.45f, sigma, outCourses, $"ystack_leg_{i}");
                dNodes[i] = portals.DivergeNode;
                mNodes[i] = portals.MergeNode;
                dirIn[i] = portals.InboundDir;
                dirOut[i] = -portals.OutboundDir;
            }

            // 3 legs: 0, 1, 2 sorted counter-clockwise
            for (int i = 0; i < 3; i++)
            {
                int nextIdx = (i + 1) % 3;
                int prevIdx = (i + 2) % 3;

                // 3. Outer turn (i to next) at StackLevel = 0 with gore taper
                BiArcFit.ApplyGoreTaper(dNodes[i], dirIn[i], (int)sigma, BiArcFit.kStandardGoreAngleRad, out float3 outerDiverge);
                BiArcFit.ApplyGoreTaper(mNodes[nextIdx], dirOut[nextIdx], -(int)sigma, BiArcFit.kStandardGoreAngleRad, out float3 outerMerge);
                outCourses.Add(new PlannedNetCourse
                {
                    Id = $"ystack_outer_{i}_{nextIdx}",
                    Role = NetRole.DirectionalRamp,
                    LegIndex = i,
                    TargetLegIndex = nextIdx,
                    Curve = BiArcFit.FitSmoothCurve(dNodes[i], outerDiverge, mNodes[nextIdx], outerMerge),
                    StackLevel = 0
                });

                // 4. Inner cross-movement (i to prev) with staggered stack levels:
                // leg 0 to 2: StackLevel = 0
                // leg 1 to 0: StackLevel = 1
                // leg 2 to 1: StackLevel = 2
                int innerLevel = i;
                BiArcFit.ApplyGoreTaper(dNodes[i], dirIn[i], -(int)sigma, BiArcFit.kStandardGoreAngleRad, out float3 innerDiverge);
                BiArcFit.ApplyGoreTaper(mNodes[prevIdx], dirOut[prevIdx], (int)sigma, BiArcFit.kStandardGoreAngleRad, out float3 innerMerge);
                outCourses.Add(new PlannedNetCourse
                {
                    Id = $"ystack_inner_{i}_{prevIdx}",
                    Role = NetRole.DirectionalRamp,
                    LegIndex = i,
                    TargetLegIndex = prevIdx,
                    Curve = BiArcFit.FitSmoothCurve(dNodes[i], innerDiverge, mNodes[prevIdx], innerMerge),
                    StackLevel = innerLevel,
                    ForceElevated = innerLevel > 0
                });
            }
        }

        private static float3 GetPortal(HighwayLeg leg, bool isInbound, float3 center, float radius)
        {
            if (isInbound && leg.Inbound != null) return leg.Inbound.PortalPosition;
            if (!isInbound && leg.Outbound != null) return leg.Outbound.PortalPosition;

            float3 outward = leg.LegAxisDirection;
            float side = (isInbound ? -1f : 1f) * (leg.CarriagewaySeparation > 0 ? leg.CarriagewaySeparation * 0.5f : 6f);
            float3 normal = new float3(-outward.z, 0f, outward.x);
            return center + outward * radius + normal * side;
        }
    }
}
