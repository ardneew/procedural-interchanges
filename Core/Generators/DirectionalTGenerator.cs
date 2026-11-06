using System.Collections.Generic;
using Colossal.Mathematics;
using ProceduralInterchanges.Core.Math;
using ProceduralInterchanges.Core.Models;
using Unity.Mathematics;

namespace ProceduralInterchanges.Core.Generators
{
    public sealed class DirectionalTGenerator : IInterchangeGenerator
    {
        public InterchangeArchetype Archetype => InterchangeArchetype.DirectionalT;
        public string DisplayName => "Directional-T Interchange";
        public string Description => "High-speed 3-way interchange replacing loop ramps with sweeping multi-level flyovers.";
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

            IdentifyMainlineAndStem(legs, out int mainAIdx, out int mainBIdx, out int stemIdx);
            var legA = legs[mainAIdx];
            var legB = legs[mainBIdx];
            var legStem = legs[stemIdx];

            float sigma = config.LeftHandTraffic ? -1f : 1f;

            var portalsA = LegPortalGeometry.EmitLegApproaches(legA, center, R, 0.20f, sigma, outCourses, "dirT_main_A");
            var portalsB = LegPortalGeometry.EmitLegApproaches(legB, center, R, 0.20f, sigma, outCourses, "dirT_main_B");
            var portalsStem = LegPortalGeometry.EmitLegApproaches(legStem, center, R, 0.20f, sigma, outCourses, "dirT_stem");

            float3 dA = portalsA.DivergeNode;
            float3 mA = portalsA.MergeNode;
            float3 dB = portalsB.DivergeNode;
            float3 mB = portalsB.MergeNode;
            float3 dS = portalsStem.DivergeNode;
            float3 mS = portalsStem.MergeNode;

            float3 dirA = portalsA.InboundDir;
            float3 dirB = portalsB.InboundDir;
            float3 dirStem = portalsStem.InboundDir;

            // Determine side of stem relative to AB direction (XZ 2D cross product)
            float3 vAB = math.normalize(dB - dA);
            float3 vStem = math.normalize(dS - center);
            float cross = vAB.x * vStem.z - vAB.z * vStem.x;
            bool stemIsRight = cross > 0f;

            // Spaced diverge and merge nodes along the continuous stem corridor
            float3 dS_near = dS;
            float3 dS_far = dS + dirStem * 26f;
            float3 mS_far = mS + dirStem * 26f;
            float3 mS_near = mS;

            float3 dS_A = stemIsRight ? dS_far : dS_near;
            float3 dS_B = stemIsRight ? dS_near : dS_far;
            float3 mS_A = stemIsRight ? mS_near : mS_far;
            float3 mS_B = stemIsRight ? mS_far : mS_near;

            // Continuous stem mainline connector courses
            outCourses.Add(new PlannedNetCourse
            {
                Id = "dirT_stem_app_connector",
                Role = NetRole.MainlineThrough,
                LegIndex = stemIdx,
                TargetLegIndex = -1,
                Curve = BiArcFit.FitSmoothCurve(dS_near, dirStem, dS_far, dirStem),
                StackLevel = 0
            });

            outCourses.Add(new PlannedNetCourse
            {
                Id = "dirT_stem_dep_connector",
                Role = NetRole.MainlineThrough,
                LegIndex = -1,
                TargetLegIndex = stemIdx,
                Curve = BiArcFit.FitSmoothCurve(mS_far, -dirStem, mS_near, -dirStem),
                StackLevel = 0
            });

            // 3. Through Mainline Cores (Level 0)
            outCourses.Add(new PlannedNetCourse
            {
                Id = "dirT_core_AB",
                Role = NetRole.MainlineThrough,
                LegIndex = mainAIdx,
                TargetLegIndex = mainBIdx,
                Curve = BiArcFit.FitSmoothCurve(dA, dirA, mB, -dirB),
                StackLevel = 0
            });

            outCourses.Add(new PlannedNetCourse
            {
                Id = "dirT_core_BA",
                Role = NetRole.MainlineThrough,
                LegIndex = mainBIdx,
                TargetLegIndex = mainAIdx,
                Curve = BiArcFit.FitSmoothCurve(dB, dirB, mA, -dirA),
                StackLevel = 0
            });

            int lvl_A_to_S = stemIsRight ? 0 : 1;
            int lvl_S_to_B = stemIsRight ? 0 : 2;
            int lvl_B_to_S = stemIsRight ? 1 : 0;
            int lvl_S_to_A = stemIsRight ? 2 : 0;

            // 4. Ramps and Flyovers connecting Mainlines and Stem with Gore Tapers
            int sideA = stemIsRight ? (int)sigma : -(int)sigma;
            int sideB = stemIsRight ? -(int)sigma : (int)sigma;
            int sideS_to_B = stemIsRight ? -(int)sigma : (int)sigma;
            int sideS_to_A = stemIsRight ? (int)sigma : -(int)sigma;

            BiArcFit.ApplyGoreTaper(dA, dirA, sideA, BiArcFit.kStandardGoreAngleRad, out float3 rampA_S_start);
            BiArcFit.ApplyGoreTaper(mS_A, -dirStem, -sideA, BiArcFit.kStandardGoreAngleRad, out float3 rampA_S_end);
            outCourses.Add(new PlannedNetCourse
            {
                Id = "dirT_ramp_A_to_S",
                Role = NetRole.DirectionalRamp,
                LegIndex = mainAIdx,
                TargetLegIndex = stemIdx,
                Curve = BiArcFit.FitSmoothCurve(dA, rampA_S_start, mS_A, rampA_S_end),
                StackLevel = lvl_A_to_S,
                ForceElevated = lvl_A_to_S > 0
            });

            BiArcFit.ApplyGoreTaper(dS_B, dirStem, sideS_to_B, BiArcFit.kStandardGoreAngleRad, out float3 rampS_B_start);
            BiArcFit.ApplyGoreTaper(mB, -dirB, -sideS_to_B, BiArcFit.kStandardGoreAngleRad, out float3 rampS_B_end);
            outCourses.Add(new PlannedNetCourse
            {
                Id = "dirT_ramp_S_to_B",
                Role = NetRole.DirectionalRamp,
                LegIndex = stemIdx,
                TargetLegIndex = mainBIdx,
                Curve = BiArcFit.FitSmoothCurve(dS_B, rampS_B_start, mB, rampS_B_end),
                StackLevel = lvl_S_to_B,
                ForceElevated = lvl_S_to_B > 0
            });

            BiArcFit.ApplyGoreTaper(dB, dirB, sideB, BiArcFit.kStandardGoreAngleRad, out float3 rampB_S_start);
            BiArcFit.ApplyGoreTaper(mS_B, -dirStem, -sideB, BiArcFit.kStandardGoreAngleRad, out float3 rampB_S_end);
            outCourses.Add(new PlannedNetCourse
            {
                Id = "dirT_ramp_B_to_S",
                Role = NetRole.DirectionalRamp,
                LegIndex = mainBIdx,
                TargetLegIndex = stemIdx,
                Curve = BiArcFit.FitSmoothCurve(dB, rampB_S_start, mS_B, rampB_S_end),
                StackLevel = lvl_B_to_S,
                ForceElevated = lvl_B_to_S > 0
            });

            BiArcFit.ApplyGoreTaper(dS_A, dirStem, sideS_to_A, BiArcFit.kStandardGoreAngleRad, out float3 rampS_A_start);
            BiArcFit.ApplyGoreTaper(mA, -dirA, -sideS_to_A, BiArcFit.kStandardGoreAngleRad, out float3 rampS_A_end);
            outCourses.Add(new PlannedNetCourse
            {
                Id = "dirT_ramp_S_to_A",
                Role = NetRole.DirectionalRamp,
                LegIndex = stemIdx,
                TargetLegIndex = mainAIdx,
                Curve = BiArcFit.FitSmoothCurve(dS_A, rampS_A_start, mA, rampS_A_end),
                StackLevel = lvl_S_to_A,
                ForceElevated = lvl_S_to_A > 0
            });
        }

        private static void IdentifyMainlineAndStem(IReadOnlyList<HighwayLeg> legs, out int mainA, out int mainB, out int stem)
        {
            float maxDiff = -1f;
            mainA = 0;
            mainB = 1;
            stem = 2;

            for (int i = 0; i < 3; i++)
            {
                for (int j = i + 1; j < 3; j++)
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

            for (int k = 0; k < 3; k++)
            {
                if (k != mainA && k != mainB)
                {
                    stem = k;
                    break;
                }
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
