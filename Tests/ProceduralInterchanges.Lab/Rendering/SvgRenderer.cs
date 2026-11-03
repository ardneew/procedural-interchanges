using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Colossal.Mathematics;
using ProceduralInterchanges.Core.Math;
using ProceduralInterchanges.Core.Models;
using ProceduralInterchanges.Tests.Audit;
using Unity.Mathematics;

namespace ProceduralInterchanges.Lab.Rendering
{
    public static class SvgRenderer
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string RenderInterchangeSvg(
            IReadOnlyList<PlannedNetCourse> courses,
            IReadOnlyList<HighwayLeg> legs,
            AuditVerdict verdict,
            float footprintRadius = 200f)
        {
            float margin = 40f;
            float viewSize = (footprintRadius + margin) * 2f;
            float minCoord = -footprintRadius - margin;

            var sb = new StringBuilder();
            sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"{minCoord.ToString("F1", Inv)} {minCoord.ToString("F1", Inv)} {viewSize.ToString("F1", Inv)} {viewSize.ToString("F1", Inv)}\" style=\"background:#121417; width:100%; height:100%;\">");
            sb.AppendLine("  <defs>");
            sb.AppendLine("    <filter id=\"glow\" x=\"-20%\" y=\"-20%\" width=\"140%\" height=\"140%\"><feGaussianBlur stdDeviation=\"2\" result=\"blur\"/><feComposite in=\"SourceGraphic\" in2=\"blur\" operator=\"over\"/></filter>");
            sb.AppendLine("    <marker id=\"arrow\" viewBox=\"0 0 10 10\" refX=\"5\" refY=\"5\" markerWidth=\"4\" markerHeight=\"4\" orient=\"auto-start-reverse\"><path d=\"M 0 1.5 L 8 5 L 0 8.5 z\" fill=\"#e2e8f0\"/></marker>");
            sb.AppendLine("  </defs>");

            // 1. Draw background grid and footprint circle
            sb.AppendLine($"  <circle cx=\"0\" cy=\"0\" r=\"{footprintRadius.ToString("F1", Inv)}\" fill=\"none\" stroke=\"#1e293b\" stroke-width=\"2\" stroke-dasharray=\"6 6\"/>");
            sb.AppendLine("  <line x1=\"-300\" y1=\"0\" x2=\"300\" y2=\"0\" stroke=\"#1e293b\" stroke-width=\"1\"/>");
            sb.AppendLine("  <line x1=\"0\" y1=\"-300\" x2=\"0\" y2=\"300\" stroke=\"#1e293b\" stroke-width=\"1\"/>");

            // 2. Sort courses by elevation/stack level (Ground first, then elevated)
            var sortedCourses = new List<PlannedNetCourse>(courses);
            sortedCourses.Sort((c1, c2) =>
            {
                float y1 = math.max(c1.Curve.a.y, c1.Curve.d.y);
                float y2 = math.max(c2.Curve.a.y, c2.Curve.d.y);
                return y1.CompareTo(y2);
            });

            // 3. Draw courses
            foreach (var course in sortedCourses)
            {
                float midY = CubicBezier3D.Position(course.Curve, 0.5f).y;
                string strokeColor = GetTierColor(midY, course.StackLevel);
                float strokeWidth = math.max(4f, (course.Lanes > 0 ? course.Lanes : 2) * 3.5f * 0.7f);

                // Bezier curve path
                // Note: CS2 coords have Z = forward/north, SVG coords have Y = down.
                // We map CS2 X -> SVG X, CS2 Z -> SVG -Y (so North is Up).
                float ax = course.Curve.a.x;
                float ay = -course.Curve.a.z;
                float bx = course.Curve.b.x;
                float by = -course.Curve.b.z;
                float cx = course.Curve.c.x;
                float cy = -course.Curve.c.z;
                float dx = course.Curve.d.x;
                float dy = -course.Curve.d.z;

                // Under-shadow for elevated spans
                if (midY > 4.5f)
                {
                    sb.AppendLine($"  <path d=\"M {ax.ToString("F1", Inv)} {ay.ToString("F1", Inv)} C {bx.ToString("F1", Inv)} {by.ToString("F1", Inv)}, {cx.ToString("F1", Inv)} {cy.ToString("F1", Inv)}, {dx.ToString("F1", Inv)} {dy.ToString("F1", Inv)}\" fill=\"none\" stroke=\"#000000\" stroke-width=\"{(strokeWidth + 4f).ToString("F1", Inv)}\" opacity=\"0.6\"/>");
                }

                // Road ribbon
                sb.AppendLine($"  <path id=\"{course.Id}\" d=\"M {ax.ToString("F1", Inv)} {ay.ToString("F1", Inv)} C {bx.ToString("F1", Inv)} {by.ToString("F1", Inv)}, {cx.ToString("F1", Inv)} {cy.ToString("F1", Inv)}, {dx.ToString("F1", Inv)} {dy.ToString("F1", Inv)}\" fill=\"none\" stroke=\"{strokeColor}\" stroke-width=\"{strokeWidth.ToString("F1", Inv)}\" stroke-linecap=\"round\" stroke-linejoin=\"round\">");
                sb.AppendLine($"    <title>{course.Id} (Lanes: {course.Lanes}, Elev: {midY:F1}m)</title>");
                sb.AppendLine("  </path>");

                // Centerline dashed line
                sb.AppendLine($"  <path d=\"M {ax.ToString("F1", Inv)} {ay.ToString("F1", Inv)} C {bx.ToString("F1", Inv)} {by.ToString("F1", Inv)}, {cx.ToString("F1", Inv)} {cy.ToString("F1", Inv)}, {dx.ToString("F1", Inv)} {dy.ToString("F1", Inv)}\" fill=\"none\" stroke=\"#f8fafc\" stroke-width=\"1\" stroke-dasharray=\"4 4\" opacity=\"0.5\" marker-mid=\"url(#arrow)\"/>");
            }

            // 4. Draw support pillars
            foreach (var course in sortedCourses)
            {
                if (course.ForceElevated || course.StackLevel > 0 || course.Curve.a.y > 4.5f || course.Curve.d.y > 4.5f)
                {
                    float3 mid = CubicBezier3D.Position(course.Curve, 0.5f);
                    if (mid.y > 5.0f && CubicBezier3D.Length(course.Curve) > 40f)
                    {
                        float px = mid.x;
                        float py = -mid.z;
                        sb.AppendLine($"  <circle cx=\"{px.ToString("F1", Inv)}\" cy=\"{py.ToString("F1", Inv)}\" r=\"3.5\" fill=\"#475569\" stroke=\"#cbd5e1\" stroke-width=\"1.5\"><title>Pillar at {mid.y:F1}m</title></circle>");
                    }
                }
            }

            // 5. Draw audit issue markers
            if (verdict != null && verdict.Issues != null)
            {
                foreach (var issue in verdict.Issues)
                {
                    if (issue.Severity == AuditSeverity.Error)
                    {
                        float ix = issue.Location.x;
                        float iy = -issue.Location.z;
                        sb.AppendLine($"  <g transform=\"translate({ix.ToString("F1", Inv)}, {iy.ToString("F1", Inv)})\">");
                        sb.AppendLine("    <circle r=\"5\" fill=\"#ef4444\" stroke=\"#ffffff\" stroke-width=\"1.5\"/>");
                        sb.AppendLine($"    <title>[Error] {issue.Category}: {issue.Message}</title>");
                        sb.AppendLine("  </g>");
                    }
                }
            }

            // 6. Draw Leg Portals
            if (legs != null)
            {
                for (int i = 0; i < legs.Count; i++)
                {
                    var leg = legs[i];
                    float lx = leg.PortalCenterPosition.x;
                    float ly = -leg.PortalCenterPosition.z;
                    sb.AppendLine($"  <text x=\"{lx.ToString("F1", Inv)}\" y=\"{ly.ToString("F1", Inv)}\" fill=\"#94a3b8\" font-family=\"sans-serif\" font-size=\"11\" font-weight=\"bold\" text-anchor=\"middle\" dominant-baseline=\"central\">Leg {leg.LegIndex}</text>");
                }
            }

            sb.AppendLine("</svg>");
            return sb.ToString();
        }

        private static string GetTierColor(float y, int stackLevel)
        {
            if (y < -1.0f) return "#06b6d4"; // Cyan (Tunnel / Depressed)
            if (y > 18.0f || stackLevel >= 3) return "#ec4899"; // Magenta (Tier 3)
            if (y > 10.0f || stackLevel == 2) return "#f59e0b"; // Amber (Tier 2)
            if (y > 3.5f || stackLevel == 1) return "#10b981";  // Emerald (Tier 1)
            return "#64748b"; // Slate (Ground)
        }
    }
}
