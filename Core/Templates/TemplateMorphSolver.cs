using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using ProceduralInterchanges.Core.Math;
using ProceduralInterchanges.Core.Models;
using ProceduralInterchanges.Core.Templates.RL;
using ProceduralInterchanges.Systems;
using Unity.Entities;
using Unity.Mathematics;

namespace ProceduralInterchanges.Core.Templates
{
    public static class TemplateMorphSolver
    {
        public static void Morph(
            InterchangePlan plan,
            WorkshopTemplateDef template,
            InterchangeConfig config,
            IReadOnlyList<HighwayLeg> legs,
            float[] customAction32 = null,
            Func<float2, float> sampleTerrainHeight = null,
            HighwayPrefabCatalogSystem catalog = null)
        {
            if (plan == null || template == null || config == null || legs == null) return;

            int n = template.RequiredLegs;
            if (legs.Count < n) return;

            var activeLegs = legs;
            if (legs.Count > n)
            {
                var trimmed = new List<HighwayLeg>(n);
                for (int i = 0; i < n; i++) trimmed.Add(legs[i]);
                activeLegs = trimmed;
            }

            bool lht = config.LeftHandTraffic;
            var subMap = TemplateSubstructureExtractor.GetOrExtract(template, lht);

            int bestS = FindOptimalCyclicShift(template, activeLegs, config.Center, lht);

            // Baseline zero-action evaluation
            float[] zeroAction = new float[InterchangeRlStateExtractor.kActionDim];
            var baselineCourses = GenerateMorphedCourses(
                template, subMap, config, activeLegs, bestS, zeroAction, sampleTerrainHeight, catalog);
            float baselineScore = InterchangeRewardEvaluator.EvaluateFast(
                baselineCourses, config, activeLegs, LearnedPolicyWeights.ActiveRewardWeights);

            float[] action32 = customAction32;
            if (action32 == null)
            {
                action32 = InterchangePolicyNetwork.PredictAndPolishAction(
                    template, subMap, config, activeLegs, bestS, sampleTerrainHeight, LearnedPolicyWeights.ActiveRewardWeights);
            }

            var policyCourses = GenerateMorphedCourses(
                template, subMap, config, activeLegs, bestS, action32, sampleTerrainHeight, catalog);
            float policyScore = InterchangeRewardEvaluator.EvaluateFast(
                policyCourses, config, activeLegs, LearnedPolicyWeights.ActiveRewardWeights);

            // Monotonic safety guarantee: never degrade below baseline setback diffeomorphism
            if (customAction32 != null || policyScore >= baselineScore)
            {
                plan.Courses.AddRange(policyCourses);
            }
            else
            {
                plan.Courses.AddRange(baselineCourses);
            }
        }

        public static void GenerateMorphedPlan(
            WorkshopTemplateDef template,
            InterchangeConfig config,
            IReadOnlyList<HighwayLeg> legs,
            HighwayPrefabCatalogSystem catalog,
            InterchangePlan plan,
            float[] customAction32 = null,
            Func<float2, float> sampleTerrainHeight = null)
        {
            Morph(plan, template, config, legs, customAction32, sampleTerrainHeight, catalog);
        }

        public static List<PlannedNetCourse> GenerateMorphedCourses(
            WorkshopTemplateDef template,
            TemplateSubstructureMap subMap,
            InterchangeConfig config,
            IReadOnlyList<HighwayLeg> legs,
            int bestS,
            float[] action32,
            Func<float2, float> sampleTerrainHeight = null,
            HighwayPrefabCatalogSystem catalog = null)
        {
            var courses = new List<PlannedNetCourse>();
            if (template == null || subMap == null || config == null || legs == null) return courses;

            int n = template.RequiredLegs;
            if (legs.Count < n) return courses;
            var activeLegs = legs;
            if (legs.Count > n)
            {
                var trimmed = new List<HighwayLeg>(n);
                for (int i = 0; i < n; i++) trimmed.Add(legs[i]);
                activeLegs = trimmed;
            }

            action32 = action32 ?? new float[InterchangeRlStateExtractor.kActionDim];

            float3 center = config.Center;
            float yBase = center.y;
            bool lht = config.LeftHandTraffic;

            // 1. Azimuths and radii of host highway legs
            float[] alphas = new float[n];
            float[] legRadii = new float[n];
            for (int i = 0; i < n; i++)
            {
                var leg = activeLegs[i];
                float3 p = leg.PortalCenterPosition;
                float2 d = p.xz - center.xz;
                alphas[i] = math.atan2(d.y, d.x);
                legRadii[i] = math.length(d);
            }

            // Canonical leg azimuths
            float[] canonAz = new float[n];
            int[] origLegToSortedLeg = new int[n];
            int[] sortedLegToOrigLeg = new int[n];
            for (int i = 0; i < n; i++) sortedLegToOrigLeg[i] = i;

            if (lht)
            {
                float[] unmirrored = template.CanonicalLegAzimuthsRad;
                float[] mirrored = new float[n];
                for (int i = 0; i < n; i++)
                {
                    mirrored[i] = WrapCCW(-unmirrored[i]);
                }
                Array.Sort(sortedLegToOrigLeg, (a, b) => mirrored[a].CompareTo(mirrored[b]));
                for (int m = 0; m < n; m++)
                {
                    canonAz[m] = mirrored[sortedLegToOrigLeg[m]];
                    origLegToSortedLeg[sortedLegToOrigLeg[m]] = m;
                }
            }
            else
            {
                for (int m = 0; m < n; m++)
                {
                    canonAz[m] = template.CanonicalLegAzimuthsRad[m];
                    origLegToSortedLeg[m] = m;
                    sortedLegToOrigLeg[m] = m;
                }
            }

            float[] deltaCanon = new float[n];
            for (int m = 0; m < n; m++)
            {
                int next = (m + 1) % n;
                deltaCanon[m] = WrapCCWPositive(canonAz[next] - canonAz[m]);
            }

            float[] thetaTarget = new float[n];
            float[] deltaTargetBest = new float[n];
            for (int m = 0; m < n; m++)
            {
                int currUser = (m + bestS) % n;
                int nextUser = (m + 1 + bestS) % n;
                thetaTarget[m] = alphas[currUser];
                deltaTargetBest[m] = WrapCCWPositive(alphas[nextUser] - alphas[currUser]);
            }

            float canonR = math.max(1f, template.CanonicalRadius);
            float hostR = math.clamp(config.FootprintRadius, 120f, 500f);

            // Two-Zone Highway Setback Parameters
            float etaCore = math.clamp(0.62f + 0.08f * action32[11], 0.52f, 0.72f);
            float rCore = etaCore * hostR;
            float sCore = rCore / canonR;

            float alphaMold = math.clamp(0.35f + 0.08f * action32[10], 0.22f, 0.45f);

            // Action decoding for center and rigid rotation
            float deltaXCore = 12.0f * action32[24];
            float deltaZCore = 12.0f * action32[25];
            float deltaThetaCore = 0.25f * action32[26];

            float sumSin = 0f;
            float sumCos = 0f;
            for (int m = 0; m < n; m++)
            {
                float diff = thetaTarget[m] - canonAz[m];
                sumSin += math.sin(diff);
                sumCos += math.cos(diff);
            }
            float thetaRigid = math.atan2(sumSin, sumCos) + deltaThetaCore;

            // Angular residual per sector and core virtual leg angles
            float[] deltaCore = new float[n];
            float[] thetaCore = new float[n];
            for (int m = 0; m < n; m++)
            {
                float residual = WrapCCW(thetaTarget[m] - (canonAz[m] + thetaRigid));
                deltaCore[m] = alphaMold * residual;
                thetaCore[m] = WrapAngleRad(canonAz[m] + thetaRigid + deltaCore[m]);
            }

            // Step 1: Smooth Conformal-Core Harmonic Diffeomorphism
            float3 BaseMap(float3 q, out float thetaRotOut, out float sLocOut)
            {
                float r = math.length(q.xz);
                if (r < 1e-4f)
                {
                    thetaRotOut = thetaRigid;
                    sLocOut = sCore;
                    float2 cCenter = new float2(center.x + deltaXCore, center.z + deltaZCore);
                    float yCenter = sampleTerrainHeight != null ? sampleTerrainHeight(cCenter) : yBase;
                    return new float3(cCenter.x, yCenter + q.y, cCenter.y);
                }

                float theta = math.atan2(q.z, q.x);

                int secM = 0;
                float minRel = float.MaxValue;
                for (int m = 0; m < n; m++)
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
                float sU = u * u * (3.0f - 2.0f * u); // C1 smoothstep: zero angular derivative kink across sector rays
                int nextSecM = (secM + 1) % n;

                float deltaThetaHarm = (1.0f - sU) * deltaCore[secM] + sU * deltaCore[nextSecM];
                float wArm = math.smoothstep(0.15f * canonR, 0.65f * canonR, r);
                float thetaRot = thetaRigid + wArm * deltaThetaHarm;

                float sLoc = sCore;
                float2 rotated = Rotate2D(q.xz, thetaRot);
                float2 posXZ = center.xz + (1.0f - wArm) * new float2(deltaXCore, deltaZCore) + sLoc * rotated;

                thetaRotOut = thetaRot;
                sLocOut = sLoc;
                float yTerr = sampleTerrainHeight != null ? sampleTerrainHeight(posXZ) : yBase;
                return new float3(posXZ.x, yTerr + q.y, posXZ.y);
            }

            // Step 2: Transform Nodes & Authoritative Deformed Tangent Frames
            int numNodes = template.Nodes.Length;
            float3[] warpedNodes = new float3[numNodes];
            float[] nodeRotationRad = new float[numNodes];
            float[] nodeScale = new float[numNodes];
            float2[] nodeTangents = new float2[numNodes];

            for (int v = 0; v < numNodes; v++)
            {
                warpedNodes[v] = BaseMap(subMap.SanitizedNodes[v], out float rot, out float scl);
                nodeRotationRad[v] = rot;
                nodeScale[v] = scl;
            }

            // Gore node sliding (bounded to preserve geometric integrity)
            int numGores = subMap.GoreNodes != null ? subMap.GoreNodes.Length : 0;
            for (int g = 0; g < numGores && g < 8; g++)
            {
                var gore = subMap.GoreNodes[g];
                int v = gore.NodeIndex;
                float deltaTau = 8.0f * action32[16 + g];

                float2 tPrime = Rotate2D(gore.MainlineTangentXZ, nodeRotationRad[v]);
                warpedNodes[v].xz += deltaTau * tPrime;
                float yGore = sampleTerrainHeight != null ? sampleTerrainHeight(warpedNodes[v].xz) : yBase;
                warpedNodes[v].y = yGore + subMap.SanitizedNodes[v].y;
            }

            // Conformal loop cluster placement
            int numClusters = subMap.LoopClusters != null ? subMap.LoopClusters.Length : 0;
            for (int k = 0; k < numClusters && k < 4; k++)
            {
                var cl = subMap.LoopClusters[k];
                float deltaR = 12.0f * action32[4 * k + 0];
                float deltaN = 12.0f * action32[4 * k + 1];
                float deltaPhi = 0.20f * action32[4 * k + 2];
                float sLoop = 1.0f + 0.12f * action32[4 * k + 3];

                var c0_3d = BaseMap(new float3(cl.CanonicalCenterXZ.x, 0f, cl.CanonicalCenterXZ.y), out float theta0, out float s0);
                float2 c0 = c0_3d.xz;

                float2 uDir = math.lengthsq(c0 - center.xz) > 1e-4f ? math.normalize(c0 - center.xz) : new float2(1f, 0f);
                float2 nDir = new float2(-uDir.y, uDir.x);

                float2 cPrime = c0 + deltaR * uDir + deltaN * nDir;
                float psiCluster = theta0 + deltaPhi;
                float lambdaCluster = sLoop * math.max(0.85f, sCore);

                for (int i = 0; i < cl.InteriorNodeIndices.Length; i++)
                {
                    int v = cl.InteriorNodeIndices[i];
                    float2 offset = subMap.SanitizedNodes[v].xz - cl.CanonicalCenterXZ;
                    warpedNodes[v].xz = cPrime + lambdaCluster * Rotate2D(offset, psiCluster);
                    float yLoop = sampleTerrainHeight != null ? sampleTerrainHeight(warpedNodes[v].xz) : yBase;
                    warpedNodes[v].y = yLoop + subMap.SanitizedNodes[v].y;
                    nodeRotationRad[v] = psiCluster;
                    nodeScale[v] = lambdaCluster;
                }
            }

            // Map terminal ports for quick lookup
            var portByNode = new Dictionary<int, (TemplateTerminalPort port, int legIdx)>();
            for (int pIdx = 0; pIdx < template.TerminalPorts.Length; pIdx++)
            {
                var port = template.TerminalPorts[pIdx];
                int canonSortedM = origLegToSortedLeg[port.CanonicalLegIndex];
                int userLegIdx = (canonSortedM + bestS) % n;
                portByNode[port.NodeIndex] = (port, userLegIdx);
            }

            // Authoritative deformed node tangents via numerical differentiation (strict node-locked G1 frame)
            for (int v = 0; v < numNodes; v++)
            {
                if (portByNode.TryGetValue(v, out var portInfo))
                {
                    int m = origLegToSortedLeg[portInfo.port.CanonicalLegIndex];
                    float coreAngle = thetaCore[m];
                    float2 coreAxis = new float2(math.cos(coreAngle), math.sin(coreAngle));
                    if (portInfo.port.IsInbound)
                    {
                        // Inbound traffic moves inward toward center
                        nodeTangents[v] = -coreAxis;
                    }
                    else
                    {
                        // Outbound traffic moves outward away from center
                        nodeTangents[v] = coreAxis;
                    }
                }
                else
                {
                    float2 tauCanon = subMap.CanonicalNodeTangentsXZ[v];
                    float3 qPlus = subMap.SanitizedNodes[v] + 0.5f * new float3(tauCanon.x, 0f, tauCanon.y);
                    float3 qMinus = subMap.SanitizedNodes[v] - 0.5f * new float3(tauCanon.x, 0f, tauCanon.y);
                    float3 pPrimePlus = BaseMap(qPlus, out _, out _);
                    float3 pPrimeMinus = BaseMap(qMinus, out _, out _);
                    float2 dPrime = (pPrimePlus - pPrimeMinus).xz;
                    if (math.lengthsq(dPrime) > 1e-4f)
                    {
                        nodeTangents[v] = math.normalize(dPrime);
                    }
                    else
                    {
                        nodeTangents[v] = Rotate2D(tauCanon, nodeRotationRad[v]);
                    }
                }
            }

            // Step 3: Internal Segment Reconstruction (Strict Node-Locked G1 Continuity)
            for (int s = 0; s < template.Segments.Length; s++)
            {
                var seg = template.Segments[s];
                int u = seg.StartNodeIndex;
                int v = seg.EndNodeIndex;

                Bezier4x3 curve;
                int clusterIdx = subMap.SegmentToLoopCluster[s];
                bool bothInLoop = clusterIdx >= 0 &&
                                  subMap.NodeToLoopCluster[u] == clusterIdx &&
                                  subMap.NodeToLoopCluster[v] == clusterIdx;

                if (bothInLoop)
                {
                    // Conformal loop interior similarity transform with node-locked G1 boundary
                    float lambda = math.max(0.70f, nodeScale[u]);
                    float3 p0 = warpedNodes[u];
                    float3 p1 = warpedNodes[v];
                    float dChord = math.distance(p0.xz, p1.xz);

                    float2 t0Canon = subMap.SegmentCanonVectorA[s].xz;
                    float2 t1Canon = -subMap.SegmentCanonVectorD[s].xz;
                    float lenA = math.max(1.0f, math.length(t0Canon));
                    float lenD = math.max(1.0f, math.length(t1Canon));

                    float2 dir0Canon = t0Canon / lenA;
                    float2 dir1Canon = t1Canon / lenD;

                    float sigma0 = math.dot(dir0Canon, subMap.CanonicalNodeTangentsXZ[u]) >= 0f ? 1.0f : -1.0f;
                    float sigma1 = math.dot(dir1Canon, subMap.CanonicalNodeTangentsXZ[v]) >= 0f ? 1.0f : -1.0f;

                    float2 t0Rot = sigma0 * nodeTangents[u];
                    float2 t1Rot = sigma1 * nodeTangents[v];

                    float lA = math.clamp(lambda * lenA, 0.30f * dChord, 0.44f * dChord);
                    float lD = math.clamp(lambda * lenD, 0.30f * dChord, 0.44f * dChord);

                    float3 bB = new float3(p0.x + lA * t0Rot.x, math.lerp(p0.y, p1.y, 0.33333334f), p0.z + lA * t0Rot.y);
                    float3 bC = new float3(p1.x - lD * t1Rot.x, math.lerp(p0.y, p1.y, 0.6666667f), p1.z - lD * t1Rot.y);

                    curve = new Bezier4x3(p0, bB, bC, p1);
                }
                else
                {
                    float3 p0 = warpedNodes[u];
                    float3 p1 = warpedNodes[v];

                    float dChord = math.distance(p0.xz, p1.xz);
                    float2 dCanon = subMap.SanitizedNodes[v].xz - subMap.SanitizedNodes[u].xz;
                    float lenCanon = math.max(1.0f, math.length(dCanon));
                    float rChord = dChord / lenCanon;

                    float2 t0Canon = subMap.SegmentCanonVectorA[s].xz;
                    float2 t1Canon = -subMap.SegmentCanonVectorD[s].xz;

                    float lenA = math.max(1.0f, math.length(t0Canon));
                    float lenD = math.max(1.0f, math.length(t1Canon));

                    float2 dir0Canon = t0Canon / lenA;
                    float2 dir1Canon = t1Canon / lenD;

                    float2 nodeTanU = nodeTangents[u];
                    float2 nodeTanV = nodeTangents[v];

                    float sigma0 = math.dot(dir0Canon, subMap.CanonicalNodeTangentsXZ[u]) >= 0f ? 1.0f : -1.0f;
                    float sigma1 = math.dot(dir1Canon, subMap.CanonicalNodeTangentsXZ[v]) >= 0f ? 1.0f : -1.0f;

                    float2 t0Rot;
                    float dotU = math.abs(math.dot(dir0Canon, subMap.CanonicalNodeTangentsXZ[u]));
                    if (dotU >= 0.98f || subMap.NodeToGoreIndex[u] < 0)
                    {
                        t0Rot = sigma0 * nodeTanU;
                    }
                    else
                    {
                        float2 b0 = sigma0 * subMap.CanonicalNodeTangentsXZ[u];
                        float cosPhi0 = math.clamp(b0.x * dir0Canon.x + b0.y * dir0Canon.y, -1f, 1f);
                        float sinPhi0 = b0.x * dir0Canon.y - b0.y * dir0Canon.x;
                        float2 c0 = sigma0 * nodeTanU;
                        t0Rot = new float2(c0.x * cosPhi0 - c0.y * sinPhi0, c0.x * sinPhi0 + c0.y * cosPhi0);
                    }

                    float2 t1Rot;
                    float dotV = math.abs(math.dot(dir1Canon, subMap.CanonicalNodeTangentsXZ[v]));
                    if (dotV >= 0.98f || subMap.NodeToGoreIndex[v] < 0)
                    {
                        t1Rot = sigma1 * nodeTanV;
                    }
                    else
                    {
                        float2 b1 = sigma1 * subMap.CanonicalNodeTangentsXZ[v];
                        float cosPhi1 = math.clamp(b1.x * dir1Canon.x + b1.y * dir1Canon.y, -1f, 1f);
                        float sinPhi1 = b1.x * dir1Canon.y - b1.y * dir1Canon.x;
                        float2 c1 = sigma1 * nodeTanV;
                        t1Rot = new float2(c1.x * cosPhi1 - c1.y * sinPhi1, c1.x * sinPhi1 + c1.y * cosPhi1);
                    }

                    float cosTurn = math.clamp(math.dot(t0Rot, t1Rot), -1f, 1f);
                    float3 bB, bC;
                    if (dChord < 14.0f && cosTurn > 0.85f)
                    {
                        // Short near-straight connector: exact collinear third points to avoid velocity collapse
                        bB = math.lerp(p0, p1, 0.33333334f);
                        bC = math.lerp(p0, p1, 0.6666667f);
                    }
                    else
                    {
                        float lA = math.clamp(rChord * lenA, 0.25f * dChord, 0.45f * dChord);
                        float lD = math.clamp(rChord * lenD, 0.25f * dChord, 0.45f * dChord);

                        float sVert = math.clamp(sCore, 0.65f, 1.0f);
                        float yB = (math.abs(subMap.SegmentCanonVectorA[s].y) > 0.05f)
                            ? p0.y + sVert * subMap.SegmentCanonVectorA[s].y
                            : math.lerp(p0.y, p1.y, 0.33333334f);
                        float yC = (math.abs(subMap.SegmentCanonVectorD[s].y) > 0.05f)
                            ? p1.y + sVert * subMap.SegmentCanonVectorD[s].y
                            : math.lerp(p0.y, p1.y, 0.6666667f);

                        bB = new float3(p0.x + lA * t0Rot.x, yB, p0.z + lA * t0Rot.y);
                        bC = new float3(p1.x - lD * t1Rot.x, yC, p1.z - lD * t1Rot.y);
                    }

                    curve = new Bezier4x3(p0, bB, bC, p1);
                }

                curve = LiftCurveToTerrain(curve, sampleTerrainHeight);

                int forwardLanes = (int)seg.WidthClass;
                Entity prefabEntity = catalog != null
                    ? catalog.FindBestHighwayPrefab(isOneWay: true, forwardLanes: forwardLanes, backwardLanes: 0, highwayRules: true, out string prefabName)
                    : Entity.Null;
                string pName = catalog != null ? (prefabEntity != Entity.Null ? "One-Way Highway" : "One-Way Highway") : "One-Way Highway";

                NetRole role = NetRole.DirectionalRamp;
                if (subMap.SegmentToLoopCluster[s] >= 0)
                    role = NetRole.LoopRamp;
                else if (seg.WidthClass > TemplateRoadWidthClass.OneWay1Lane)
                    role = NetRole.MainlineThrough;

                int stackLevel = seg.IsElevated ? 1 : 0;
                bool forceElevated = seg.IsElevated;

                var plannedCourse = new PlannedNetCourse
                {
                    Id = $"seg_{s}",
                    Role = role,
                    StackLevel = stackLevel,
                    ForceElevated = forceElevated,
                    Curve = curve,
                    StartElevation = new float2(curve.a.y, curve.a.y),
                    EndElevation = new float2(curve.d.y, curve.d.y),
                    Lanes = forwardLanes,
                    IsOneWay = true,
                    ResolvedPrefabName = pName,
                    ResolvedPrefabEntity = prefabEntity,
                    Upgrades = RoadUpgradeFlags.StreetLighting,
                    StartAttachEntity = Entity.Null,
                    StartSplitPosition = 0f,
                    StartAttachIsNode = false,
                    EndAttachEntity = Entity.Null,
                    EndSplitPosition = 0f,
                    EndAttachIsNode = false,
                    LengthMeters = MathUtils.Length(curve),
                    MinRadiusMeters = 30f,
                    MaxGradePercent = 0.04f,
                    MinClearanceMeters = 9.5f,
                    Compliance = ComplianceLevel.Optimal,
                    LegIndex = -1,
                    TargetLegIndex = -1
                };

                courses.Add(plannedCourse);
            }

            // Step 4: Outer Setback Highway Approach Transition Corridors (Zone B)
            for (int pIdx = 0; pIdx < template.TerminalPorts.Length; pIdx++)
            {
                var port = template.TerminalPorts[pIdx];
                int canonSortedM = origLegToSortedLeg[port.CanonicalLegIndex];
                int userLegIdx = (canonSortedM + bestS) % n;
                var leg = activeLegs[userLegIdx];
                int u = port.NodeIndex;

                if (leg.IsSeparatedDualCarriageway)
                {
                    float3 pStart, pEnd;
                    float2 tStart, tEnd;

                    if (port.IsInbound)
                    {
                        // Inbound: from Host Inbound Portal into Core Terminal Node
                        pStart = leg.Inbound.PortalPosition;
                        pEnd = warpedNodes[u];

                        float2 inTan = leg.Inbound.InwardTangent.xz;
                        if (math.lengthsq(inTan) < 1e-4f) inTan = -leg.LegAxisDirection.xz;
                        tStart = math.normalize(inTan);
                        if (math.dot(tStart, (pEnd - pStart).xz) < 0f) tStart = -tStart;

                        tEnd = nodeTangents[u];
                        if (math.dot(tEnd, (pEnd - pStart).xz) < 0f) tEnd = -tEnd;
                    }
                    else
                    {
                        // Outbound: from Core Terminal Node out to Host Outbound Portal
                        pStart = warpedNodes[u];
                        pEnd = leg.Outbound.PortalPosition;

                        tStart = nodeTangents[u];
                        if (math.dot(tStart, (pEnd - pStart).xz) < 0f) tStart = -tStart;

                        float2 outTan = -leg.Outbound.InwardTangent.xz;
                        if (math.lengthsq(outTan) < 1e-4f) outTan = leg.LegAxisDirection.xz;
                        tEnd = math.normalize(outTan);
                        if (math.dot(tEnd, (pEnd - pStart).xz) < 0f) tEnd = -tEnd;
                    }

                    float chord = math.distance(pStart.xz, pEnd.xz);
                    if (chord > 1.5f)
                    {
                        float2 chordDir = (pEnd - pStart).xz / chord;
                        float cos0 = math.clamp(math.dot(tStart, chordDir), -1f, 1f);
                        float cos1 = math.clamp(math.dot(tEnd, chordDir), -1f, 1f);
                        float alpha0 = math.acos(cos0);
                        float alpha1 = math.acos(cos1);

                        float l0 = math.clamp(2.0f * chord / (3.0f * (1.0f + math.cos(alpha1)) + 0.5f * math.sin(alpha0)), 0.20f * chord, 0.34f * chord);
                        float l1 = math.clamp(2.0f * chord / (3.0f * (1.0f + math.cos(alpha0)) + 0.5f * math.sin(alpha1)), 0.20f * chord, 0.34f * chord);

                        float3 bB = new float3(pStart.x + l0 * tStart.x, math.lerp(pStart.y, pEnd.y, 0.33333334f), pStart.z + l0 * tStart.y);
                        float3 bC = new float3(pEnd.x - l1 * tEnd.x, math.lerp(pStart.y, pEnd.y, 0.6666667f), pEnd.z - l1 * tEnd.y);

                        var sbCurve = new Bezier4x3(pStart, bB, bC, pEnd);
                        sbCurve = LiftCurveToTerrain(sbCurve, sampleTerrainHeight);

                        int lanes = port.IsInbound ? leg.Inbound.LaneCount : leg.Outbound.LaneCount;
                        lanes = math.max(1, lanes);

                        Entity prefab = Entity.Null;
                        string prefabName = "One-Way Highway";
                        if (catalog != null)
                        {
                            prefab = catalog.FindBestHighwayPrefab(isOneWay: true, forwardLanes: lanes, backwardLanes: 0, highwayRules: true, out prefabName);
                        }

                        var setbackCourse = new PlannedNetCourse
                        {
                            Id = $"setback_{pIdx}",
                            Role = NetRole.MainlineThrough,
                            StackLevel = 0,
                            ForceElevated = false,
                            Curve = sbCurve,
                            StartElevation = new float2(sbCurve.a.y, sbCurve.a.y),
                            EndElevation = new float2(sbCurve.d.y, sbCurve.d.y),
                            Lanes = lanes,
                            IsOneWay = true,
                            ResolvedPrefabName = prefabName,
                            ResolvedPrefabEntity = prefab,
                            Upgrades = RoadUpgradeFlags.StreetLighting,
                            StartAttachEntity = port.IsInbound ? (leg.Inbound.AttachToNode ? leg.Inbound.AttachNodeEntity : leg.Inbound.SourceEdge) : Entity.Null,
                            StartSplitPosition = port.IsInbound ? leg.Inbound.CutCurveT : 0f,
                            StartAttachIsNode = port.IsInbound && leg.Inbound.AttachToNode,
                            EndAttachEntity = !port.IsInbound ? (leg.Outbound.AttachToNode ? leg.Outbound.AttachNodeEntity : leg.Outbound.SourceEdge) : Entity.Null,
                            EndSplitPosition = !port.IsInbound ? leg.Outbound.CutCurveT : 0f,
                            EndAttachIsNode = !port.IsInbound && leg.Outbound.AttachToNode,
                            LengthMeters = MathUtils.Length(sbCurve),
                            MinRadiusMeters = 50f,
                            MaxGradePercent = 0.04f,
                            MinClearanceMeters = 9.5f,
                            Compliance = ComplianceLevel.Optimal,
                            LegIndex = userLegIdx,
                            TargetLegIndex = userLegIdx
                        };

                        courses.Add(setbackCourse);
                    }
                }
            }

            return courses;
        }

        public static int FindOptimalCyclicShift(
            WorkshopTemplateDef template,
            IReadOnlyList<HighwayLeg> legs,
            float3 center,
            bool lht = false)
        {
            int n = template.RequiredLegs;
            float[] userAzimuths = new float[n];
            for (int i = 0; i < n; i++)
            {
                float3 p = legs[i].PortalCenterPosition;
                float2 d = p.xz - center.xz;
                userAzimuths[i] = math.atan2(d.y, d.x);
            }

            float[] canonAz = new float[n];
            if (lht)
            {
                float[] unmirrored = template.CanonicalLegAzimuthsRad;
                float[] mirrored = new float[n];
                for (int i = 0; i < n; i++)
                {
                    mirrored[i] = WrapCCW(-unmirrored[i]);
                }
                Array.Sort(mirrored);
                for (int m = 0; m < n; m++) canonAz[m] = mirrored[m];
            }
            else
            {
                for (int m = 0; m < n; m++) canonAz[m] = template.CanonicalLegAzimuthsRad[m];
            }

            int bestS = 0;
            float minError = float.MaxValue;

            for (int s = 0; s < n; s++)
            {
                float sumSin = 0f;
                float sumCos = 0f;
                for (int m = 0; m < n; m++)
                {
                    int u = (m + s) % n;
                    float diff = userAzimuths[u] - canonAz[m];
                    sumSin += math.sin(diff);
                    sumCos += math.cos(diff);
                }
                float thetaRigid = math.atan2(sumSin, sumCos);

                float sse = 0f;
                for (int m = 0; m < n; m++)
                {
                    int u = (m + s) % n;
                    float residual = WrapCCW(userAzimuths[u] - (canonAz[m] + thetaRigid));
                    sse += residual * residual;
                }

                if (sse < minError)
                {
                    minError = sse;
                    bestS = s;
                }
            }

            return bestS;
        }

        private static float2 Rotate2D(float2 v, float angleRad)
        {
            float c = math.cos(angleRad);
            float s = math.sin(angleRad);
            return new float2(c * v.x - s * v.y, s * v.x + c * v.y);
        }

        private static Bezier4x3 LiftCurveToTerrain(Bezier4x3 curve, Func<float2, float> sampleTerrainHeight)
        {
            if (sampleTerrainHeight == null) return curve;

            float y0 = curve.a.y;
            float y3 = curve.d.y;

            float terr0 = sampleTerrainHeight(curve.a.xz);
            float terr3 = sampleTerrainHeight(curve.d.xz);

            float elev0 = math.max(0f, y0 - terr0);
            float elev3 = math.max(0f, y3 - terr3);

            float3 p1_3 = CubicBezier3D.Position(curve, 1f / 3f);
            float3 p2_3 = CubicBezier3D.Position(curve, 2f / 3f);

            float terr1 = sampleTerrainHeight(p1_3.xz);
            float terr2 = sampleTerrainHeight(p2_3.xz);

            float min1 = terr1 + math.lerp(elev0, elev3, 1f / 3f);
            float min2 = terr2 + math.lerp(elev0, elev3, 2f / 3f);

            if (p1_3.y >= min1 - 0.05f && p2_3.y >= min2 - 0.05f)
            {
                return curve;
            }

            float h1 = math.max(p1_3.y, min1);
            float h2 = math.max(p2_3.y, min2);

            float r1 = 27f * h1 - 8f * y0 - y3;
            float r2 = 27f * h2 - y0 - 8f * y3;

            float y1 = (2f * r1 - r2) / 18f;
            float y2 = (2f * r2 - r1) / 18f;

            return new Bezier4x3(curve.a, new float3(curve.b.x, y1, curve.b.z), new float3(curve.c.x, y2, curve.c.z), curve.d);
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

        private static float WrapAngleRad(float angle)
        {
            return WrapCCW(angle);
        }
    }
}
