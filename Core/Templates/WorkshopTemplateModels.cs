using Unity.Mathematics;
using ProceduralInterchanges.Core.Models;

namespace ProceduralInterchanges.Core.Templates
{
    public enum TemplateRoadWidthClass : byte
    {
        OneWay1Lane = 1, // 1-lane ramp
        OneWay2Lane = 2, // 2-lane highway
        OneWay3Lane = 3, // 3-lane highway
        OneWay4Lane = 4  // 4-lane highway
    }

    public struct TemplateSegmentDef
    {
        public int StartNodeIndex;
        public int EndNodeIndex;
        public float3 BezierA;
        public float3 BezierB;
        public float3 BezierC;
        public float3 BezierD;
        public float2 Elevation;
        public TemplateRoadWidthClass WidthClass;
        public bool IsElevated; // true when m_Upgrades contains 11 (NetPieceRequirements.Elevated)
    }

    public struct TemplateTerminalPort
    {
        public int NodeIndex;
        public int SegmentIndex;
        public bool IsInbound; // true if segment starts at NodeIndex (traffic enters interchange); false if segment ends at NodeIndex (traffic exits interchange)
        public int CanonicalLegIndex; // 0 .. RequiredLegs-1 (sorted counter-clockwise by canonical leg azimuth)
    }

    public sealed class WorkshopTemplateDef
    {
        public string Name;
        public InterchangeArchetype Archetype;
        public int RequiredLegs;
        public float CanonicalRadius;
        public float[] CanonicalLegAzimuthsRad; // Sorted CCW in [-pi, pi]
        public float3[] Nodes;
        public TemplateSegmentDef[] Segments;
        public TemplateTerminalPort[] TerminalPorts;
    }
}
