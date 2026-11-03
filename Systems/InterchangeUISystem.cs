using System;
using System.Collections.Generic;
using Colossal.UI.Binding;
using Game;
using Game.Tools;
using Game.UI;
using ProceduralInterchanges.Core.Generators;
using ProceduralInterchanges.Core.Math;
using ProceduralInterchanges.Core.Models;
using Unity.Entities;
using Unity.Mathematics;

namespace ProceduralInterchanges.Systems
{
    public partial class InterchangeUISystem : UISystemBase
    {
        public const string kGroup = "proceduralInterchanges";

        private ToolSystem m_ToolSystem;
        private InterchangeToolSystem m_InterchangeTool;
        private InterchangeGroupSystem m_GroupSystem;
        private HighwayPrefabCatalogSystem m_PrefabCatalogSystem;

        private RawValueBinding m_StateBinding;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            m_InterchangeTool = World.GetOrCreateSystemManaged<InterchangeToolSystem>();
            m_GroupSystem = World.GetOrCreateSystemManaged<InterchangeGroupSystem>();
            m_PrefabCatalogSystem = World.GetOrCreateSystemManaged<HighwayPrefabCatalogSystem>();

            AddBinding(m_StateBinding = new RawValueBinding(kGroup, "state", WriteState));
            AddUpdateBinding(m_StateBinding);

            AddBinding(new TriggerBinding(kGroup, "toggleTool", ToggleTool));
            AddBinding(new TriggerBinding<int>(kGroup, "setStage", SetStage));
            AddBinding(new TriggerBinding(kGroup, "autoDetectHighways", AutoDetectHighways));
            AddBinding(new TriggerBinding<int>(kGroup, "removeLeg", RemoveLeg));
            AddBinding(new TriggerBinding<int>(kGroup, "setArchetype", SetArchetype));
            AddBinding(new TriggerBinding<string, float>(kGroup, "setGlobalParam", SetGlobalParam));
            AddBinding(new TriggerBinding<string, string>(kGroup, "setGlobalPrefab", SetGlobalPrefab));
            AddBinding(new TriggerBinding<int, bool>(kGroup, "setGlobalUpgrade", SetGlobalUpgrade));
            AddBinding(new TriggerBinding<string>(kGroup, "selectCourseForProfile", SelectCourseForProfile));
            AddBinding(new TriggerBinding<int, int, string, int>(kGroup, "setLegOverride", SetLegOverride));
            AddBinding(new TriggerBinding<string, int, string, int>(kGroup, "setCourseOverride", SetCourseOverride));
            AddBinding(new TriggerBinding(kGroup, "buildInterchange", BuildInterchange));
            AddBinding(new TriggerBinding(kGroup, "undoLastBuild", UndoLastBuild));
            AddBinding(new TriggerBinding<bool>(kGroup, "setGhostEnabled", SetGhostEnabled));
            AddBinding(new TriggerBinding<bool>(kGroup, "setOverlayEnabled", SetOverlayEnabled));
            AddBinding(new TriggerBinding(kGroup, "autoFitFootprint", AutoFitFootprint));
        }

        protected override void OnUpdate()
        {
            base.OnUpdate();
            if (Mod.m_ToggleToolAction != null && Mod.m_ToggleToolAction.WasPerformedThisFrame())
            {
                ToggleTool();
            }
        }

        public void ToggleTool()
        {
            m_InterchangeTool.EnqueueUIAction(() =>
            {
                if (m_ToolSystem.activeTool == m_InterchangeTool)
                {
                    m_InterchangeTool.ClearAllStateAndExit();
                }
                else
                {
                    m_ToolSystem.activeTool = m_InterchangeTool;
                    m_InterchangeTool.SetStage(ToolStage.PickCenter);
                }
            });
        }

        public void SetStage(int stageInt)
        {
            m_InterchangeTool.EnqueueUIAction(() => m_InterchangeTool.SetStage((ToolStage)stageInt));
        }

        public void AutoDetectHighways()
        {
            m_InterchangeTool.EnqueueUIAction(() => m_InterchangeTool.AutoDetectHighways());
        }

        public void RemoveLeg(int legIndex)
        {
            m_InterchangeTool.EnqueueUIAction(() => m_InterchangeTool.RemoveLeg(legIndex));
        }

        public void SetArchetype(int archetypeId)
        {
            m_InterchangeTool.EnqueueUIAction(() => m_InterchangeTool.SetArchetype((InterchangeArchetype)archetypeId));
        }

        public void SetGlobalParam(string key, float value)
        {
            m_InterchangeTool.EnqueueUIAction(() => m_InterchangeTool.SetGlobalParam(key, value));
        }

        public void SetGlobalPrefab(string role, string prefabName)
        {
            m_InterchangeTool.EnqueueUIAction(() => m_InterchangeTool.SetGlobalPrefab(role, prefabName));
        }

        public void SetGlobalUpgrade(int flagBit, bool enabled)
        {
            m_InterchangeTool.EnqueueUIAction(() => m_InterchangeTool.SetGlobalUpgrade(flagBit, enabled));
        }

        public void SelectCourseForProfile(string courseId)
        {
            m_InterchangeTool.EnqueueUIAction(() => m_InterchangeTool.SelectCourseForProfile(courseId));
        }

        public void SetLegOverride(int legIndex, int lanes, string prefabName, int upgradesMask)
        {
            m_InterchangeTool.EnqueueUIAction(() => m_InterchangeTool.SetLegOverride(legIndex, lanes, prefabName, upgradesMask));
        }

        public void SetCourseOverride(string courseId, int lanes, string prefabName, int upgradesMask)
        {
            m_InterchangeTool.EnqueueUIAction(() => m_InterchangeTool.SetCourseOverride(courseId, lanes, prefabName, upgradesMask));
        }

        public void BuildInterchange()
        {
            m_InterchangeTool.EnqueueUIAction(() => m_InterchangeTool.BuildCurrentInterchange());
        }

        public void UndoLastBuild()
        {
            m_InterchangeTool.EnqueueUIAction(() => m_InterchangeTool.UndoLastInterchange());
        }

        public void SetGhostEnabled(bool enabled)
        {
            m_InterchangeTool.EnqueueUIAction(() =>
            {
                m_InterchangeTool.LiveGhostPreviewEnabled = enabled;
                if (!enabled)
                {
                    World.GetOrCreateSystemManaged<InterchangeBuildService>().ClearGhostPreview();
                }
                else if (m_InterchangeTool.Stage == ToolStage.ConfigureInterchange)
                {
                    m_InterchangeTool.RegeneratePlan();
                }
            });
        }

        public void SetOverlayEnabled(bool enabled)
        {
            m_InterchangeTool.EnqueueUIAction(() => m_InterchangeTool.EngineeringOverlayEnabled = enabled);
        }

        public void AutoFitFootprint()
        {
            m_InterchangeTool.EnqueueUIAction(() => m_InterchangeTool.AutoFitFootprint());
        }

        private void WriteState(IJsonWriter writer)
        {
            bool isActive = m_ToolSystem.activeTool == m_InterchangeTool;
            var config = m_InterchangeTool.CurrentConfig;
            var legs = m_InterchangeTool.CurrentLegs;
            var plan = m_InterchangeTool.CurrentPlan;

            writer.TypeBegin("ProceduralInterchanges.InterchangeUIState");

            writer.PropertyName("toolActive");
            writer.Write(isActive);

            writer.PropertyName("stage");
            writer.Write((int)m_InterchangeTool.Stage);

            writer.PropertyName("stageName");
            writer.Write(m_InterchangeTool.Stage.ToString());

            writer.PropertyName("center");
            writer.TypeBegin("float3");
            writer.PropertyName("x"); writer.Write(config.Center.x);
            writer.PropertyName("y"); writer.Write(config.Center.y);
            writer.PropertyName("z"); writer.Write(config.Center.z);
            writer.TypeEnd();

            writer.PropertyName("footprintRadius");
            writer.Write(config.FootprintRadius);

            writer.PropertyName("rotationDeg");
            writer.Write(math.degrees(config.RotationOffsetRad));

            writer.PropertyName("selectedArchetype");
            writer.Write((int)config.Archetype);

            writer.PropertyName("maxMainlineGrade");
            writer.Write(config.MaxMainlineGradePercent);

            writer.PropertyName("maxRampGrade");
            writer.Write(config.MaxRampGradePercent);

            writer.PropertyName("minClearance");
            writer.Write(config.MinBridgeClearanceMeters);

            writer.PropertyName("designSpeed");
            writer.Write(config.DesignSpeedKmh);

            writer.PropertyName("leftHandTraffic");
            writer.Write(config.LeftHandTraffic);

            writer.PropertyName("gradeSeparateMainline");
            writer.Write(config.GradeSeparateMainline);

            writer.PropertyName("mainlineOverpass");
            writer.Write(config.MainlineOverpass);

            writer.PropertyName("liveGhostEnabled");
            writer.Write(m_InterchangeTool.LiveGhostPreviewEnabled);

            writer.PropertyName("overlayEnabled");
            writer.Write(m_InterchangeTool.EngineeringOverlayEnabled);

            writer.PropertyName("canUndo");
            writer.Write(m_GroupSystem.CanUndo);

            // Available Archetypes
            writer.PropertyName("availableArchetypes");
            var archetypes = InterchangeGeneratorRegistry.All;
            writer.ArrayBegin(archetypes.Count);
            for (int i = 0; i < archetypes.Count; i++)
            {
                var g = archetypes[i];
                bool supported = g.SupportsConfiguration(legs, out string reason);
                writer.TypeBegin("ArchetypeInfo");
                writer.PropertyName("id"); writer.Write((int)g.Archetype);
                writer.PropertyName("name"); writer.Write(g.DisplayName);
                writer.PropertyName("description"); writer.Write(g.Description);
                writer.PropertyName("minLegs"); writer.Write(g.MinLegs);
                writer.PropertyName("maxLegs"); writer.Write(g.MaxLegs);
                writer.PropertyName("supported"); writer.Write(supported);
                writer.PropertyName("reason"); writer.Write(reason ?? string.Empty);
                writer.TypeEnd();
            }
            writer.ArrayEnd();

            // Legs
            writer.PropertyName("legs");
            writer.ArrayBegin(legs.Count);
            for (int i = 0; i < legs.Count; i++)
            {
                var leg = legs[i];
                writer.TypeBegin("LegInfo");
                writer.PropertyName("index"); writer.Write(leg.LegIndex);
                writer.PropertyName("azimuthDeg"); writer.Write(math.degrees(leg.AzimuthRad));
                writer.PropertyName("isDual"); writer.Write(leg.IsSeparatedDualCarriageway);
                writer.PropertyName("separation"); writer.Write(leg.CarriagewaySeparation);
                writer.PropertyName("inboundLanes"); writer.Write(leg.Inbound != null ? leg.Inbound.LaneCount : 0);
                writer.PropertyName("outboundLanes"); writer.Write(leg.Outbound != null ? leg.Outbound.LaneCount : 0);
                writer.PropertyName("overrideLanes"); writer.Write(leg.OverrideMainlineLanes);
                writer.PropertyName("overridePrefab"); writer.Write(leg.OverrideMainlinePrefabName ?? string.Empty);
                writer.PropertyName("upgrades"); writer.Write((uint)leg.Upgrades);
                writer.TypeEnd();
            }
            writer.ArrayEnd();

            // 2D SVG Plan Curves
            writer.PropertyName("courses2D");
            if (plan != null && plan.Courses != null)
            {
                writer.ArrayBegin(plan.Courses.Count);
                float R = math.max(10f, config.FootprintRadius);
                float3 cPt = config.Center;

                for (int i = 0; i < plan.Courses.Count; i++)
                {
                    var c = plan.Courses[i];
                    writer.TypeBegin("Course2D");
                    writer.PropertyName("id"); writer.Write(c.Id);
                    writer.PropertyName("role"); writer.Write((int)c.Role);
                    writer.PropertyName("roleName"); writer.Write(c.Role.ToString());
                    writer.PropertyName("stackLevel"); writer.Write(c.StackLevel);
                    writer.PropertyName("lanes"); writer.Write(c.Lanes);
                    writer.PropertyName("compliance"); writer.Write((int)c.Compliance);
                    writer.PropertyName("complianceMessage"); writer.Write(c.ComplianceMessage ?? string.Empty);
                    writer.PropertyName("maxGrade"); writer.Write(c.MaxGradePercent);
                    writer.PropertyName("minRadius"); writer.Write(c.MinRadiusMeters);
                    writer.PropertyName("minClearance"); writer.Write(c.MinClearanceMeters);
                    writer.PropertyName("prefabName"); writer.Write(c.ResolvedPrefabName ?? string.Empty);

                    // Normalized 2D coordinates in [-1, +1] range
                    writer.PropertyName("p0x"); writer.Write((c.Curve.a.x - cPt.x) / R);
                    writer.PropertyName("p0y"); writer.Write((c.Curve.a.z - cPt.z) / R);
                    writer.PropertyName("p1x"); writer.Write((c.Curve.b.x - cPt.x) / R);
                    writer.PropertyName("p1y"); writer.Write((c.Curve.b.z - cPt.z) / R);
                    writer.PropertyName("p2x"); writer.Write((c.Curve.c.x - cPt.x) / R);
                    writer.PropertyName("p2y"); writer.Write((c.Curve.c.z - cPt.z) / R);
                    writer.PropertyName("p3x"); writer.Write((c.Curve.d.x - cPt.x) / R);
                    writer.PropertyName("p3y"); writer.Write((c.Curve.d.z - cPt.z) / R);
                    writer.TypeEnd();
                }
                writer.ArrayEnd();
            }
            else
            {
                writer.ArrayBegin(0);
                writer.ArrayEnd();
            }

            // Elevation Profile
            writer.PropertyName("selectedCourseId");
            writer.Write(plan?.SelectedCourseForProfileId ?? string.Empty);

            writer.PropertyName("elevationProfile");
            if (plan != null && plan.ActiveElevationProfile != null)
            {
                writer.ArrayBegin(plan.ActiveElevationProfile.Count);
                for (int i = 0; i < plan.ActiveElevationProfile.Count; i++)
                {
                    var sample = plan.ActiveElevationProfile[i];
                    writer.TypeBegin("ElevationSample");
                    writer.PropertyName("distance"); writer.Write(sample.Distance);
                    writer.PropertyName("roadY"); writer.Write(sample.RoadElevation);
                    writer.PropertyName("terrainY"); writer.Write(sample.TerrainElevation);
                    writer.PropertyName("grade"); writer.Write(sample.GradePercent);
                    writer.PropertyName("clearance"); writer.Write(sample.Clearance);
                    writer.TypeEnd();
                }
                writer.ArrayEnd();
            }
            else
            {
                writer.ArrayBegin(0);
                writer.ArrayEnd();
            }

            // Telemetry
            writer.PropertyName("telemetry");
            writer.TypeBegin("Telemetry");
            writer.PropertyName("estimatedCost"); writer.Write(plan?.EstimatedCost ?? 0);
            writer.PropertyName("worstGrade"); writer.Write(plan?.WorstGradePercent ?? 0f);
            writer.PropertyName("tightestRadius"); writer.Write(plan?.TightestRadiusMeters ?? 0f);
            writer.PropertyName("lowestClearance"); writer.Write(plan?.MinClearanceMeters ?? 0f);
            writer.PropertyName("violationCount"); writer.Write(plan?.ViolationCount ?? 0);
            writer.TypeEnd();

            // Available Road Prefabs for dropdowns
            writer.PropertyName("availablePrefabs");
            var prefabs = m_PrefabCatalogSystem.GetAvailablePrefabs();
            writer.ArrayBegin(prefabs != null ? prefabs.Count : 0);
            if (prefabs != null)
            {
                for (int i = 0; i < prefabs.Count; i++)
                {
                    writer.Write(prefabs[i].Name);
                }
            }
            writer.ArrayEnd();

            writer.TypeEnd();
        }
    }
}
