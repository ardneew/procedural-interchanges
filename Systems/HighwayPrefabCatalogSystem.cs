using System;
using System.Collections.Generic;
using Game;
using Game.Net;
using Game.Prefabs;
using ProceduralInterchanges.Core.Math;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace ProceduralInterchanges.Systems
{
    public struct RoadPrefabInfo
    {
        public Entity PrefabEntity;
        public string Name;
        public bool IsOneWay;
        public bool IsHighwayRules;
        public int ForwardLanes;
        public int BackwardLanes;
        public float Width;
        public float SpeedLimit;
    }

    public partial class HighwayPrefabCatalogSystem : GameSystemBase, IHighwayPrefabLookup
    {
        private EntityQuery m_PrefabQuery;
        private PrefabSystem m_PrefabSystem;
        private readonly List<RoadPrefabInfo> m_Catalog = new List<RoadPrefabInfo>();
        private bool m_CatalogBuilt = false;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_PrefabQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<PrefabData>(),
                    ComponentType.ReadOnly<RoadData>(),
                    ComponentType.ReadOnly<NetGeometryData>()
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Game.Common.Deleted>(),
                    ComponentType.ReadOnly<BridgeData>(),
                    ComponentType.ReadOnly<MoveableBridgeData>()
                }
            });
        }

        protected override void OnUpdate()
        {
            if (!m_CatalogBuilt && m_PrefabQuery.CalculateEntityCount() > 0)
            {
                BuildCatalog();
            }
        }

        private void BuildCatalog()
        {
            m_Catalog.Clear();
            var entities = m_PrefabQuery.ToEntityArray(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                Entity e = entities[i];
                var roadData = EntityManager.GetComponentData<RoadData>(e);
                var geomData = EntityManager.GetComponentData<NetGeometryData>(e);

                string name = "Road";
                if (m_PrefabSystem.TryGetPrefab<PrefabBase>(e, out var prefabBase))
                {
                    name = prefabBase.name;
                }

                // Strictly exclude specialty bridges and landmarks
                if (EntityManager.HasComponent<BridgeData>(e) ||
                    EntityManager.HasComponent<MoveableBridgeData>(e) ||
                    EntityManager.HasBuffer<FixedNetElement>(e) ||
                    EntityManager.HasComponent<SignatureBuildingData>(e) ||
                    EntityManager.HasComponent<PlacedSignatureBuildingData>(e))
                {
                    continue;
                }

                string lowerName = name.ToLowerInvariant();
                if (lowerName.Contains("bridge") ||
                    lowerName.Contains("suspension") ||
                    lowerName.Contains("cable") ||
                    lowerName.Contains("extradosed") ||
                    lowerName.Contains("truss") ||
                    lowerName.Contains("goldengate") ||
                    lowerName.Contains("golden gate") ||
                    lowerName.Contains("sanfrancisco") ||
                    lowerName.Contains("san francisco") ||
                    lowerName.Contains("drawbridge") ||
                    lowerName.Contains("grand"))
                {
                    continue;
                }

                // Pure highways never permit zoning
                if ((roadData.m_Flags & Game.Prefabs.RoadFlags.EnableZoning) != 0)
                {
                    continue;
                }

                // Inspect whether this network contains pedestrian lanes or crosswalks
                bool hasPedestrianLanes = false;
                if (EntityManager.HasBuffer<NetGeometryComposition>(e))
                {
                    var compBuf = EntityManager.GetBuffer<NetGeometryComposition>(e);
                    for (int c = 0; c < compBuf.Length; c++)
                    {
                        Entity compEntity = compBuf[c].m_Composition;
                        if (compEntity != Entity.Null && EntityManager.Exists(compEntity))
                        {
                            if (EntityManager.HasComponent<NetCompositionCrosswalk>(compEntity))
                            {
                                hasPedestrianLanes = true;
                                break;
                            }
                            if (EntityManager.HasBuffer<NetCompositionLane>(compEntity))
                            {
                                var lanes = EntityManager.GetBuffer<NetCompositionLane>(compEntity);
                                for (int l = 0; l < lanes.Length; l++)
                                {
                                    if ((lanes[l].m_Flags & LaneFlags.Pedestrian) != 0)
                                    {
                                        hasPedestrianLanes = true;
                                        break;
                                    }
                                }
                            }
                        }
                        if (hasPedestrianLanes) break;
                    }
                }
                if (hasPedestrianLanes)
                {
                    continue;
                }

                bool isHighway = (roadData.m_Flags & Game.Prefabs.RoadFlags.UseHighwayRules) != 0;
                InspectRoadPrefabLanes(EntityManager, e, name, out int forwardLanes, out int backwardLanes, out bool isOneWay);

                m_Catalog.Add(new RoadPrefabInfo
                {
                    PrefabEntity = e,
                    Name = name,
                    IsOneWay = isOneWay,
                    IsHighwayRules = isHighway,
                    ForwardLanes = forwardLanes,
                    BackwardLanes = backwardLanes,
                    Width = geomData.m_DefaultWidth,
                    SpeedLimit = roadData.m_SpeedLimit
                });
            }

            entities.Dispose();
            m_CatalogBuilt = true;
        }

        public Entity FindBestHighwayPrefab(bool isOneWay, int forwardLanes, int backwardLanes, bool highwayRules, out string prefabName)
        {
            if (!m_CatalogBuilt && m_PrefabQuery.CalculateEntityCount() > 0)
            {
                BuildCatalog();
            }

            Entity bestEntity = Entity.Null;
            string bestName = "One-Way Highway";
            int bestScore = int.MaxValue;

            for (int i = 0; i < m_Catalog.Count; i++)
            {
                var info = m_Catalog[i];
                if (info.IsOneWay != isOneWay) continue;

                int laneDiff = math.abs(info.ForwardLanes - forwardLanes);
                int score = laneDiff * 10;
                if (info.IsHighwayRules == highwayRules) score -= 5;

                if (score < bestScore)
                {
                    bestScore = score;
                    bestEntity = info.PrefabEntity;
                    bestName = info.Name;
                }
            }

            if (bestEntity == Entity.Null && !isOneWay)
            {
                Entity fallback = FindPrefabByName("Two-Lane Two-Way Highway");
                if (fallback == Entity.Null) fallback = FindPrefabByName("Two-Lane Highway");
                if (fallback != Entity.Null)
                {
                    prefabName = "Two-Lane Two-Way Highway";
                    return fallback;
                }
            }

            prefabName = bestName;
            return bestEntity;
        }

        public Entity FindPrefabByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return Entity.Null;

            if (!m_CatalogBuilt && m_PrefabQuery.CalculateEntityCount() > 0)
            {
                BuildCatalog();
            }

            for (int i = 0; i < m_Catalog.Count; i++)
            {
                if (string.Equals(m_Catalog[i].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return m_Catalog[i].PrefabEntity;
                }
            }

            for (int i = 0; i < m_Catalog.Count; i++)
            {
                if (m_Catalog[i].Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return m_Catalog[i].PrefabEntity;
                }
            }

            return Entity.Null;
        }

        public IReadOnlyList<RoadPrefabInfo> GetAvailablePrefabs()
        {
            if (!m_CatalogBuilt && m_PrefabQuery.CalculateEntityCount() > 0)
            {
                BuildCatalog();
            }
            return m_Catalog;
        }

        public static bool InspectRoadPrefabLanes(
            EntityManager em,
            Entity prefabEntity,
            string name,
            out int forwardLanes,
            out int backwardLanes,
            out bool isOneWay)
        {
            forwardLanes = 0;
            backwardLanes = 0;
            isOneWay = true;

            if (prefabEntity == Entity.Null || !em.Exists(prefabEntity))
            {
                return InspectFallbackLanes(name, out forwardLanes, out backwardLanes, out isOneWay);
            }

            // 1. Authoritative check: NetGeometryComposition -> NetCompositionLane
            if (em.HasBuffer<NetGeometryComposition>(prefabEntity))
            {
                var compBuf = em.GetBuffer<NetGeometryComposition>(prefabEntity);
                Entity selectedComp = Entity.Null;

                for (int c = 0; c < compBuf.Length; c++)
                {
                    Entity compEntity = compBuf[c].m_Composition;
                    if (compEntity == Entity.Null || !em.Exists(compEntity)) continue;

                    if (em.HasComponent<NetCompositionData>(compEntity))
                    {
                        var compData = em.GetComponentData<NetCompositionData>(compEntity);
                        var general = compData.m_Flags.m_General;
                        if ((general & (CompositionFlags.General.Node | CompositionFlags.General.Elevated | CompositionFlags.General.Tunnel | CompositionFlags.General.MiddlePlatform)) == 0)
                        {
                            selectedComp = compEntity;
                            break;
                        }
                    }

                    if (selectedComp == Entity.Null && em.HasBuffer<NetCompositionLane>(compEntity))
                    {
                        selectedComp = compEntity;
                    }
                }

                if (selectedComp != Entity.Null && em.HasBuffer<NetCompositionLane>(selectedComp))
                {
                    var lanes = em.GetBuffer<NetCompositionLane>(selectedComp);
                    for (int l = 0; l < lanes.Length; l++)
                    {
                        var lane = lanes[l];
                        if ((lane.m_Flags & (LaneFlags.Master | LaneFlags.Road)) == LaneFlags.Road && (lane.m_Flags & LaneFlags.Slave) == 0)
                        {
                            if ((lane.m_Flags & LaneFlags.Invert) == 0)
                                forwardLanes++;
                            else
                                backwardLanes++;
                        }
                    }

                    if (forwardLanes > 0 || backwardLanes > 0)
                    {
                        forwardLanes = math.max(1, forwardLanes);
                        isOneWay = (backwardLanes == 0);
                        return true;
                    }
                }
            }

            // 2. Fallback check for DefaultNetLane buffer if present
            if (em.HasBuffer<DefaultNetLane>(prefabEntity))
            {
                var laneBuf = em.GetBuffer<DefaultNetLane>(prefabEntity);
                for (int l = 0; l < laneBuf.Length; l++)
                {
                    var lane = laneBuf[l];
                    if ((lane.m_Flags & LaneFlags.Road) != 0 && (lane.m_Flags & LaneFlags.Slave) == 0)
                    {
                        if ((lane.m_Flags & LaneFlags.Invert) == 0)
                            forwardLanes++;
                        else
                            backwardLanes++;
                    }
                }

                if (forwardLanes > 0 || backwardLanes > 0)
                {
                    forwardLanes = math.max(1, forwardLanes);
                    isOneWay = (backwardLanes == 0);
                    return true;
                }
            }

            return InspectFallbackLanes(name, out forwardLanes, out backwardLanes, out isOneWay);
        }

        private static bool InspectFallbackLanes(string name, out int forwardLanes, out int backwardLanes, out bool isOneWay)
        {
            forwardLanes = 2;
            backwardLanes = 0;
            isOneWay = true;

            if (string.IsNullOrEmpty(name)) return false;

            if (name.IndexOf("One-Way", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("OneWay", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Ramp", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                isOneWay = true;
                backwardLanes = 0;
                if (name.Contains("1")) forwardLanes = 1;
                else if (name.Contains("2")) forwardLanes = 2;
                else if (name.Contains("3")) forwardLanes = 3;
                else if (name.Contains("4")) forwardLanes = 4;
                else if (name.Contains("5")) forwardLanes = 5;
                return true;
            }

            if (name.IndexOf("Highway", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                isOneWay = false;
                forwardLanes = 2;
                backwardLanes = 2;
                if (name.Contains("1")) { forwardLanes = 1; backwardLanes = 1; }
                else if (name.Contains("3")) { forwardLanes = 3; backwardLanes = 3; }
                else if (name.Contains("4")) { forwardLanes = 4; backwardLanes = 4; }
                return true;
            }

            return false;
        }
    }
}
