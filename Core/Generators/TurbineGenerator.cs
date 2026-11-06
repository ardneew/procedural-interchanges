using System.Collections.Generic;
using Colossal.Mathematics;
using ProceduralInterchanges.Core.Math;
using ProceduralInterchanges.Core.Models;
using Unity.Mathematics;

namespace ProceduralInterchanges.Core.Generators
{
    public sealed class TurbineGenerator : IInterchangeGenerator
    {
        public InterchangeArchetype Archetype => InterchangeArchetype.Turbine;
        public string DisplayName => "Turbine Interchange";
        public string Description => "High-capacity 4-way systems interchange using sweeping spiral directional ramps around a central vortex.";
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
            float3[] dNodes = new float3[4];
            float3[] mNodes = new float3[4];
            float3[] dLeftNodes = new float3[4];
            float3[] mLeftNodes = new float3[4];
            float3[] dirs = new float3[4];

            for (int k = 0; k < 4; k++)
            {
                port[k] = LegPortalGeometry.EmitLegApproaches(legs[k], center, R, 0.12f, sigma, outCourses, $"turbine_leg_{k}");
                dNodes[k] = port[k].DivergeNode;
                mNodes[k] = port[k].MergeNode;
                dirs[k] = port[k].InboundDir;

                // Spaced interior nodes for left turn turbine ramps
                dLeftNodes[k] = dNodes[k] + dirs[k] * 24f;
                int opp = (k + 2) % 4;
                mLeftNodes[opp] = mNodes[opp] - dirs[opp] * 24f;
            }

            // 1. Level 0: Primary Mainline (0 <-> 2)
            AddCorridorMainline(0, 2, 0, false, dirs, dNodes, mNodes, outCourses);
            AddCorridorMainline(2, 0, 0, false, dirs, dNodes, mNodes, outCourses);

            // 2. Level 1: Secondary Mainline (1 <-> 3) (Bridge overpass)
            AddCorridorMainline(1, 3, 1, true, dirs, dNodes, mNodes, outCourses);
            AddCorridorMainline(3, 1, 1, true, dirs, dNodes, mNodes, outCourses);

            // 3. Four Outer Right-Turn Ramps (Level 0) with Gore Taper
            for (int k = 0; k < 4; k++)
            {
                int next = (k + 1) % 4;
                BiArcFit.ApplyGoreTaper(dNodes[k], dirs[k], (int)sigma, BiArcFit.kStandardGoreAngleRad, out float3 rampRightDir);
                outCourses.Add(new PlannedNetCourse
                {
                    Id = $"turbine_right_{k}_{next}",
                    Role = NetRole.DirectionalRamp,
                    LegIndex = k,
                    TargetLegIndex = next,
                    Curve = BiArcFit.FitSmoothCurve(dNodes[k], rampRightDir, mNodes[next], -dirs[next]),
                    StackLevel = 0
                });
            }

            // 4. Four Sweeping Orbital Left-Turn Turbine Ramps around vortex perimeter at R_orbit = 0.38 * R
            float rOrbit = math.clamp(R * 0.38f, 55f, 130f);
            for (int i = 0; i < 4; i++)
            {
                int targetLeg = (i + 3) % 4;
                float3 vFrom = math.normalize(-dirs[i]);
                float3 vTo = math.normalize(-dirs[targetLeg]);
                float3 midDir = math.normalize(vFrom + vTo);

                float effectiveClearance = math.max(8.0f, config.MinBridgeClearanceMeters);
                float3 wPos = center + midDir * rOrbit;
                wPos.y = center.y + effectiveClearance * (2 + (i % 2));

                float3 tMid = new float3(-midDir.z, 0f, midDir.x);
                if (math.dot(tMid, vTo) < 0f) tMid = -tMid;
                tMid = math.normalize(tMid);

                BiArcFit.ApplyGoreTaper(dLeftNodes[i], dirs[i], -(int)sigma, BiArcFit.kStandardGoreAngleRad, out float3 rampLeftDir);

                // Sub-course A: Diverge to orbital waypoint
                outCourses.Add(new PlannedNetCourse
                {
                    Id = $"turbine_left_{i}_a",
                    Role = NetRole.DirectionalRamp,
                    LegIndex = i,
                    TargetLegIndex = targetLeg,
                    Curve = BiArcFit.FitSmoothCurve(dLeftNodes[i], rampLeftDir, wPos, tMid),
                    StackLevel = 2 + (i % 2),
                    ForceElevated = true
                });

                // Sub-course B: Orbital waypoint to merge
                outCourses.Add(new PlannedNetCourse
                {
                    Id = $"turbine_left_{i}_b",
                    Role = NetRole.DirectionalRamp,
                    LegIndex = i,
                    TargetLegIndex = targetLeg,
                    Curve = BiArcFit.FitSmoothCurve(wPos, tMid, mLeftNodes[targetLeg], -dirs[targetLeg]),
                    StackLevel = 2 + (i % 2),
                    ForceElevated = true
                });
            }
        }

        private static void AddCorridorMainline(
            int fromLeg, int toLeg, int level, bool elevated,
            float3[] dirs,
            float3[] dNodes, float3[] mNodes,
            List<PlannedNetCourse> outCourses)
        {
            float3 d = dNodes[fromLeg];
            float3 m = mNodes[toLeg];
            float3 dir = dirs[fromLeg];
            float3 targetDir = -dirs[toLeg];

            if (level != 0)
            {
                float3 p1 = d + dir * 35f;
                float3 p2 = m - targetDir * 35f;

                outCourses.Add(new PlannedNetCourse
                {
                    Id = $"turbine_main_{fromLeg}_{toLeg}_app",
                    Role = NetRole.MainlineThrough,
                    LegIndex = fromLeg,
                    TargetLegIndex = toLeg,
                    Curve = BiArcFit.FitSmoothCurve(d, dir, p1, dir),
                    StackLevel = 0,
                    ForceElevated = false
                });

                outCourses.Add(new PlannedNetCourse
                {
                    Id = $"turbine_main_{fromLeg}_{toLeg}_core",
                    Role = NetRole.MainlineThrough,
                    LegIndex = fromLeg,
                    TargetLegIndex = toLeg,
                    Curve = BiArcFit.FitSmoothCurve(p1, dir, p2, targetDir),
                    StackLevel = level,
                    ForceElevated = elevated
                });

                outCourses.Add(new PlannedNetCourse
                {
                    Id = $"turbine_main_{fromLeg}_{toLeg}_dep",
                    Role = NetRole.MainlineThrough,
                    LegIndex = fromLeg,
                    TargetLegIndex = toLeg,
                    Curve = BiArcFit.FitSmoothCurve(p2, targetDir, m, targetDir),
                    StackLevel = 0,
                    ForceElevated = false
                });
            }
            else
            {
                outCourses.Add(new PlannedNetCourse
                {
                    Id = $"turbine_main_{fromLeg}_{toLeg}_core",
                    Role = NetRole.MainlineThrough,
                    LegIndex = fromLeg,
                    TargetLegIndex = toLeg,
                    Curve = BiArcFit.FitSmoothCurve(d, dir, m, targetDir),
                    StackLevel = 0,
                    ForceElevated = false
                });
            }
        }
    }
}
