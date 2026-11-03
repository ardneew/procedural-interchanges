using Colossal.Mathematics;
using Unity.Entities;
using Unity.Mathematics;

namespace ProceduralInterchanges.Core.Models
{
    public sealed class PlannedNetCourse
    {
        public string Id;
        public NetRole Role;
        public int LegIndex = -1;
        public int TargetLegIndex = -1;
        public Bezier4x3 Curve;
        public int StackLevel;               // 0 = ground, 1..3 = elevated overpass tiers
        public bool ForceElevated;
        public float2 StartElevation;        // Height above terrain at A
        public float2 EndElevation;          // Height above terrain at D
        public int Lanes;                    // Drivable forward lanes (or per-direction for two-way)
        public bool IsOneWay = true;
        public string ResolvedPrefabName;
        public Entity ResolvedPrefabEntity;
        public RoadUpgradeFlags Upgrades;

        // Attachment at A (Start) or D (End) to existing network when not replacing outer edge
        public Entity StartAttachEntity;
        public float StartSplitPosition;
        public bool StartAttachIsNode;
        public Entity EndAttachEntity;
        public float EndSplitPosition;
        public bool EndAttachIsNode;

        // Engineering telemetry computed by solvers for 3D/2D visualizers
        public float LengthMeters;
        public float MinRadiusMeters;
        public float MaxGradePercent;
        public float MinClearanceMeters;
        public ComplianceLevel Compliance = ComplianceLevel.Optimal;
        public string ComplianceMessage;

        public PlannedNetCourse Clone()
        {
            return new PlannedNetCourse
            {
                Id = Id,
                Role = Role,
                LegIndex = LegIndex,
                TargetLegIndex = TargetLegIndex,
                Curve = Curve,
                StackLevel = StackLevel,
                ForceElevated = ForceElevated,
                StartElevation = StartElevation,
                EndElevation = EndElevation,
                Lanes = Lanes,
                IsOneWay = IsOneWay,
                ResolvedPrefabName = ResolvedPrefabName,
                ResolvedPrefabEntity = ResolvedPrefabEntity,
                Upgrades = Upgrades,
                StartAttachEntity = StartAttachEntity,
                StartSplitPosition = StartSplitPosition,
                StartAttachIsNode = StartAttachIsNode,
                EndAttachEntity = EndAttachEntity,
                EndSplitPosition = EndSplitPosition,
                EndAttachIsNode = EndAttachIsNode,
                LengthMeters = LengthMeters,
                MinRadiusMeters = MinRadiusMeters,
                MaxGradePercent = MaxGradePercent,
                MinClearanceMeters = MinClearanceMeters,
                Compliance = Compliance,
                ComplianceMessage = ComplianceMessage
            };
        }
    }
}
