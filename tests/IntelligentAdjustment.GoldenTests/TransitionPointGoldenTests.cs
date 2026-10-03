using IntelligentAdjustment.Core.Transitions;
using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.GoldenTests;

public sealed class TransitionPointGoldenTests
{
    [Fact]
    public void AutoDetect_MarksNumericDegreeTwoPointsButProtectsKnownPoints()
    {
        var processor = new TransitionPointProcessor();
        var source = GoldenData.TransitionBaseline(allAdjustment: true);

        var result = processor.AutoDetectTransitionPoints(source, new HashSet<string>(["2"], StringComparer.Ordinal));

        Assert.Equal(PointRole.TransitionPoint, result.Single(x => x.ToPoint == "1").ToPointRole);
        Assert.Equal(PointRole.AdjustmentPoint, result.Single(x => x.ToPoint == "2").ToPointRole);
        Assert.Equal(PointRole.TransitionPoint, result.Single(x => x.ToPoint == "3").ToPointRole);
        Assert.Equal(PointRole.AdjustmentPoint, result.Single(x => x.ToPoint == "B").ToPointRole);
        Assert.Equal(PointRole.AdjustmentPoint, result.Single(x => x.ToPoint == "C").ToPointRole);
    }

    [Fact]
    public void DeleteTransitionPoints_CompressesHeightDistanceAndStations()
    {
        var processor = new TransitionPointProcessor();
        var result = processor.DeleteTransitionPoints(GoldenData.TransitionBaseline());

        Assert.Equal(3, result.Count);
        AssertDifference(result[0], "A", "B", 0.60000, 330.0000, 3);
        AssertDifference(result[1], "B", "C", 0.40000, 270.0000, 2);
        AssertDifference(result[2], "C", "D", 0.70000, 150.0000, 1);
    }

    [Fact]
    public void MergeSelectedAndDelete_OnAllAdjustmentRows_CreatesCompressedSelectedChain()
    {
        var processor = new TransitionPointProcessor();
        var source = GoldenData.TransitionBaseline(allAdjustment: true);
        var selected = new HashSet<long> { 1, 2 };

        var result = processor.MergeSelectedAndDelete(source, selected);

        Assert.Equal(4, result.Count);
        AssertDifference(result[0], "A", "B", 0.60000, 330.0000, 3);
        AssertDifference(result[1], "B", "3", -0.10000, 130.0000, 1);
    }

    private static void AssertDifference(LevelDifference actual, string from, string to, double diff, double distance, int stations)
    {
        Assert.Equal(from, actual.FromPoint);
        Assert.Equal(to, actual.ToPoint);
        Assert.InRange(actual.HeightDifference, diff - 1e-12, diff + 1e-12);
        Assert.InRange(actual.DistanceMeters, distance - 1e-12, distance + 1e-12);
        Assert.Equal(stations, actual.StationCount);
    }
}
