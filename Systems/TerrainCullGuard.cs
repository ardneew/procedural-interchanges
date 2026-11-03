using System;
using System.Collections;
using System.Reflection;
using Game.Simulation;
using Unity.Entities;
using Unity.Jobs;

namespace ProceduralInterchanges.Systems
{
    public static class TerrainCullGuard
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static readonly FieldInfo CullFinished = typeof(TerrainSystem).GetField("m_CullFinished", Any);
        private static readonly FieldInfo LaneCull = typeof(TerrainSystem).GetField("m_LaneCull", Any);
        private static readonly FieldInfo ClipMapCull = typeof(TerrainSystem).GetField("m_ClipMapCull", Any);
        private static readonly FieldInfo Cascades = typeof(TerrainSystem).GetField("m_CascadeCulling", Any);
        private static FieldInfo s_LaneHandle;

        public static void EnsureSafeCapacity(World world, int expectedNets)
        {
            if (world == null) return;
            var terrain = world.GetExistingSystemManaged<TerrainSystem>();
            if (terrain == null) return;

            try
            {
                Complete(CullFinished, terrain);
                Complete(LaneCull, terrain);
                Complete(ClipMapCull, terrain);

                if (Cascades?.GetValue(terrain) is IList list)
                {
                    foreach (var info in list)
                    {
                        if (info == null) continue;
                        s_LaneHandle ??= info.GetType().GetField("m_LaneHandle", Any);
                        if (s_LaneHandle?.GetValue(info) is JobHandle h) h.Complete();
                    }
                }

                var roads = terrain.GetRoads();
                if (!roads.IsCreated) return;

                int want = roads.Length * 2 + Math.Max(0, expectedNets) * 18 + 2000;
                if (roads.Capacity < want)
                {
                    roads.Capacity = want;
                }
            }
            catch (Exception ex)
            {
                Mod.log.Warn($"TerrainCullGuard: {ex.Message}");
            }
        }

        private static void Complete(FieldInfo field, TerrainSystem terrain)
        {
            if (field?.GetValue(terrain) is JobHandle h)
            {
                h.Complete();
            }
        }
    }
}
