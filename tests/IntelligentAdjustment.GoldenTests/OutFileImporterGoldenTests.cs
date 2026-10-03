using IntelligentAdjustment.Application.Import;

namespace IntelligentAdjustment.GoldenTests;

public sealed class OutFileImporterGoldenTests
{
    [Fact]
    public void Parse_KnownOutSample_ProducesDifferencesAndHeight()
    {
        string[] lines =
        [
            "From          To            Hei_Diff      Distance      Station",
            "1             2             -0.29420      80.2200       1",
            "2             3              0.09429      79.8850       1",
            "Height",
            "1             20.00000"
        ];

        var result = new OutFileImporter().Parse(lines);

        Assert.Equal(2, result.LevelDifferences.Count);
        Assert.Single(result.KnownHeights);
        Assert.Equal("1", result.LevelDifferences[0].FromPoint);
        Assert.Equal("2", result.LevelDifferences[0].ToPoint);
        Assert.InRange(result.LevelDifferences[0].HeightDifference, -0.29420 - 1e-12, -0.29420 + 1e-12);
        Assert.InRange(result.LevelDifferences[0].DistanceMeters, 80.2200 - 1e-12, 80.2200 + 1e-12);
        Assert.Equal(1, result.LevelDifferences[0].StationCount);
        Assert.Equal("1", result.KnownHeights[0].PointName);
        Assert.InRange(result.KnownHeights[0].Height, 20.0 - 1e-12, 20.0 + 1e-12);
    }
}
