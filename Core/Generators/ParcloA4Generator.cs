using System.Collections.Generic;
using Colossal.Mathematics;
using ProceduralInterchanges.Core.Math;
using ProceduralInterchanges.Core.Models;
using Unity.Mathematics;

namespace ProceduralInterchanges.Core.Generators
{
    public sealed class ParcloA4Generator : IInterchangeGenerator
    {
        public InterchangeArchetype Archetype => InterchangeArchetype.ParcloA4;
        public string DisplayName => "Parclo A4 Interchange";
        public string Description => "AASHTO standard Partial Cloverleaf (A4) with free-flowing freeway mainline, arterial overpass, 4 directional slip ramps, and 2 advance loop entrance ramps.";
        public int MinLegs => 4;
        public int MaxLegs => 4;

        public bool SupportsConfiguration(IReadOnlyList<HighwayLeg> legs, out string reason)
        {
            if (legs == null || legs.Count != 4)
            {
                reason = "Requires exactly 4 connected highway legs.";
                return false;
            }
            reason = string.Empty;
            return true;
        }

        public void GenerateGeometry(InterchangeConfig config, IReadOnlyList<HighwayLeg> legs, List<PlannedNetCourse> outCourses)
        {
            float3 center = config.Center;
            float R = math.clamp(config.FootprintRadius, 140f, 220f);
            float sigma = config.LeftHandTraffic ? -1f : 1f;
            float H = math.max(9.0f, config.MinBridgeClearanceMeters);

            var port = new LegPortals[4];
            for (int k = 0; k < 4; k++)
            {
                port[k] = LegPortalGeometry.EmitLegApproaches(legs[k], center, R, 0.25f, sigma, outCourses, $"parclo_leg_{k}");
            }

            // 1. Freeway Mainline Corridor (Leg 0 <-> Leg 2) at Level 0 (Ground)
            // Leg 0 Inbound (travels towards Leg 2) -> Leg 2 Outbound
            EmitMainlineCorridor(port[0].DivergeNode, port[0].InboundDir, port[2].MergeNode, -port[2].OutboundDir,
                center.y, center.y, 0, false, 0, 2, outCourses, "parclo_freeway_0_2");
            // Leg 2 Inbound (travels towards Leg 0) -> Leg 0 Outbound
            EmitMainlineCorridor(port[2].DivergeNode, port[2].InboundDir, port[0].MergeNode, -port[0].OutboundDir,
                center.y, center.y, 0, false, 2, 0, outCourses, "parclo_freeway_2_0");

            // 2. Arterial Overpass Corridor (Leg 1 <-> Leg 3) at Level 1 (+H)
            // Leg 1 Inbound (travels South towards Leg 3) -> Leg 3 Outbound
            EmitMainlineCorridor(port[1].DivergeNode, port[1].InboundDir, port[3].MergeNode, -port[3].OutboundDir,
                center.y + H, center.y + H, 1, true, 1, 3, outCourses, "parclo_arterial_1_3");
            // Leg 3 Inbound (travels North towards Leg 1) -> Leg 1 Outbound
            EmitMainlineCorridor(port[3].DivergeNode, port[3].InboundDir, port[1].MergeNode, -port[1].OutboundDir,
                center.y + H, center.y + H, 1, true, 3, 1, outCourses, "parclo_arterial_3_1");

            // 3. Four Directional Outer Slip Ramps
            // Quadrant 0 -> 1: Freeway 0 exit to Arterial 1 outbound
            EmitSlipRamp(port[0].DivergeNode, port[0].InboundDir, port[1].MergeNode, -port[1].OutboundDir,
                center.y, center.y + H, sigma, 0, 1, outCourses, "parclo_slip_0_1");

            // Quadrant 1 -> 2: Arterial 1 right turn to Freeway 2 outbound
            EmitSlipRamp(port[1].DivergeNode, port[1].InboundDir, port[2].MergeNode, -port[2].OutboundDir,
                center.y + H, center.y, sigma, 1, 2, outCourses, "parclo_slip_1_2");

            // Quadrant 2 -> 3: Freeway 2 exit to Arterial 3 outbound
            EmitSlipRamp(port[2].DivergeNode, port[2].InboundDir, port[3].MergeNode, -port[3].OutboundDir,
                center.y, center.y + H, sigma, 2, 3, outCourses, "parclo_slip_2_3");

            // Quadrant 3 -> 0: Arterial 3 right turn to Freeway 0 outbound
            EmitSlipRamp(port[3].DivergeNode, port[3].InboundDir, port[0].MergeNode, -port[0].OutboundDir,
                center.y + H, center.y, sigma, 3, 0, outCourses, "parclo_slip_3_0");

            // 4. Two Advance Loop Entrance Ramps (from Arterial onto Freeway)
            float loopRadius = math.clamp(R * 0.25f, 40.0f, 55.0f);

            // Loop 1: From Arterial 1 (inbound before overpass) to Freeway 0 (outbound eastbound)
            float3 dLoop1 = math.lerp(port[1].DivergeNode, center, 0.45f);
            dLoop1.y = center.y + H;
            float3 mLoop0 = math.lerp(port[0].MergeNode, center, 0.45f);
            mLoop0.y = center.y;
            BuildAASHTOParcloLoop("parclo_loop_1_0", dLoop1, port[1].InboundDir, mLoop0, -port[0].OutboundDir,
                loopRadius, sigma, center.y + H, center.y, outCourses, 1, 0);

            // Loop 2: From Arterial 3 (inbound before overpass) to Freeway 2 (outbound westbound)
            float3 dLoop3 = math.lerp(port[3].DivergeNode, center, 0.45f);
            dLoop3.y = center.y + H;
            float3 mLoop2 = math.lerp(port[2].MergeNode, center, 0.45f);
            mLoop2.y = center.y;
            BuildAASHTOParcloLoop("parclo_loop_3_2", dLoop3, port[3].InboundDir, mLoop2, -port[2].OutboundDir,
                loopRadius, sigma, center.y + H, center.y, outCourses, 3, 2);
        }

        private static void EmitMainlineCorridor(
            float3 start, float3 startDir, float3 end, float3 endDir,
            float startY, float endY, int stackLevel, bool forceElevated,
            int fromLeg, int toLeg,
            List<PlannedNetCourse> outCourses, string idPrefix)
        {
            var curve = BiArcFit.FitSmoothCurve(start, startDir, end, endDir);
            curve.a.y = startY;
            curve.d.y = endY;
            curve.b.y = math.lerp(startY, endY, 0.333f);
            curve.c.y = math.lerp(startY, endY, 0.667f);

            outCourses.Add(new PlannedNetCourse
            {
                Id = idPrefix,
                Role = NetRole.MainlineThrough,
                LegIndex = fromLeg,
                TargetLegIndex = toLeg,
                Curve = curve,
                StackLevel = stackLevel,
                ForceElevated = forceElevated,
                StartElevation = new float2(math.max(0f, startY - start.y)),
                EndElevation = new float2(math.max(0f, endY - end.y)),
                Lanes = 2,
                IsOneWay = true
            });
        }

        private static void EmitSlipRamp(
            float3 start, float3 startDir, float3 end, float3 endDir,
            float startY, float endY, float sigma,
            int fromLeg, int toLeg,
            List<PlannedNetCourse> outCourses, string idPrefix)
        {
            BiArcFit.ApplyGoreTaper(start, startDir, (int)sigma, BiArcFit.kStandardGoreAngleRad, out float3 rampStartDir);
            BiArcFit.ApplyGoreTaper(end, endDir, -(int)sigma, BiArcFit.kStandardGoreAngleRad, out float3 rampEndDir);

            var curve = BiArcFit.FitSmoothCurve(start, rampStartDir, end, rampEndDir);
            curve.a.y = startY;
            curve.d.y = endY;
            curve.b.y = math.lerp(startY, endY, 0.333f);
            curve.c.y = math.lerp(startY, endY, 0.667f);

            bool elevated = (startY > endY + 2f) || (endY > startY + 2f);

            outCourses.Add(new PlannedNetCourse
            {
                Id = idPrefix,
                Role = NetRole.DirectionalRamp,
                LegIndex = fromLeg,
                TargetLegIndex = toLeg,
                Curve = curve,
                StackLevel = elevated ? 1 : 0,
                ForceElevated = elevated,
                StartElevation = new float2(math.max(0f, startY - start.y)),
                EndElevation = new float2(math.max(0f, endY - end.y)),
                Lanes = 1,
                IsOneWay = true
            });
        }

        private static void BuildAASHTOParcloLoop(
            string idPrefix, float3 startPt, float3 startDir, float3 endPt, float3 endDir,
            float radius, float sigma, float startY, float endY,
            List<PlannedNetCourse> outCourses, int fromLeg, int toLeg)
        {
            BiArcFit.ApplyGoreTaper(startPt, startDir, (int)sigma, BiArcFit.kStandardGoreAngleRad, out float3 loopStartDir);
            BiArcFit.ApplyGoreTaper(endPt, endDir, -(int)sigma, BiArcFit.kStandardGoreAngleRad, out float3 loopEndDir);

            float peakY = (startY + endY) * 0.5f;
            var arcs = new List<Bezier4x3>();
            BiArcFit.FitConnectedLoop270(startPt, loopStartDir, endPt, loopEndDir, radius, (int)sigma, startY, peakY, endY, arcs);

            for (int k = 0; k < arcs.Count; k++)
            {
                var curve = arcs[k];
                float maxElev = math.max(curve.a.y - endY, curve.d.y - endY);
                bool elevated = maxElev > 2.5f;

                outCourses.Add(new PlannedNetCourse
                {
                    Id = $"{idPrefix}_{k}",
                    Role = NetRole.LoopRamp,
                    LegIndex = fromLeg,
                    TargetLegIndex = toLeg,
                    Curve = curve,
                    StackLevel = elevated ? 1 : 0,
                    ForceElevated = elevated,
                    Lanes = 1,
                    IsOneWay = true
                });
            }
        }
    }
}
