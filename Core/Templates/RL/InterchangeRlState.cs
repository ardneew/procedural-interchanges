using System;
using System.Collections.Generic;
using ProceduralInterchanges.Core.Models;
using Unity.Mathematics;

namespace ProceduralInterchanges.Core.Templates.RL
{
    public sealed class RewardWeightVector
    {
        // 8 loss component weights
        public float WClearance = 25.0f;       // L1: 3D vertical clearance violations (<7.3m)
        public float WCurvature = 30.0f;       // L2: Sharp curvature / loop pinching (R < 18m)
        public float WGrade = 20.0f;           // L3: Maximum road grade (>20%)
        public float WKink = 20.0f;            // L4: Portal boundary tangent kink
        public float WOvershoot = 35.0f;       // L5: Boundary plane overshoot
        public float WDistortion = 30.0f;      // L6: Conformal loop distortion / non-circularity
        public float WInflection = 12.0f;      // L7: Unwanted S-bend inflection points
        public float WLength = 2.5f;           // L8: Excess network wireframe length

        public float[] ToArray()
        {
            return new float[8] { WClearance, WCurvature, WGrade, WKink, WOvershoot, WDistortion, WInflection, WLength };
        }

        public void FromArray(float[] arr)
        {
            if (arr == null || arr.Length < 8) return;
            WClearance = math.clamp(arr[0], 2f, 120f);
            WCurvature = math.clamp(arr[1], 2f, 120f);
            WGrade = math.clamp(arr[2], 2f, 120f);
            WKink = math.clamp(arr[3], 2f, 120f);
            WOvershoot = math.clamp(arr[4], 2f, 120f);
            WDistortion = math.clamp(arr[5], 2f, 120f);
            WInflection = math.clamp(arr[6], 2f, 120f);
            WLength = math.clamp(arr[7], 1f, 50f);
        }
    }

    public static class InterchangeRlStateExtractor
    {
        public const int kStateDim = 28;
        public const int kActionDim = 32;

        public static float[] ExtractState(
            WorkshopTemplateDef template,
            InterchangeConfig config,
            IReadOnlyList<HighwayLeg> legs,
            int bestS,
            Func<float2, float> sampleTerrainHeight)
        {
            float[] state = new float[kStateDim];
            if (template == null || config == null || legs == null || legs.Count == 0)
            {
                return state;
            }

            int n = legs.Count;
            float rCanon = math.max(10f, template.CanonicalRadius);
            float3 center = config.Center;

            // 1. Sector features for up to 4 legs (m = 0..3)
            for (int m = 0; m < 4; m++)
            {
                if (m >= template.RequiredLegs || m >= n)
                {
                    state[m] = 0f;
                    state[4 + m] = 0f;
                    state[8 + m] = -1f;
                    state[12 + m] = 0f;
                    state[16 + m] = 0f;
                    continue;
                }

                int userIdx = (m + bestS) % n;
                int nextM = (m + 1) % template.RequiredLegs;
                int nextUserIdx = (nextM + bestS) % n;

                var curLeg = legs[userIdx];
                var nextLeg = legs[nextUserIdx];

                // Target sector span
                float targetSpan = math.atan2(
                    math.sin(nextLeg.AzimuthRad - curLeg.AzimuthRad),
                    math.cos(nextLeg.AzimuthRad - curLeg.AzimuthRad));
                if (targetSpan < 0f) targetSpan += 2f * math.PI;

                // Canonical sector span
                float canonA0 = template.CanonicalLegAzimuthsRad[m % template.CanonicalLegAzimuthsRad.Length];
                float canonA1 = template.CanonicalLegAzimuthsRad[nextM % template.CanonicalLegAzimuthsRad.Length];
                float canonSpan = math.atan2(math.sin(canonA1 - canonA0), math.cos(canonA1 - canonA0));
                if (canonSpan < 0f) canonSpan += 2f * math.PI;

                // 0..3: Sector Angle Skew
                state[m] = math.clamp((targetSpan - canonSpan) / (math.PI * 0.5f), -1f, 1f);

                // 4..7: Normalized Portal Distance
                float dPortal = math.distance(curLeg.PortalCenterPosition.xz, center.xz);
                state[4 + m] = math.clamp((dPortal - rCanon) / rCanon, -1f, 1f);

                // 8..11: Normalized Carriageway Separation
                state[8 + m] = curLeg.IsSeparatedDualCarriageway
                    ? math.clamp((curLeg.CarriagewaySeparation - 28.0f) / 28.0f, -1f, 1f)
                    : -1.0f;

                // 12..15: Inbound Approach Tangent Skew
                float2 rIn = -math.normalize(curLeg.LegAxisDirection.xz);
                float2 tIn = (curLeg.Inbound != null && math.lengthsq(curLeg.Inbound.InwardTangent.xz) > 1e-4f)
                    ? math.normalize(curLeg.Inbound.InwardTangent.xz)
                    : rIn;
                float crossIn = rIn.x * tIn.y - rIn.y * tIn.x;
                state[12 + m] = math.clamp(crossIn / 0.50f, -1f, 1f);

                // 16..19: Outbound Departure Tangent Skew
                float2 rOut = math.normalize(curLeg.LegAxisDirection.xz);
                float2 tOut = (curLeg.Outbound != null && math.lengthsq(curLeg.Outbound.InwardTangent.xz) > 1e-4f)
                    ? math.normalize(-curLeg.Outbound.InwardTangent.xz)
                    : rOut;
                float crossOut = rOut.x * tOut.y - rOut.y * tOut.x;
                state[16 + m] = math.clamp(crossOut / 0.50f, -1f, 1f);
            }

            // 20: Footprint radius
            state[20] = math.clamp((config.FootprintRadius - 250.0f) / 200.0f, -1f, 1f);

            // 21: LeftHandTraffic
            state[21] = config.LeftHandTraffic ? -1.0f : 1.0f;

            // 22: Underground Mode
            state[22] = (float)config.Underground / 3.0f;

            // 23: Bridge clearance
            state[23] = math.clamp((config.MinBridgeClearanceMeters - 9.0f) / 6.0f, -1f, 1f);

            // 24..26: Topography
            if (sampleTerrainHeight != null)
            {
                float2 cXZ = center.xz;
                float hC = sampleTerrainHeight(cXZ);
                float hE = sampleTerrainHeight(cXZ + new float2(100f, 0f));
                float hW = sampleTerrainHeight(cXZ - new float2(100f, 0f));
                float hN = sampleTerrainHeight(cXZ + new float2(0f, 100f));
                float hS = sampleTerrainHeight(cXZ - new float2(0f, 100f));

                state[24] = math.clamp((hE - hW) / 20.0f, -1f, 1f);
                state[25] = math.clamp((hN - hS) / 20.0f, -1f, 1f);
                state[26] = math.clamp((hE + hW + hN + hS - 4f * hC) / 20.0f, -1f, 1f);
            }
            else
            {
                state[24] = 0f;
                state[25] = 0f;
                state[26] = 0f;
            }

            // 27: Max Longitudinal Stagger
            float maxStagger = 0f;
            for (int i = 0; i < n; i++)
            {
                if (legs[i].LongitudinalStaggerMeters > maxStagger)
                {
                    maxStagger = legs[i].LongitudinalStaggerMeters;
                }
            }
            state[27] = math.clamp(maxStagger / 40.0f, 0f, 1f);

            return state;
        }
    }
}
