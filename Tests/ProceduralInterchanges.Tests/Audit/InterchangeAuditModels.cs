using System.Collections.Generic;
using Unity.Mathematics;

namespace ProceduralInterchanges.Tests.Audit
{
    public enum AuditSeverity
    {
        Info,
        Warning,
        Error
    }

    public enum AuditCategory
    {
        VerticalClearance,
        PillarEncroachment,
        MaximumGrade,
        MinimumTurningRadius,
        TopologicalReachability,
        TangentContinuity,
        CurveDegeneracy
    }

    public sealed class AuditIssue
    {
        public AuditSeverity Severity { get; set; }
        public AuditCategory Category { get; set; }
        public string Message { get; set; }
        public float3 Location { get; set; }
        public string CourseIdA { get; set; }
        public string CourseIdB { get; set; }

        public override string ToString()
        {
            return $"[{Severity}] {Category}: {Message} at ({Location.x:F1}, {Location.y:F1}, {Location.z:F1})";
        }
    }

    public sealed class AuditMetrics
    {
        public int TotalCourses { get; set; }
        public float TotalLengthMeters { get; set; }
        public float MaxGradePercent { get; set; }
        public float MinClearanceMeters { get; set; } = float.MaxValue;
        public float MinTurningRadiusMeters { get; set; } = float.MaxValue;
        public int CrossingCount { get; set; }
        public int PillarCount { get; set; }
        public double GenerationTimeMs { get; set; }
        public double AuditTimeMs { get; set; }
    }

    public sealed class AuditVerdict
    {
        public bool Passed => Issues.TrueForAll(i => i.Severity != AuditSeverity.Error);
        public List<AuditIssue> Issues { get; } = new List<AuditIssue>();
        public AuditMetrics Metrics { get; } = new AuditMetrics();

        public void AddIssue(AuditSeverity severity, AuditCategory category, string message, float3 location = default, string idA = null, string idB = null)
        {
            Issues.Add(new AuditIssue
            {
                Severity = severity,
                Category = category,
                Message = message,
                Location = location,
                CourseIdA = idA,
                CourseIdB = idB
            });
        }
    }
}
