using System.Collections.Generic;
using Unity.Mathematics;

namespace ProceduralInterchanges.Core.Models
{
    public enum UndergroundMode
    {
        None = 0,                      // Standard surface (Level 0) and elevated bridges (Levels +1, +2, +3)
        DepressSecondaryThruway = 1,   // Secondary mainline at Level -1 (-10m tunnel), Primary at Level 0, ramps at 0/+1
        ShiftStackDown = 2,            // Entire stack shifted down 1 level: Primary -1, Secondary 0, Flyovers +1/+2
        BothThruwaysUnderground = 3    // Primary at Level -1, Secondary at Level -2, all ramps at Level 0/+1
    }

    public enum FlowPriorityMode
    {
        MaintainThruLanes = 0,         // Keep 100% of mainline lanes through diverge/merge; peel-off exit ramp (1-2L)
        LaneDropSplit = 1              // Drop 1 lane at diverge (e.g., 3L -> 2L mainline + 1L/2L exit) and restore at merge
    }

    public sealed class RampOverride
    {
        public int LaneCount;
        public string PrefabName;
        public RoadUpgradeFlags Upgrades;
    }

    public sealed class InterchangeConfig
    {
        public float3 Center;
        public float3 CenterPosition { get => Center; set => Center = value; }
        public float FootprintRadius = 260f;            // 120m to 500m
        public float RotationOffsetRad = 0f;
        public InterchangeArchetype Archetype = InterchangeArchetype.Diamond;

        // Engineering Standards & Constraints
        public float MaxMainlineGradePercent = 4.0f;     // AASHTO standard: 4% (game max slope steepness is up to 20%)
        public float MaxRampGradePercent = 6.0f;         // AASHTO standard: 6%
        public float MinBridgeClearanceMeters = 7.5f;    // Overpass vertical clearance (default 7.5m, range 6.0m - 15.0m)
        public float TunnelDepthMeters = 10.0f;          // Standard depth per underground level (-10m Level -1, -18m Level -2)
        public float DesignSpeedKmh = 100f;              // Highway design speed km/h
        public bool LeftHandTraffic = false;             // Synchronized with CityConfigurationSystem
        public bool GradeSeparateMainline = true;
        public bool MainlineOverpass = false;

        // Flow Priority & Multi-Level / Underground Modes
        public UndergroundMode Underground = UndergroundMode.None;
        public FlowPriorityMode FlowPriority = FlowPriorityMode.MaintainThruLanes;
        public int PrimaryAxisOffset = 0;                // 0 = auto-detected best collinear pair; 1, 2 = user-cycled primary leg pair

        // Global and per-leg / per-ramp overrides
        public int GlobalMainlineLanesOverride = 0;      // 0 = auto-balance
        public int GlobalRampLanesOverride = 0;          // 0 = auto-balance
        public string GlobalMainlinePrefabOverride = "";
        public string GlobalRampPrefabOverride = "";
        public RoadUpgradeFlags GlobalUpgrades = RoadUpgradeFlags.StreetLighting;

        public Dictionary<string, RampOverride> PerCourseOverrides = new Dictionary<string, RampOverride>();

        public InterchangeConfig Clone()
        {
            var copy = new InterchangeConfig
            {
                Center = Center,
                FootprintRadius = FootprintRadius,
                RotationOffsetRad = RotationOffsetRad,
                Archetype = Archetype,
                MaxMainlineGradePercent = MaxMainlineGradePercent,
                MaxRampGradePercent = MaxRampGradePercent,
                MinBridgeClearanceMeters = MinBridgeClearanceMeters,
                TunnelDepthMeters = TunnelDepthMeters,
                DesignSpeedKmh = DesignSpeedKmh,
                LeftHandTraffic = LeftHandTraffic,
                GradeSeparateMainline = GradeSeparateMainline,
                MainlineOverpass = MainlineOverpass,
                Underground = Underground,
                FlowPriority = FlowPriority,
                PrimaryAxisOffset = PrimaryAxisOffset,
                GlobalMainlineLanesOverride = GlobalMainlineLanesOverride,
                GlobalRampLanesOverride = GlobalRampLanesOverride,
                GlobalMainlinePrefabOverride = GlobalMainlinePrefabOverride,
                GlobalRampPrefabOverride = GlobalRampPrefabOverride,
                GlobalUpgrades = GlobalUpgrades
            };

            foreach (var kvp in PerCourseOverrides)
            {
                copy.PerCourseOverrides[kvp.Key] = new RampOverride
                {
                    LaneCount = kvp.Value.LaneCount,
                    PrefabName = kvp.Value.PrefabName,
                    Upgrades = kvp.Value.Upgrades
                };
            }

            return copy;
        }
    }
}
