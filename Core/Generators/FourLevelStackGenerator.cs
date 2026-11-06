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
        public string Description => "Full directional 4-way systems interchange using multi-level flyover ramps and shared topological graph.";
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

            // 1. Resolve Stack Levels (UndergroundMode and MainlineOverpass aware)
            InterchangeGraphBuilder.ResolveStackLevels(
                config,
                out int priLevel,
                out int secLevel,
                out int outerLevel,
                out int fly1Level,
                out int fly2Level);

            // 2. Identify Corridor Pairs
            InterchangeGraphBuilder.IdentifyCorridorPairs(
                legs,
                config,
                out CorridorLegPair primaryPair,
                out CorridorLegPair secondaryPair,
                out _);

            var legA = legs[primaryPair.LegA];
            var legB = legs[primaryPair.LegB];
            var legC = legs[secondaryPair.LegA];
            var legD = legs[secondaryPair.LegB];

            // 3. Emit host highway approaches & departures with valid attachment entities and strict collinearity
            var portA = LegPortalGeometry.EmitLegApproaches(legA, center, R, 0.12f, sigma, outCourses, "stack_leg_A");
            var portB = LegPortalGeometry.EmitLegApproaches(legB, center, R, 0.12f, sigma, outCourses, "stack_leg_B");
            var portC = LegPortalGeometry.EmitLegApproaches(legC, center, R, 0.12f, sigma, outCourses, "stack_leg_C");
            var portD = LegPortalGeometry.EmitLegApproaches(legD, center, R, 0.12f, sigma, outCourses, "stack_leg_D");

            var spinePri = InterchangeGraphBuilder.BuildCorridorSpineFromPortals(legA.LegIndex, portA, legB.LegIndex, portB);
            var spineSec = InterchangeGraphBuilder.BuildCorridorSpineFromPortals(legC.LegIndex, portC, legD.LegIndex, portD);

            // On-Spine Node Subdivision Parameters: utilize maximum runout distance for gentle AASHTO grades
            // t1: outer right diverge, t2: left flyover diverge, t3: left flyover merge, t4: outer right merge
            const float t1 = 0.05f;
            const float t2 = 0.12f;
            const float t3 = 0.88f;
            const float t4 = 0.95f;

            // Subdivide Primary Forward (A -> B)
            SubdivideMainline(spinePri.ForwardSpine, legA.LegIndex, legB.LegIndex, priLevel, "stack_pri_fwd", t1, t2, t3, t4, outCourses,
                out float3 dPriFwdRight, out float3 tPriFwdRight,
                out float3 dPriFwdFly, out float3 tPriFwdFly,
                out float3 mPriFwdFly, out float3 tPriFwdFlyArr,
                out float3 mPriFwdRight, out float3 tPriFwdRightArr, sigma);

            // Subdivide Primary Reverse (B -> A)
            SubdivideMainline(spinePri.ReverseSpine, legB.LegIndex, legA.LegIndex, priLevel, "stack_pri_rev", t1, t2, t3, t4, outCourses,
                out float3 dPriRevRight, out float3 tPriRevRight,
                out float3 dPriRevFly, out float3 tPriRevFly,
                out float3 mPriRevFly, out float3 tPriRevFlyArr,
                out float3 mPriRevRight, out float3 tPriRevRightArr, sigma);

            // Subdivide Secondary Forward (C -> D)
            SubdivideMainline(spineSec.ForwardSpine, legC.LegIndex, legD.LegIndex, secLevel, "stack_sec_fwd", t1, t2, t3, t4, outCourses,
                out float3 dSecFwdRight, out float3 tSecFwdRight,
                out float3 dSecFwdFly, out float3 tSecFwdFly,
                out float3 mSecFwdFly, out float3 tSecFwdFlyArr,
                out float3 mSecFwdRight, out float3 tSecFwdRightArr, sigma);

            // Subdivide Secondary Reverse (D -> C)
            SubdivideMainline(spineSec.ReverseSpine, legD.LegIndex, legC.LegIndex, secLevel, "stack_sec_rev", t1, t2, t3, t4, outCourses,
                out float3 dSecRevRight, out float3 tSecRevRight,
                out float3 dSecRevFly, out float3 tSecRevFly,
                out float3 mSecRevFly, out float3 tSecRevFlyArr,
                out float3 mSecRevRight, out float3 tSecRevRightArr, sigma);

            // Determine geometrically for each leg which crossing leg is on its RIGHT and which is on its LEFT
            bool cIsRightOfA = (portA.InboundDir.x * (portC.MergeNode.z - portA.DivergeNode.z) - portA.InboundDir.z * (portC.MergeNode.x - portA.DivergeNode.x)) < 0f;
            bool cIsRightOfB = (portB.InboundDir.x * (portC.MergeNode.z - portB.DivergeNode.z) - portB.InboundDir.z * (portC.MergeNode.x - portB.DivergeNode.x)) < 0f;
            bool aIsRightOfC = (portC.InboundDir.x * (portA.MergeNode.z - portC.DivergeNode.z) - portC.InboundDir.z * (portA.MergeNode.x - portC.DivergeNode.x)) < 0f;
            bool aIsRightOfD = (portD.InboundDir.x * (portA.MergeNode.z - portD.DivergeNode.z) - portD.InboundDir.z * (portA.MergeNode.x - portD.DivergeNode.x)) < 0f;

            if (sigma >= 0)
            {
                // RHT:
                // 4. Connect 4 Outer Right-Turn Directional Ramps (Level outerLevel)
                if (cIsRightOfA)
                    AddRamp(dPriFwdRight, tPriFwdRight, mSecRevRight, tSecRevRightArr, legA.LegIndex, legC.LegIndex, outerLevel, false, "stack_right_A_C", outCourses);
                else
                    AddRamp(dPriFwdRight, tPriFwdRight, mSecFwdRight, tSecFwdRightArr, legA.LegIndex, legD.LegIndex, outerLevel, false, "stack_right_A_D", outCourses);

                if (cIsRightOfB)
                    AddRamp(dPriRevRight, tPriRevRight, mSecRevRight, tSecRevRightArr, legB.LegIndex, legC.LegIndex, outerLevel, false, "stack_right_B_C", outCourses);
                else
                    AddRamp(dPriRevRight, tPriRevRight, mSecFwdRight, tSecFwdRightArr, legB.LegIndex, legD.LegIndex, outerLevel, false, "stack_right_B_D", outCourses);

                if (aIsRightOfC)
                    AddRamp(dSecFwdRight, tSecFwdRight, mPriRevRight, tPriRevRightArr, legC.LegIndex, legA.LegIndex, outerLevel, false, "stack_right_C_A", outCourses);
                else
                    AddRamp(dSecFwdRight, tSecFwdRight, mPriFwdRight, tPriFwdRightArr, legC.LegIndex, legB.LegIndex, outerLevel, false, "stack_right_C_B", outCourses);

                if (aIsRightOfD)
                    AddRamp(dSecRevRight, tSecRevRight, mPriRevRight, tPriRevRightArr, legD.LegIndex, legA.LegIndex, outerLevel, false, "stack_right_D_A", outCourses);
                else
                    AddRamp(dSecRevRight, tSecRevRight, mPriFwdRight, tPriFwdRightArr, legD.LegIndex, legB.LegIndex, outerLevel, false, "stack_right_D_B", outCourses);

                // 5. Connect 4 Semi-Directional Left-Turn Flyovers
                // Tier 1 Flyovers: from A and B (StackLevel fly1Level)
                if (cIsRightOfA)
                    AddRamp(dPriFwdFly, tPriFwdFly, mSecFwdFly, tSecFwdFlyArr, legA.LegIndex, legD.LegIndex, fly1Level, fly1Level > 0, "stack_fly_A_D", outCourses);
                else
                    AddRamp(dPriFwdFly, tPriFwdFly, mSecRevFly, tSecRevFlyArr, legA.LegIndex, legC.LegIndex, fly1Level, fly1Level > 0, "stack_fly_A_C", outCourses);

                if (cIsRightOfB)
                    AddRamp(dPriRevFly, tPriRevFly, mSecFwdFly, tSecFwdFlyArr, legB.LegIndex, legD.LegIndex, fly1Level, fly1Level > 0, "stack_fly_B_D", outCourses);
                else
                    AddRamp(dPriRevFly, tPriRevFly, mSecRevFly, tSecRevFlyArr, legB.LegIndex, legC.LegIndex, fly1Level, fly1Level > 0, "stack_fly_B_C", outCourses);

                // Tier 2 Flyovers: from C and D (StackLevel fly2Level)
                if (aIsRightOfC)
                    AddRamp(dSecFwdFly, tSecFwdFly, mPriFwdFly, tPriFwdFlyArr, legC.LegIndex, legB.LegIndex, fly2Level, fly2Level > 0, "stack_fly_C_B", outCourses);
                else
                    AddRamp(dSecFwdFly, tSecFwdFly, mPriRevFly, tPriRevFlyArr, legC.LegIndex, legA.LegIndex, fly2Level, fly2Level > 0, "stack_fly_C_A", outCourses);

                if (aIsRightOfD)
                    AddRamp(dSecRevFly, tSecRevFly, mPriFwdFly, tPriFwdFlyArr, legD.LegIndex, legB.LegIndex, fly2Level, fly2Level > 0, "stack_fly_D_B", outCourses);
                else
                    AddRamp(dSecRevFly, tSecRevFly, mPriRevFly, tPriRevFlyArr, legD.LegIndex, legA.LegIndex, fly2Level, fly2Level > 0, "stack_fly_D_A", outCourses);
            }
            else
            {
                // LHT: Mirrored outer and flyover turn directions
                if (cIsRightOfA)
                    AddRamp(dPriFwdRight, tPriFwdRight, mSecFwdRight, tSecFwdRightArr, legA.LegIndex, legD.LegIndex, outerLevel, false, "stack_left_A_D", outCourses);
                else
                    AddRamp(dPriFwdRight, tPriFwdRight, mSecRevRight, tSecRevRightArr, legA.LegIndex, legC.LegIndex, outerLevel, false, "stack_left_A_C", outCourses);

                if (cIsRightOfB)
                    AddRamp(dPriRevRight, tPriRevRight, mSecFwdRight, tSecFwdRightArr, legB.LegIndex, legD.LegIndex, outerLevel, false, "stack_left_B_D", outCourses);
                else
                    AddRamp(dPriRevRight, tPriRevRight, mSecRevRight, tSecRevRightArr, legB.LegIndex, legC.LegIndex, outerLevel, false, "stack_left_B_C", outCourses);

                if (aIsRightOfC)
                    AddRamp(dSecFwdRight, tSecFwdRight, mPriFwdRight, tPriFwdRightArr, legC.LegIndex, legB.LegIndex, outerLevel, false, "stack_left_C_B", outCourses);
                else
                    AddRamp(dSecFwdRight, tSecFwdRight, mPriRevRight, tPriRevRightArr, legC.LegIndex, legA.LegIndex, outerLevel, false, "stack_left_C_A", outCourses);

                if (aIsRightOfD)
                    AddRamp(dSecRevRight, tSecRevRight, mPriFwdRight, tPriFwdRightArr, legD.LegIndex, legB.LegIndex, outerLevel, false, "stack_left_D_B", outCourses);
                else
                    AddRamp(dSecRevRight, tSecRevRight, mPriRevRight, tPriRevRightArr, legD.LegIndex, legA.LegIndex, outerLevel, false, "stack_left_D_A", outCourses);

                if (cIsRightOfA)
                    AddRamp(dPriFwdFly, tPriFwdFly, mSecRevFly, tSecRevFlyArr, legA.LegIndex, legC.LegIndex, fly1Level, fly1Level > 0, "stack_fly_A_C", outCourses);
                else
                    AddRamp(dPriFwdFly, tPriFwdFly, mSecFwdFly, tSecFwdFlyArr, legA.LegIndex, legD.LegIndex, fly1Level, fly1Level > 0, "stack_fly_A_D", outCourses);

                if (cIsRightOfB)
                    AddRamp(dPriRevFly, tPriRevFly, mSecRevFly, tSecRevFlyArr, legB.LegIndex, legC.LegIndex, fly1Level, fly1Level > 0, "stack_fly_B_C", outCourses);
                else
                    AddRamp(dPriRevFly, tPriRevFly, mSecFwdFly, tSecFwdFlyArr, legB.LegIndex, legD.LegIndex, fly1Level, fly1Level > 0, "stack_fly_B_D", outCourses);

                if (aIsRightOfC)
                    AddRamp(dSecFwdFly, tSecFwdFly, mPriRevFly, tPriRevFlyArr, legC.LegIndex, legA.LegIndex, fly2Level, fly2Level > 0, "stack_fly_C_A", outCourses);
                else
                    AddRamp(dSecFwdFly, tSecFwdFly, mPriFwdFly, tPriFwdFlyArr, legC.LegIndex, legB.LegIndex, fly2Level, fly2Level > 0, "stack_fly_C_B", outCourses);

                if (aIsRightOfD)
                    AddRamp(dSecRevFly, tSecRevFly, mPriRevFly, tPriRevFlyArr, legD.LegIndex, legA.LegIndex, fly2Level, fly2Level > 0, "stack_fly_D_A", outCourses);
                else
                    AddRamp(dSecRevFly, tSecRevFly, mPriFwdFly, tPriFwdFlyArr, legD.LegIndex, legB.LegIndex, fly2Level, fly2Level > 0, "stack_fly_D_B", outCourses);
            }
        }

        private static void SubdivideMainline(
            Bezier4x3 spine,
            int fromLeg, int toLeg,
            int level,
            string idPrefix,
            float t1, float t2, float t3, float t4,
            List<PlannedNetCourse> outCourses,
            out float3 dRight, out float3 tRight,
            out float3 dFly, out float3 tFly,
            out float3 mFly, out float3 tFlyArr,
            out float3 mRight, out float3 tRightArr,
            float sigma)
        {
            // Subdivide into 5 continuous G1 segments: [0..t1], [t1..t2], [t2..t3], [t3..t4], [t4..1]
            var s0 = CubicBezier3D.Subcurve(spine, 0f, t1);
            var s1 = CubicBezier3D.Subcurve(spine, t1, t2);
            var s2 = CubicBezier3D.Subcurve(spine, t2, t3);
            var s3 = CubicBezier3D.Subcurve(spine, t3, t4);
            var s4 = CubicBezier3D.Subcurve(spine, t4, 1f);

            bool isTunnel = level < 0;
            bool isElevated = level > 0;

            // seg0 and seg4 stay on ground at portals; core is at level; seg1/seg3 transition
            outCourses.Add(new PlannedNetCourse { Id = $"{idPrefix}_seg0", Role = NetRole.MainlineThrough, LegIndex = fromLeg, TargetLegIndex = toLeg, Curve = s0, StackLevel = 0, ForceElevated = false, ForceTunnel = false });
            outCourses.Add(new PlannedNetCourse { Id = $"{idPrefix}_seg1", Role = NetRole.MainlineThrough, LegIndex = fromLeg, TargetLegIndex = toLeg, Curve = s1, StackLevel = level, ForceElevated = isElevated, ForceTunnel = isTunnel });
            outCourses.Add(new PlannedNetCourse { Id = $"{idPrefix}_seg2_core", Role = NetRole.MainlineThrough, LegIndex = fromLeg, TargetLegIndex = toLeg, Curve = s2, StackLevel = level, ForceElevated = isElevated, ForceTunnel = isTunnel });
            outCourses.Add(new PlannedNetCourse { Id = $"{idPrefix}_seg3", Role = NetRole.MainlineThrough, LegIndex = fromLeg, TargetLegIndex = toLeg, Curve = s3, StackLevel = level, ForceElevated = isElevated, ForceTunnel = isTunnel });
            outCourses.Add(new PlannedNetCourse { Id = $"{idPrefix}_seg4", Role = NetRole.MainlineThrough, LegIndex = fromLeg, TargetLegIndex = toLeg, Curve = s4, StackLevel = 0, ForceElevated = false, ForceTunnel = false });

            // Exactly matching coordinates and acute gore departure tangents
            dRight = s0.d;
            float3 tan0 = math.normalize(s0.d - s0.c);
            tRight = InterchangeGraphBuilder.RotateGoreTangent(tan0, -sigma * 0.24f); // Right peel off

            dFly = s1.d;
            float3 tan1 = math.normalize(s1.d - s1.c);
            tFly = InterchangeGraphBuilder.RotateGoreTangent(tan1, sigma * 0.22f); // Left flyover peel off

            mFly = s3.a;
            float3 tan2 = math.normalize(s3.b - s3.a);
            tFlyArr = InterchangeGraphBuilder.RotateGoreTangent(tan2, sigma * 0.22f);

            mRight = s4.a;
            float3 tan3 = math.normalize(s4.b - s4.a);
            tRightArr = InterchangeGraphBuilder.RotateGoreTangent(tan3, -sigma * 0.24f);
        }

        private static void AddRamp(
            float3 pStart, float3 tStart,
            float3 pEnd, float3 tEnd,
            int fromLeg, int toLeg,
            int level, bool elevated,
            string id,
            List<PlannedNetCourse> outCourses)
        {
            var curve = BiArcFit.FitSmoothCurve(pStart, tStart, pEnd, tEnd);
            outCourses.Add(new PlannedNetCourse
            {
                Id = id,
                Role = NetRole.DirectionalRamp,
                LegIndex = fromLeg,
                TargetLegIndex = toLeg,
                Curve = curve,
                StackLevel = level,
                ForceElevated = elevated,
                ForceTunnel = level < 0
            });
        }
    }
}
