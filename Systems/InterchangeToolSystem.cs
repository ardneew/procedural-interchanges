using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Colossal.Entities;
using Colossal.Mathematics;
using Game;
using Game.City;
using Game.Common;
using Game.Input;
using Game.Net;
using Game.Prefabs;
using Game.Rendering;
using Game.Simulation;
using Game.Tools;
using ProceduralInterchanges.Core.Generators;
using ProceduralInterchanges.Core.Integration;
using ProceduralInterchanges.Core.Math;
using ProceduralInterchanges.Core.Models;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Scripting;

namespace ProceduralInterchanges.Systems
{
    public sealed partial class InterchangeToolSystem : ToolBaseSystem
    {
        public const string ToolId = "ProceduralInterchangesTool";
        public override string toolID => ToolId;

        // Subsystems
        private OverlayRenderSystem m_OverlayRenderSystem;
        private SearchSystem m_NetSearchSystem;
        private HighwayPrefabCatalogSystem m_PrefabCatalogSystem;
        private InterchangeBuildService m_BuildService;
        private InterchangeGroupSystem m_GroupSystem;
        private CityConfigurationSystem m_CityConfigSystem;

        // State
        public ToolStage Stage { get; private set; } = ToolStage.PickCenter;
        public InterchangeConfig CurrentConfig { get; private set; } = new InterchangeConfig();
        public List<HighwayLeg> CurrentLegs { get; private set; } = new List<HighwayLeg>();
        public InterchangePlan CurrentPlan { get; private set; }

        private readonly ConcurrentQueue<Action> m_PendingUIActions = new ConcurrentQueue<Action>();

        public void EnqueueUIAction(Action action)
        {
            if (action != null)
            {
                m_PendingUIActions.Enqueue(action);
            }
        }

        public void DrainPendingUIActions()
        {
            while (m_PendingUIActions.TryDequeue(out var action))
            {
                try
                {
                    action.Invoke();
                }
                catch (Exception ex)
                {
                    Mod.log.Error(ex, "Error executing queued UI action in InterchangeToolSystem");
                }
            }
        }

        public void ClearAllStateAndExit()
        {
            m_BuildService.ClearGhostPreview();
            CurrentPlan = null;
            CurrentLegs.Clear();
            Stage = ToolStage.PickCenter;
            m_ToolSystem.activeTool = World.GetOrCreateSystemManaged<DefaultToolSystem>();
        }

        public bool LiveGhostPreviewEnabled { get; set; } = true;
        public bool EngineeringOverlayEnabled { get; set; } = true;
        public ApplyMode RequestedApplyMode { get; set; } = ApplyMode.None;

        private EntityQuery m_VisibleTempIconQuery;

        private float3 m_HoverPoint;
        private bool m_HasHover;
        private Entity m_HoveredEntity;

        public override PrefabBase GetPrefab() => null;
        public override bool TrySetPrefab(PrefabBase prefab) => false;

        public void SetStage(ToolStage newStage)
        {
            if (Stage == newStage) return;

            Stage = newStage;
            if (Stage == ToolStage.ConfigureInterchange)
            {
                RegeneratePlan();
            }
            else if (Stage == ToolStage.PickCenter || Stage == ToolStage.SelectHighways)
            {
                m_BuildService.ClearGhostPreview();
            }
        }

        public void AutoDetectHighways()
        {
            if (Stage == ToolStage.PickCenter || math.lengthsq(CurrentConfig.Center) < 1f) return;

            bool lht = m_CityConfigSystem != null && m_CityConfigSystem.leftHandTraffic;
            CurrentConfig.LeftHandTraffic = lht;

            var detected = HighwayPairingSolver.ScanAndPairNearbyHighways(
                EntityManager,
                m_NetSearchSystem,
                CurrentConfig.Center,
                CurrentConfig.FootprintRadius,
                lht,
                m_PrefabSystem);

            CurrentLegs.Clear();
            CurrentLegs.AddRange(detected);

            if (Stage == ToolStage.ConfigureInterchange)
            {
                RegeneratePlan();
            }
        }

        public void RemoveLeg(int legIndex)
        {
            for (int i = 0; i < CurrentLegs.Count; i++)
            {
                if (CurrentLegs[i].LegIndex == legIndex)
                {
                    CurrentLegs.RemoveAt(i);
                    break;
                }
            }

            // Re-index remaining legs
            for (int i = 0; i < CurrentLegs.Count; i++)
            {
                CurrentLegs[i].LegIndex = i;
            }

            if (Stage == ToolStage.ConfigureInterchange)
            {
                RegeneratePlan();
            }
        }

        public void SetArchetype(InterchangeArchetype archetype)
        {
            CurrentConfig.Archetype = archetype;
            if (Stage == ToolStage.ConfigureInterchange)
            {
                RegeneratePlan();
            }
        }

        public void SetGlobalParam(string key, float value)
        {
            switch (key.ToLowerInvariant())
            {
                case "radius":
                case "footprintradius":
                    CurrentConfig.FootprintRadius = math.clamp(value, 120f, 500f);
                    if (Stage == ToolStage.SelectHighways && CurrentLegs.Count > 0)
                    {
                        HighwayPairingSolver.RefreshLegsForRadius(
                            EntityManager,
                            m_NetSearchSystem,
                            CurrentLegs,
                            CurrentConfig.Center,
                            CurrentConfig.FootprintRadius,
                            CurrentConfig.LeftHandTraffic,
                            m_PrefabSystem);
                    }
                    break;
                case "rotation":
                case "rotationdeg":
                    CurrentConfig.RotationOffsetRad = math.radians(value);
                    break;
                case "maxmainlinegrade":
                    CurrentConfig.MaxMainlineGradePercent = math.clamp(value, 2.0f, 8.0f);
                    break;
                case "maxrampgrade":
                    CurrentConfig.MaxRampGradePercent = math.clamp(value, 3.0f, 12.0f);
                    break;
                case "minclearance":
                case "bridgeclearance":
                    CurrentConfig.MinBridgeClearanceMeters = math.clamp(value, 8.0f, 15.0f);
                    break;
                case "designspeed":
                    CurrentConfig.DesignSpeedKmh = math.clamp(value, 60f, 130f);
                    break;
                case "mainlineoverpass":
                    CurrentConfig.MainlineOverpass = value > 0.5f;
                    break;
                case "gradeseparatemainline":
                    CurrentConfig.GradeSeparateMainline = value > 0.5f;
                    break;
            }

            if (Stage == ToolStage.ConfigureInterchange)
            {
                RegeneratePlan();
            }
        }

        public void SetGlobalPrefab(string role, string prefabName)
        {
            if (role.ToLowerInvariant().Contains("main"))
            {
                CurrentConfig.GlobalMainlinePrefabOverride = prefabName;
            }
            else
            {
                CurrentConfig.GlobalRampPrefabOverride = prefabName;
            }

            if (Stage == ToolStage.ConfigureInterchange)
            {
                RegeneratePlan();
            }
        }

        public void SetGlobalUpgrade(int flagBit, bool enabled)
        {
            var flag = (RoadUpgradeFlags)flagBit;
            if (enabled) CurrentConfig.GlobalUpgrades |= flag;
            else CurrentConfig.GlobalUpgrades &= ~flag;

            if (Stage == ToolStage.ConfigureInterchange)
            {
                RegeneratePlan();
            }
        }

        public void SetLegOverride(int legIndex, int lanes, string prefabName, int upgradesMask)
        {
            for (int i = 0; i < CurrentLegs.Count; i++)
            {
                if (CurrentLegs[i].LegIndex == legIndex)
                {
                    CurrentLegs[i].OverrideMainlineLanes = lanes;
                    CurrentLegs[i].OverrideMainlinePrefabName = prefabName;
                    CurrentLegs[i].Upgrades = (RoadUpgradeFlags)upgradesMask;
                    break;
                }
            }

            if (Stage == ToolStage.ConfigureInterchange)
            {
                RegeneratePlan();
            }
        }

        public void SetCourseOverride(string courseId, int lanes, string prefabName, int upgradesMask)
        {
            CurrentConfig.PerCourseOverrides[courseId] = new RampOverride
            {
                LaneCount = lanes,
                PrefabName = prefabName,
                Upgrades = (RoadUpgradeFlags)upgradesMask
            };

            if (Stage == ToolStage.ConfigureInterchange)
            {
                RegeneratePlan();
            }
        }

        public void SelectCourseForProfile(string courseId)
        {
            if (CurrentPlan == null) return;
            CurrentPlan.SelectedCourseForProfileId = courseId;
            ComputeElevationProfileForSelected();
        }

        public void RegeneratePlan()
        {
            if (CurrentLegs.Count < 3)
            {
                CurrentPlan = null;
                m_BuildService.ClearGhostPreview();
                return;
            }

            bool lht = m_CityConfigSystem != null && m_CityConfigSystem.leftHandTraffic;
            CurrentConfig.LeftHandTraffic = lht;

            HighwayPairingSolver.RefreshLegsForRadius(
                EntityManager,
                m_NetSearchSystem,
                CurrentLegs,
                CurrentConfig.Center,
                CurrentConfig.FootprintRadius,
                lht,
                m_PrefabSystem);

            var generator = InterchangeGeneratorRegistry.GetGenerator(CurrentConfig.Archetype);
            if (generator == null || !generator.SupportsConfiguration(CurrentLegs, out _))
            {
                // Fallback to first supported generator
                var all = InterchangeGeneratorRegistry.All;
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i].SupportsConfiguration(CurrentLegs, out _))
                    {
                        generator = all[i];
                        CurrentConfig.Archetype = generator.Archetype;
                        break;
                    }
                }
            }

            if (generator == null)
            {
                CurrentPlan = null;
                m_BuildService.ClearGhostPreview();
                return;
            }

            var plan = new InterchangePlan
            {
                Config = CurrentConfig.Clone(),
                Legs = new List<HighwayLeg>(CurrentLegs)
            };

            // 1. Solve trim-and-splice boundary cuts & outer replacements first so endpoint portals are snapped to exact split points
            TrimAndSpliceSolver.ComputeBoundaryCutsAndOuterReplacements(
                EntityManager,
                plan.Legs,
                plan.Config.Center,
                plan.Config.FootprintRadius,
                p => m_BuildService.SampleTerrainHeight(p),
                plan.OuterReplacementCourses,
                plan.EdgesToDelete,
                plan.RestorableSnapshots);

            // 2. Generate Archetype geometry with exact snapped portal endpoints
            generator.GenerateGeometry(plan.Config, plan.Legs, plan.Courses);

            // 3. Solve elevations, clearances, bridge pieces, and shift pillars
            ClearanceAndPillarSolver.SolveElevationsAndSubdivide(
                plan.Courses,
                p => m_BuildService.SampleTerrainHeight(p),
                plan.Config);

            // Also solve ground heights for ground-level outer replacements, preserving all sub-courses
            var newOuterList = new List<PlannedNetCourse>();
            for (int i = 0; i < plan.OuterReplacementCourses.Count; i++)
            {
                var or = plan.OuterReplacementCourses[i];
                if (!or.ForceElevated && or.StackLevel == 0)
                {
                    var sub = new List<Bezier4x3>();
                    TopographySolver.FitGroundCourse(or.Curve, p => m_BuildService.SampleTerrainHeight(p), true, true, sub);
                    if (sub.Count > 0)
                    {
                        var origEndAttach = or.EndAttachEntity;
                        var origEndSplit = or.EndSplitPosition;
                        var origEndIsNode = or.EndAttachIsNode;

                        or.Curve = sub[0];
                        if (sub.Count > 1)
                        {
                            or.EndAttachEntity = Unity.Entities.Entity.Null;
                            or.EndSplitPosition = 0f;
                            or.EndAttachIsNode = false;
                        }
                        newOuterList.Add(or);

                        for (int s = 1; s < sub.Count; s++)
                        {
                            var extra = or.Clone();
                            extra.Id = $"{or.Id}_sub_{s}";
                            extra.Curve = sub[s];
                            extra.StartAttachEntity = Unity.Entities.Entity.Null;
                            extra.StartSplitPosition = 0f;
                            extra.StartAttachIsNode = false;

                            if (s == sub.Count - 1)
                            {
                                extra.EndAttachEntity = origEndAttach;
                                extra.EndSplitPosition = origEndSplit;
                                extra.EndAttachIsNode = origEndIsNode;
                            }
                            else
                            {
                                extra.EndAttachEntity = Unity.Entities.Entity.Null;
                                extra.EndSplitPosition = 0f;
                                extra.EndAttachIsNode = false;
                            }
                            newOuterList.Add(extra);
                        }
                    }
                    else
                    {
                        newOuterList.Add(or);
                    }
                }
                else
                {
                    newOuterList.Add(or);
                }
            }
            plan.OuterReplacementCourses.Clear();
            plan.OuterReplacementCourses.AddRange(newOuterList);

            // 4. AASHTO Lane Balance & Prefab Resolution
            LaneBalanceSolver.ResolveLanesAndPrefabs(
                plan.Courses,
                plan.Legs,
                plan.Config,
                m_PrefabCatalogSystem);

            // 5. Aggregate engineering telemetry
            float worstGrade = 0f;
            float tightestRadius = 9999f;
            float lowestClearance = 99f;
            int violations = 0;
            float totalLen = 0f;

            for (int i = 0; i < plan.Courses.Count; i++)
            {
                var c = plan.Courses[i];
                totalLen += c.LengthMeters;
                if (c.MaxGradePercent > worstGrade) worstGrade = c.MaxGradePercent;
                if (c.MinRadiusMeters < tightestRadius) tightestRadius = c.MinRadiusMeters;
                if (c.StackLevel > 0 && c.MinClearanceMeters < lowestClearance) lowestClearance = c.MinClearanceMeters;
                if (c.Compliance == ComplianceLevel.Violation) violations++;
            }

            plan.WorstGradePercent = worstGrade;
            plan.TightestRadiusMeters = tightestRadius;
            plan.MinClearanceMeters = lowestClearance;
            plan.ViolationCount = violations;
            plan.EstimatedCost = (int)(totalLen * 450f + plan.Courses.Count * 12000f);

            CurrentPlan = plan;

            // Generate elevation profile for active course
            ComputeElevationProfileForSelected();

            // Request live 3D ghost preview if enabled
            if (LiveGhostPreviewEnabled)
            {
                m_BuildService.RequestGhostPreview(CurrentPlan);
            }
            else
            {
                m_BuildService.ClearGhostPreview();
            }
        }

        private void ComputeElevationProfileForSelected()
        {
            if (CurrentPlan == null || CurrentPlan.Courses.Count == 0) return;

            PlannedNetCourse target = null;
            if (!string.IsNullOrEmpty(CurrentPlan.SelectedCourseForProfileId))
            {
                for (int i = 0; i < CurrentPlan.Courses.Count; i++)
                {
                    if (CurrentPlan.Courses[i].Id == CurrentPlan.SelectedCourseForProfileId)
                    {
                        target = CurrentPlan.Courses[i];
                        break;
                    }
                }
            }

            if (target == null)
            {
                target = CurrentPlan.Courses[0];
                CurrentPlan.SelectedCourseForProfileId = target.Id;
            }

            CurrentPlan.ActiveElevationProfile.Clear();
            float len = target.LengthMeters;
            const int kSamples = 20;

            for (int k = 0; k <= kSamples; k++)
            {
                float t = (float)k / kSamples;
                float3 pt = CubicBezier3D.Position(target.Curve, t);
                float terrainY = m_BuildService.SampleTerrainHeight(pt.xz);
                float grade = 0f;
                float3 tan = CubicBezier3D.Tangent(target.Curve, t);
                float hLen = math.length(tan.xz);
                if (hLen > 1e-4f) grade = (math.abs(tan.y) / hLen) * 100f;

                CurrentPlan.ActiveElevationProfile.Add(new ElevationProfileSample
                {
                    Distance = t * len,
                    RoadElevation = pt.y,
                    TerrainElevation = terrainY,
                    GradePercent = grade,
                    Clearance = target.StackLevel > 0 ? pt.y - terrainY : 0f
                });
            }
        }

        public void AutoFitFootprint()
        {
            if ((Stage != ToolStage.ConfigureInterchange && Stage != ToolStage.SelectHighways) || CurrentLegs.Count < 3) return;

            float bestR = CurrentConfig.FootprintRadius;
            int bestViolations = CurrentPlan != null ? CurrentPlan.ViolationCount : 999;
            float bestWorstGrade = CurrentPlan != null ? CurrentPlan.WorstGradePercent : 999f;

            bool prevGhost = LiveGhostPreviewEnabled;
            LiveGhostPreviewEnabled = false;

            float startR = math.clamp(math.min(200f, CurrentConfig.FootprintRadius), 160f, 480f);
            for (float r = startR; r <= 480f; r += 25f)
            {
                CurrentConfig.FootprintRadius = r;
                RegeneratePlan();
                if (CurrentPlan != null)
                {
                    if (CurrentPlan.ViolationCount < bestViolations ||
                       (CurrentPlan.ViolationCount == bestViolations && CurrentPlan.WorstGradePercent < bestWorstGrade))
                    {
                        bestViolations = CurrentPlan.ViolationCount;
                        bestWorstGrade = CurrentPlan.WorstGradePercent;
                        bestR = r;
                    }
                    if (CurrentPlan.ViolationCount == 0 && CurrentPlan.WorstGradePercent <= 6.0f)
                    {
                        break;
                    }
                }
            }

            CurrentConfig.FootprintRadius = bestR;
            LiveGhostPreviewEnabled = prevGhost;

            if (Stage == ToolStage.ConfigureInterchange)
            {
                RegeneratePlan();
            }
            else if (Stage == ToolStage.SelectHighways)
            {
                m_BuildService.ClearGhostPreview();
                CurrentPlan = null;
                HighwayPairingSolver.RefreshLegsForRadius(
                    EntityManager,
                    m_NetSearchSystem,
                    CurrentLegs,
                    CurrentConfig.Center,
                    CurrentConfig.FootprintRadius,
                    CurrentConfig.LeftHandTraffic,
                    m_PrefabSystem);
            }
        }

        public bool BuildCurrentInterchange()
        {
            if (CurrentPlan == null || m_BuildService.IsBuilding) return false;

            Stage = ToolStage.Building;
            bool started = m_BuildService.StartBuild(CurrentPlan);
            if (started)
            {
                Stage = ToolStage.PickCenter;
                CurrentLegs.Clear();
                CurrentPlan = null;
            }
            return started;
        }

        public bool UndoLastInterchange()
        {
            if (m_BuildService.IsBuilding)
            {
                return false;
            }

            // 1. If currently previewing/configuring in the tool, step back to SelectHighways
            if (Stage == ToolStage.ConfigureInterchange)
            {
                m_BuildService.ClearGhostPreview();
                CurrentPlan = null;
                SetStage(ToolStage.SelectHighways);
                return true;
            }

            // 2. If in SelectHighways and legs have been picked, remove the most recent leg or back to PickCenter
            if (Stage == ToolStage.SelectHighways)
            {
                if (CurrentLegs.Count > 0)
                {
                    CurrentLegs.RemoveAt(CurrentLegs.Count - 1);
                    return true;
                }
                SetStage(ToolStage.PickCenter);
                return true;
            }

            // 3. Otherwise, undo the most recently built interchange in the city
            bool ok = m_GroupSystem.UndoLastReceipt(out var undone);
            if (ok && undone != null && undone.SavedConfig != null)
            {
                // Restore configuration for Re-Edit
                CurrentConfig = undone.SavedConfig.Clone();
                CurrentLegs.Clear();
                CurrentLegs.AddRange(undone.SavedLegs);
                SetStage(ToolStage.ConfigureInterchange);
            }
            return ok;
        }

        public override void InitializeRaycast()
        {
            base.InitializeRaycast();
            if (Stage == ToolStage.PickCenter)
            {
                m_ToolRaycastSystem.typeMask = TypeMask.Terrain | TypeMask.Net;
                m_ToolRaycastSystem.netLayerMask = Layer.Road;
                m_ToolRaycastSystem.collisionMask = CollisionMask.OnGround | CollisionMask.Overground;
            }
            else if (Stage == ToolStage.SelectHighways)
            {
                m_ToolRaycastSystem.typeMask = TypeMask.Net | TypeMask.Terrain;
                m_ToolRaycastSystem.netLayerMask = Layer.Road;
                m_ToolRaycastSystem.collisionMask = CollisionMask.OnGround | CollisionMask.Overground;
            }
            else
            {
                m_ToolRaycastSystem.typeMask = TypeMask.Terrain;
                m_ToolRaycastSystem.collisionMask = CollisionMask.OnGround;
            }
        }

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_OverlayRenderSystem = World.GetOrCreateSystemManaged<OverlayRenderSystem>();
            m_NetSearchSystem = World.GetOrCreateSystemManaged<SearchSystem>();
            m_PrefabCatalogSystem = World.GetOrCreateSystemManaged<HighwayPrefabCatalogSystem>();
            m_BuildService = World.GetOrCreateSystemManaged<InterchangeBuildService>();
            m_GroupSystem = World.GetOrCreateSystemManaged<InterchangeGroupSystem>();
            m_CityConfigSystem = World.GetOrCreateSystemManaged<CityConfigurationSystem>();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_VisibleTempIconQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Notifications.Icon>(), ComponentType.ReadOnly<Temp>() },
                None = new[] { ComponentType.ReadOnly<Hidden>(), ComponentType.ReadOnly<Deleted>() }
            });
        }

        [Preserve]
        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            DrainPendingUIActions();

            applyMode = RequestedApplyMode;
            RequestedApplyMode = ApplyMode.None;

            if (LiveGhostPreviewEnabled && !m_VisibleTempIconQuery.IsEmptyIgnoreFilter)
            {
                EntityManager.AddComponent<Hidden>(m_VisibleTempIconQuery);
            }

            var mouse = Mouse.current;
            var kb = Keyboard.current;
            bool overWorld = InputManager.instance != null && InputManager.instance.controlOverWorld && !InputManager.instance.hasInputFieldFocus;

            m_HasHover = GetRaycastResult(out Entity hitEntity, out RaycastHit hit);
            if (m_HasHover)
            {
                m_HoverPoint = hit.m_HitPosition;
                m_HoveredEntity = hitEntity;
            }

            bool leftClick = (mouse != null && mouse.leftButton.wasPressedThisFrame && overWorld) || (applyAction != null && applyAction.WasPressedThisFrame());
            bool rightClick = (mouse != null && mouse.rightButton.wasPressedThisFrame && overWorld) || (secondaryApplyAction != null && secondaryApplyAction.WasPressedThisFrame());
            bool enterKey = kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame);
            bool shiftHeld = kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);
            bool undoKey = (Mod.m_UndoAction != null && Mod.m_UndoAction.WasPerformedThisFrame()) ||
                           (kb != null && (kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed) && kb.zKey.wasPressedThisFrame);

            if (undoKey)
            {
                UndoLastInterchange();
                return inputDeps;
            }

            // Footprint radius keyboard / mouse adjustments ([ / ] or Alt + scroll)
            if (kb != null)
            {
                if (kb.leftBracketKey.wasPressedThisFrame)
                {
                    CurrentConfig.FootprintRadius = math.clamp(CurrentConfig.FootprintRadius - 15f, 120f, 500f);
                    if (Stage == ToolStage.ConfigureInterchange) RegeneratePlan();
                    else if (Stage == ToolStage.SelectHighways && CurrentLegs.Count > 0)
                    {
                        HighwayPairingSolver.RefreshLegsForRadius(EntityManager, m_NetSearchSystem, CurrentLegs, CurrentConfig.Center, CurrentConfig.FootprintRadius, CurrentConfig.LeftHandTraffic, m_PrefabSystem);
                    }
                }
                else if (kb.rightBracketKey.wasPressedThisFrame)
                {
                    CurrentConfig.FootprintRadius = math.clamp(CurrentConfig.FootprintRadius + 15f, 120f, 500f);
                    if (Stage == ToolStage.ConfigureInterchange) RegeneratePlan();
                    else if (Stage == ToolStage.SelectHighways && CurrentLegs.Count > 0)
                    {
                        HighwayPairingSolver.RefreshLegsForRadius(EntityManager, m_NetSearchSystem, CurrentLegs, CurrentConfig.Center, CurrentConfig.FootprintRadius, CurrentConfig.LeftHandTraffic, m_PrefabSystem);
                    }
                }
            }
            if (mouse != null && kb != null && (kb.leftAltKey.isPressed || kb.rightAltKey.isPressed))
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (math.abs(scroll) > 0.01f)
                {
                    CurrentConfig.FootprintRadius = math.clamp(CurrentConfig.FootprintRadius + math.sign(scroll) * 15f, 120f, 500f);
                    if (Stage == ToolStage.ConfigureInterchange) RegeneratePlan();
                    else if (Stage == ToolStage.SelectHighways && CurrentLegs.Count > 0)
                    {
                        HighwayPairingSolver.RefreshLegsForRadius(EntityManager, m_NetSearchSystem, CurrentLegs, CurrentConfig.Center, CurrentConfig.FootprintRadius, CurrentConfig.LeftHandTraffic, m_PrefabSystem);
                    }
                }
            }

            // Handle Navigation & State Transitions
            if (rightClick)
            {
                if (Stage == ToolStage.ConfigureInterchange) SetStage(ToolStage.SelectHighways);
                else if (Stage == ToolStage.SelectHighways) SetStage(ToolStage.PickCenter);
                else if (Stage == ToolStage.PickCenter)
                {
                    // Exit tool
                    m_ToolSystem.activeTool = World.GetOrCreateSystemManaged<DefaultToolSystem>();
                    return inputDeps;
                }
            }

            if (leftClick && m_HasHover)
            {
                if (Stage == ToolStage.PickCenter)
                {
                    CurrentConfig.Center = m_HoverPoint;
                    AutoDetectHighways();
                    SetStage(ToolStage.SelectHighways);
                }
                else if (Stage == ToolStage.SelectHighways)
                {
                    if (m_HoveredEntity != Entity.Null && EntityManager.HasComponent<Edge>(m_HoveredEntity))
                    {
                        bool lht = m_CityConfigSystem != null && m_CityConfigSystem.leftHandTraffic;
                        float r = CurrentConfig.FootprintRadius;
                        float oldR = r;
                        var clickedLegs = HighwayPairingSolver.PickAndPairThroughHighway(
                            EntityManager,
                            m_NetSearchSystem,
                            m_HoveredEntity,
                            CurrentConfig.Center,
                            ref r,
                            lht,
                            autoPairTwin: !shiftHeld,
                            m_PrefabSystem);
                        CurrentConfig.FootprintRadius = r;
                        if (r != oldR && CurrentLegs.Count > 0)
                        {
                            HighwayPairingSolver.RefreshLegsForRadius(
                                EntityManager,
                                m_NetSearchSystem,
                                CurrentLegs,
                                CurrentConfig.Center,
                                CurrentConfig.FootprintRadius,
                                lht,
                                m_PrefabSystem);
                        }

                        if (clickedLegs != null && clickedLegs.Count > 0)
                        {
                            for (int l = 0; l < clickedLegs.Count; l++)
                            {
                                var leg = clickedLegs[l];
                                int existingIdx = -1;
                                for (int i = 0; i < CurrentLegs.Count; i++)
                                {
                                    if (math.abs(math.acos(math.clamp(math.dot(CurrentLegs[i].LegAxisDirection.xz, leg.LegAxisDirection.xz), -1f, 1f))) < 0.25f)
                                    {
                                        existingIdx = i;
                                        break;
                                    }
                                }

                                if (existingIdx >= 0)
                                {
                                    CurrentLegs.RemoveAt(existingIdx);
                                }
                                else
                                {
                                    leg.LegIndex = CurrentLegs.Count;
                                    CurrentLegs.Add(leg);
                                }
                            }

                            // Re-index
                            CurrentLegs.Sort((a, b) => a.AzimuthRad.CompareTo(b.AzimuthRad));
                            for (int i = 0; i < CurrentLegs.Count; i++) CurrentLegs[i].LegIndex = i;
                        }
                    }
                }
            }

            if (enterKey)
            {
                if (Stage == ToolStage.SelectHighways && CurrentLegs.Count >= 3)
                {
                    SetStage(ToolStage.ConfigureInterchange);
                }
                else if (Stage == ToolStage.ConfigureInterchange)
                {
                    BuildCurrentInterchange();
                }
            }

            // Render 3D Engineering Overlay
            RenderOverlay();

            return inputDeps;
        }
    }
}
