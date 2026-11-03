using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Net;
using ProceduralInterchanges.Core.Models;
using ProceduralInterchanges.Systems;
using Unity.Entities;
using Unity.Mathematics;

namespace ProceduralInterchanges.Core.Templates
{
    public static class TemplateMorphSolver
    {
        public static void GenerateMorphedPlan(
            WorkshopTemplateDef template,
            InterchangeConfig config,
            List<HighwayLeg> legs,
            HighwayPrefabCatalogSystem catalog,
            InterchangePlan plan)
        {
            if (template == null || config == null || legs == null || plan == null) return;

            plan.Courses.Clear();
            plan.AngleWarningMessage = string.Empty;
            plan.Config = config;
            plan.Legs = legs;

            int N = template.RequiredLegs;
            if (legs.Count != N) return;

            float3 center = config.Center;
            float yBase = center.y;

            // 1. Compute user leg azimuths alpha_i in [-pi, pi] and radial distances R_i
            float[] alphas = new float[N];
            float[] legRadii = new float[N];
            float minRadius = float.MaxValue;

            for (int i = 0; i < N; i++)
            {
                var leg = legs[i];
                float3 p = leg.IsSeparatedDualCarriageway
                    ? (leg.Inbound.PortalPosition + leg.Outbound.PortalPosition) * 0.5f
                    : leg.Inbound.PortalPosition;
                float2 d = p.xz - center.xz;
                alphas[i] = math.atan2(d.y, d.x);
                float r = math.length(d);
                legRadii[i] = r;
                if (r < minRadius) minRadius = r;
            }

            // Canonical leg azimuths and index permutation tracking
            float[] canonAz = new float[N];
            int[] origLegToSortedLeg = new int[N];
            int[] sortedLegToOrigLeg = new int[N];
            for (int i = 0; i < N; i++) sortedLegToOrigLeg[i] = i;

            bool lht = config.LeftHandTraffic;
            if (lht)
            {
                float[] unmirrored = template.CanonicalLegAzimuthsRad;
                float[] mirrored = new float[N];
                for (int i = 0; i < N; i++)
                {
                    mirrored[i] = WrapCCW(-unmirrored[i]);
                }
                Array.Sort(sortedLegToOrigLeg, (a, b) => mirrored[a].CompareTo(mirrored[b]));
                for (int m = 0; m < N; m++)
                {
                    canonAz[m] = mirrored[sortedLegToOrigLeg[m]];
                    origLegToSortedLeg[sortedLegToOrigLeg[m]] = m;
                }
            }
            else
            {
                for (int m = 0; m < N; m++)
                {
                    canonAz[m] = template.CanonicalLegAzimuthsRad[m];
                    origLegToSortedLeg[m] = m;
                    sortedLegToOrigLeg[m] = m;
                }
            }

            // Canonical sector angles
            float[] deltaCanon = new float[N];
            for (int m = 0; m < N; m++)
            {
                int next = (m + 1) % N;
                deltaCanon[m] = WrapCCWPositive(canonAz[next] - canonAz[m]);
            }

            // Find optimal cyclic shift s*
            int bestS = 0;
            float bestEnergy = float.MaxValue;

            for (int s = 0; s < N; s++)
            {
                float distortionEnergy = 0f;
                float alignEnergy = 0f;
                for (int m = 0; m < N; m++)
                {
                    int currUser = (m + s) % N;
                    int nextUser = (m + 1 + s) % N;
                    float deltaTarget = WrapCCWPositive(alphas[nextUser] - alphas[currUser]);
                    float diff = deltaTarget - deltaCanon[m];
                    distortionEnergy += diff * diff;

                    float rot = WrapCCW(alphas[currUser] - canonAz[m]);
                    alignEnergy += rot * rot;
                }

                float totalEnergy = distortionEnergy + 0.05f * alignEnergy;
                if (totalEnergy < bestEnergy)
                {
                    bestEnergy = totalEnergy;
                    bestS = s;
                }
            }

            // Compute target azimuths and target sector angles with bestS
            float[] thetaTarget = new float[N];
            float[] deltaTargetBest = new float[N];
            for (int m = 0; m < N; m++)
            {
                int currUser = (m + bestS) % N;
                int nextUser = (m + 1 + bestS) % N;
                thetaTarget[m] = alphas[currUser];
                deltaTargetBest[m] = WrapCCWPositive(alphas[nextUser] - alphas[currUser]);
            }

            // Severe Angle Warning Check
            float minSectorDeg = float.MaxValue;
            float maxSkewDeg = 0f;
            for (int m = 0; m < N; m++)
            {
                float secDeg = math.degrees(deltaTargetBest[m]);
                if (secDeg < minSectorDeg) minSectorDeg = secDeg;

                float skewDeg = math.abs(math.degrees(deltaTargetBest[m] - deltaCanon[m]));
                if (skewDeg > maxSkewDeg) maxSkewDeg = skewDeg;
            }

            if (minSectorDeg < 55.0f || maxSkewDeg > 25.0f)
            {
                plan.AngleWarningMessage = $"Severe highway skew angle ({minSectorDeg:F0}° sector / {maxSkewDeg:F0}° skew). Expect compressed or sharp ramp geometry.";
            }
            else
            {
                plan.AngleWarningMessage = string.Empty;
            }

            // 2D Polar-Sector Diffeomorphism
            float sCore = math.clamp(minRadius / math.max(1f, template.CanonicalRadius), 0.35f, 1.60f);

            float3 WarpPoint(float3 q)
            {
                // In LHT, mirror across X axis: z -> -z
                float qx = q.x;
                float qy = q.y;
                float qz = lht ? -q.z : q.z;

                float r = math.length(new float2(qx, qz));
                if (r < 1e-4f)
                {
                    return new float3(center.x, yBase + qy, center.z);
                }

                float theta = math.atan2(qz, qx);

                // Find sector m
                int secM = 0;
                float minRel = float.MaxValue;
                for (int m = 0; m < N; m++)
                {
                    float rel = WrapCCWPositive(theta - canonAz[m]);
                    if (rel <= deltaCanon[m] + 1e-3f)
                    {
                        secM = m;
                        break;
                    }
                    if (rel < minRel)
                    {
                        minRel = rel;
                        secM = m;
                    }
                }

                float u = math.clamp(WrapCCWPositive(theta - canonAz[secM]) / math.max(1e-4f, deltaCanon[secM]), 0f, 1f);
                float thetaPrime = thetaTarget[secM] + u * deltaTargetBest[secM];

                float wx = center.x + sCore * r * math.cos(thetaPrime);
                float wz = center.z + sCore * r * math.sin(thetaPrime);
                float wy = yBase + qy;

                return new float3(wx, wy, wz);
            }

            // Shared Node Table Invariant
            float3[] warpedNodes = new float3[template.Nodes.Length];
            for (int k = 0; k < template.Nodes.Length; k++)
            {
                warpedNodes[k] = WarpPoint(template.Nodes[k]);
            }

            // Map terminal ports by segment index
            // Key: segment index, Value: TemplateTerminalPort
            var startPortBySeg = new Dictionary<int, TemplateTerminalPort>();
            var endPortBySeg = new Dictionary<int, TemplateTerminalPort>();

            for (int pIdx = 0; pIdx < template.TerminalPorts.Length; pIdx++)
            {
                var port = template.TerminalPorts[pIdx];
                if (port.IsInbound)
                {
                    startPortBySeg[port.SegmentIndex] = port;
                }
                else
                {
                    endPortBySeg[port.SegmentIndex] = port;
                }
            }

            // Track single two-way trunk courses emitted per leg so we don't emit duplicates
            var emittedTwoWayTrunk = new HashSet<int>();

            // Emit all segments
            for (int segIdx = 0; segIdx < template.Segments.Length; segIdx++)
            {
                var seg = template.Segments[segIdx];
                bool isStartTerminal = startPortBySeg.TryGetValue(segIdx, out var startPort);
                bool isEndTerminal = endPortBySeg.TryGetValue(segIdx, out var endPort);

                int startNode = seg.StartNodeIndex;
                int endNode = seg.EndNodeIndex;

                Bezier4x3 curve;
                Entity startAttach = Entity.Null;
                float startSplit = 0f;
                bool startAttachIsNode = false;

                Entity endAttach = Entity.Null;
                float endSplit = 0f;
                bool endAttachIsNode = false;

                int segLegIndex = -1;
                int segTargetLegIndex = -1;

                if (isStartTerminal)
                {
                    int canonSortedM = origLegToSortedLeg[startPort.CanonicalLegIndex];
                    int userLegIdx = (canonSortedM + bestS) % N;
                    var leg = legs[userLegIdx];
                    segLegIndex = userLegIdx;

                    if (leg.IsSeparatedDualCarriageway)
                    {
                        float3 pStart = new float3(leg.Inbound.PortalPosition.x, yBase, leg.Inbound.PortalPosition.z);
                        float3 tStart = math.normalize(new float3(leg.Inbound.InwardTangent.x, 0f, leg.Inbound.InwardTangent.z));
                        float3 pEnd = warpedNodes[endNode];
                        float3 warpedC = WarpPoint(seg.BezierC);
                        float3 tEnd = math.normalize(pEnd - warpedC);
                        if (math.lengthsq(tEnd) < 1e-4f) tEnd = tStart;

                        curve = FitHermite3D(pStart, tStart, pEnd, tEnd);
                        curve.a = pStart;
                        curve.d = pEnd;

                        startAttach = leg.Inbound.AttachToNode ? leg.Inbound.AttachNodeEntity : leg.Inbound.SourceEdge;
                        startSplit = leg.Inbound.CutCurveT;
                        startAttachIsNode = leg.Inbound.AttachToNode;
                    }
                    else
                    {
                        // Single two-way highway user leg
                        float3 pRoad = new float3(leg.Inbound.PortalPosition.x, yBase, leg.Inbound.PortalPosition.z);
                        float3 tIn = math.normalize(new float3(leg.Inbound.InwardTangent.x, 0f, leg.Inbound.InwardTangent.z));
                        float3 bifurcationJ = pRoad + tIn * 18.0f;

                        // Emit trunk if not yet emitted
                        if (!emittedTwoWayTrunk.Contains(userLegIdx))
                        {
                            emittedTwoWayTrunk.Add(userLegIdx);
                            EmitTwoWayTrunk(plan, leg, catalog, pRoad, tIn, bifurcationJ, userLegIdx);
                        }

                        float3 pEnd = warpedNodes[endNode];
                        float3 warpedC = WarpPoint(seg.BezierC);
                        float3 tEnd = math.normalize(pEnd - warpedC);
                        if (math.lengthsq(tEnd) < 1e-4f) tEnd = tIn;

                        curve = FitHermite3D(bifurcationJ, tIn, pEnd, tEnd);
                        curve.a = bifurcationJ;
                        curve.d = pEnd;

                        startAttach = Entity.Null;
                    }
                }
                else if (isEndTerminal)
                {
                    int canonSortedM = origLegToSortedLeg[endPort.CanonicalLegIndex];
                    int userLegIdx = (canonSortedM + bestS) % N;
                    var leg = legs[userLegIdx];
                    segTargetLegIndex = userLegIdx;

                    if (leg.IsSeparatedDualCarriageway)
                    {
                        float3 pStart = warpedNodes[startNode];
                        float3 warpedB = WarpPoint(seg.BezierB);
                        float3 tStart = math.normalize(warpedB - pStart);
                        if (math.lengthsq(tStart) < 1e-4f) tStart = new float3(1f, 0f, 0f);

                        float3 pEnd = new float3(leg.Outbound.PortalPosition.x, yBase, leg.Outbound.PortalPosition.z);
                        float3 tEnd = math.normalize(new float3(-leg.Outbound.InwardTangent.x, 0f, -leg.Outbound.InwardTangent.z));

                        curve = FitHermite3D(pStart, tStart, pEnd, tEnd);
                        curve.a = pStart;
                        curve.d = pEnd;

                        endAttach = leg.Outbound.AttachToNode ? leg.Outbound.AttachNodeEntity : leg.Outbound.SourceEdge;
                        endSplit = leg.Outbound.CutCurveT;
                        endAttachIsNode = leg.Outbound.AttachToNode;
                    }
                    else
                    {
                        // Single two-way highway user leg
                        float3 pRoad = new float3(leg.Inbound.PortalPosition.x, yBase, leg.Inbound.PortalPosition.z);
                        float3 tIn = math.normalize(new float3(leg.Inbound.InwardTangent.x, 0f, leg.Inbound.InwardTangent.z));
                        float3 bifurcationJ = pRoad + tIn * 18.0f;

                        if (!emittedTwoWayTrunk.Contains(userLegIdx))
                        {
                            emittedTwoWayTrunk.Add(userLegIdx);
                            EmitTwoWayTrunk(plan, leg, catalog, pRoad, tIn, bifurcationJ, userLegIdx);
                        }

                        float3 pStart = warpedNodes[startNode];
                        float3 warpedB = WarpPoint(seg.BezierB);
                        float3 tStart = math.normalize(warpedB - pStart);
                        if (math.lengthsq(tStart) < 1e-4f) tStart = tIn;

                        curve = FitHermite3D(pStart, tStart, bifurcationJ, -tIn);
                        curve.a = pStart;
                        curve.d = bifurcationJ;

                        endAttach = Entity.Null;
                    }
                }
                else
                {
                    // Internal segment: bit-for-bit shared node coordinates
                    curve = new Bezier4x3(
                        warpedNodes[startNode],
                        WarpPoint(seg.BezierB),
                        WarpPoint(seg.BezierC),
                        warpedNodes[endNode]
                    );
                }

                // Resolve road prefab
                int forwardLanes = (int)seg.WidthClass;
                Entity prefabEntity = catalog != null
                    ? catalog.FindBestHighwayPrefab(isOneWay: true, forwardLanes: forwardLanes, backwardLanes: 0, highwayRules: true, out string prefabName)
                    : Entity.Null;
                string pName = catalog != null ? catalog.FindBestHighwayPrefab(isOneWay: true, forwardLanes: forwardLanes, backwardLanes: 0, highwayRules: true, out var pn) != Entity.Null ? pn : "One-Way Highway" : "One-Way Highway";

                float maxElev = math.max(curve.a.y - yBase, curve.d.y - yBase);
                bool forceElev = seg.IsElevated || maxElev >= 2.5f;

                var course = new PlannedNetCourse
                {
                    Id = $"seg_{segIdx}",
                    Role = (isStartTerminal || isEndTerminal) ? NetRole.MainlineThrough : NetRole.DirectionalRamp,
                    LegIndex = segLegIndex,
                    TargetLegIndex = segTargetLegIndex,
                    Curve = curve,
                    StackLevel = forceElev ? (maxElev >= 9.0f ? 2 : 1) : 0,
                    ForceElevated = forceElev,
                    StartElevation = new float2(curve.a.y - yBase, curve.a.y - yBase),
                    EndElevation = new float2(curve.d.y - yBase, curve.d.y - yBase),
                    Lanes = forwardLanes,
                    IsOneWay = true,
                    ResolvedPrefabName = pName,
                    ResolvedPrefabEntity = prefabEntity,
                    Upgrades = RoadUpgradeFlags.StreetLighting,
                    StartAttachEntity = startAttach,
                    StartSplitPosition = startSplit,
                    StartAttachIsNode = startAttachIsNode,
                    EndAttachEntity = endAttach,
                    EndSplitPosition = endSplit,
                    EndAttachIsNode = endAttachIsNode,
                    LengthMeters = MathUtils.Length(curve),
                    MinRadiusMeters = 50f,
                    MaxGradePercent = 4.0f,
                    MinClearanceMeters = 9.5f,
                    Compliance = ComplianceLevel.Optimal
                };

                plan.Courses.Add(course);
            }
        }

        private static void EmitTwoWayTrunk(
            InterchangePlan plan,
            HighwayLeg leg,
            HighwayPrefabCatalogSystem catalog,
            float3 pRoad,
            float3 tIn,
            float3 bifurcationJ,
            int legIdx)
        {
            var trunkCurve = new Bezier4x3(
                pRoad,
                pRoad + tIn * 6.0f,
                bifurcationJ - tIn * 6.0f,
                bifurcationJ
            );

            Entity prefab = leg.Inbound.SourcePrefab != Entity.Null
                ? leg.Inbound.SourcePrefab
                : (catalog != null ? catalog.FindBestHighwayPrefab(isOneWay: false, forwardLanes: 2, backwardLanes: 2, highwayRules: true, out _) : Entity.Null);

            var trunk = new PlannedNetCourse
            {
                Id = $"trunk_leg_{legIdx}",
                Role = NetRole.MainlineThrough,
                LegIndex = legIdx,
                TargetLegIndex = legIdx,
                Curve = trunkCurve,
                StackLevel = 0,
                ForceElevated = false,
                StartElevation = float2.zero,
                EndElevation = float2.zero,
                Lanes = 2,
                IsOneWay = false,
                ResolvedPrefabName = "Highway",
                ResolvedPrefabEntity = prefab,
                Upgrades = RoadUpgradeFlags.StreetLighting,
                StartAttachEntity = leg.Inbound.AttachToNode ? leg.Inbound.AttachNodeEntity : leg.Inbound.SourceEdge,
                StartSplitPosition = leg.Inbound.CutCurveT,
                StartAttachIsNode = leg.Inbound.AttachToNode,
                EndAttachEntity = Entity.Null,
                LengthMeters = MathUtils.Length(trunkCurve),
                MinRadiusMeters = 100f,
                MaxGradePercent = 0f,
                MinClearanceMeters = 0f,
                Compliance = ComplianceLevel.Optimal
            };

            plan.Courses.Add(trunk);
        }

        private static Bezier4x3 FitHermite3D(float3 p0, float3 t0, float3 p1, float3 t1)
        {
            float chord = math.distance(p0.xz, p1.xz);
            float handle = math.clamp(chord * 0.36f, 6f, 120f);
            float3 b = p0 + t0 * handle;
            float3 c = p1 - t1 * handle;
            b.y = math.lerp(p0.y, p1.y, 0.33333334f);
            c.y = math.lerp(p0.y, p1.y, 0.6666667f);
            return new Bezier4x3(p0, b, c, p1);
        }

        private static float WrapCCWPositive(float angle)
        {
            float twoPi = 2f * math.PI;
            float w = angle % twoPi;
            if (w < 0f) w += twoPi;
            return w;
        }

        private static float WrapCCW(float angle)
        {
            float twoPi = 2f * math.PI;
            float w = (angle + math.PI) % twoPi;
            if (w < 0f) w += twoPi;
            return w - math.PI;
        }
    }
}
