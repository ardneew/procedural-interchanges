using System.Collections.Generic;
using ProceduralInterchanges.Core.Models;

namespace ProceduralInterchanges.Core.Generators
{
    public interface IInterchangeGenerator
    {
        InterchangeArchetype Archetype { get; }
        string DisplayName { get; }
        string Description { get; }
        int MinLegs { get; }
        int MaxLegs { get; }

        bool SupportsConfiguration(IReadOnlyList<HighwayLeg> legs, out string reason);
        void GenerateGeometry(InterchangeConfig config, IReadOnlyList<HighwayLeg> legs, List<PlannedNetCourse> outCourses);
    }
}
