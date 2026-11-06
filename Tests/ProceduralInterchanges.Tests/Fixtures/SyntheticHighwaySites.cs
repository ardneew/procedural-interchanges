using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using ProceduralInterchanges.Core.Models;
using Unity.Entities;
using Unity.Mathematics;

namespace ProceduralInterchanges.Tests.Fixtures
{
    public static class SyntheticHighwaySites
    {
        public static List<HighwayLeg> CreateOrthogonal4Way(
            float radius = 180f,
            float separation = 24f,
            int lanes = 3,
            float elevation = 0f,
            float3 center = default,
            bool leftHandTraffic = false)
        {
            var legs = new List<HighwayLeg>();
            float[] angles = { 0f, math.PI * 0.5f, math.PI, -math.PI * 0.5f };

            for (int i = 0; i < 4; i++)
            {
                legs.Add(CreateLeg(i, angles[i], radius, separation, lanes, elevation, center, leftHandTraffic));
            }

            return legs;
        }

        public static List<HighwayLeg> CreateSkewed4Way(
            float skewAngleDeg,
            float radius = 180f,
            float separation = 24f,
            int lanes = 3,
            float elevation = 0f,
            float3 center = default,
            bool leftHandTraffic = false,
            Func<float2, float> terrainSampler = null)
        {
            float skewRad = math.radians(skewAngleDeg);
            var legs = new List<HighwayLeg>
            {
                CreateLeg(0, 0f, radius, separation, lanes, elevation, center, leftHandTraffic, terrainSampler),
                CreateLeg(1, skewRad, radius, separation, lanes, elevation, center, leftHandTraffic, terrainSampler),
                CreateLeg(2, math.PI, radius, separation, lanes, elevation, center, leftHandTraffic, terrainSampler),
                CreateLeg(3, skewRad - math.PI, radius, separation, lanes, elevation, center, leftHandTraffic, terrainSampler)
            };

            legs.Sort((a, b) => a.AzimuthRad.CompareTo(b.AzimuthRad));
            for (int i = 0; i < legs.Count; i++) legs[i].LegIndex = i;

            return legs;
        }

        public static List<HighwayLeg> Create3WayJunction(
            float angleDeg = 90f,
            float radius = 180f,
            float separation = 24f,
            int lanes = 3,
            float elevation = 0f,
            float3 center = default,
            bool leftHandTraffic = false,
            Func<float2, float> terrainSampler = null)
        {
            float stemAngle = -math.radians(angleDeg);
            var legs = new List<HighwayLeg>
            {
                CreateLeg(0, 0f, radius, separation, lanes, elevation, center, leftHandTraffic, terrainSampler),
                CreateLeg(1, stemAngle, radius, separation, lanes, elevation, center, leftHandTraffic, terrainSampler),
                CreateLeg(2, math.PI, radius, separation, lanes, elevation, center, leftHandTraffic, terrainSampler)
            };

            legs.Sort((a, b) => a.AzimuthRad.CompareTo(b.AzimuthRad));
            for (int i = 0; i < legs.Count; i++) legs[i].LegIndex = i;

            return legs;
        }

        public static List<HighwayLeg> CreateConstrained3WayJunction(
            float radius = 120f,
            float angleDeg = 90f,
            float separation = 20f,
            int lanes = 3,
            float elevation = 0f,
            float3 center = default,
            bool leftHandTraffic = false)
        {
            return Create3WayJunction(angleDeg, radius, separation, lanes, elevation, center, leftHandTraffic);
        }

        public static List<HighwayLeg> CreateSingleTwoWay3WayJunction(
            float radius = 130f,
            float angleDeg = 90f,
            float elevation = 0f,
            float3 center = default)
        {
            float stemAngle = -math.radians(angleDeg);
            var legs = new List<HighwayLeg>
            {
                CreateSingleTwoWayLeg(0, 0f, radius, elevation, center),
                CreateSingleTwoWayLeg(1, stemAngle, radius, elevation, center),
                CreateSingleTwoWayLeg(2, math.PI, radius, elevation, center)
            };

            legs.Sort((a, b) => a.AzimuthRad.CompareTo(b.AzimuthRad));
            for (int i = 0; i < legs.Count; i++) legs[i].LegIndex = i;

            return legs;
        }

        public static HighwayLeg CreateSingleTwoWayLeg(
            int index,
            float angleRad,
            float radius,
            float elevation,
            float3 center)
        {
            float3 outward = new float3(math.cos(angleRad), 0f, math.sin(angleRad));
            float3 inward = -outward;
            float3 portalCenter = center + outward * radius + new float3(0f, elevation, 0f);

            var inEndpoint = new CarriagewayEndpoint
            {
                SourceEdge = new Entity { Index = 1000 + index, Version = 1 },
                SourcePrefab = new Entity { Index = 60, Version = 1 },
                PrefabName = "Two-Lane Two-Way Highway",
                OriginalCurve = new Bezier4x3(
                    portalCenter - inward * 100f,
                    portalCenter - inward * 50f,
                    portalCenter - inward * 10f,
                    portalCenter),
                CutCurveT = 1.0f,
                PortalPosition = portalCenter,
                FlowDirection = inward,
                InwardTangent = inward,
                Direction = CarriagewayDirection.TwoWay,
                IsInboundToCenter = true,
                IsOneWay = false,
                LaneCount = 2,
                Width = 14f,
                SpeedLimitMs = 27.8f
            };

            return new HighwayLeg
            {
                LegIndex = index,
                AzimuthRad = angleRad,
                LegAxisDirection = outward,
                PortalCenterPosition = portalCenter,
                PortalInwardTangent = inward,
                IsSeparatedDualCarriageway = false,
                CarriagewaySeparation = 0f,
                Inbound = inEndpoint,
                Outbound = null,
                OverrideMainlineLanes = 2,
                OverrideMainlinePrefabName = inEndpoint.PrefabName
            };
        }

        public static List<HighwayLeg> CreateAsymmetricRadii3Way(
            float rA = 140f,
            float rB = 180f,
            float rStem = 110f,
            float stemAngleDeg = 105f,
            float separation = 20f,
            int lanes = 3,
            float elevation = 0f,
            float3 center = default,
            bool leftHandTraffic = false)
        {
            float stemAngle = math.radians(stemAngleDeg);
            var legs = new List<HighwayLeg>
            {
                CreateLeg(0, 0f, rA, separation, lanes, elevation, center, leftHandTraffic),
                CreateLeg(1, stemAngle, rStem, separation, lanes, elevation, center, leftHandTraffic),
                CreateLeg(2, math.PI, rB, separation, lanes, elevation, center, leftHandTraffic)
            };

            legs.Sort((a, b) => a.AzimuthRad.CompareTo(b.AzimuthRad));
            for (int i = 0; i < legs.Count; i++) legs[i].LegIndex = i;

            return legs;
        }

        public static List<HighwayLeg> CreateAsymmetricFreewayArterial(
            float radius = 180f,
            int freewayLanes = 4,
            int arterialLanes = 2,
            float separation = 24f,
            float elevation = 0f,
            float3 center = default,
            bool leftHandTraffic = false)
        {
            var legs = new List<HighwayLeg>
            {
                CreateLeg(0, 0f, radius, separation, freewayLanes, elevation, center, leftHandTraffic),
                CreateLeg(1, math.PI * 0.5f, radius, separation * 0.75f, arterialLanes, elevation, center, leftHandTraffic),
                CreateLeg(2, math.PI, radius, separation, freewayLanes, elevation, center, leftHandTraffic),
                CreateLeg(3, -math.PI * 0.5f, radius, separation * 0.75f, arterialLanes, elevation, center, leftHandTraffic)
            };

            return legs;
        }

        public static HighwayLeg CreateLeg(
            int index,
            float angleRad,
            float radius,
            float separation,
            int lanes,
            float elevation,
            float3 center,
            bool leftHandTraffic = false,
            Func<float2, float> terrainSampler = null)
        {
            float3 outward = new float3(math.cos(angleRad), 0f, math.sin(angleRad));
            float3 inward = -outward;
            float3 rightOfInward = new float3(inward.z, 0f, -inward.x);

            float yBase = terrainSampler != null ? terrainSampler((center + outward * radius).xz) : elevation;
            float3 portalCenter = center + outward * radius + new float3(0f, yBase, 0f);
            float halfSep = separation * 0.5f;
            float sigma = leftHandTraffic ? -1f : 1f;

            float3 pIn = portalCenter + rightOfInward * (halfSep * sigma);
            float3 pOut = portalCenter - rightOfInward * (halfSep * sigma);

            if (terrainSampler != null)
            {
                pIn.y = terrainSampler(pIn.xz);
                pOut.y = terrainSampler(pOut.xz);
            }

            float roadWidth = lanes * 3.5f + 3f;

            var inEndpoint = new CarriagewayEndpoint
            {
                SourceEdge = new Entity { Index = 1000 + index * 2, Version = 1 },
                SourcePrefab = new Entity { Index = 50 + lanes, Version = 1 },
                PrefabName = $"Highway {lanes}-Lane One-Way",
                OriginalCurve = new Bezier4x3(
                    pIn - inward * 100f,
                    pIn - inward * 50f,
                    pIn - inward * 10f,
                    pIn),
                CutCurveT = 1.0f,
                PortalPosition = pIn,
                FlowDirection = inward,
                InwardTangent = inward,
                Direction = CarriagewayDirection.Inbound,
                IsInboundToCenter = true,
                IsOneWay = true,
                LaneCount = lanes,
                Width = roadWidth,
                SpeedLimitMs = 27.8f
            };

            var outEndpoint = new CarriagewayEndpoint
            {
                SourceEdge = new Entity { Index = 1001 + index * 2, Version = 1 },
                SourcePrefab = new Entity { Index = 50 + lanes, Version = 1 },
                PrefabName = $"Highway {lanes}-Lane One-Way",
                OriginalCurve = new Bezier4x3(
                    pOut,
                    pOut + outward * 10f,
                    pOut + outward * 50f,
                    pOut + outward * 100f),
                CutCurveT = 0.0f,
                PortalPosition = pOut,
                FlowDirection = outward,
                InwardTangent = inward,
                Direction = CarriagewayDirection.Outbound,
                IsInboundToCenter = false,
                IsOneWay = true,
                LaneCount = lanes,
                Width = roadWidth,
                SpeedLimitMs = 27.8f
            };

            return new HighwayLeg
            {
                LegIndex = index,
                AzimuthRad = angleRad,
                LegAxisDirection = outward,
                PortalCenterPosition = portalCenter,
                PortalInwardTangent = inward,
                IsSeparatedDualCarriageway = true,
                CarriagewaySeparation = separation,
                Inbound = inEndpoint,
                Outbound = outEndpoint,
                OverrideMainlineLanes = lanes,
                OverrideMainlinePrefabName = inEndpoint.PrefabName
            };
        }

        public static List<HighwayLeg> ConvertToLeftHandTraffic(IReadOnlyList<HighwayLeg> legs)
        {
            var lhtLegs = new List<HighwayLeg>();
            foreach (var leg in legs)
            {
                lhtLegs.Add(CreateLeg(
                    leg.LegIndex,
                    leg.AzimuthRad,
                    math.length(leg.PortalCenterPosition.xz),
                    leg.CarriagewaySeparation,
                    leg.OverrideMainlineLanes > 0 ? leg.OverrideMainlineLanes : 3,
                    leg.PortalCenterPosition.y,
                    default,
                    leftHandTraffic: true));
            }
            return lhtLegs;
        }

        public static class TerrainSamplers
        {
            public static Func<float2, float> Flat => _ => 0f;

            public static Func<float2, float> UniformSlope(float gradePercent, float2 direction)
            {
                float2 d = math.normalize(direction);
                float slope = gradePercent / 100f;
                return p => math.dot(p, d) * slope;
            }

            public static Func<float2, float> GaussianHill(float peakHeight, float2 center, float sigma)
            {
                float inv2SigmaSq = 1f / (2f * sigma * sigma);
                return p => peakHeight * math.exp(-math.lengthsq(p - center) * inv2SigmaSq);
            }

            public static Func<float2, float> ValleyCross(float depth, float width)
            {
                float halfW = width * 0.5f;
                return p =>
                {
                    float dist = math.abs(p.y);
                    if (dist > halfW) return 0f;
                    float u = dist / halfW;
                    return -depth * (1f - u * u);
                };
            }

            public static Func<float2, float> TerraceStep(float stepHeight, float stepX)
            {
                return p =>
                {
                    float dx = p.x - stepX;
                    float t = math.clamp((dx + 10f) / 20f, 0f, 1f);
                    return math.smoothstep(0f, 1f, t) * stepHeight;
                };
            }

            public static Func<float2, float> MultiFrequencyNoise(float amplitude)
            {
                return p => amplitude * (0.6f * math.sin(p.x * 0.02f) * math.cos(p.y * 0.02f)
                                       + 0.3f * math.sin(p.x * 0.05f + 1.2f)
                                       + 0.1f * math.cos(p.y * 0.08f));
            }
        }
    }
}
