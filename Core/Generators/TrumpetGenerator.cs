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
            var portalsA = LegPortalGeometry.EmitLegApproaches(legA, center, R, 0.14f, sigma, outCourses, "trumpet_main_A");
            var portalsB = LegPortalGeometry.EmitLegApproaches(legB, center, R, 0.14f, sigma, outCourses, "trumpet_main_B");
            var portalsStem = LegPortalGeometry.EmitLegApproaches(legStem, center, R, 0.18f, sigma, outCourses, "trumpet_stem");

            float3 dA = portalsA.DivergeNode;
            float3 mA = portalsA.MergeNode;
            float3 dB = portalsB.DivergeNode;
            float3 mB = portalsB.MergeNode;
            float3 dS = portalsStem.DivergeNode;
            float3 mS = portalsStem.MergeNode;

            float3 dirA = portalsA.InboundDir;
            float3 dirB = portalsB.InboundDir;
            float3 dirStem = portalsStem.InboundDir;

            const float kGoreAngle = 0.20f; // ~11.5° AASHTO gore divergence angle

            // Staggered nodes on the stem leg
            float3 dS_direct = dS;
            float3 dS_overpass = dS + dirStem * 26f;
            float3 mS_slip = mS + dirStem * 26f;
            float3 mS_loop = mS;

            // Stem approach connector: dS_direct -> dS_overpass
            outCourses.Add(new PlannedNetCourse
            {
                Id = "trumpet_stem_app_connector",
                Role = NetRole.MainlineThrough,
                LegIndex = stemIdx,
                TargetLegIndex = -1,
                Curve = BiArcFit.FitSmoothCurve(dS_direct, dirStem, dS_overpass, dirStem),
                StackLevel = 0
            });

            // Stem departure connector: mS_slip -> mS_loop
            outCourses.Add(new PlannedNetCourse
            {
                Id = "trumpet_stem_dep_connector",
                Role = NetRole.MainlineThrough,
                LegIndex = -1,
                TargetLegIndex = stemIdx,
                Curve = BiArcFit.FitSmoothCurve(mS_slip, -dirStem, mS_loop, -dirStem),
                StackLevel = 0
            });

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

            // 2. Near-Side Outer Slip Ramp: dA -> mS_slip (pure ground ramp, never crosses any road)
            BiArcFit.ApplyGoreTaper(dA, dirA, (int)sigma, kGoreAngle, out float3 slipStartDir);
            BiArcFit.ApplyGoreTaper(mS_slip, -dirStem, -(int)sigma, kGoreAngle, out float3 slipEndDir);
            outCourses.Add(new PlannedNetCourse
            {
                Id = "trumpet_outer_slip",
                Role = NetRole.DirectionalRamp,
                LegIndex = mainAIdx,
                TargetLegIndex = stemIdx,
                Curve = BiArcFit.FitSmoothCurve(dA, slipStartDir, mS_slip, slipEndDir),
                StackLevel = 0
            });

            // 3. Near-Side Direct Merge: dS_direct -> mB (ground ramp merging into near-side mainline)
            BiArcFit.ApplyGoreTaper(dS_direct, dirStem, (int)sigma, kGoreAngle, out float3 directMergeDir);
            BiArcFit.ApplyGoreTaper(mB, -dirB, -(int)sigma, kGoreAngle, out float3 directEndDir);
            outCourses.Add(new PlannedNetCourse
            {
                Id = "trumpet_stem_to_B",
                Role = NetRole.DirectionalRamp,
                LegIndex = stemIdx,
                TargetLegIndex = mainBIdx,
                Curve = BiArcFit.FitSmoothCurve(dS_direct, directMergeDir, mB, directEndDir),
                StackLevel = 0
            });

            // 4. Semi-Directional Overpass Ramp: dS_overpass -> mA across center (StackLevel = 1, crosses near-side mainline AB)
            BiArcFit.ApplyGoreTaper(dS_overpass, dirStem, -(int)sigma, kGoreAngle, out float3 overpassStartDir);
            BiArcFit.ApplyGoreTaper(mA, -dirA, (int)sigma, kGoreAngle, out float3 overpassEndDir);
            outCourses.Add(new PlannedNetCourse
            {
                Id = "trumpet_overpass_ramp",
                Role = NetRole.DirectionalRamp,
                LegIndex = stemIdx,
                TargetLegIndex = mainAIdx,
                Curve = BiArcFit.FitSmoothCurve(dS_overpass, overpassStartDir, mA, overpassEndDir),
                StackLevel = 1,
                ForceElevated = true
            });

            // 5. 270-degree Trumpet Loop Ramp: dB -> mS_loop with G1 Tangency via FitConnectedLoop270
            float loopRadius = math.clamp(R * 0.26f, 44.0f, 62.0f);
            BiArcFit.ApplyGoreTaper(dB, dirB, (int)sigma, kGoreAngle, out float3 loopStartDir);
            BiArcFit.ApplyGoreTaper(mS_loop, -dirStem, (int)sigma, kGoreAngle, out float3 loopEndDir);

            var loopArcs = new List<Bezier4x3>();
            BiArcFit.FitConnectedLoop270(dB, loopStartDir, mS_loop, loopEndDir, loopRadius, (int)sigma, center.y, center.y + config.MinBridgeClearanceMeters, center.y, loopArcs);

            for (int l = 0; l < loopArcs.Count; l++)
            {
                outCourses.Add(new PlannedNetCourse
                {
                    Id = $"trumpet_loop_{l}",
                    Role = NetRole.LoopRamp,
                    LegIndex = mainBIdx,
                    TargetLegIndex = stemIdx,
                    Curve = loopArcs[l],
                    StackLevel = (l > 0 && l < loopArcs.Count - 1) ? 1 : 0,
                    ForceElevated = (l > 0 && l < loopArcs.Count - 1)
                });
            }
        }

        private static void IdentifyMainlineAndStem(IReadOnlyList<HighwayLeg> legs, float sigma, out int mainA, out int mainB, out int stem)
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

            float3 dirAtoB = math.normalize(legs[mainB].PortalCenterPosition - legs[mainA].PortalCenterPosition);
            float3 toStem = math.normalize(legs[stem].PortalCenterPosition - (legs[mainA].PortalCenterPosition + legs[mainB].PortalCenterPosition) * 0.5f);

            float3 rightHandSide = new float3(dirAtoB.z * sigma, 0f, -dirAtoB.x * sigma);
            if (math.dot(rightHandSide, toStem) < 0f)
            {
                int tmp = mainA;
                mainA = mainB;
                mainB = tmp;
            }
        }
    }
}
