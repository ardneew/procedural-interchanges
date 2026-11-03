using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using ProceduralInterchanges.Core.Math;
using ProceduralInterchanges.Core.Models;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace ProceduralInterchanges.Systems
{
    public partial class InterchangeBuildService : GameSystemBase
    {
        private ToolSystem m_ToolSystem;
        private TerrainSystem m_TerrainSystem;
        private HighwayPrefabCatalogSystem m_CatalogSystem;
        private InterchangeBuildToolSystem m_BuildTool;
        private InterchangeToolSystem m_InterchangeTool;
        private InterchangeGroupSystem m_GroupSystem;

        private readonly List<Entity> m_ActiveGhostDefinitions = new List<Entity>();
        private readonly List<Entity> m_TrackedTempEntities = new List<Entity>();
        private readonly List<Entity> m_ActiveBuildDefinitions = new List<Entity>();
        private EntityQuery m_PermanentEdgeQuery;
        private EntityQuery m_PermanentNodeQuery;
        private EntityQuery m_TempNetQuery;

        private bool m_GhostDirty = false;
        private InterchangePlan m_PendingGhostPlan;

        private enum GhostPhase
        {
            Idle,
            WaitingClear
        }
        private GhostPhase m_GhostPhase = GhostPhase.Idle;

        // Commit transaction state machine
        private enum BuildState
        {
            Idle,
            ClearingGhosts,
            Emitted,
            WaitingTemps,
            Applying,
            Settling
        }

        private BuildState m_State = BuildState.Idle;
        private int m_StateFrameCounter = 0;
        private ToolBaseSystem m_PreviousTool;
        private InterchangePlan m_CurrentBuildPlan;
        private List<Entity> m_CreatedEdges = new List<Entity>();
        private bool m_IsUndoing = false;
        private InterchangeReceipt m_ActiveUndoReceipt;
        private readonly HashSet<Entity> m_PreBuildEdges = new HashSet<Entity>();

        public bool IsBuilding => m_State != BuildState.Idle;
        public bool IsBusy => IsBuilding;
        public bool ScheduleBuild(InterchangePlan plan) => StartBuild(plan);
        public bool ScheduleUndo(InterchangeReceipt receipt) => StartUndoRestore(receipt);

        public float SampleTerrainHeight(float2 xz)
        {
            if (m_TerrainSystem == null) return 0f;
            var heightData = m_TerrainSystem.GetHeightData();
            return TerrainUtils.SampleHeight(ref heightData, new float3(xz.x, 0f, xz.y));
        }

        public void RequestGhostPreview(InterchangePlan plan)
        {
            m_PendingGhostPlan = plan;
            m_GhostDirty = true;
        }

        public void ClearGhostPreview()
        {
            ClearGhostEntities();
            m_PendingGhostPlan = null;
            m_GhostDirty = true;
        }

        private void ClearGhostEntities()
        {
            for (int i = 0; i < m_ActiveGhostDefinitions.Count; i++)
            {
                Entity def = m_ActiveGhostDefinitions[i];
                if (EntityManager.Exists(def) && !EntityManager.HasComponent<Deleted>(def))
                {
                    EntityManager.AddComponent<Deleted>(def);
                }
            }
            m_ActiveGhostDefinitions.Clear();
            m_TrackedTempEntities.Clear();
        }

        public bool StartBuild(InterchangePlan plan)
        {
            if (IsBuilding || plan == null || plan.Courses.Count == 0) return false;

            m_CurrentBuildPlan = plan;
            m_PendingGhostPlan = null;
            m_GhostDirty = false;
            ClearGhostEntities();
            m_ActiveBuildDefinitions.Clear();

            TerrainCullGuard.EnsureSafeCapacity(World, plan.Courses.Count + plan.OuterReplacementCourses.Count + 10);

            m_PreviousTool = m_ToolSystem.activeTool;
            if (m_InterchangeTool != null && m_ToolSystem.activeTool == m_InterchangeTool)
            {
                m_InterchangeTool.RequestedApplyMode = ApplyMode.Clear;
            }
            m_ToolSystem.activeTool = m_BuildTool;
            m_BuildTool.Requested = ApplyMode.Clear;

            m_IsUndoing = false;
            m_State = BuildState.ClearingGhosts;
            m_StateFrameCounter = 0;
            m_CreatedEdges.Clear();
            m_PreBuildEdges.Clear();
            using (NativeArray<Entity> existing = m_PermanentEdgeQuery.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < existing.Length; i++)
                    m_PreBuildEdges.Add(existing[i]);
            }

            return true;
        }

        public bool StartUndoRestore(InterchangeReceipt receipt)
        {
            if (IsBuilding || receipt == null) return false;

            m_PendingGhostPlan = null;
            m_GhostDirty = false;
            ClearGhostEntities();
            m_ActiveBuildDefinitions.Clear();

            TerrainCullGuard.EnsureSafeCapacity(World, receipt.CreatedEntities.Count + receipt.DeletedOriginalEdges.Count + 10);

            m_PreviousTool = m_ToolSystem.activeTool;
            if (m_InterchangeTool != null && m_ToolSystem.activeTool == m_InterchangeTool)
            {
                m_InterchangeTool.RequestedApplyMode = ApplyMode.Clear;
            }
            m_ToolSystem.activeTool = m_BuildTool;
            m_BuildTool.Requested = ApplyMode.Clear;

            m_IsUndoing = true;
            m_ActiveUndoReceipt = receipt;
            m_State = BuildState.ClearingGhosts;
            m_StateFrameCounter = 0;
            return true;
        }

        private void EmitUndoDefinitions(InterchangeReceipt receipt)
        {
            if (receipt == null) return;

            // 1. Emit deletion definitions for all created interchange edges
            for (int i = 0; i < receipt.CreatedEntities.Count; i++)
            {
                Entity e = receipt.CreatedEntities[i];
                if (EntityManager.Exists(e) && EntityManager.HasComponent<Edge>(e) && EntityManager.HasComponent<Curve>(e))
                {
                    Entity def = EntityManager.CreateEntity();
                    EntityManager.AddComponentData(def, new CreationDefinition
                    {
                        m_Original = e,
                        m_Flags = CreationFlags.Delete
                    });
                    EntityManager.AddComponent<Updated>(def);

                    var curve = EntityManager.GetComponentData<Curve>(e).m_Bezier;
                    var edge = EntityManager.GetComponentData<Edge>(e);

                    EntityManager.AddComponentData(def, new NetCourse
                    {
                        m_Curve = curve,
                        m_Length = MathUtils.Length(curve),
                        m_FixedIndex = -1,
                        m_StartPosition = new CoursePos { m_Entity = edge.m_Start, m_Position = curve.a, m_CourseDelta = 0f, m_ParentMesh = -1 },
                        m_EndPosition = new CoursePos { m_Entity = edge.m_End, m_Position = curve.d, m_CourseDelta = 1f, m_ParentMesh = -1 }
                    });
                    m_ActiveBuildDefinitions.Add(def);
                }
            }

            // 2. Emit restoration definitions for deleted original edges
            CoursePosFlags baseFlags = CoursePosFlags.IsLeft | CoursePosFlags.IsRight;
            for (int i = 0; i < receipt.DeletedOriginalEdges.Count; i++)
            {
                var snap = receipt.DeletedOriginalEdges[i];
                if (snap.PrefabEntity == Entity.Null) continue;

                Entity def = EntityManager.CreateEntity();
                EntityManager.AddComponentData(def, new CreationDefinition
                {
                    m_Prefab = snap.PrefabEntity,
                    m_RandomSeed = 8000 + i,
                    m_Flags = 0
                });
                EntityManager.AddComponent<Updated>(def);

                Entity validStartNode = (snap.StartNode != Entity.Null &&
                    EntityManager.Exists(snap.StartNode) &&
                    !EntityManager.HasComponent<Deleted>(snap.StartNode))
                    ? snap.StartNode : Entity.Null;

                Entity validEndNode = (snap.EndNode != Entity.Null &&
                    EntityManager.Exists(snap.EndNode) &&
                    !EntityManager.HasComponent<Deleted>(snap.EndNode))
                    ? snap.EndNode : Entity.Null;

                var startPos = new CoursePos
                {
                    m_Entity = validStartNode,
                    m_Position = snap.Curve.a,
                    m_Rotation = NetUtils.GetNodeRotation(MathUtils.StartTangent(snap.Curve)),
                    m_CourseDelta = 0f,
                    m_Flags = baseFlags | CoursePosFlags.IsFirst,
                    m_ParentMesh = -1,
                    m_Elevation = snap.StartElevation
                };

                var endPos = new CoursePos
                {
                    m_Entity = validEndNode,
                    m_Position = snap.Curve.d,
                    m_Rotation = NetUtils.GetNodeRotation(MathUtils.EndTangent(snap.Curve)),
                    m_CourseDelta = 1f,
                    m_Flags = baseFlags | CoursePosFlags.IsLast,
                    m_ParentMesh = -1,
                    m_Elevation = snap.EndElevation
                };

                EntityManager.AddComponentData(def, new NetCourse
                {
                    m_Curve = snap.Curve,
                    m_Length = MathUtils.Length(snap.Curve),
                    m_FixedIndex = -1,
                    m_Elevation = float2.zero,
                    m_StartPosition = startPos,
                    m_EndPosition = endPos
                });

                if (!snap.Upgrades.Equals(default(CompositionFlags)))
                {
                    EntityManager.AddComponentData(def, new Upgraded { m_Flags = snap.Upgrades });
                }
                m_ActiveBuildDefinitions.Add(def);
            }
        }

        private void EmitBuildDefinitions(InterchangePlan plan)
        {
            int seed = 1000;

            // 1. Emit outer replacement segments
            for (int i = 0; i < plan.OuterReplacementCourses.Count; i++)
            {
                var c = plan.OuterReplacementCourses[i];
                Entity def = EmitSingleCourse(c, seed++);
                if (def != Entity.Null)
                {
                    m_ActiveBuildDefinitions.Add(def);
                }
            }

            // 2. Emit planned interchange courses
            for (int i = 0; i < plan.Courses.Count; i++)
            {
                var c = plan.Courses[i];
                Entity def = EmitSingleCourse(c, seed++);
                if (def != Entity.Null)
                {
                    m_ActiveBuildDefinitions.Add(def);
                }
            }

            // 3. Emit deletion definitions for trimmed original edges
            for (int i = 0; i < plan.EdgesToDelete.Count; i++)
            {
                Entity origEdge = plan.EdgesToDelete[i];
                if (EntityManager.Exists(origEdge) && EntityManager.HasComponent<Edge>(origEdge) && EntityManager.HasComponent<Curve>(origEdge))
                {
                    Entity def = EntityManager.CreateEntity();
                    EntityManager.AddComponentData(def, new CreationDefinition
                    {
                        m_Original = origEdge,
                        m_Flags = CreationFlags.Delete
                    });
                    EntityManager.AddComponent<Updated>(def);

                    var curve = EntityManager.GetComponentData<Curve>(origEdge).m_Bezier;
                    var edge = EntityManager.GetComponentData<Edge>(origEdge);

                    EntityManager.AddComponentData(def, new NetCourse
                    {
                        m_Curve = curve,
                        m_Length = MathUtils.Length(curve),
                        m_FixedIndex = -1,
                        m_StartPosition = new CoursePos { m_Entity = edge.m_Start, m_Position = curve.a, m_CourseDelta = 0f, m_ParentMesh = -1 },
                        m_EndPosition = new CoursePos { m_Entity = edge.m_End, m_Position = curve.d, m_CourseDelta = 1f, m_ParentMesh = -1 }
                    });
                    m_ActiveBuildDefinitions.Add(def);
                }
            }
        }

        private void EmitGhostDefinitions()
        {
            if (m_PendingGhostPlan == null) return;
            int ghostSeed = 5000;

            // 1. Emit ghost outer replacements
            if (m_PendingGhostPlan.OuterReplacementCourses != null)
            {
                for (int i = 0; i < m_PendingGhostPlan.OuterReplacementCourses.Count; i++)
                {
                    var c = m_PendingGhostPlan.OuterReplacementCourses[i];
                    Entity ghostDef = EmitSingleCourse(c, ghostSeed++);
                    if (ghostDef != Entity.Null)
                    {
                        m_ActiveGhostDefinitions.Add(ghostDef);
                    }
                }
            }

            // 2. Emit ghost planned courses
            if (m_PendingGhostPlan.Courses != null)
            {
                for (int i = 0; i < m_PendingGhostPlan.Courses.Count; i++)
                {
                    var c = m_PendingGhostPlan.Courses[i];
                    Entity ghostDef = EmitSingleCourse(c, ghostSeed++);
                    if (ghostDef != Entity.Null)
                    {
                        m_ActiveGhostDefinitions.Add(ghostDef);
                    }
                }
            }

            // 3. Emit deletion definitions for trimmed original edges
            if (m_PendingGhostPlan.EdgesToDelete != null)
            {
                for (int i = 0; i < m_PendingGhostPlan.EdgesToDelete.Count; i++)
                {
                    Entity origEdge = m_PendingGhostPlan.EdgesToDelete[i];
                    if (EntityManager.Exists(origEdge) && EntityManager.HasComponent<Edge>(origEdge) && EntityManager.HasComponent<Curve>(origEdge))
                    {
                        Entity def = EntityManager.CreateEntity();
                        EntityManager.AddComponentData(def, new CreationDefinition
                        {
                            m_Original = origEdge,
                            m_Flags = CreationFlags.Delete
                        });
                        EntityManager.AddComponent<Updated>(def);

                        var curve = EntityManager.GetComponentData<Curve>(origEdge).m_Bezier;
                        var edge = EntityManager.GetComponentData<Edge>(origEdge);

                        EntityManager.AddComponentData(def, new NetCourse
                        {
                            m_Curve = curve,
                            m_Length = MathUtils.Length(curve),
                            m_FixedIndex = -1,
                            m_StartPosition = new CoursePos { m_Entity = edge.m_Start, m_Position = curve.a, m_CourseDelta = 0f, m_ParentMesh = -1 },
                            m_EndPosition = new CoursePos { m_Entity = edge.m_End, m_Position = curve.d, m_CourseDelta = 1f, m_ParentMesh = -1 }
                        });
                        m_ActiveGhostDefinitions.Add(def);
                    }
                }
            }
        }

        private Entity EmitSingleCourse(PlannedNetCourse c, int randomSeed)
        {
            if (c.ResolvedPrefabEntity == Entity.Null && m_CatalogSystem != null)
            {
                c.ResolvedPrefabEntity = m_CatalogSystem.FindBestHighwayPrefab(c.IsOneWay, c.Lanes, c.IsOneWay ? 0 : c.Lanes, true, out string name);
                c.ResolvedPrefabName = name;
            }

            if (c.ResolvedPrefabEntity == Entity.Null)
            {
                return Entity.Null;
            }

            Entity validStartAttach = (c.StartAttachEntity != Entity.Null &&
                EntityManager.Exists(c.StartAttachEntity) &&
                !EntityManager.HasComponent<Deleted>(c.StartAttachEntity))
                ? c.StartAttachEntity : Entity.Null;

            Entity validEndAttach = (c.EndAttachEntity != Entity.Null &&
                EntityManager.Exists(c.EndAttachEntity) &&
                !EntityManager.HasComponent<Deleted>(c.EndAttachEntity))
                ? c.EndAttachEntity : Entity.Null;

            Bezier4x3 curve = c.Curve;
            if (validStartAttach != Entity.Null && EntityManager.HasComponent<Game.Net.Node>(validStartAttach))
            {
                curve.a = EntityManager.GetComponentData<Game.Net.Node>(validStartAttach).m_Position;
            }
            if (validEndAttach != Entity.Null && EntityManager.HasComponent<Game.Net.Node>(validEndAttach))
            {
                curve.d = EntityManager.GetComponentData<Game.Net.Node>(validEndAttach).m_Position;
            }

            Entity def = EntityManager.CreateEntity();

            EntityManager.AddComponentData(def, new CreationDefinition
            {
                m_Prefab = c.ResolvedPrefabEntity,
                m_RandomSeed = randomSeed,
                m_Flags = 0
            });
            EntityManager.AddComponent<Updated>(def);

            CoursePosFlags startFlags = CoursePosFlags.IsFirst | CoursePosFlags.IsLeft | CoursePosFlags.IsRight;
            if (validStartAttach == Entity.Null)
                startFlags |= CoursePosFlags.DisableMerge;

            CoursePosFlags endFlags = CoursePosFlags.IsLast | CoursePosFlags.IsLeft | CoursePosFlags.IsRight;
            if (validEndAttach == Entity.Null)
                endFlags |= CoursePosFlags.DisableMerge;

            var startPos = new CoursePos
            {
                m_Entity = validStartAttach,
                m_Position = curve.a,
                m_Rotation = NetUtils.GetNodeRotation(MathUtils.StartTangent(curve)),
                m_CourseDelta = 0f,
                m_SplitPosition = c.StartSplitPosition,
                m_Flags = startFlags,
                m_ParentMesh = -1,
                m_Elevation = c.StartElevation
            };

            var endPos = new CoursePos
            {
                m_Entity = validEndAttach,
                m_Position = curve.d,
                m_Rotation = NetUtils.GetNodeRotation(MathUtils.EndTangent(curve)),
                m_CourseDelta = 1f,
                m_SplitPosition = c.EndSplitPosition,
                m_Flags = endFlags,
                m_ParentMesh = -1,
                m_Elevation = c.EndElevation
            };

            if (c.StartElevation.x < 0.25f) startPos.m_Elevation = float2.zero;
            if (c.EndElevation.x < 0.25f) endPos.m_Elevation = float2.zero;

            bool isElevated = c.ForceElevated || c.StackLevel > 0 || math.max(c.StartElevation.x, c.EndElevation.x) >= 2.5f;
            if (isElevated)
            {
                startPos.m_Flags |= CoursePosFlags.ForceElevatedEdge;
                endPos.m_Flags |= CoursePosFlags.ForceElevatedEdge;
            }

            EntityManager.AddComponentData(def, new NetCourse
            {
                m_Curve = curve,
                m_Length = MathUtils.Length(curve),
                m_FixedIndex = -1,
                m_Elevation = float2.zero,
                m_StartPosition = startPos,
                m_EndPosition = endPos
            });

            var composition = BuildCompositionFlags(c.Upgrades);
            if (isElevated)
            {
                composition.m_General |= CompositionFlags.General.Elevated;
            }

            if (!composition.Equals(default(CompositionFlags)))
            {
                EntityManager.AddComponentData(def, new Upgraded { m_Flags = composition });
            }

            return def;
        }

        public static CompositionFlags BuildCompositionFlags(RoadUpgradeFlags upgrades)
        {
            var flags = default(CompositionFlags);
            if ((upgrades & RoadUpgradeFlags.StreetLighting) != 0) flags.m_General |= CompositionFlags.General.Lighting;
            if ((upgrades & RoadUpgradeFlags.WideMedian) != 0) flags.m_General |= CompositionFlags.General.WideMedian;
            if ((upgrades & RoadUpgradeFlags.RemoveTrafficLights) != 0) flags.m_General |= CompositionFlags.General.RemoveTrafficLights;

            if ((upgrades & RoadUpgradeFlags.SoundBarrierLeft) != 0) flags.m_Left |= CompositionFlags.Side.SoundBarrier;
            if ((upgrades & RoadUpgradeFlags.SoundBarrierRight) != 0) flags.m_Right |= CompositionFlags.Side.SoundBarrier;
            if ((upgrades & RoadUpgradeFlags.ForbidLeftTurn) != 0) flags.m_Left |= CompositionFlags.Side.ForbidLeftTurn;
            if ((upgrades & RoadUpgradeFlags.ForbidRightTurn) != 0) flags.m_Right |= CompositionFlags.Side.ForbidRightTurn;
            return flags;
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            m_TerrainSystem = World.GetOrCreateSystemManaged<TerrainSystem>();
            m_CatalogSystem = World.GetOrCreateSystemManaged<HighwayPrefabCatalogSystem>();
            m_BuildTool = World.GetOrCreateSystemManaged<InterchangeBuildToolSystem>();
            m_InterchangeTool = World.GetOrCreateSystemManaged<InterchangeToolSystem>();
            m_GroupSystem = World.GetOrCreateSystemManaged<InterchangeGroupSystem>();

            m_PermanentEdgeQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Curve>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() }
            });

            m_PermanentNodeQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Net.Node>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() }
            });

            m_TempNetQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Temp>() },
                Any = new[] { ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Game.Net.Node>() },
                None = new[] { ComponentType.ReadOnly<Deleted>() }
            });
        }

        protected override void OnUpdate()
        {
            m_InterchangeTool?.DrainPendingUIActions();

            // Process Live Ghost Preview Updates (2-phase clear-then-emit cycle)
            if (m_GhostDirty && !IsBuilding)
            {
                m_GhostDirty = false;
                if (m_ActiveGhostDefinitions.Count > 0)
                {
                    ClearGhostEntities();
                    if (m_InterchangeTool != null && m_ToolSystem.activeTool == m_InterchangeTool)
                    {
                        m_InterchangeTool.RequestedApplyMode = ApplyMode.Clear;
                    }
                    m_GhostPhase = GhostPhase.WaitingClear;
                }
                else
                {
                    EmitGhostDefinitions();
                    m_GhostPhase = GhostPhase.Idle;
                }
            }
            else if (m_GhostPhase == GhostPhase.WaitingClear && !IsBuilding)
            {
                EmitGhostDefinitions();
                m_GhostPhase = GhostPhase.Idle;
            }

            // Process Multi-frame Commit / Undo Transaction State Machine
            if (m_State != BuildState.Idle)
            {
                m_StateFrameCounter++;
                switch (m_State)
                {
                    case BuildState.ClearingGhosts:
                        if (m_StateFrameCounter == 1)
                        {
                            m_BuildTool.Requested = ApplyMode.Clear;
                            if (!m_TempNetQuery.IsEmptyIgnoreFilter)
                            {
                                EntityManager.AddComponent<Deleted>(m_TempNetQuery);
                            }
                        }
                        else if (m_StateFrameCounter >= 2)
                        {
                            if (!m_TempNetQuery.IsEmptyIgnoreFilter)
                            {
                                EntityManager.AddComponent<Deleted>(m_TempNetQuery);
                            }
                            m_BuildTool.Requested = ApplyMode.None;

                            if (!m_IsUndoing)
                            {
                                EmitBuildDefinitions(m_CurrentBuildPlan);
                            }
                            else
                            {
                                EmitUndoDefinitions(m_ActiveUndoReceipt);
                            }
                            m_State = BuildState.Emitted;
                            m_StateFrameCounter = 0;
                        }
                        break;

                    case BuildState.Emitted:
                        if (m_StateFrameCounter >= 2)
                        {
                            m_State = BuildState.WaitingTemps;
                            m_StateFrameCounter = 0;
                        }
                        break;

                    case BuildState.WaitingTemps:
                        if (m_StateFrameCounter >= 1)
                        {
                            m_BuildTool.Requested = ApplyMode.Apply;
                            m_State = BuildState.Applying;
                            m_StateFrameCounter = 0;
                        }
                        break;

                    case BuildState.Applying:
                        if (m_StateFrameCounter >= 1)
                        {
                            // Immediately delete all active build definitions after Apply is pulsed
                            for (int i = 0; i < m_ActiveBuildDefinitions.Count; i++)
                            {
                                Entity def = m_ActiveBuildDefinitions[i];
                                if (EntityManager.Exists(def) && !EntityManager.HasComponent<Deleted>(def))
                                {
                                    EntityManager.AddComponent<Deleted>(def);
                                }
                            }
                            m_ActiveBuildDefinitions.Clear();

                            m_BuildTool.Requested = ApplyMode.None;
                            m_State = BuildState.Settling;
                            m_StateFrameCounter = 0;
                        }
                        break;

                    case BuildState.Settling:
                        if (m_StateFrameCounter >= 2)
                        {
                            // Destroy all active build/undo definitions
                            for (int i = 0; i < m_ActiveBuildDefinitions.Count; i++)
                            {
                                Entity def = m_ActiveBuildDefinitions[i];
                                if (EntityManager.Exists(def) && !EntityManager.HasComponent<Deleted>(def))
                                {
                                    EntityManager.AddComponent<Deleted>(def);
                                }
                            }
                            m_ActiveBuildDefinitions.Clear();

                            if (!m_IsUndoing && m_CurrentBuildPlan != null)
                            {
                                // Query permanent edges created by diffing against m_PreBuildEdges
                                var createdSet = new HashSet<Entity>();
                                using (var permanentEdges = m_PermanentEdgeQuery.ToEntityArray(Allocator.Temp))
                                {
                                    for (int e = 0; e < permanentEdges.Length; e++)
                                    {
                                        Entity edgeEnt = permanentEdges[e];
                                        if (!m_PreBuildEdges.Contains(edgeEnt))
                                        {
                                            createdSet.Add(edgeEnt);
                                        }
                                    }
                                }
                                m_PreBuildEdges.Clear();

                                m_CreatedEdges = new List<Entity>(createdSet);

                                var receipt = new InterchangeReceipt
                                {
                                    SavedConfig = m_CurrentBuildPlan.Config.Clone(),
                                    SavedLegs = new List<HighwayLeg>(m_CurrentBuildPlan.Legs),
                                    CreatedEntities = new List<Entity>(m_CreatedEdges),
                                    DeletedOriginalEdges = new List<RestorableEdgeSnapshot>(m_CurrentBuildPlan.RestorableSnapshots)
                                };

                                m_GroupSystem.RegisterReceipt(receipt);
                            }

                            // Restore active tool
                            if (m_PreviousTool != null && m_PreviousTool != m_BuildTool)
                            {
                                m_ToolSystem.activeTool = m_PreviousTool;
                            }
                            else
                            {
                                m_ToolSystem.activeTool = World.GetOrCreateSystemManaged<DefaultToolSystem>();
                            }

                            m_IsUndoing = false;
                            m_ActiveUndoReceipt = null;
                            m_State = BuildState.Idle;
                            m_CurrentBuildPlan = null;
                        }
                        break;
                }
            }
        }
    }
}
