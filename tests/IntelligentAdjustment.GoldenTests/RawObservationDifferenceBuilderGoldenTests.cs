using IntelligentAdjustment.Application.Observations;
using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.GoldenTests;

public sealed class RawObservationDifferenceBuilderGoldenTests
{
    [Fact]
    public void DoubleReadingStation_UsesVerifiedBffbFormula()
    {
        var raw = new RawObservation
        {
            Id = 1,
            LineId = 7,
            Sequence = 3,
            FromPoint = "A",
            ToPoint = "B",
            B1 = 1.5000,
            B2 = 1.7000,
            F1 = 1.2000,
            F2 = 1.3000,
            DistanceB1 = 40.0,
            DistanceB2 = 40.2,
            DistanceF1 = 39.8,
            DistanceF2 = 40.0,
            MeasurementMode = MeasurementMode.BFFB,
            ObservationOrder = ObservationOrder.BFFB
        };

        bool ok = RawObservationDifferenceBuilder.TryBuild(raw, 99, out LevelDifference? result, out string? error);

        Assert.True(ok, error);
        Assert.NotNull(result);
        Assert.Equal(99, result.Id);
        Assert.Equal(7, result.LineId);
        Assert.Equal(3, result.Sequence);
        Assert.InRange(result.HeightDifference, 0.35 - 1e-12, 0.35 + 1e-12);
        Assert.InRange(result.DistanceMeters, 80.0 - 1e-12, 80.0 + 1e-12);
    }

    [Fact]
    public void SingleReadingStation_UsesBackMinusForeAndDistanceSum()
    {
        var raw = new RawObservation
        {
            Id = 1,
            LineId = 1,
            Sequence = 0,
            FromPoint = "A",
            ToPoint = "B",
            B1 = 1.8000,
            F1 = 1.2500,
            DistanceB1 = 42.5,
            DistanceF1 = 41.5,
            MeasurementMode = MeasurementMode.BF,
            ObservationOrder = ObservationOrder.BF
        };

        bool ok = RawObservationDifferenceBuilder.TryBuild(raw, -1, out LevelDifference? result, out string? error);

        Assert.True(ok, error);
        Assert.NotNull(result);
        Assert.InRange(result.HeightDifference, 0.55 - 1e-12, 0.55 + 1e-12);
        Assert.InRange(result.DistanceMeters, 84.0 - 1e-12, 84.0 + 1e-12);
    }
}
