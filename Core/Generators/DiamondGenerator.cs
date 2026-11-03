using System.Collections.Generic;
using Colossal.Mathematics;
using ProceduralInterchanges.Core.Math;
using ProceduralInterchanges.Core.Models;
using Unity.Mathematics;

namespace ProceduralInterchanges.Core.Generators
{
    public sealed class DiamondGenerator : IInterchangeGenerator
    {
        public InterchangeArchetype Archetype => InterchangeArchetype.Diamond;
        public string DisplayName => "Diamond Interchange";
        public string Description => "Standard 4-way freeway/arterial diamond interchange with 4 diagonal ramps.";
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

            // Legs ordered CCW: 0=MainlineEast, 1=CrossNorth, 2=MainlineWest, 3=CrossSouth
            var leg0 = legs[0];
            var leg1 = legs[1];
            var leg2 = legs[2];
            var leg3 = legs[3];

            float sigma = config.LeftHandTraffic ? -1f : 1f;
            int mainLevel = config.MainlineOverpass ? 1 : 0;
            int crossLevel = config.MainlineOverpass ? 0 : 1;

            var port0 = LegPortalGeometry.EmitLegApproaches(leg0, center, R, 0.25f, sigma, outCourses, "diamond_leg_0");
            var port1 = LegPortalGeometry.EmitLegApproaches(leg1, center, R, 0.38f, sigma, outCourses, "diamond_leg_1");
            var port2 = LegPortalGeometry.EmitLegApproaches(leg2, center, R, 0.25f, sigma, outCourses, "diamond_leg_2");
            var port3 = LegPortalGeometry.EmitLegApproaches(leg3, center, R, 0.38f, sigma, outCourses, "diamond_leg_3");

            float3 d0 = port0.DivergeNode;
            float3 m0 = port0.MergeNode;
            float3 d2 = port2.DivergeNode;
            float3 m2 = port2.MergeNode;
            float3 dir0 = port0.InboundDir;
            float3 dir2 = port2.InboundDir;

            float3 d1 = port1.DivergeNode;
            float3 m1 = port1.MergeNode;
            float3 d3 = port3.DivergeNode;
            float3 m3 = port3.MergeNode;
            float3 dir1 = port1.InboundDir;
            float3 dir3 = port3.InboundDir;

            // 1. Primary Mainline Through Cores (Level mainLevel)
            // 0 -> 2
            outCourses.Add(new PlannedNetCourse
            {
                Id = "diamond_main_0_2_core",
                Role = NetRole.MainlineThrough,
                LegIndex = 0,
                TargetLegIndex = 2,
                Curve = BiArcFit.FitSmoothCurve(d0, dir0, m2, -dir2),
                StackLevel = mainLevel,
                ForceElevated = mainLevel > 0
            });

            // 2 -> 0
            outCourses.Add(new PlannedNetCourse
            {
                Id = "diamond_main_2_0_core",
                Role = NetRole.MainlineThrough,
                LegIndex = 2,
                TargetLegIndex = 0,
                Curve = BiArcFit.FitSmoothCurve(d2, dir2, m0, -dir0),
                StackLevel = mainLevel,
                ForceElevated = mainLevel > 0
            });

            // 2. Cross Street (1 <-> 3) Bridge Cores (Level crossLevel)
            // 1 -> 3
            outCourses.Add(new PlannedNetCourse
            {
                Id = "diamond_cross_1_3_bridge",
                Role = NetRole.CrossStreet,
                LegIndex = 1,
                TargetLegIndex = 3,
                Curve = BiArcFit.FitSmoothCurve(d1, dir1, m3, -dir3),
                StackLevel = crossLevel,
                ForceElevated = crossLevel > 0
            });

            // 3 -> 1
            outCourses.Add(new PlannedNetCourse
            {
                Id = "diamond_cross_3_1_bridge",
                Role = NetRole.CrossStreet,
                LegIndex = 3,
                TargetLegIndex = 1,
                Curve = BiArcFit.FitSmoothCurve(d3, dir3, m1, -dir1),
                StackLevel = crossLevel,
                ForceElevated = crossLevel > 0
            });

            // 3. Four Diagonal Service Ramps connecting (d0, d1, m2, d2, d3, m0)
            BiArcFit.ApplyGoreTaper(d0, dir0, (int)sigma, BiArcFit.kStandardGoreAngleRad, out float3 ramp0Dir);
            BiArcFit.ApplyGoreTaper(m2, -dir2, -(int)sigma, BiArcFit.kStandardGoreAngleRad, out float3 ramp2MergeDir);
            BiArcFit.ApplyGoreTaper(d2, dir2, (int)sigma, BiArcFit.kStandardGoreAngleRad, out float3 ramp2Dir);
            BiArcFit.ApplyGoreTaper(m0, -dir0, -(int)sigma, BiArcFit.kStandardGoreAngleRad, out float3 ramp0MergeDir);

            // Ramp 0 In to Cross Street 1 Out: d0 -> m1
            outCourses.Add(new PlannedNetCourse
            {
                Id = "diamond_ramp_0_in",
                Role = NetRole.DirectionalRamp,
                LegIndex = 0,
                TargetLegIndex = 1,
                Curve = BiArcFit.FitSmoothCurve(d0, ramp0Dir, m1, -dir1),
                StackLevel = 0
            });

            // Ramp Cross Street 1 In to 2 Out: d1 -> m2
            outCourses.Add(new PlannedNetCourse
            {
                Id = "diamond_ramp_2_out",
                Role = NetRole.DirectionalRamp,
                LegIndex = 1,
                TargetLegIndex = 2,
                Curve = BiArcFit.FitSmoothCurve(d1, dir1, m2, ramp2MergeDir),
                StackLevel = 0
            });

            // Ramp 2 In to Cross Street 3 Out: d2 -> m3
            outCourses.Add(new PlannedNetCourse
            {
                Id = "diamond_ramp_2_in",
                Role = NetRole.DirectionalRamp,
                LegIndex = 2,
                TargetLegIndex = 3,
                Curve = BiArcFit.FitSmoothCurve(d2, ramp2Dir, m3, -dir3),
                StackLevel = 0
            });

            // Ramp Cross Street 3 In to 0 Out: d3 -> m0
            outCourses.Add(new PlannedNetCourse
            {
                Id = "diamond_ramp_0_out",
                Role = NetRole.DirectionalRamp,
                LegIndex = 3,
                TargetLegIndex = 0,
                Curve = BiArcFit.FitSmoothCurve(d3, dir3, m0, ramp0MergeDir),
                StackLevel = 0
            });
        }
    }
}
