using Game.Prefabs;
using Game.Tools;
using Unity.Jobs;
using UnityEngine.Scripting;

namespace ProceduralInterchanges.Systems
{
    public sealed partial class InterchangeBuildToolSystem : ToolBaseSystem
    {
        public const string Id = "ProceduralInterchanges Build";

        public ApplyMode Requested = ApplyMode.None;
        public int LastAppliedFrame = -1;
        public ApplyMode LastMode = ApplyMode.None;

        public override string toolID => Id;
        public override PrefabBase GetPrefab() => null;
        public override bool TrySetPrefab(PrefabBase prefab) => false;

        public override void InitializeRaycast()
        {
            base.InitializeRaycast();
            m_ToolRaycastSystem.typeMask = global::Game.Common.TypeMask.None;
        }

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
        }

        [Preserve]
        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            applyMode = Requested;
            LastMode = Requested;
            LastAppliedFrame = UnityEngine.Time.frameCount;
            Requested = ApplyMode.None; // Reset one-shot mode
            return inputDeps;
        }
    }
}
