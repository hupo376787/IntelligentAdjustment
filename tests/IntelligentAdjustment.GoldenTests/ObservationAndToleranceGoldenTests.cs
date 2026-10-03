using IntelligentAdjustment.Core.Observations;
using IntelligentAdjustment.Core.Routes;
using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.GoldenTests;

public sealed class ObservationAndToleranceGoldenTests
{
    [Fact]
    public void BffbFormula_UsesNormalizedRawReadings()
    {
        var result = LevelDifferenceBuilder.FromDoubleRun(
            b1: 1.5000,
            b2: 1.7000,
            f1: 1.2000,
            f2: 1.3000,
            distanceB1: 40.0,
            distanceB2: 40.2,
            distanceF1: 39.8,
            distanceF2: 40.0);

        Assert.InRange(result.HeightDifference, 0.35 - 1e-12, 0.35 + 1e-12);
        Assert.InRange(result.DistanceMeters, 80.0 - 1e-12, 80.0 + 1e-12);
    }

    [Fact]
    public void LimitMode_ChangesOnlyOverLimitDecision_NotCalculatedLimits()
    {
        var route = new NetworkRoute(
            1,
            RouteType.Attached,
            ["A", "B", "C", "D"],
            [],
            ClosureMeters: 0.001,
            LengthMeters: 300,
            StationCount: 3,
            LengthToleranceMeters: ClosureToleranceCalculator.ByDistanceMeters(300, 4),
            StationToleranceMeters: ClosureToleranceCalculator.ByStationMeters(3, 0.3));

        Assert.False(ClosureToleranceCalculator.IsOverLimit(route, new ProjectSettings { ToleranceMode = ClosureToleranceMode.Distance }));
        Assert.True(ClosureToleranceCalculator.IsOverLimit(route, new ProjectSettings { ToleranceMode = ClosureToleranceMode.StationCount }));
    }
}
