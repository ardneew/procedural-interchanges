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
        public string Description => "Standard 4-way freeway/arterial diamond interchange using shared topological graph.";
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

            InterchangeGraphBuilder.ResolveStackLevels(
                config,
                out int priLevel,
                out int secLevel,
                out int _,
                out int _,
                out int _);

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

            // 1. Emit host highway approaches & departures with valid attachment entities and strict collinearity
            var portA = LegPortalGeometry.EmitLegApproaches(legA, center, R, 0.12f, sigma, outCourses, "diamond_leg_A");
            var portB = LegPortalGeometry.EmitLegApproaches(legB, center, R, 0.12f, sigma, outCourses, "diamond_leg_B");
            var portC = LegPortalGeometry.EmitLegApproaches(legC, center, R, 0.12f, sigma, outCourses, "diamond_leg_C");
            var portD = LegPortalGeometry.EmitLegApproaches(legD, center, R, 0.12f, sigma, outCourses, "diamond_leg_D");

            var spinePri = InterchangeGraphBuilder.BuildCorridorSpineFromPortals(legA.LegIndex, portA, legB.LegIndex, portB);
            var spineSec = InterchangeGraphBuilder.BuildCorridorSpineFromPortals(legC.LegIndex, portC, legD.LegIndex, portD);

            // Freeway on-spine parameters: diverge t1=0.20, merge t2=0.80
            const float tDiv = 0.20f;
            const float tMerge = 0.80f;

            // Cross-street intersection nodes: tJunc1=0.22, tJunc2=0.78
            const float tJunc1 = 0.22f;
            const float tJunc2 = 0.78f;

            // Subdivide Primary Forward (A -> B)
            SubdivideFreeway(spinePri.ForwardSpine, legA.LegIndex, legB.LegIndex, priLevel, "diamond_pri_fwd", tDiv, tMerge, outCourses,
                out float3 dPriFwd, out float3 tPriFwd, out float3 mPriFwd, out float3 tPriFwdArr, sigma);

            // Subdivide Primary Reverse (B -> A)
            SubdivideFreeway(spinePri.ReverseSpine, legB.LegIndex, legA.LegIndex, priLevel, "diamond_pri_rev", tDiv, tMerge, outCourses,
                out float3 dPriRev, out float3 tPriRev, out float3 mPriRev, out float3 tPriRevArr, sigma);

            // Subdivide Secondary Cross-Street Forward (C -> D)
            SubdivideCrossStreet(spineSec.ForwardSpine, legC.LegIndex, legD.LegIndex, secLevel, "diamond_sec_fwd", tJunc1, tJunc2, outCourses,
                out float3 juncC1, out float3 tJuncC1Arr, out float3 tJuncC1Dep,
                out float3 juncC2, out float3 tJuncC2Arr, out float3 tJuncC2Dep);

            // Subdivide Secondary Cross-Street Reverse (D -> C)
            SubdivideCrossStreet(spineSec.ReverseSpine, legD.LegIndex, legC.LegIndex, secLevel, "diamond_sec_rev", tJunc1, tJunc2, outCourses,
                out float3 juncD1, out float3 tJuncD1Arr, out float3 tJuncD1Dep,
                out float3 juncD2, out float3 tJuncD2Arr, out float3 tJuncD2Dep);

            // Connect 4 Diagonal Service Ramps with orientation-invariant cross-product matching
            bool cIsRightOfA = (portA.InboundDir.x * (portC.MergeNode.z - portA.DivergeNode.z) - portA.InboundDir.z * (portC.MergeNode.x - portA.DivergeNode.x)) * sigma < 0f;
            if (cIsRightOfA)
            {
                AddRamp(dPriFwd, tPriFwd, juncD2, tJuncD2Dep, legA.LegIndex, legC.LegIndex, 0, false, "diamond_ramp_A_C", outCourses);
                AddRamp(juncC1, tJuncC1Dep, mPriFwd, tPriFwdArr, legC.LegIndex, legB.LegIndex, 0, false, "diamond_ramp_C_B", outCourses);
                AddRamp(dPriRev, tPriRev, juncC2, tJuncC2Dep, legB.LegIndex, legD.LegIndex, 0, false, "diamond_ramp_B_D", outCourses);
                AddRamp(juncD1, tJuncD1Dep, mPriRev, tPriRevArr, legD.LegIndex, legA.LegIndex, 0, false, "diamond_ramp_D_A", outCourses);
            }
            else
            {
                AddRamp(dPriFwd, tPriFwd, juncC2, tJuncC2Dep, legA.LegIndex, legD.LegIndex, 0, false, "diamond_ramp_A_D", outCourses);
                AddRamp(juncD1, tJuncD1Dep, mPriFwd, tPriFwdArr, legD.LegIndex, legB.LegIndex, 0, false, "diamond_ramp_D_B", outCourses);
                AddRamp(dPriRev, tPriRev, juncD2, tJuncD2Dep, legB.LegIndex, legC.LegIndex, 0, false, "diamond_ramp_B_C", outCourses);
                AddRamp(juncC1, tJuncC1Dep, mPriRev, tPriRevArr, legC.LegIndex, legA.LegIndex, 0, false, "diamond_ramp_C_A", outCourses);
            }
        }

        private static void SubdivideFreeway(
            Bezier4x3 spine,
            int fromLeg, int toLeg,
            int level,
            string idPrefix,
            float tDiv, float tMerge,
            List<PlannedNetCourse> outCourses,
            out float3 dNode, out float3 tDivRamp,
            out float3 mNode, out float3 tMergeRampArr,
            float sigma)
        {
            var s0 = CubicBezier3D.Subcurve(spine, 0f, tDiv);
            var s1 = CubicBezier3D.Subcurve(spine, tDiv, tMerge);
            var s2 = CubicBezier3D.Subcurve(spine, tMerge, 1f);

            bool isTunnel = level < 0;
            bool isElevated = level > 0;

            outCourses.Add(new PlannedNetCourse { Id = $"{idPrefix}_app", Role = NetRole.MainlineThrough, LegIndex = fromLeg, TargetLegIndex = toLeg, Curve = s0, StackLevel = 0, ForceElevated = false, ForceTunnel = false });
            outCourses.Add(new PlannedNetCourse { Id = $"{idPrefix}_core", Role = NetRole.MainlineThrough, LegIndex = fromLeg, TargetLegIndex = toLeg, Curve = s1, StackLevel = level, ForceElevated = isElevated, ForceTunnel = isTunnel });
            outCourses.Add(new PlannedNetCourse { Id = $"{idPrefix}_dep", Role = NetRole.MainlineThrough, LegIndex = fromLeg, TargetLegIndex = toLeg, Curve = s2, StackLevel = 0, ForceElevated = false, ForceTunnel = false });

            dNode = s0.d;
            float3 tan0 = math.normalize(s0.d - s0.c);
            tDivRamp = InterchangeGraphBuilder.RotateGoreTangent(tan0, -sigma * 0.22f);

            mNode = s2.a;
            float3 tan1 = math.normalize(s2.b - s2.a);
            tMergeRampArr = InterchangeGraphBuilder.RotateGoreTangent(tan1, sigma * 0.22f);
        }

        private static void SubdivideCrossStreet(
            Bezier4x3 spine,
            int fromLeg, int toLeg,
            int level,
            string idPrefix,
            float t1, float t2,
            List<PlannedNetCourse> outCourses,
            out float3 junc1, out float3 tJunc1Arr, out float3 tJunc1Dep,
            out float3 junc2, out float3 tJunc2Arr, out float3 tJunc2Dep)
        {
            var s0 = CubicBezier3D.Subcurve(spine, 0f, t1);
            var s1 = CubicBezier3D.Subcurve(spine, t1, t2);
            var s2 = CubicBezier3D.Subcurve(spine, t2, 1f);

            bool isTunnel = level < 0;
            bool isElevated = level > 0;

            outCourses.Add(new PlannedNetCourse { Id = $"{idPrefix}_seg0", Role = NetRole.CrossStreet, LegIndex = fromLeg, TargetLegIndex = toLeg, Curve = s0, StackLevel = 0, ForceElevated = false, ForceTunnel = false });
            outCourses.Add(new PlannedNetCourse { Id = $"{idPrefix}_bridge", Role = NetRole.CrossStreet, LegIndex = fromLeg, TargetLegIndex = toLeg, Curve = s1, StackLevel = level, ForceElevated = isElevated, ForceTunnel = isTunnel });
            outCourses.Add(new PlannedNetCourse { Id = $"{idPrefix}_seg2", Role = NetRole.CrossStreet, LegIndex = fromLeg, TargetLegIndex = toLeg, Curve = s2, StackLevel = 0, ForceElevated = false, ForceTunnel = false });

            junc1 = s0.d;
            tJunc1Arr = math.normalize(s0.d - s0.c);
            tJunc1Dep = math.normalize(s1.b - s1.a);

            junc2 = s1.d;
            tJunc2Arr = math.normalize(s1.d - s1.c);
            tJunc2Dep = math.normalize(s2.b - s2.a);
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
                Role = NetRole.SlipRamp,
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
