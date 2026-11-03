using System.Collections.Generic;
using Unity.Mathematics;

namespace ProceduralInterchanges.Core.Models
{
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
        public float MinBridgeClearanceMeters = 9.5f;    // Overpass vertical clearance (game min 8.0m elevated threshold, default 9.5m)
        public float DesignSpeedKmh = 100f;              // Highway design speed km/h
        public bool LeftHandTraffic = false;             // Synchronized with CityConfigurationSystem
        public bool GradeSeparateMainline = true;
        public bool MainlineOverpass = false;

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
                DesignSpeedKmh = DesignSpeedKmh,
                LeftHandTraffic = LeftHandTraffic,
                GradeSeparateMainline = GradeSeparateMainline,
                MainlineOverpass = MainlineOverpass,
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
