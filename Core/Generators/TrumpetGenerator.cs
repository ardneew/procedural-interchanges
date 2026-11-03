using System.Collections.Generic;
using Colossal.Mathematics;
using ProceduralInterchanges.Core.Math;
using ProceduralInterchanges.Core.Models;
using Unity.Mathematics;

namespace ProceduralInterchanges.Core.Generators
{
    public sealed class TrumpetGenerator : IInterchangeGenerator
    {
        public InterchangeArchetype Archetype => InterchangeArchetype.Trumpet;
        public string DisplayName => "Trumpet Interchange";
        public string Description => "Classic 3-way freeway junction using a single 270° loop ramp and one semi-directional overpass.";
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

            // Identify mainline pair and stem leg
            IdentifyMainlineAndStem(legs, sigma, out int mainAIdx, out int mainBIdx, out int stemIdx);
            var legA = legs[mainAIdx];
            var legB = legs[mainBIdx];
            var legStem = legs[stemIdx];

            // Emit leg approaches & departures cleanly handling both dual-carriageway and single two-way roads
            var portalsA = LegPortalGeometry.EmitLegApproaches(legA, center, R, 0.25f, sigma, outCourses, "trumpet_main_A");
            var portalsB = LegPortalGeometry.EmitLegApproaches(legB, center, R, 0.25f, sigma, outCourses, "trumpet_main_B");
            var portalsStem = LegPortalGeometry.EmitLegApproaches(legStem, center, R, 0.25f, sigma, outCourses, "trumpet_stem");

            float3 dA = portalsA.DivergeNode;
            float3 mA = portalsA.MergeNode;
            float3 dB = portalsB.DivergeNode;
            float3 mB = portalsB.MergeNode;
            float3 dS = portalsStem.DivergeNode;
            float3 mS = portalsStem.MergeNode;

            float3 dirA = portalsA.InboundDir;
            float3 dirB = portalsB.InboundDir;
            float3 dirStem = portalsStem.InboundDir;

            // 1. Through Mainlines (StackLevel = 0)
            // Near-side Mainline Core: A -> B
            outCourses.Add(new PlannedNetCourse
            {
                Id = "trumpet_main_AB_core",
                Role = NetRole.MainlineThrough,
                LegIndex = mainAIdx,
                TargetLegIndex = mainBIdx,
                Curve = BiArcFit.FitSmoothCurve(dA, dirA, mB, -dirB),
                StackLevel = 0
            });

            // Far-side Mainline Core: B -> A
            outCourses.Add(new PlannedNetCourse
            {
                Id = "trumpet_main_BA_core",
                Role = NetRole.MainlineThrough,
                LegIndex = mainBIdx,
                TargetLegIndex = mainAIdx,
                Curve = BiArcFit.FitSmoothCurve(dB, dirB, mA, -dirA),
                StackLevel = 0
            });

            // 2. Near-Side Outer Slip Ramp: dA -> mS (pure ground ramp, never crosses any road)
            BiArcFit.ApplyGoreTaper(dA, dirA, (int)sigma, BiArcFit.kStandardGoreAngleRad, out float3 slipStartDir);
            outCourses.Add(new PlannedNetCourse
            {
                Id = "trumpet_outer_slip",
                Role = NetRole.DirectionalRamp,
                LegIndex = mainAIdx,
                TargetLegIndex = stemIdx,
                Curve = BiArcFit.FitSmoothCurve(dA, slipStartDir, mS, -dirStem),
                StackLevel = 0
            });

            // 3. Near-Side Direct Merge: dS -> mB (ground ramp merging into near-side mainline)
            BiArcFit.ApplyGoreTaper(dS, dirStem, (int)sigma, BiArcFit.kStandardGoreAngleRad, out float3 directMergeDir);
            outCourses.Add(new PlannedNetCourse
            {
                Id = "trumpet_stem_to_B",
                Role = NetRole.DirectionalRamp,
                LegIndex = stemIdx,
                TargetLegIndex = mainBIdx,
                Curve = BiArcFit.FitSmoothCurve(dS, directMergeDir, mB, -dirB),
                StackLevel = 0
            });

            // 4. Semi-Directional Overpass Ramp: dS -> mA across center (StackLevel = 1, crosses near-side mainline AB)
            BiArcFit.ApplyGoreTaper(dS, dirStem, -(int)sigma, BiArcFit.kStandardGoreAngleRad, out float3 overpassStartDir);
            outCourses.Add(new PlannedNetCourse
            {
                Id = "trumpet_overpass_ramp",
                Role = NetRole.DirectionalRamp,
                LegIndex = stemIdx,
                TargetLegIndex = mainAIdx,
                Curve = BiArcFit.FitSmoothCurve(dS, overpassStartDir, mA, -dirA),
                StackLevel = 1,
                ForceElevated = true
            });

            // 5. 270-degree Trumpet Loop Ramp: dB -> mS with G1 Tangency via FitConnectedLoop270
            // Loops from far-side mainline B around and crosses OVER near-side mainline AB into Stem
            float loopRadius = math.clamp(R * 0.22f, 30.0f, 55.0f);
            BiArcFit.ApplyGoreTaper(dB, dirB, (int)sigma, BiArcFit.kStandardGoreAngleRad, out float3 loopStartDir);
            BiArcFit.ApplyGoreTaper(mS, -dirStem, -(int)sigma, BiArcFit.kStandardGoreAngleRad, out float3 loopEndDir);

            var loopArcs = new List<Bezier4x3>();
            BiArcFit.FitConnectedLoop270(dB, loopStartDir, mS, loopEndDir, loopRadius, (int)sigma, center.y, center.y + 10f, center.y, loopArcs);

            for (int l = 0; l < loopArcs.Count; l++)
            {
                outCourses.Add(new PlannedNetCourse
                {
                    Id = $"trumpet_loop_{l}",
                    Role = NetRole.LoopRamp,
                    LegIndex = mainBIdx,
                    TargetLegIndex = stemIdx,
                    Curve = loopArcs[l],
                    StackLevel = 1,
                    ForceElevated = true
                });
            }
        }

        private static void IdentifyMainlineAndStem(IReadOnlyList<HighwayLeg> legs, float sigma, out int mainA, out int mainB, out int stem)
        {
            float maxDiff = -1f;
            mainA = 0;
            mainB = 1;
            stem = 2;

            // Find pair of legs with angle closest to 180 degrees (PI radians)
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

            // Ensure leg mainA -> mainB is on the near side (closest to stem)
            float3 dirAtoB = math.normalize(legs[mainB].PortalCenterPosition - legs[mainA].PortalCenterPosition);
            float3 toStem = math.normalize(legs[stem].PortalCenterPosition - (legs[mainA].PortalCenterPosition + legs[mainB].PortalCenterPosition) * 0.5f);

            // In RHT (sigma = 1), traffic from A to B flows forward with its right side at (dirAtoB.z, 0, -dirAtoB.x)
            float3 rightHandSide = new float3(dirAtoB.z * sigma, 0f, -dirAtoB.x * sigma);
            if (math.dot(rightHandSide, toStem) < 0f)
            {
                // Swap mainA and mainB so mainA is always the near-side origin
                int tmp = mainA;
                mainA = mainB;
                mainB = tmp;
            }
        }
    }
}
