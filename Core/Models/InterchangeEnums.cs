using System;

namespace ProceduralInterchanges.Core.Models
{
    public enum ToolStage : int
    {
        Idle = 0,
        PickCenter = 1,
        SelectHighways = 2,
        ConfigureInterchange = 3,
        Building = 4,
        ManageExisting = 5
    }

    public enum InterchangeArchetype : int
    {
        // 3-Leg Archetypes
        Trumpet = 10,
        DirectionalT = 11,
        YStack = 12,

        // 4-Leg Archetypes
        Diamond = 20,
        ParcloA4 = 21,
        Cloverleaf = 22,
        FourLevelStack = 23,
        Turbine = 24
    }

    public enum NetRole : int
    {
        MainlineThrough = 0,
        DirectionalRamp = 1,
        LoopRamp = 2,
        CollectorDistributor = 3,
        CrossStreet = 5,
        TransitionTaper = 6,
        SlipRamp = 7
    }

    [Flags]
    public enum RoadUpgradeFlags : uint
    {
        None = 0,
        SoundBarrierLeft = 1 << 0,
        SoundBarrierRight = 1 << 1,
        StreetLighting = 1 << 2,
        WideMedian = 1 << 3,
        RetainingWallLeft = 1 << 4,
        RetainingWallRight = 1 << 5,
        ForbidLeftTurn = 1 << 6,
        ForbidRightTurn = 1 << 7,
        RemoveTrafficLights = 1 << 8
    }

    public enum ComplianceLevel : int
    {
        Optimal = 0,   // Within design speed radius & grade <= 4%
        Marginal = 1,  // Within 80% of min radius or grade in (4%, MaxGrade]
        Violation = 2  // Below min radius, exceeds MaxGrade, or clearance < MinClearance
    }
}
