using Colossal.Mathematics;
using Game.Rendering;
using ProceduralInterchanges.Core.Math;
using ProceduralInterchanges.Core.Models;
using Unity.Mathematics;
using UnityEngine;

namespace ProceduralInterchanges.Systems
{
    public sealed partial class InterchangeToolSystem
    {
        private static readonly Color kColorFootprint = new Color(0.15f, 0.65f, 1.0f, 0.40f);
        private static readonly Color kColorFootprintBorder = new Color(0.15f, 0.65f, 1.0f, 0.85f);
        private static readonly Color kColorInboundPortal = new Color(0.0f, 0.90f, 1.0f, 0.95f);  // Cyan
        private static readonly Color kColorOutboundPortal = new Color(1.0f, 0.70f, 0.0f, 0.95f); // Amber
        private static readonly Color kColorOptimal = new Color(0.18f, 0.80f, 0.44f, 0.90f);     // Emerald
        private static readonly Color kColorMarginal = new Color(0.95f, 0.77f, 0.06f, 0.90f);    // Gold
        private static readonly Color kColorViolation = new Color(0.91f, 0.30f, 0.24f, 0.95f);   // Crimson
        private static readonly Color kColorCrosshair = new Color(1.0f, 1.0f, 1.0f, 0.65f);

        private void RenderOverlay()
        {
            if (!EngineeringOverlayEnabled) return;

            var buffer = m_OverlayRenderSystem.GetBuffer(out var deps);
            deps.Complete();

            float3 center = CurrentConfig.Center;
            float R = CurrentConfig.FootprintRadius;

            // 1. Hover crosshair when picking center
            if (Stage == ToolStage.PickCenter && m_HasHover)
            {
                buffer.DrawCircle(
                    kColorFootprintBorder,
                    new Color(0.15f, 0.65f, 1.0f, 0.02f),
                    2.0f,
                    OverlayRenderSystem.StyleFlags.Projected,
                    new float2(0f, 1f),
                    m_HoverPoint,
                    R * 2f);
                buffer.DrawLine(kColorCrosshair, new Line3.Segment(m_HoverPoint - new float3(15f, 0f, 0f), m_HoverPoint + new float3(15f, 0f, 0f)), 0.6f);
                buffer.DrawLine(kColorCrosshair, new Line3.Segment(m_HoverPoint - new float3(0f, 0f, 15f), m_HoverPoint + new float3(0f, 0f, 15f)), 0.6f);
            }
            else if (Stage != ToolStage.Idle && math.lengthsq(center) > 1f)
            {
                // 2. Footprint boundary circle (crisp hollow outline)
                buffer.DrawCircle(
                    kColorFootprintBorder,
                    Color.clear,
                    2.5f,
                    OverlayRenderSystem.StyleFlags.Projected,
                    new float2(0f, 1f),
                    center,
                    R * 2f);

                // Compass / Azimuth indicators (N, S, E, W ticks)
                float3 north = center + new float3(0f, 0f, R);
                float3 south = center + new float3(0f, 0f, -R);
                float3 east = center + new float3(R, 0f, 0f);
                float3 west = center + new float3(-R, 0f, 0f);

                buffer.DrawLine(kColorCrosshair, new Line3.Segment(north - new float3(0f, 0f, 6f), north + new float3(0f, 0f, 6f)), 0.8f);
                buffer.DrawLine(kColorCrosshair, new Line3.Segment(south - new float3(0f, 0f, 6f), south + new float3(0f, 0f, 6f)), 0.8f);
                buffer.DrawLine(kColorCrosshair, new Line3.Segment(east - new float3(6f, 0f, 0f), east + new float3(6f, 0f, 0f)), 0.8f);
                buffer.DrawLine(kColorCrosshair, new Line3.Segment(west - new float3(6f, 0f, 0f), west + new float3(6f, 0f, 0f)), 0.8f);

                // 3. Highlight connected Highway Legs
                for (int i = 0; i < CurrentLegs.Count; i++)
                {
                    var leg = CurrentLegs[i];
                    float3 axis = leg.LegAxisDirection;

                    if (leg.Inbound != null)
                    {
                        float3 p = leg.Inbound.PortalPosition;
                        buffer.DrawCircle(kColorInboundPortal, p, 7f);
                        buffer.DrawLine(kColorInboundPortal, new Line3.Segment(p, p - axis * 14f), 1.2f);
                    }

                    if (leg.Outbound != null)
                    {
                        float3 p = leg.Outbound.PortalPosition;
                        buffer.DrawCircle(kColorOutboundPortal, p, 7f);
                        buffer.DrawLine(kColorOutboundPortal, new Line3.Segment(p, p + axis * 14f), 1.2f);
                    }
                }

                // 4. In ConfigureInterchange: Render all planned network curves with compliance coloring
                if (Stage == ToolStage.ConfigureInterchange && CurrentPlan != null && CurrentPlan.Courses != null)
                {
                    for (int i = 0; i < CurrentPlan.Courses.Count; i++)
                    {
                        var c = CurrentPlan.Courses[i];
                        Color curveColor = c.Compliance switch
                        {
                            ComplianceLevel.Optimal => kColorOptimal,
                            ComplianceLevel.Marginal => kColorMarginal,
                            _ => kColorViolation
                        };

                        float curveWidth = 1.6f + c.Lanes * 0.7f;
                        buffer.DrawCurve(curveColor, c.Curve, curveWidth);

                        // If elevated, draw support clearance indicator at midpoint
                        if (c.StackLevel > 0)
                        {
                            float3 midRoad = CubicBezier3D.Position(c.Curve, 0.5f);
                            float midTerrain = m_BuildService.SampleTerrainHeight(midRoad.xz);
                            float3 groundPoint = new float3(midRoad.x, midTerrain, midRoad.z);

                            buffer.DrawDashedLine(curveColor, new Line3.Segment(groundPoint, midRoad), 0.5f, 1.5f, 1.0f);
                        }
                    }
                }
            }

            m_OverlayRenderSystem.AddBufferWriter(default);
        }
    }
}
