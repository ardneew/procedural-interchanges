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
        public string Description => "Partial cloverleaf with loop ramps in two opposite quadrants and free-flowing directional outer ramps.";
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
            float R = config.FootprintRadius;
            float sigma = config.LeftHandTraffic ? -1f : 1f;

            var port = new LegPortals[4];
            float3[] dSlip = new float3[4];
            float3[] mSlip = new float3[4];
            float3[] dirs = new float3[4];
            float3[] dLoop = new float3[4];
            float3[] mLoop = new float3[4];

            for (int k = 0; k < 4; k++)
            {
                port[k] = LegPortalGeometry.EmitLegApproaches(legs[k], center, R, 0.22f, sigma, outCourses, $"parclo_leg_{k}");
                dSlip[k] = port[k].DivergeNode;
                mSlip[k] = port[k].MergeNode;
                dirs[k] = port[k].InboundDir;
            }

            for (int k = 0; k < 4; k++)
            {
                mLoop[k] = math.lerp(dSlip[k], center, 0.50f);
                dLoop[k] = math.lerp(mSlip[(k + 2) % 4], center, 0.50f);
            }

            // 1. Primary Mainline (0 <-> 2) at StackLevel = 0
            AddCorridorMainline(0, 2, 0, false, dirs, dSlip, mSlip, mLoop, dLoop, outCourses);
            AddCorridorMainline(2, 0, 0, false, dirs, dSlip, mSlip, mLoop, dLoop, outCourses);

            // 2. Cross Street Bridge (1 <-> 3) at StackLevel = 1 (Overpass)
            AddCorridorMainline(1, 3, 1, true, dirs, dSlip, mSlip, mLoop, dLoop, outCourses);
            AddCorridorMainline(3, 1, 1, true, dirs, dSlip, mSlip, mLoop, dLoop, outCourses);

            // 3. Four Outer Slip Ramps with Gore Taper
            for (int k = 0; k < 4; k++)
            {
                int next = (k + 1) % 4;
                BiArcFit.ApplyGoreTaper(dSlip[k], dirs[k], (int)sigma, BiArcFit.kStandardGoreAngleRad, out float3 rampDir);
                outCourses.Add(new PlannedNetCourse
                {
                    Id = $"parclo_slip_{k}_{next}",
                    Role = NetRole.DirectionalRamp,
                    LegIndex = k,
                    TargetLegIndex = next,
                    Curve = BiArcFit.FitSmoothCurve(dSlip[k], rampDir, mSlip[next], -dirs[next]),
                    StackLevel = 0
                });
            }

            // 4. Two 270-degree Loop Ramps in Opposite Quadrants (0 -> 3 and 2 -> 1) with G1 Tangency
            float loopRadius = math.clamp(R * 0.22f, 30.0f, 50.0f);
            float effectiveClearance = math.max(8.0f, config.MinBridgeClearanceMeters);

            // Loop in Quadrant 0-3: from dLoop[0] to mLoop[3]
            BuildConnectedLoop("parclo_loop0", dLoop[0], mLoop[3], -dirs[2], dirs[3], loopRadius, sigma, outCourses, 0, 3, center.y, effectiveClearance);

            // Loop in Quadrant 2-1: from dLoop[2] to mLoop[1]
            BuildConnectedLoop("parclo_loop2", dLoop[2], mLoop[1], -dirs[0], dirs[1], loopRadius, sigma, outCourses, 2, 1, center.y, effectiveClearance);
        }

        private static void BuildConnectedLoop(
            string idPrefix, float3 startPt, float3 endPt,
            float3 startDir, float3 endDir,
            float radius, float sigma,
            List<PlannedNetCourse> outCourses, int fromLeg, int toLeg,
            float centerGroundY, float effectiveClearance)
        {
            BiArcFit.ApplyGoreTaper(startPt, startDir, (int)sigma, BiArcFit.kStandardGoreAngleRad, out float3 loopStartDir);
            BiArcFit.ApplyGoreTaper(endPt, endDir, -(int)sigma, BiArcFit.kStandardGoreAngleRad, out float3 loopEndDir);

            float startY = centerGroundY;
            float endY = centerGroundY + effectiveClearance;
            float peakY = (startY + endY) * 0.5f;

            var arcs = new List<Bezier4x3>();
            BiArcFit.FitConnectedLoop270(startPt, loopStartDir, endPt, loopEndDir, radius, (int)sigma, startY, peakY, endY, arcs);

            for (int k = 0; k < arcs.Count; k++)
            {
                outCourses.Add(new PlannedNetCourse
                {
                    Id = $"{idPrefix}_{k}",
                    Role = NetRole.LoopRamp,
                    LegIndex = fromLeg,
                    TargetLegIndex = toLeg,
                    Curve = arcs[k],
                    StackLevel = 1,
                    ForceElevated = true
                });
            }
        }

        private static void AddCorridorMainline(
            int fromLeg, int toLeg, int level, bool elevated,
            float3[] dirs,
            float3[] dS, float3[] mS, float3[] mL, float3[] dL,
            List<PlannedNetCourse> outCourses)
        {
            float3 d1 = dS[fromLeg];
            float3 m1 = mL[fromLeg];
            float3 d2 = dL[fromLeg];
            float3 m2 = mS[toLeg];

            float3 dir = dirs[fromLeg];
            float3 targetDir = -dirs[toLeg];

            // 1. Lead to Loop Merge: dSlip -> mLoop
            outCourses.Add(new PlannedNetCourse
            {
                Id = $"parclo_main_{fromLeg}_{toLeg}_lead1",
                Role = NetRole.MainlineThrough,
                LegIndex = fromLeg,
                TargetLegIndex = toLeg,
                Curve = BiArcFit.FitSmoothCurve(d1, dir, m1, dir),
                StackLevel = level,
                ForceElevated = elevated
            });

            // 2. Central Overpass / Underpass Core: mLoop -> dLoop
            outCourses.Add(new PlannedNetCourse
            {
                Id = $"parclo_main_{fromLeg}_{toLeg}_core",
                Role = NetRole.MainlineThrough,
                LegIndex = fromLeg,
                TargetLegIndex = toLeg,
                Curve = BiArcFit.FitSmoothCurve(m1, dir, d2, targetDir),
                StackLevel = level,
                ForceElevated = elevated
            });

            // 3. Lead to Slip Merge: dLoop -> mSlip
            outCourses.Add(new PlannedNetCourse
            {
                Id = $"parclo_main_{fromLeg}_{toLeg}_lead2",
                Role = NetRole.MainlineThrough,
                LegIndex = fromLeg,
                TargetLegIndex = toLeg,
                Curve = BiArcFit.FitSmoothCurve(d2, targetDir, m2, targetDir),
                StackLevel = level,
                ForceElevated = elevated
            });
        }
    }
}
