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
            float3[] dirs = new float3[4];

            for (int k = 0; k < 4; k++)
            {
                port[k] = LegPortalGeometry.EmitLegApproaches(legs[k], center, R, 0.25f, sigma, outCourses, $"turbine_leg_{k}");
                dNodes[k] = port[k].DivergeNode;
                mNodes[k] = port[k].MergeNode;
                dirs[k] = port[k].InboundDir;
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

            // 4. Four Sweeping Orbital Left-Turn Turbine Ramps around vortex perimeter at R_orbit = 0.34 * R
            float rOrbit = R * 0.34f;
            for (int i = 0; i < 4; i++)
            {
                int targetLeg = (i + 3) % 4;
                float theta_i = math.atan2(-dirs[i].z, -dirs[i].x);
                float theta_mid = theta_i - sigma * (math.PI * 0.25f);

                float effectiveClearance = math.max(8.0f, config.MinBridgeClearanceMeters);
                float3 wPos = center + new float3(math.cos(theta_mid), 0f, math.sin(theta_mid)) * rOrbit;
                wPos.y = center.y + effectiveClearance * (2 + (i % 2));

                float3 tMid = math.normalize(new float3(-math.sin(theta_mid), 0f, math.cos(theta_mid)) * (-sigma));

                BiArcFit.ApplyGoreTaper(dNodes[i], dirs[i], -(int)sigma, BiArcFit.kStandardGoreAngleRad, out float3 rampLeftDir);

                // Sub-course A: Diverge to orbital waypoint
                outCourses.Add(new PlannedNetCourse
                {
                    Id = $"turbine_left_{i}_a",
                    Role = NetRole.DirectionalRamp,
                    LegIndex = i,
                    TargetLegIndex = targetLeg,
                    Curve = BiArcFit.FitSmoothCurve(dNodes[i], rampLeftDir, wPos, tMid),
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
                    Curve = BiArcFit.FitSmoothCurve(wPos, tMid, mNodes[targetLeg], -dirs[targetLeg]),
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

            outCourses.Add(new PlannedNetCourse
            {
                Id = $"turbine_main_{fromLeg}_{toLeg}_core",
                Role = NetRole.MainlineThrough,
                LegIndex = fromLeg,
                TargetLegIndex = toLeg,
                Curve = BiArcFit.FitSmoothCurve(d, dir, m, targetDir),
                StackLevel = level,
                ForceElevated = elevated
            });
        }
    }
}
