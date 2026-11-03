using System.Collections.Generic;
using Colossal.Mathematics;
using ProceduralInterchanges.Core.Math;
using ProceduralInterchanges.Core.Models;
using Unity.Mathematics;

namespace ProceduralInterchanges.Core.Generators
{
    public sealed class FourLevelStackGenerator : IInterchangeGenerator
    {
        public InterchangeArchetype Archetype => InterchangeArchetype.FourLevelStack;
        public string DisplayName => "Four-Level Stack";
        public string Description => "Full directional 4-way systems interchange using multi-level flyover ramps.";
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
                port[k] = LegPortalGeometry.EmitLegApproaches(legs[k], center, R, 0.25f, sigma, outCourses, $"stack_leg_{k}");
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

            // 3. Four Outer Right-Turn Directional Ramps (Level 0) connecting dNodes[k] to mNodes[(k+1)%4]
            for (int k = 0; k < 4; k++)
            {
                int next = (k + 1) % 4;
                BiArcFit.ApplyGoreTaper(dNodes[k], dirs[k], (int)sigma, BiArcFit.kStandardGoreAngleRad, out float3 rightStartDir);
                BiArcFit.ApplyGoreTaper(mNodes[next], -dirs[next], -(int)sigma, BiArcFit.kStandardGoreAngleRad, out float3 rightMergeDir);
                outCourses.Add(new PlannedNetCourse
                {
                    Id = $"stack_right_{k}_{next}",
                    Role = NetRole.DirectionalRamp,
                    LegIndex = k,
                    TargetLegIndex = next,
                    Curve = BiArcFit.FitSmoothCurve(dNodes[k], rightStartDir, mNodes[next], rightMergeDir),
                    StackLevel = 0
                });
            }

            // 4. Level 2: Semi-Directional Left-Turn Flyovers (0 -> 3 and 2 -> 1)
            BiArcFit.ApplyGoreTaper(dNodes[0], dirs[0], -(int)sigma, BiArcFit.kStandardGoreAngleRad, out float3 fly0Start);
            BiArcFit.ApplyGoreTaper(mNodes[3], -dirs[3], (int)sigma, BiArcFit.kStandardGoreAngleRad, out float3 fly3Merge);
            outCourses.Add(new PlannedNetCourse
            {
                Id = "stack_flyover_0_3",
                Role = NetRole.DirectionalRamp,
                LegIndex = 0,
                TargetLegIndex = 3,
                Curve = BiArcFit.FitSmoothCurve(dNodes[0], fly0Start, mNodes[3], fly3Merge),
                StackLevel = 2,
                ForceElevated = true
            });

            BiArcFit.ApplyGoreTaper(dNodes[2], dirs[2], -(int)sigma, BiArcFit.kStandardGoreAngleRad, out float3 fly2Start);
            BiArcFit.ApplyGoreTaper(mNodes[1], -dirs[1], (int)sigma, BiArcFit.kStandardGoreAngleRad, out float3 fly1Merge);
            outCourses.Add(new PlannedNetCourse
            {
                Id = "stack_flyover_2_1",
                Role = NetRole.DirectionalRamp,
                LegIndex = 2,
                TargetLegIndex = 1,
                Curve = BiArcFit.FitSmoothCurve(dNodes[2], fly2Start, mNodes[1], fly1Merge),
                StackLevel = 2,
                ForceElevated = true
            });

            // 5. Level 3: Top-Tier Semi-Directional Left-Turn Flyovers (1 -> 0 and 3 -> 2)
            BiArcFit.ApplyGoreTaper(dNodes[1], dirs[1], -(int)sigma, BiArcFit.kStandardGoreAngleRad, out float3 fly1TopStart);
            BiArcFit.ApplyGoreTaper(mNodes[0], -dirs[0], (int)sigma, BiArcFit.kStandardGoreAngleRad, out float3 fly0TopMerge);
            outCourses.Add(new PlannedNetCourse
            {
                Id = "stack_flyover_1_0",
                Role = NetRole.DirectionalRamp,
                LegIndex = 1,
                TargetLegIndex = 0,
                Curve = BiArcFit.FitSmoothCurve(dNodes[1], fly1TopStart, mNodes[0], fly0TopMerge),
                StackLevel = 3,
                ForceElevated = true
            });

            BiArcFit.ApplyGoreTaper(dNodes[3], dirs[3], -(int)sigma, BiArcFit.kStandardGoreAngleRad, out float3 fly3TopStart);
            BiArcFit.ApplyGoreTaper(mNodes[2], -dirs[2], (int)sigma, BiArcFit.kStandardGoreAngleRad, out float3 fly2TopMerge);
            outCourses.Add(new PlannedNetCourse
            {
                Id = "stack_flyover_3_2",
                Role = NetRole.DirectionalRamp,
                LegIndex = 3,
                TargetLegIndex = 2,
                Curve = BiArcFit.FitSmoothCurve(dNodes[3], fly3TopStart, mNodes[2], fly2TopMerge),
                StackLevel = 3,
                ForceElevated = true
            });
        }

        private static void AddCorridorMainline(
            int fromLeg, int toLeg, int level, bool elevated,
            float3[] dirs, float3[] dNodes, float3[] mNodes,
            List<PlannedNetCourse> outCourses)
        {
            float3 d = dNodes[fromLeg];
            float3 m = mNodes[toLeg];
            float3 dir = dirs[fromLeg];
            float3 targetDir = -dirs[toLeg];

            outCourses.Add(new PlannedNetCourse
            {
                Id = $"stack_main_{fromLeg}_{toLeg}_core",
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
