using System.Collections.Generic;
using Colossal.Mathematics;
using ProceduralInterchanges.Core.Math;
using ProceduralInterchanges.Core.Models;
using Unity.Mathematics;

namespace ProceduralInterchanges.Core.Generators
{
    public sealed class CloverleafGenerator : IInterchangeGenerator
    {
        public InterchangeArchetype Archetype => InterchangeArchetype.Cloverleaf;
        public string DisplayName => "Cloverleaf Interchange";
        public string Description => "Full 4-way systems interchange with 4 loop ramps and collector-distributor weave lanes.";
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
            float3[] dRight = new float3[4];
            float3[] mRight = new float3[4];
            float3[] dirs = new float3[4];
            float3[] dLoop = new float3[4];
            float3[] mLoop = new float3[4];

            for (int k = 0; k < 4; k++)
            {
                port[k] = LegPortalGeometry.EmitLegApproaches(legs[k], center, R, 0.20f, sigma, outCourses, $"clover_leg_{k}");
                dRight[k] = port[k].DivergeNode;
                mRight[k] = port[k].MergeNode;
                dirs[k] = port[k].InboundDir;
            }

            for (int k = 0; k < 4; k++)
            {
                mLoop[k] = math.lerp(dRight[k], center, 0.55f);
                dLoop[k] = math.lerp(mRight[(k + 2) % 4], center, 0.55f);
            }

            // 1. Primary Mainline (0 <-> 2) at StackLevel = 0
            AddCorridorMainline(0, 2, 0, false, dirs, dRight, mRight, mLoop, dLoop, outCourses);
            AddCorridorMainline(2, 0, 0, false, dirs, dRight, mRight, mLoop, dLoop, outCourses);

            // 2. Secondary Mainline (1 <-> 3) at StackLevel = 1 (Bridge overpass)
            AddCorridorMainline(1, 3, 1, true, dirs, dRight, mRight, mLoop, dLoop, outCourses);
            AddCorridorMainline(3, 1, 1, true, dirs, dRight, mRight, mLoop, dLoop, outCourses);

            // 3. Four Outer Sweeping Slip Ramps with Gore Taper
            for (int k = 0; k < 4; k++)
            {
                int next = (k + 1) % 4;
                BiArcFit.ApplyGoreTaper(dRight[k], dirs[k], (int)sigma, BiArcFit.kStandardGoreAngleRad, out float3 rampDir);
                outCourses.Add(new PlannedNetCourse
                {
                    Id = $"clover_outer_{k}_{next}",
                    Role = NetRole.DirectionalRamp,
                    LegIndex = k,
                    TargetLegIndex = next,
                    Curve = BiArcFit.FitSmoothCurve(dRight[k], rampDir, mRight[next], -dirs[next]),
                    StackLevel = 0
                });
            }

            // 4. Collector-Distributor (C-D) Lanes alongside corridors to isolate loop weaving
            for (int k = 0; k < 4; k++)
            {
                int opp = (k + 2) % 4;
                float3 normal = new float3(-dirs[k].z, 0f, dirs[k].x) * (sigma >= 0 ? 1f : -1f);
                float cdOffset = 12f;

                float3 cdDiv = dRight[k] + dirs[k] * 12f;
                float3 cdMerge = mRight[opp] - dirs[opp] * 12f;

                float3 cdMidA = mLoop[k] + normal * cdOffset;
                float3 cdMidB = dLoop[k] + normal * cdOffset;

                outCourses.Add(new PlannedNetCourse
                {
                    Id = $"clover_cd_{k}_app",
                    Role = NetRole.CollectorDistributor,
                    LegIndex = k,
                    TargetLegIndex = opp,
                    Curve = BiArcFit.FitSmoothCurve(cdDiv, dirs[k], cdMidA, dirs[k]),
                    StackLevel = (k % 2 == 1) ? 1 : 0,
                    ForceElevated = (k % 2 == 1)
                });

                outCourses.Add(new PlannedNetCourse
                {
                    Id = $"clover_cd_{k}_mid",
                    Role = NetRole.CollectorDistributor,
                    LegIndex = k,
                    TargetLegIndex = opp,
                    Curve = BiArcFit.FitSmoothCurve(cdMidA, dirs[k], cdMidB, -dirs[opp]),
                    StackLevel = (k % 2 == 1) ? 1 : 0,
                    ForceElevated = (k % 2 == 1)
                });

                outCourses.Add(new PlannedNetCourse
                {
                    Id = $"clover_cd_{k}_dep",
                    Role = NetRole.CollectorDistributor,
                    LegIndex = k,
                    TargetLegIndex = opp,
                    Curve = BiArcFit.FitSmoothCurve(cdMidB, -dirs[opp], cdMerge, -dirs[opp]),
                    StackLevel = (k % 2 == 1) ? 1 : 0,
                    ForceElevated = (k % 2 == 1)
                });
            }

            // 5. Four 270-degree Inner Loop Ramps with G1 Tangency via FitConnectedLoop270
            float loopRadius = math.clamp(R * 0.22f, 30.0f, 48.0f);
            float effClear = math.max(8.0f, config.MinBridgeClearanceMeters);

            for (int q = 0; q < 4; q++)
            {
                int targetLeg = (q + 1) % 4;
                float3 startPt = dLoop[q];
                float3 endPt = mLoop[targetLeg];

                bool startElevated = (q % 2 != 0); // legs 1 and 3 are on overpass mainline
                bool endElevated = (targetLeg % 2 != 0);

                float startY = startElevated ? center.y + effClear : center.y;
                float endY = endElevated ? center.y + effClear : center.y;
                float peakY = (startY + endY) * 0.5f;

                BiArcFit.ApplyGoreTaper(startPt, -dirs[(q + 2) % 4], (int)sigma, BiArcFit.kStandardGoreAngleRad, out float3 loopStartDir);
                BiArcFit.ApplyGoreTaper(endPt, dirs[targetLeg], -(int)sigma, BiArcFit.kStandardGoreAngleRad, out float3 loopEndDir);

                var loopArcs = new List<Bezier4x3>();
                BiArcFit.FitConnectedLoop270(
                    startPt, loopStartDir,
                    endPt, loopEndDir,
                    loopRadius, (int)sigma,
                    startY, peakY, endY,
                    loopArcs);

                for (int a = 0; a < loopArcs.Count; a++)
                {
                    outCourses.Add(new PlannedNetCourse
                    {
                        Id = $"clover_loop_q{q}_{a}",
                        Role = NetRole.LoopRamp,
                        LegIndex = q,
                        TargetLegIndex = targetLeg,
                        Curve = loopArcs[a],
                        StackLevel = (startElevated || endElevated) ? 1 : 0,
                        ForceElevated = (startElevated || endElevated)
                    });
                }
            }
        }

        private static void AddCorridorMainline(
            int fromLeg, int toLeg, int level, bool elevated,
            float3[] dirs,
            float3[] dR, float3[] mR, float3[] mL, float3[] dL,
            List<PlannedNetCourse> outCourses)
        {
            float3 d1 = dR[fromLeg];
            float3 m1 = mL[fromLeg];
            float3 d2 = dL[fromLeg];
            float3 m2 = mR[toLeg];

            float3 dir = dirs[fromLeg];
            float3 targetDir = -dirs[toLeg];

            // 1. Lead to Loop Merge: dRight -> mLoop
            outCourses.Add(new PlannedNetCourse
            {
                Id = $"clover_main_{fromLeg}_{toLeg}_lead1",
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
                Id = $"clover_main_{fromLeg}_{toLeg}_core",
                Role = NetRole.MainlineThrough,
                LegIndex = fromLeg,
                TargetLegIndex = toLeg,
                Curve = BiArcFit.FitSmoothCurve(m1, dir, d2, targetDir),
                StackLevel = level,
                ForceElevated = elevated
            });

            // 3. Lead to Right Merge: dLoop -> mRight
            outCourses.Add(new PlannedNetCourse
            {
                Id = $"clover_main_{fromLeg}_{toLeg}_lead2",
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
