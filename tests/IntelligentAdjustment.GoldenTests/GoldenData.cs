using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.GoldenTests;

internal static class GoldenData
{
    public static IReadOnlyList<LevelDifference> FourPointLoop(double distanceMeters = 100) =>
    [
        D(1, 0, "A", "B", 1.00000, distanceMeters),
        D(2, 1, "B", "C", 1.00000, distanceMeters),
        D(3, 2, "C", "D", 1.00000, distanceMeters),
        D(4, 3, "D", "A", -3.00200, distanceMeters)
    ];

    public static IReadOnlyList<LevelDifference> TransitionBaseline(bool allAdjustment = false) =>
    [
        D(1, 0, "A", "1",  0.10000, 100, allAdjustment ? PointRole.AdjustmentPoint : PointRole.TransitionPoint),
        D(2, 1, "1", "2",  0.20000, 110, allAdjustment ? PointRole.AdjustmentPoint : PointRole.TransitionPoint),
        D(3, 2, "2", "B",  0.30000, 120, PointRole.AdjustmentPoint),
        D(4, 3, "B", "3", -0.10000, 130, allAdjustment ? PointRole.AdjustmentPoint : PointRole.TransitionPoint),
        D(5, 4, "3", "C",  0.50000, 140, PointRole.AdjustmentPoint),
        D(6, 5, "C", "D",  0.70000, 150, PointRole.AdjustmentPoint)
    ];

    public static LevelDifference D(
        long id,
        int sequence,
        string from,
        string to,
        double heightDifference,
        double distanceMeters,
        PointRole role = PointRole.AdjustmentPoint,
        int stationCount = 1,
        long lineId = 1) =>
        new(id, lineId, sequence, from, to, heightDifference, distanceMeters, stationCount, role);
}
