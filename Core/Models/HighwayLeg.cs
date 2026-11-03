using System.Collections.Generic;
using Colossal.Mathematics;
using Unity.Entities;
using Unity.Mathematics;

namespace ProceduralInterchanges.Core.Models
{
    public sealed class CarriagewayEndpoint
    {
        public Entity SourceEdge;
        public Entity SourcePrefab;
        public string PrefabName;
        public Bezier4x3 OriginalCurve;
        public float CutCurveT;          // Parameter t in [0, 1] where the footprint boundary cuts OriginalCurve
        public float3 PortalPosition;    // Exact 3D point on the footprint boundary (or stub end)
        public float3 FlowDirection;     // Normalized 3D tangent pointing INBOUND (+1) or OUTBOUND (-1)
        public bool IsInboundToCenter;   // true = traffic flows toward interchange center; false = flows away
        public bool IsOneWay;            // true if one-way carriageway; false if two-way
        public int LaneCount;            // Drivable car lane count on this carriageway
        public float Width;              // NetGeometryData.m_DefaultWidth
        public float SpeedLimitMs;       // RoadData.m_SpeedLimit
        public bool AttachToNode;        // true if snapping to an existing stub node rather than splitting an edge
        public Entity AttachNodeEntity;  // Non-null when AttachToNode == true
    }

    public sealed class HighwayLeg
    {
        public int LegIndex;                           // 0..N-1 sorted counter-clockwise by AzimuthRad
        public float AzimuthRad;                       // Angle in XZ plane from Center: atan2(dir.z, dir.x) in (-pi, pi]
        public float3 LegAxisDirection;                // Outward unit vector in XZ from interchange center toward leg portal
        public float3 PortalCenterPosition;            // Midpoint between Inbound and Outbound portals (or single two-way portal)
        public float3 PortalInwardTangent;             // Highway's actual inbound tangent at cut point for exact G1 alignment
        public bool IsSeparatedDualCarriageway;        // true if Inbound != null && Outbound != null as separate one-way edges
        public float CarriagewaySeparation;            // Centerline-to-centerline lateral spacing (m) between Inbound and Outbound
        public CarriagewayEndpoint Inbound;            // Approaches center (One-Way Inbound, or shared Two-Way)
        public CarriagewayEndpoint Outbound;           // Leaves center (One-Way Outbound, or null if single Two-Way)
        public List<Entity> InteriorEdgesToDelete = new List<Entity>(); // Existing edges strictly inside footprint to delete

        // Fine-tuning overrides per leg (null/0 = use auto-matched from scanned highway)
        public int OverrideMainlineLanes;              // 0 = auto, 1..5 = explicit lane count
        public string OverrideMainlinePrefabName;      // "" = auto
        public RoadUpgradeFlags Upgrades;
    }
}
