using System.Collections.Generic;
using Colossal.Mathematics;
using ProceduralInterchanges.Core.Math;
using ProceduralInterchanges.Core.Models;
using Unity.Mathematics;

namespace ProceduralInterchanges.Core.Generators
{
    public sealed class RoundaboutInterchangeGenerator : IInterchangeGenerator
    {
        public InterchangeArchetype Archetype => InterchangeArchetype.RoundaboutInterchange;
        public string DisplayName => "Roundabout Interchange";
        public string Description => "Flexible 3-to-6-way grade-separated roundabout with optional continuous mainline underpass.";
        public int MinLegs => 3;
        public int MaxLegs => 6;

        public bool SupportsConfiguration(IReadOnlyList<HighwayLeg> legs, out string reason)
        {
            if (legs == null || legs.Count < 3 || legs.Count > 6)
            {
                reason = "Supports between 3 and 6 connected highway legs.";
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
            int nLegs = legs.Count;

            float rRing = math.clamp(R * 0.38f, 40f, 95f);
            int ringLevel = config.GradeSeparateMainline ? 1 : 0;
            int throughLevel = 0;

            float rot = config.RotationOffsetRad;

            // Emit leg approaches for ALL legs
            var portals = new LegPortals[nLegs];
            for (int i = 0; i < nLegs; i++)
            {
                portals[i] = LegPortalGeometry.EmitLegApproaches(legs[i], center, R, 0.40f, sigma, outCourses, $"roundabout_leg_{i}");
            }

            // 1. If GradeSeparateMainline is active and nLegs >= 4, pass the two most opposed legs through
            int mainA = -1;
            int mainB = -1;

            if (config.GradeSeparateMainline && nLegs >= 4)
            {
                IdentifyOpposingPair(legs, out mainA, out mainB);

                float3 dA = portals[mainA].DivergeNode;
                float3 mA = portals[mainA].MergeNode;
                float3 dB = portals[mainB].DivergeNode;
                float3 mB = portals[mainB].MergeNode;

                float3 dirA = portals[mainA].InboundDir;
                float3 dirB = portals[mainB].InboundDir;

                // Through Mainline Cores under the ring
                outCourses.Add(new PlannedNetCourse
                {
                    Id = "roundabout_core_AB",
                    Role = NetRole.MainlineThrough,
                    LegIndex = mainA,
                    TargetLegIndex = mainB,
                    Curve = BiArcFit.FitSmoothCurve(dA, dirA, mB, -dirB),
                    StackLevel = throughLevel
                });

                outCourses.Add(new PlannedNetCourse
                {
                    Id = "roundabout_core_BA",
                    Role = NetRole.MainlineThrough,
                    LegIndex = mainB,
                    TargetLegIndex = mainA,
                    Curve = BiArcFit.FitSmoothCurve(dB, dirB, mA, -dirA),
                    StackLevel = throughLevel
                });
            }

            // 2. Build the circulatory ring around the center
            var ringNodes = new List<RingNode>();
            float angleOffset = 0.18f; // ~10 degrees spacing between exit and entry of a leg

            for (int i = 0; i < nLegs; i++)
            {
                float legAngle = legs[i].AzimuthRad + rot;

                // Exit node precedes entry node along flow
                float exitAngle = legAngle - sigma * angleOffset;
                float entryAngle = legAngle + sigma * angleOffset;

                float3 exitPos = center + new float3(math.cos(exitAngle), 0f, math.sin(exitAngle)) * rRing;
                float3 entryPos = center + new float3(math.cos(entryAngle), 0f, math.sin(entryAngle)) * rRing;

                // Tangent along circulatory ring (counter-clockwise in RHT, clockwise in LHT)
                float3 exitTan = math.normalize(new float3(-math.sin(exitAngle), 0f, math.cos(exitAngle)) * sigma);
                float3 entryTan = math.normalize(new float3(-math.sin(entryAngle), 0f, math.cos(entryAngle)) * sigma);

                ringNodes.Add(new RingNode { Angle = exitAngle, Position = exitPos, Tangent = exitTan, LegIndex = i, IsEntry = false });
                ringNodes.Add(new RingNode { Angle = entryAngle, Position = entryPos, Tangent = entryTan, LegIndex = i, IsEntry = true });

                // Connect leg to ring nodes via entry and exit slip ramps
                float3 slipInStart = portals[i].DivergeNode;
                float3 slipInTan = portals[i].InboundDir;
                float3 slipOutEnd = portals[i].MergeNode;
                float3 slipOutTan = -portals[i].OutboundDir;

                outCourses.Add(new PlannedNetCourse
                {
                    Id = $"roundabout_slip_in_{i}",
                    Role = NetRole.DirectionalRamp,
                    LegIndex = i,
                    TargetLegIndex = -1,
                    Curve = BiArcFit.FitSmoothCurve(slipInStart, slipInTan, entryPos, entryTan),
                    StackLevel = ringLevel,
                    ForceElevated = ringLevel > 0
                });

                outCourses.Add(new PlannedNetCourse
                {
                    Id = $"roundabout_slip_out_{i}",
                    Role = NetRole.DirectionalRamp,
                    LegIndex = -1,
                    TargetLegIndex = i,
                    Curve = BiArcFit.FitSmoothCurve(exitPos, exitTan, slipOutEnd, slipOutTan),
                    StackLevel = ringLevel,
                    ForceElevated = ringLevel > 0
                });
            }

            // Sort ring nodes sequentially in direction of flow
            ringNodes.Sort((a, b) =>
            {
                float aAngle = (a.Angle % (2f * math.PI) + 2f * math.PI) % (2f * math.PI);
                float bAngle = (b.Angle % (2f * math.PI) + 2f * math.PI) % (2f * math.PI);
                return sigma > 0 ? aAngle.CompareTo(bAngle) : bAngle.CompareTo(aAngle);
            });

            // Connect consecutive ring nodes with circular arc courses
            for (int r = 0; r < ringNodes.Count; r++)
            {
                int nextR = (r + 1) % ringNodes.Count;
                var n1 = ringNodes[r];
                var n2 = ringNodes[nextR];

                outCourses.Add(new PlannedNetCourse
                {
                    Id = $"roundabout_ring_{r}",
                    Role = NetRole.RoundaboutRing,
                    Curve = BiArcFit.FitSmoothCurve(n1.Position, n1.Tangent, n2.Position, n2.Tangent),
                    StackLevel = ringLevel,
                    ForceElevated = ringLevel > 0,
                    IsOneWay = true,
                    Lanes = 2
                });
            }
        }

        private struct RingNode
        {
            public float Angle;
            public float3 Position;
            public float3 Tangent;
            public int LegIndex;
            public bool IsEntry;
        }

        private static void IdentifyOpposingPair(IReadOnlyList<HighwayLeg> legs, out int mainA, out int mainB)
        {
            float maxDiff = -1f;
            mainA = 0;
            mainB = 1;

            for (int i = 0; i < legs.Count; i++)
            {
                for (int j = i + 1; j < legs.Count; j++)
                {
                    float diff = math.abs(legs[i].AzimuthRad - legs[j].AzimuthRad);
                    if (diff > math.PI) diff = 2f * math.PI - diff;
                    if (diff > maxDiff)
                    {
                        maxDiff = diff;
                        mainA = i;
                        mainB = j;
                    }
                }
            }
        }
    }
}
