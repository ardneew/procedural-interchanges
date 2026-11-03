using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Prefabs;
using Unity.Entities;
using Unity.Mathematics;

namespace ProceduralInterchanges.Core.Models
{
    public sealed class RestorableEdgeSnapshot
    {
        public Entity OriginalEntity;
        public Entity PrefabEntity;
        public string PrefabName;
        public Bezier4x3 Curve;
        public float2 StartElevation;
        public float2 EndElevation;
        public CompositionFlags Upgrades;
        public Entity StartNode;
        public Entity EndNode;
    }

    public sealed class InterchangeReceipt
    {
        public int GroupId;
        public DateTime Timestamp = DateTime.UtcNow;
        public InterchangeConfig SavedConfig;
        public List<HighwayLeg> SavedLegs = new List<HighwayLeg>();
        public List<Entity> CreatedEntities = new List<Entity>();
        public List<RestorableEdgeSnapshot> DeletedOriginalEdges = new List<RestorableEdgeSnapshot>();
    }
}
