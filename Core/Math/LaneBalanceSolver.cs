using System;
using System.Collections.Generic;
using ProceduralInterchanges.Core.Models;
using Unity.Entities;

namespace ProceduralInterchanges.Core.Math
{
    public interface IHighwayPrefabLookup
    {
        Entity FindBestHighwayPrefab(bool isOneWay, int forwardLanes, int backwardLanes, bool highwayRules, out string prefabName);
        Entity FindPrefabByName(string name);
    }

    public static class LaneBalanceSolver
    {
        public static void ResolveLanesAndPrefabs(
            List<PlannedNetCourse> courses,
            IReadOnlyList<HighwayLeg> legs,
            InterchangeConfig config,
            IHighwayPrefabLookup catalog)
        {
            if (courses == null || courses.Count == 0) return;

            int globalMainlineLanes = config.GlobalMainlineLanesOverride;
            int globalRampLanes = config.GlobalRampLanesOverride;

            for (int i = 0; i < courses.Count; i++)
            {
                var c = courses[i];

                // Determine base leg inbound lane count
                int legInboundLanes = 3;
                if (c.LegIndex >= 0 && legs != null && c.LegIndex < legs.Count && legs[c.LegIndex].Inbound != null && legs[c.LegIndex].Inbound.LaneCount > 0)
                {
                    legInboundLanes = legs[c.LegIndex].Inbound.LaneCount;
                }
                else if (legs != null && legs.Count > 0 && legs[0].Inbound != null && legs[0].Inbound.LaneCount > 0)
                {
                    legInboundLanes = legs[0].Inbound.LaneCount;
                }

                bool hasPerCourse = config.PerCourseOverrides.TryGetValue(c.Id, out var courseOverride) && courseOverride.LaneCount > 0;
                bool hasPerLeg = c.LegIndex >= 0 && legs != null && c.LegIndex < legs.Count &&
                    (legs[c.LegIndex].OverrideMainlineLanes > 0 || !string.IsNullOrEmpty(legs[c.LegIndex].OverrideMainlinePrefabName) || legs[c.LegIndex].Upgrades != 0);

                if (hasPerCourse)
                {
                    // 1. Per-Course Override (Highest precedence)
                    c.Lanes = courseOverride.LaneCount;
                    if (!string.IsNullOrEmpty(courseOverride.PrefabName))
                    {
                        c.ResolvedPrefabName = courseOverride.PrefabName;
                    }
                    c.Upgrades = courseOverride.Upgrades;
                }
                else if (hasPerLeg && (c.Role == NetRole.MainlineThrough || c.Role == NetRole.CrossStreet))
                {
                    // 2. Per-Leg Override
                    var leg = legs[c.LegIndex];
                    c.Lanes = leg.OverrideMainlineLanes > 0 ? leg.OverrideMainlineLanes : legInboundLanes;
                    if (!string.IsNullOrEmpty(leg.OverrideMainlinePrefabName))
                    {
                        c.ResolvedPrefabName = leg.OverrideMainlinePrefabName;
                    }
                    c.Upgrades = leg.Upgrades != 0 ? leg.Upgrades : config.GlobalUpgrades;
                }
                else
                {
                    // 3. Global Overrides / 4. AASHTO Lane Balance
                    if (c.Role == NetRole.MainlineThrough || c.Role == NetRole.CrossStreet)
                    {
                        if (globalMainlineLanes > 0)
                        {
                            c.Lanes = globalMainlineLanes;
                        }
                        else
                        {
                            // AASHTO Lane Balance: Core mainline between diverge and merge drops 1 lane if approach >= 3
                            if (config.FlowPriority == FlowPriorityMode.MaintainThruLanes)
                            {
                                c.Lanes = legInboundLanes;
                            }
                            else
                            {
                                if (c.Id.Contains("_core") && legInboundLanes >= 2)
                                {
                                    c.Lanes = Unity.Mathematics.math.max(1, legInboundLanes - 1);
                                }
                                else
                                {
                                    c.Lanes = legInboundLanes;
                                }
                            }
                        }

                        if (!string.IsNullOrEmpty(config.GlobalMainlinePrefabOverride))
                        {
                            c.ResolvedPrefabName = config.GlobalMainlinePrefabOverride;
                        }
                    }
                    else if (c.Role == NetRole.CollectorDistributor)
                    {
                        c.Lanes = 2;
                        if (!string.IsNullOrEmpty(config.GlobalRampPrefabOverride))
                        {
                            c.ResolvedPrefabName = config.GlobalRampPrefabOverride;
                        }
                    }
                    else
                    {
                        // Ramps (DirectionalRamp, LoopRamp, OuterSlipRamp)
                        c.Lanes = globalRampLanes > 0 ? globalRampLanes : 1;
                        if (!string.IsNullOrEmpty(config.GlobalRampPrefabOverride))
                        {
                            c.ResolvedPrefabName = config.GlobalRampPrefabOverride;
                        }
                    }

                    c.Upgrades = config.GlobalUpgrades;
                }

                // Prefab resolution via catalog
                if (catalog != null && c.ResolvedPrefabEntity == Entity.Null)
                {
                    // First attempt exact name resolution
                    if (!string.IsNullOrEmpty(c.ResolvedPrefabName))
                    {
                        Entity exact = catalog.FindPrefabByName(c.ResolvedPrefabName);
                        if (exact != Entity.Null)
                        {
                            c.ResolvedPrefabEntity = exact;
                        }
                    }

                    // Fallback to best matching highway prefab by lane count & one-way status
                    if (c.ResolvedPrefabEntity == Entity.Null)
                    {
                        Entity resolved = catalog.FindBestHighwayPrefab(
                            c.IsOneWay,
                            c.Lanes,
                            c.IsOneWay ? 0 : c.Lanes,
                            true,
                            out string matchedName);

                        c.ResolvedPrefabEntity = resolved;
                        if (string.IsNullOrEmpty(c.ResolvedPrefabName))
                        {
                            c.ResolvedPrefabName = matchedName;
                        }
                    }
                }
            }
        }
    }
}
