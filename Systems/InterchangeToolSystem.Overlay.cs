using Colossal.Mathematics;
using Game.Net;
using Game.Rendering;
using ProceduralInterchanges.Core.Math;
using ProceduralInterchanges.Core.Models;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace ProceduralInterchanges.Systems
{
    public sealed partial class InterchangeToolSystem
    {
        private static readonly Color kColorHoverEdge = new Color(0.15f, 0.85f, 1.0f, 0.90f);     // Bright Cyan
        private static readonly Color kColorInboundPortal = new Color(0.18f, 0.80f, 0.44f, 0.95f);  // Emerald Green
        private static readonly Color kColorOutboundPortal = new Color(0.20f, 0.60f, 1.0f, 0.95f); // Sky Blue
        private static readonly Color kColorGuideline = new Color(1.0f, 1.0f, 1.0f, 0.45f);
        private static readonly Color kColorGroundTier = new Color(0.18f, 0.80f, 0.44f, 0.90f);    // Emerald
        private static readonly Color kColorElevatedTier1 = new Color(0.20f, 0.60f, 1.0f, 0.95f);  // Sky Blue
        private static readonly Color kColorElevatedTier2 = new Color(0.95f, 0.60f, 0.10f, 0.95f);  // Amber
        private static readonly Color kColorViolation = new Color(0.91f, 0.30f, 0.24f, 0.95f);     // Crimson

        private void RenderOverlay()
        {
            if (!EngineeringOverlayEnabled) return;

            var buffer = m_OverlayRenderSystem.GetBuffer(out var deps);
            deps.Complete();

            if (Stage == ToolStage.SelectHighways)
            {
                RenderSelectHighwaysOverlay(buffer);
            }
            else if (Stage == ToolStage.ConfigureInterchange)
            {
                RenderConfigureOverlay(buffer);
            }

            m_OverlayRenderSystem.AddBufferWriter(default);
        }

        private void RenderSelectHighwaysOverlay(OverlayRenderSystem.Buffer buffer)
        {
            // 1. Highlight hovered edge and snapped portal
            if (m_HoveredEntity != Entity.Null && EntityManager.Exists(m_HoveredEntity) && EntityManager.HasComponent<Edge>(m_HoveredEntity) && EntityManager.HasComponent<Curve>(m_HoveredEntity))
            {
                var curve = EntityManager.GetComponentData<Curve>(m_HoveredEntity).m_Bezier;
                buffer.DrawCurve(kColorHoverEdge, curve, 4.0f);

                float pulse = 8f + 1.5f * math.sin((float)World.Time.ElapsedTime * 5f);
                buffer.DrawCircle(kColorHoverEdge, m_HoverPoint, pulse);
            }

            // 2. Render already-selected legs
            RenderSelectedLegs(buffer);
        }

        private void RenderConfigureOverlay(OverlayRenderSystem.Buffer buffer)
        {
            // 1. Render selected highway portal circles and guidelines
            RenderSelectedLegs(buffer);

            // 2. Render all planned network curves
            if (CurrentPlan != null && CurrentPlan.Courses != null)
            {
                bool hasViolations = CurrentPlan.ViolationCount > 0;

                for (int i = 0; i < CurrentPlan.Courses.Count; i++)
                {
                    var c = CurrentPlan.Courses[i];
                    Color curveColor;
                    if (hasViolations && c.Compliance == ComplianceLevel.Violation)
                    {
                        curveColor = kColorViolation;
                    }
                    else
                    {
                        curveColor = c.StackLevel switch
                        {
                            0 => kColorGroundTier,
                            1 => kColorElevatedTier1,
                            _ => kColorElevatedTier2
                        };
                    }

                    float curveWidth = 1.8f + c.Lanes * 0.7f;
                    buffer.DrawCurve(curveColor, c.Curve, curveWidth);

                    // Support clearance indicator if elevated
                    if (c.StackLevel > 0)
                    {
                        float3 midRoad = CubicBezier3D.Position(c.Curve, 0.5f);
                        float midTerrain = m_BuildService != null ? m_BuildService.SampleTerrainHeight(midRoad.xz) : midRoad.y;
                        float3 groundPoint = new float3(midRoad.x, midTerrain, midRoad.z);
                        buffer.DrawDashedLine(curveColor, new Line3.Segment(groundPoint, midRoad), 0.5f, 1.5f, 1.0f);
                    }
                }
            }
        }

        private void RenderSelectedLegs(OverlayRenderSystem.Buffer buffer)
        {
            for (int i = 0; i < CurrentLegs.Count; i++)
            {
                var leg = CurrentLegs[i];

                if (leg.Inbound != null)
                {
                    if (leg.Inbound.SourceEdge != Entity.Null && EntityManager.Exists(leg.Inbound.SourceEdge) && EntityManager.HasComponent<Curve>(leg.Inbound.SourceEdge))
                    {
                        var curve = EntityManager.GetComponentData<Curve>(leg.Inbound.SourceEdge).m_Bezier;
                        buffer.DrawCurve(kColorInboundPortal, curve, 3.0f);
                    }
                    buffer.DrawCircle(kColorInboundPortal, leg.Inbound.PortalPosition, 7f);
                }

                if (leg.Outbound != null)
                {
                    if (leg.Outbound.SourceEdge != Entity.Null && EntityManager.Exists(leg.Outbound.SourceEdge) && EntityManager.HasComponent<Curve>(leg.Outbound.SourceEdge))
                    {
                        var curve = EntityManager.GetComponentData<Curve>(leg.Outbound.SourceEdge).m_Bezier;
                        buffer.DrawCurve(kColorOutboundPortal, curve, 3.0f);
                    }
                    buffer.DrawCircle(kColorOutboundPortal, leg.Outbound.PortalPosition, 7f);
                }

                // Dashed guideline toward center when >= 2 legs
                if (CurrentLegs.Count >= 2 && math.lengthsq(CurrentConfig.Center) > 1f)
                {
                    float3 midPos = leg.PortalCenterPosition;
                    buffer.DrawDashedLine(kColorGuideline, new Line3.Segment(midPos, CurrentConfig.Center), 0.6f, 3.0f, 2.0f);
                }
            }
        }
    }
}
