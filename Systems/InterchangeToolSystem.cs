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
using ProceduralInterchanges.Core.Templates;
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
        public ToolStage Stage { get; private set; } = ToolStage.SelectHighways;
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
            Stage = ToolStage.SelectHighways;
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

            int requiredLegs = ProceduralInterchanges.Core.Templates.WorkshopTemplateLibrary.GetTemplate(CurrentConfig.Archetype).RequiredLegs;
            if (CurrentLegs.Count > 0)
            {
                HighwayPairingSolver.RecomputeCenterAndSortLegs(
                    EntityManager,
                    CurrentLegs,
                    CurrentConfig,
                    0f,
                    p => m_BuildService != null ? m_BuildService.SampleTerrainHeight(p) : 0f);
            }

            if (CurrentLegs.Count == requiredLegs)
            {
                Stage = ToolStage.ConfigureInterchange;
                RegeneratePlan();
            }
            else
            {
                Stage = ToolStage.SelectHighways;
                CurrentPlan = null;
                m_BuildService.ClearGhostPreview();
            }
        }

        public void SetArchetype(InterchangeArchetype archetype)
        {
            CurrentConfig.Archetype = archetype;
            int requiredLegs = ProceduralInterchanges.Core.Templates.WorkshopTemplateLibrary.GetTemplate(archetype).RequiredLegs;
            if (CurrentLegs.Count > requiredLegs)
            {
                CurrentLegs.RemoveRange(requiredLegs, CurrentLegs.Count - requiredLegs);
            }
            if (CurrentLegs.Count == requiredLegs)
            {
                Stage = ToolStage.ConfigureInterchange;
                RegeneratePlan();
            }
            else
            {
                Stage = ToolStage.SelectHighways;
                CurrentPlan = null;
                m_BuildService.ClearGhostPreview();
            }
        }

        public void SetGlobalParam(string key, float value)
        {
            switch (key.ToLowerInvariant())
            {
                case "lefthandtraffic":
                    CurrentConfig.LeftHandTraffic = value >= 0.5f;
                    break;
                case "radius":
                case "footprintradius":
                    CurrentConfig.FootprintRadius = math.clamp(value, 120f, 500f);
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
                case "undergroundmode":
                case "underground":
                    CurrentConfig.Underground = (UndergroundMode)math.clamp((int)value, 0, 3);
                    break;
                case "flowprioritymode":
                case "flowpriority":
                    CurrentConfig.FlowPriority = (FlowPriorityMode)math.clamp((int)value, 0, 1);
                    break;
                case "primaryaxisoffset":
                case "swapprimary":
                    CurrentConfig.PrimaryAxisOffset = (int)value;
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
            var template = ProceduralInterchanges.Core.Templates.WorkshopTemplateLibrary.GetTemplate(CurrentConfig.Archetype);
            int requiredLegs = template.RequiredLegs;

            if (CurrentLegs.Count < requiredLegs)
            {
                CurrentPlan = null;
                m_BuildService.ClearGhostPreview();
                return;
            }

            if (CurrentLegs.Count > requiredLegs)
            {
                CurrentLegs.RemoveRange(requiredLegs, CurrentLegs.Count - requiredLegs);
            }

            HighwayPairingSolver.RecomputeCenterAndSortLegs(
                EntityManager,
                CurrentLegs,
                CurrentConfig,
                0f,
                p => m_BuildService != null ? m_BuildService.SampleTerrainHeight(p) : 0f);

            var plan = new InterchangePlan
            {
                Config = CurrentConfig.Clone(),
                Legs = new List<HighwayLeg>(CurrentLegs)
            };

            // 1. Solve trim-and-splice boundary cuts & outer replacements
            TrimAndSpliceSolver.ComputeBoundaryCutsAndOuterReplacements(
                EntityManager,
                plan.Legs,
                plan.Config.Center,
                plan.Config.FootprintRadius,
                p => m_BuildService != null ? m_BuildService.SampleTerrainHeight(p) : 0f,
                plan.OuterReplacementCourses,
                plan.EdgesToDelete,
                plan.RestorableSnapshots);

            // 2. Generation Engine (RL-Driven Substructure Template Solver or Legacy Parametric)
            bool useLegacy = Mod.InstanceSetting != null && Mod.InstanceSetting.UseLegacyParametricGenerators;
            if (!useLegacy && template != null)
            {
                TemplateMorphSolver.GenerateMorphedPlan(
                    template,
                    plan.Config,
                    plan.Legs,
                    m_PrefabCatalogSystem,
                    plan,
                    customAction32: null,
                    p => m_BuildService != null ? m_BuildService.SampleTerrainHeight(p) : 0f);
            }
            else
            {
                var generator = InterchangeGeneratorRegistry.GetGenerator(plan.Config.Archetype);
                if (generator != null && generator.SupportsConfiguration(plan.Legs, out _))
                {
                    generator.GenerateGeometry(plan.Config, plan.Legs, plan.Courses);
                }
                else
                {
                    var fallbackGen = InterchangeGeneratorRegistry.GetGenerator(InterchangeArchetype.Diamond);
                    if (fallbackGen != null && fallbackGen.SupportsConfiguration(plan.Legs, out _))
                    {
                        fallbackGen.GenerateGeometry(plan.Config, plan.Legs, plan.Courses);
                    }
                }
            }

            // 3. AASHTO Lane Balance & Prefab Resolution if needed
            LaneBalanceSolver.ResolveLanesAndPrefabs(
                plan.Courses,
                plan.Legs,
                plan.Config,
                m_PrefabCatalogSystem);

            // 3b. Solve 3D Elevations, Vertical Clearances, and Bridge Subdivisions
            ClearanceAndPillarSolver.SolveElevationsAndSubdivide(
                plan.Courses,
                p => m_BuildService != null ? m_BuildService.SampleTerrainHeight(p) : 0f,
                plan.Config);

            // 4. Aggregate telemetry
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
                Stage = ToolStage.SelectHighways;
                CurrentLegs.Clear();
                CurrentPlan = null;
            }
            return started;
        }

        public bool UndoLastInterchange()
        {
            if (m_BuildService != null && m_BuildService.IsBusy)
            {
                Mod.log.Warn("Cannot undo: build service is busy.");
                return false;
            }

            if (m_GroupSystem != null && m_GroupSystem.UndoLastReceipt(out var undone))
            {
                if (undone != null)
                {
                    m_BuildService.ScheduleUndo(undone);
                }
                CurrentLegs.Clear();
                CurrentPlan = null;
                Stage = ToolStage.SelectHighways;
                return true;
            }

            return false;
        }

        public override void InitializeRaycast()
        {
            base.InitializeRaycast();
            m_ToolRaycastSystem.typeMask = TypeMask.Terrain | TypeMask.Net;
            m_ToolRaycastSystem.netLayerMask = Layer.Road;
            m_ToolRaycastSystem.collisionMask = CollisionMask.OnGround | CollisionMask.Overground;
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
                if (Stage == ToolStage.ConfigureInterchange)
                {
                    if (CurrentLegs.Count > 0)
                    {
                        CurrentLegs.RemoveAt(CurrentLegs.Count - 1);
                        HighwayPairingSolver.RecomputeCenterAndSortLegs(
                            EntityManager,
                            CurrentLegs,
                            CurrentConfig,
                            0f,
                            p => m_BuildService != null ? m_BuildService.SampleTerrainHeight(p) : 0f);
                    }
                    Stage = ToolStage.SelectHighways;
                    CurrentPlan = null;
                    m_BuildService.ClearGhostPreview();
                    return inputDeps;
                }
                else if (Stage == ToolStage.SelectHighways)
                {
                    if (CurrentLegs.Count > 0)
                    {
                        CurrentLegs.RemoveAt(CurrentLegs.Count - 1);
                        HighwayPairingSolver.RecomputeCenterAndSortLegs(
                            EntityManager,
                            CurrentLegs,
                            CurrentConfig,
                            0f,
                            p => m_BuildService != null ? m_BuildService.SampleTerrainHeight(p) : 0f);
                        return inputDeps;
                    }
                    else
                    {
                        // Exit tool
                        m_ToolSystem.activeTool = World.GetOrCreateSystemManaged<DefaultToolSystem>();
                        return inputDeps;
                    }
                }
            }

            if (leftClick && m_HasHover)
            {
                const int kMaxSelectableLegs = 4;

                if (m_HoveredEntity != Entity.Null && EntityManager.HasComponent<Edge>(m_HoveredEntity))
                {
                    int existingIdx = -1;
                    for (int i = 0; i < CurrentLegs.Count; i++)
                    {
                        if ((CurrentLegs[i].Inbound != null && CurrentLegs[i].Inbound.SourceEdge == m_HoveredEntity) ||
                            (CurrentLegs[i].Outbound != null && CurrentLegs[i].Outbound.SourceEdge == m_HoveredEntity))
                        {
                            existingIdx = i;
                            break;
                        }
                    }

                    if (existingIdx >= 0)
                    {
                        CurrentLegs.RemoveAt(existingIdx);
                    }
                    else if (CurrentLegs.Count < kMaxSelectableLegs)
                    {
                        var newLeg = HighwayPairingSolver.PickHighwayEndpointAtClick(
                            EntityManager,
                            m_NetSearchSystem,
                            m_PrefabSystem,
                            m_HoveredEntity,
                            m_HoverPoint,
                            CurrentConfig.LeftHandTraffic,
                            autoPairTwin: !shiftHeld);

                        if (newLeg != null)
                        {
                            CurrentLegs.Add(newLeg);
                        }
                    }

                    HighwayPairingSolver.RecomputeCenterAndSortLegs(
                        EntityManager,
                        CurrentLegs,
                        CurrentConfig,
                        0f,
                        p => m_BuildService != null ? m_BuildService.SampleTerrainHeight(p) : 0f);

                    if (CurrentLegs.Count == 4)
                    {
                        int curReq = ProceduralInterchanges.Core.Templates.WorkshopTemplateLibrary.GetTemplate(CurrentConfig.Archetype).RequiredLegs;
                        if (curReq != 4)
                        {
                            CurrentConfig.Archetype = InterchangeArchetype.Diamond;
                        }
                        Stage = ToolStage.ConfigureInterchange;
                        RegeneratePlan();
                    }
                    else if (CurrentLegs.Count == 3)
                    {
                        int curReq = ProceduralInterchanges.Core.Templates.WorkshopTemplateLibrary.GetTemplate(CurrentConfig.Archetype).RequiredLegs;
                        if (curReq != 3)
                        {
                            CurrentConfig.Archetype = InterchangeArchetype.Trumpet;
                        }
                        Stage = ToolStage.ConfigureInterchange;
                        RegeneratePlan();
                    }
                    else
                    {
                        Stage = ToolStage.SelectHighways;
                        CurrentPlan = null;
                        m_BuildService.ClearGhostPreview();
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
