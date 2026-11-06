using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using ProceduralInterchanges.Core.Generators;
using ProceduralInterchanges.Core.Models;
using Unity.Mathematics;

namespace ProceduralInterchanges.Core.Math
{
    public struct CorridorLegPair
    {
        public int LegA;
        public int LegB;
        public float CollinearityScore;
    }

    public struct CorridorSpine
    {
        public int LegA;
        public int LegB;
        // Directional spine from LegA inbound to LegB outbound
        public Bezier4x3 ForwardSpine;
        // Directional spine from LegB inbound to LegA outbound
        public Bezier4x3 ReverseSpine;
        public float LengthForward;
        public float LengthReverse;
    }

    public static class InterchangeGraphBuilder
    {
        /// <summary>
        /// Identifies the most collinear opposing leg pairs.
        /// Returns the primary pair index and secondary pair index based on config.PrimaryAxisOffset.
        /// </summary>
        public static void IdentifyCorridorPairs(
            IReadOnlyList<HighwayLeg> legs,
            InterchangeConfig config,
            out CorridorLegPair primaryPair,
            out CorridorLegPair secondaryPair,
            out int stemLegIndex)
        {
            int n = legs != null ? legs.Count : 0;
            primaryPair = new CorridorLegPair { LegA = 0, LegB = (n > 1 ? 1 : 0), CollinearityScore = 0f };
            secondaryPair = new CorridorLegPair { LegA = -1, LegB = -1, CollinearityScore = 0f };
            stemLegIndex = -1;

            if (n == 3)
            {
                // Evaluate 3 candidate pairs: (0,1), (1,2), (2,0)
                var pairs = new List<CorridorLegPair>(3);
                for (int i = 0; i < 3; i++)
                {
                    int j = (i + 1) % 3;
                    float3 tA = math.normalize(new float3(legs[i].Inbound != null ? legs[i].Inbound.InwardTangent.xz : -legs[i].LegAxisDirection.xz, 0f)).xzy;
                    float3 tB = math.normalize(new float3(legs[j].Inbound != null ? legs[j].Inbound.InwardTangent.xz : -legs[j].LegAxisDirection.xz, 0f)).xzy;
                    // Score = -dot(tA, tB) -> perfectly opposite = +1.0
                    float dotVal = math.dot(tA.xz, tB.xz);
                    float score = -dotVal;
                    pairs.Add(new CorridorLegPair { LegA = i, LegB = j, CollinearityScore = score });
                }

                pairs.Sort((a, b) => b.CollinearityScore.CompareTo(a.CollinearityScore));
                int selectedIdx = (config != null ? math.abs(config.PrimaryAxisOffset) : 0) % 3;
                primaryPair = pairs[selectedIdx];

                for (int i = 0; i < 3; i++)
                {
                    if (i != primaryPair.LegA && i != primaryPair.LegB)
                    {
                        stemLegIndex = i;
                        break;
                    }
                }
            }
            else if (n == 4)
            {
                // In CCW sorted order 0, 1, 2, 3, crossing pairs are (0, 2) and (1, 3)
                float3 t0 = math.normalize(new float3(legs[0].Inbound != null ? legs[0].Inbound.InwardTangent.xz : -legs[0].LegAxisDirection.xz, 0f)).xzy;
                float3 t2 = math.normalize(new float3(legs[2].Inbound != null ? legs[2].Inbound.InwardTangent.xz : -legs[2].LegAxisDirection.xz, 0f)).xzy;
                float score02 = -math.dot(t0.xz, t2.xz);

                float3 t1 = math.normalize(new float3(legs[1].Inbound != null ? legs[1].Inbound.InwardTangent.xz : -legs[1].LegAxisDirection.xz, 0f)).xzy;
                float3 t3 = math.normalize(new float3(legs[3].Inbound != null ? legs[3].Inbound.InwardTangent.xz : -legs[3].LegAxisDirection.xz, 0f)).xzy;
                float score13 = -math.dot(t1.xz, t3.xz);

                var pair02 = new CorridorLegPair { LegA = 0, LegB = 2, CollinearityScore = score02 };
                var pair13 = new CorridorLegPair { LegA = 1, LegB = 3, CollinearityScore = score13 };

                bool pair02IsFirst = score02 >= score13;
                if ((config != null && (config.PrimaryAxisOffset % 2 != 0)))
                {
                    pair02IsFirst = !pair02IsFirst;
                }

                primaryPair = pair02IsFirst ? pair02 : pair13;
                secondaryPair = pair02IsFirst ? pair13 : pair02;
            }
        }

        /// <summary>
        /// Constructs a smooth continuous corridor spine connecting the LegPortals of legA and legB.
        /// Guaranteed G1 tangency with the approach and departure portal leads.
        /// </summary>
        public static CorridorSpine BuildCorridorSpineFromPortals(
            int legAIdx,
            LegPortals portA,
            int legBIdx,
            LegPortals portB)
        {
            float3 pInA = portA.DivergeNode;
            float3 tInA = portA.InboundDir;
            float3 pOutB = portB.MergeNode;
            float3 tOutB = -portB.OutboundDir;

            float3 pInB = portB.DivergeNode;
            float3 tInB = portB.InboundDir;
            float3 pOutA = portA.MergeNode;
            float3 tOutA = -portA.OutboundDir;

            Bezier4x3 fwd = FitSpineBezier(pInA, tInA, pOutB, tOutB);
            Bezier4x3 rev = FitSpineBezier(pInB, tInB, pOutA, tOutA);

            return new CorridorSpine
            {
                LegA = legAIdx,
                LegB = legBIdx,
                ForwardSpine = fwd,
                ReverseSpine = rev,
                LengthForward = CubicBezier3D.Length(fwd),
                LengthReverse = CubicBezier3D.Length(rev)
            };
        }

        /// <summary>
        /// Constructs a smooth continuous corridor spine between legA and legB matching host highway portal tangents.
        /// </summary>
        public static CorridorSpine BuildCorridorSpine(
            HighwayLeg legA,
            HighwayLeg legB,
            float3 center,
            float footprintRadius)
        {
            // Portals and tangents for A -> B
            float3 pInA = GetLegInboundPos(legA, center, footprintRadius, out float3 tInA);
            float3 pOutB = GetLegOutboundPos(legB, center, footprintRadius, out float3 tOutB);

            // Portals and tangents for B -> A
            float3 pInB = GetLegInboundPos(legB, center, footprintRadius, out float3 tInB);
            float3 pOutA = GetLegOutboundPos(legA, center, footprintRadius, out float3 tOutA);

            Bezier4x3 fwd = FitSpineBezier(pInA, tInA, pOutB, tOutB);
            Bezier4x3 rev = FitSpineBezier(pInB, tInB, pOutA, tOutA);

            return new CorridorSpine
            {
                LegA = legA.LegIndex,
                LegB = legB.LegIndex,
                ForwardSpine = fwd,
                ReverseSpine = rev,
                LengthForward = CubicBezier3D.Length(fwd),
                LengthReverse = CubicBezier3D.Length(rev)
            };
        }

        private static Bezier4x3 FitSpineBezier(float3 pStart, float3 tStart, float3 pEnd, float3 tEnd)
        {
            float chord = math.distance(pStart.xz, pEnd.xz);
            float h = math.clamp(chord * 0.333f, 20f, 150f);

            float3 b = pStart + tStart * h;
            float3 c = pEnd - tEnd * h;
            b.y = math.lerp(pStart.y, pEnd.y, 0.3333333f);
            c.y = math.lerp(pStart.y, pEnd.y, 0.6666667f);

            return new Bezier4x3(pStart, b, c, pEnd);
        }

        public static float3 GetLegInboundPos(HighwayLeg leg, float3 center, float R, out float3 inwardTan)
        {
            if (leg.Inbound != null)
            {
                float3 tan = leg.Inbound.FlowDirection;
                if (math.lengthsq(tan.xz) < 1e-4f) tan = leg.PortalInwardTangent;
                inwardTan = math.normalize(new float3(tan.x, 0f, tan.z));
                return leg.Inbound.PortalPosition;
            }
            float3 outDir = leg.LegAxisDirection;
            inwardTan = -math.normalize(new float3(outDir.x, 0f, outDir.z));
            return center + outDir * R;
        }

        public static float3 GetLegOutboundPos(HighwayLeg leg, float3 center, float R, out float3 outwardTan)
        {
            if (leg.Outbound != null)
            {
                float3 tan = leg.Outbound.FlowDirection;
                if (math.lengthsq(tan.xz) < 1e-4f) tan = -leg.PortalInwardTangent;
                outwardTan = math.normalize(new float3(tan.x, 0f, tan.z));
                return leg.Outbound.PortalPosition;
            }
            if (leg.Inbound != null)
            {
                float3 tan = leg.Inbound.FlowDirection;
                if (math.lengthsq(tan.xz) < 1e-4f) tan = leg.PortalInwardTangent;
                outwardTan = -math.normalize(new float3(tan.x, 0f, tan.z));
                return leg.Inbound.PortalPosition;
            }
            float3 outDir = leg.LegAxisDirection;
            outwardTan = math.normalize(new float3(outDir.x, 0f, outDir.z));
            return center + outDir * R;
        }

        /// <summary>
        /// Rotates a tangent vector in the horizontal XZ plane by an acute angle (positive = CCW / left, negative = CW / right).
        /// </summary>
        public static float3 RotateGoreTangent(float3 dir, float angleRad)
        {
            float c = math.cos(angleRad);
            float s = math.sin(angleRad);
            return new float3(dir.x * c - dir.z * s, dir.y, dir.x * s + dir.z * c);
        }

        /// <summary>
        /// Resolves stack levels according to user-selected UndergroundMode and MainlineOverpass settings.
        /// </summary>
        public static void ResolveStackLevels(
            InterchangeConfig config,
            out int primaryMainlineLevel,
            out int secondaryMainlineLevel,
            out int outerRampsLevel,
            out int flyoverTier1Level,
            out int flyoverTier2Level)
        {
            var mode = config != null ? config.Underground : UndergroundMode.None;
            bool overpass = config != null && config.MainlineOverpass;

            switch (mode)
            {
                case UndergroundMode.DepressSecondaryThruway:
                    primaryMainlineLevel = 0;
                    secondaryMainlineLevel = -1; // -10m Tunnel
                    outerRampsLevel = 0;
                    flyoverTier1Level = 1;
                    flyoverTier2Level = 2;
                    break;

                case UndergroundMode.ShiftStackDown:
                case UndergroundMode.BothThruwaysUnderground:
                    primaryMainlineLevel = -1;
                    secondaryMainlineLevel = 0;
                    outerRampsLevel = 0;
                    flyoverTier1Level = 1;
                    flyoverTier2Level = 2;
                    break;

                case UndergroundMode.None:
                default:
                    primaryMainlineLevel = overpass ? 1 : 0;
                    secondaryMainlineLevel = overpass ? 0 : 1;
                    outerRampsLevel = 0;
                    flyoverTier1Level = 2;
                    flyoverTier2Level = 3;
                    break;
            }
        }
    }
}
