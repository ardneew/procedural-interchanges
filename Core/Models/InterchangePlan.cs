using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;

namespace ProceduralInterchanges.Core.Models
{
    public struct ElevationProfileSample
    {
        public float Distance;
        public float RoadElevation;
        public float TerrainElevation;
        public float GradePercent;
        public float Clearance;
    }

    public sealed class InterchangePlan
    {
        public InterchangeConfig Config;
        public List<HighwayLeg> Legs = new List<HighwayLeg>();
        public List<PlannedNetCourse> Courses = new List<PlannedNetCourse>();
        public List<PlannedNetCourse> OuterReplacementCourses = new List<PlannedNetCourse>();
        public List<Entity> EdgesToDelete = new List<Entity>();
        public List<RestorableEdgeSnapshot> RestorableSnapshots = new List<RestorableEdgeSnapshot>();

        public int EstimatedCost;
        public int ViolationCount;
        public float WorstGradePercent;
        public float TightestRadiusMeters;
        public float MinClearanceMeters;
        public string ValidationSummary = "Valid";

        public string SelectedCourseForProfileId;
        public List<ElevationProfileSample> ActiveElevationProfile = new List<ElevationProfileSample>();
    }
}
