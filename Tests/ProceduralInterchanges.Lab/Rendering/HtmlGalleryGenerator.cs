using System;
using System.Collections.Generic;
using System.Text;
using ProceduralInterchanges.Tests.Audit;

namespace ProceduralInterchanges.Lab.Rendering
{
    public sealed class GalleryCard
    {
        public string Archetype { get; set; }
        public string Variant { get; set; }
        public string SvgContent { get; set; }
        public AuditVerdict Verdict { get; set; }
        public double ExecutionTimeMs { get; set; }
    }

    public static class HtmlGalleryGenerator
    {
        public static string GenerateGalleryHtml(IReadOnlyList<GalleryCard> cards)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang=\"en\">");
            sb.AppendLine("<head>");
            sb.AppendLine("  <meta charset=\"UTF-8\">");
            sb.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
            sb.AppendLine("  <title>Procedural Interchanges Lab — Visual Test Gallery</title>");
            sb.AppendLine("  <style>");
            sb.AppendLine("    :root { --bg: #0f172a; --card-bg: #1e293b; --text: #f8fafc; --text-muted: #94a3b8; --accent: #38bdf8; --border: #334155; }");
            sb.AppendLine("    body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; background: var(--bg); color: var(--text); margin: 0; padding: 24px; }");
            sb.AppendLine("    header { margin-bottom: 24px; border-bottom: 1px solid var(--border); padding-bottom: 16px; display: flex; justify-content: space-between; align-items: center; }");
            sb.AppendLine("    h1 { font-size: 24px; margin: 0; }");
            sb.AppendLine("    .stats { display: flex; gap: 16px; }");
            sb.AppendLine("    .stat-badge { background: var(--card-bg); border: 1px solid var(--border); padding: 8px 14px; border-radius: 8px; font-size: 13px; }");
            sb.AppendLine("    .stat-val { font-weight: bold; color: var(--accent); margin-left: 4px; }");
            sb.AppendLine("    .grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(420px, 1fr)); gap: 24px; }");
            sb.AppendLine("    .card { background: var(--card-bg); border: 1px solid var(--border); border-radius: 12px; overflow: hidden; display: flex; flex-direction: column; }");
            sb.AppendLine("    .card-header { padding: 12px 16px; border-bottom: 1px solid var(--border); display: flex; justify-content: space-between; align-items: center; font-weight: bold; font-size: 14px; }");
            sb.AppendLine("    .status-pass { color: #10b981; }");
            sb.AppendLine("    .status-fail { color: #ef4444; }");
            sb.AppendLine("    .svg-container { aspect-ratio: 1; width: 100%; background: #121417; position: relative; }");
            sb.AppendLine("    .card-footer { padding: 12px 16px; border-top: 1px solid var(--border); font-size: 12px; color: var(--text-muted); display: grid; grid-template-columns: 1fr 1fr; gap: 6px; }");
            sb.AppendLine("    .issue-list { padding: 8px 16px; background: #271e29; font-size: 11px; color: #fca5a5; max-height: 80px; overflow-y: auto; border-top: 1px solid var(--border); }");
            sb.AppendLine("  </style>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");

            int passCount = 0;
            int totalCount = cards.Count;
            foreach (var c in cards) if (c.Verdict != null && c.Verdict.Passed) passCount++;

            sb.AppendLine("  <header>");
            sb.AppendLine("    <div>");
            sb.AppendLine("      <h1>Procedural Interchanges Lab</h1>");
            sb.AppendLine("      <div style=\"color:var(--text-muted); font-size:13px; margin-top:4px;\">Headless Geometric & Clearance Visual Inspection Gallery</div>");
            sb.AppendLine("    </div>");
            sb.AppendLine("    <div class=\"stats\">");
            sb.AppendLine($"      <div class=\"stat-badge\">Pass Rate: <span class=\"stat-val\">{passCount}/{totalCount} ({(totalCount > 0 ? (passCount * 100 / totalCount) : 0)}%)</span></div>");
            sb.AppendLine("      <div class=\"stat-badge\">Engine: <span class=\"stat-val\">.NET 8 Headless</span></div>");
            sb.AppendLine("    </div>");
            sb.AppendLine("  </header>");

            sb.AppendLine("  <div class=\"grid\">");
            foreach (var card in cards)
            {
                bool passed = card.Verdict != null && card.Verdict.Passed;
                string statusClass = passed ? "status-pass" : "status-fail";
                string statusText = passed ? "✓ Passed Audit" : $"✗ {card.Verdict.Issues.FindAll(i => i.Severity == AuditSeverity.Error).Count} Errors";

                sb.AppendLine("    <div class=\"card\">");
                sb.AppendLine("      <div class=\"card-header\">");
                sb.AppendLine($"        <span>{card.Archetype} <span style=\"font-weight:normal; color:var(--text-muted);\">({card.Variant})</span></span>");
                sb.AppendLine($"        <span class=\"{statusClass}\">{statusText}</span>");
                sb.AppendLine("      </div>");
                sb.AppendLine($"      <div class=\"svg-container\">{card.SvgContent}</div>");

                if (!passed && card.Verdict != null && card.Verdict.Issues.Count > 0)
                {
                    sb.AppendLine("      <div class=\"issue-list\">");
                    foreach (var issue in card.Verdict.Issues)
                    {
                        if (issue.Severity == AuditSeverity.Error)
                        {
                            sb.AppendLine($"        <div>• {issue.Category}: {issue.Message}</div>");
                        }
                    }
                    sb.AppendLine("      </div>");
                }

                sb.AppendLine("      <div class=\"card-footer\">");
                if (card.Verdict != null)
                {
                    sb.AppendLine($"        <div>Length: <b>{card.Verdict.Metrics.TotalLengthMeters:F0} m</b></div>");
                    sb.AppendLine($"        <div>Min Clearance: <b>{(card.Verdict.Metrics.MinClearanceMeters == float.MaxValue ? "N/A" : card.Verdict.Metrics.MinClearanceMeters.ToString("F1") + " m")}</b></div>");
                    sb.AppendLine($"        <div>Max Grade: <b>{card.Verdict.Metrics.MaxGradePercent:F1}%</b></div>");
                    sb.AppendLine($"        <div>Time: <b>{card.ExecutionTimeMs:F1} ms</b></div>");
                }
                sb.AppendLine("      </div>");
                sb.AppendLine("    </div>");
            }
            sb.AppendLine("  </div>");

            sb.AppendLine("</body>");
            sb.AppendLine("</html>");
            return sb.ToString();
        }
    }
}
