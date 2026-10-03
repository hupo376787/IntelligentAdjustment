using IntelligentAdjustment.Core.Routes;
using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.GoldenTests;

public sealed class RouteSearchGoldenTests
{
    private static readonly ProjectSettings Settings = new();

    [Fact]
    public void DuplicateParallelEdges_FormTwoEdgeClosedLoop()
    {
        LevelDifference[] data =
        [
            GoldenData.D(1, 0, "A", "B", 1, 100),
            GoldenData.D(2, 1, "A", "B", 1, 100),
            GoldenData.D(3, 2, "B", "C", 1, 100)
        ];

        var routes = new RouteSearchEngine().FindClosedLoops(data, Settings);

        var route = Assert.Single(routes);
        Assert.Equal(RouteType.ClosedLoop, route.RouteType);
        Assert.Equal(2, route.EdgeCount);
        Assert.Equal(route.Points[0], route.Points[^1]);
        Assert.True(route.Edges.Select(x => x.ObservationId).ToHashSet().SetEquals(new long[] { 1, 2 }));
    }

    [Fact]
    public void SquareWithDiagonal_ChoosesTwoShortIndependentTriangles()
    {
        LevelDifference[] data =
        [
            GoldenData.D(1, 0, "A", "B", 1, 100),
            GoldenData.D(2, 1, "B", "C", 1, 100),
            GoldenData.D(3, 2, "C", "D", 1, 100),
            GoldenData.D(4, 3, "D", "A", -3, 100),
            GoldenData.D(5, 4, "A", "C", 2, 100)
        ];

        var routes = new RouteSearchEngine().FindClosedLoops(data, Settings);

        Assert.Equal(2, routes.Count);
        Assert.All(routes, x => Assert.Equal(3, x.EdgeCount));
        Assert.DoesNotContain(routes, x => x.EdgeCount == 4);
    }

    [Fact]
    public void SharedEdge_TwoTrianglesAreBothRetained()
    {
        LevelDifference[] data =
        [
            GoldenData.D(1, 0, "A", "B", 1, 100),
            GoldenData.D(2, 1, "B", "C", 1, 100),
            GoldenData.D(3, 2, "C", "A", -2, 100),
            GoldenData.D(4, 3, "B", "D", 2, 100),
            GoldenData.D(5, 4, "D", "C", -1, 100)
        ];

        var routes = new RouteSearchEngine().FindClosedLoops(data, Settings);

        Assert.Equal(2, routes.Count);
        Assert.All(routes, x => Assert.Equal(3, x.EdgeCount));
        Assert.Equal(2, routes.Count(x => x.Edges.Any(e => e.ObservationId == 2)));
    }

    [Fact]
    public void TailEdges_AreNotIncludedInClosedLoopBasis()
    {
        LevelDifference[] data =
        [
            GoldenData.D(1, 0, "A", "B", 1, 100),
            GoldenData.D(2, 1, "B", "C", 1, 100),
            GoldenData.D(3, 2, "C", "A", -2, 100),
            GoldenData.D(4, 3, "C", "D", 1, 100),
            GoldenData.D(5, 4, "D", "E", 1, 100)
        ];

        var route = Assert.Single(new RouteSearchEngine().FindClosedLoops(data, Settings));

        Assert.True(route.Edges.Select(x => x.ObservationId).ToHashSet().SetEquals(new long[] { 1, 2, 3 }));
    }

    [Fact]
    public void IntermediateKnownPoint_SplitsAttachedRoute()
    {
        LevelDifference[] data =
        [
            GoldenData.D(1, 0, "A", "B", 1, 100),
            GoldenData.D(2, 1, "B", "C", 1, 100),
            GoldenData.D(3, 2, "C", "D", 1, 100),
            GoldenData.D(4, 3, "D", "E", 1, 100)
        ];
        KnownHeight[] known =
        [
            new("A", 100),
            new("C", 102),
            new("E", 104)
        ];

        var attached = new RouteSearchEngine().Search(data, known, Settings)
            .Where(x => x.RouteType == RouteType.Attached)
            .ToArray();

        Assert.Equal(2, attached.Length);
        Assert.Contains(attached, x => PointPath(x) == "A-B-C");
        Assert.Contains(attached, x => PointPath(x) == "C-D-E");
    }

    [Fact]
    public void YBranch_SelectsNonRedundantAttachedRoutesThatCoverNetwork()
    {
        LevelDifference[] data =
        [
            GoldenData.D(1, 0, "A", "B", 1, 100),
            GoldenData.D(2, 1, "B", "C", 1, 100),
            GoldenData.D(3, 2, "C", "E", 1, 100),
            GoldenData.D(4, 3, "B", "D", 1, 100)
        ];
        KnownHeight[] known = [new("A", 100), new("D", 102), new("E", 103)];

        var attached = new RouteSearchEngine().Search(data, known, Settings)
            .Where(x => x.RouteType == RouteType.Attached)
            .ToArray();

        Assert.Equal(2, attached.Length);
        var covered = attached.SelectMany(x => x.Edges).Select(x => x.ObservationId).ToHashSet();
        Assert.True(covered.SetEquals(new long[] { 1, 2, 3, 4 }));
    }

    [Fact]
    public void TwoIndependentPathsBetweenSameKnownEndpoints_AreBothAttachedRoutesAndAlsoFormLoop()
    {
        LevelDifference[] data =
        [
            GoldenData.D(1, 0, "A", "B", 1, 100),
            GoldenData.D(2, 1, "B", "D", 2, 100),
            GoldenData.D(3, 2, "A", "C", 2, 100),
            GoldenData.D(4, 3, "C", "D", 1, 100)
        ];
        KnownHeight[] known = [new("A", 100), new("D", 103)];

        var routes = new RouteSearchEngine().Search(data, known, Settings);

        _ = Assert.Single(routes, x => x.RouteType == RouteType.ClosedLoop);
        Assert.Equal(2, routes.Count(x => x.RouteType == RouteType.Attached));
    }

    [Fact]
    public void AttachedClosureSign_IsObservedMinusKnownHeightDifference()
    {
        LevelDifference[] data =
        [
            GoldenData.D(1, 0, "A", "B", 1.000, 100),
            GoldenData.D(2, 1, "B", "C", 1.000, 100),
            GoldenData.D(3, 2, "C", "D", 1.005, 100)
        ];
        KnownHeight[] known = [new("A", 100), new("D", 103)];

        var route = Assert.Single(new RouteSearchEngine().Search(data, known, Settings));

        Assert.Equal(RouteType.Attached, route.RouteType);
        AssertClose(0.005, route.ClosureMeters, 1e-12);
        AssertClose(0.0021908902300206646, route.LengthToleranceMeters, 1e-12);
        AssertClose(0.0005196152422706632, route.StationToleranceMeters, 1e-12);
    }

    private static string PointPath(NetworkRoute route) => string.Join("-", route.Points);
    private static void AssertClose(double expected, double actual, double tolerance) => Assert.InRange(actual, expected - tolerance, expected + tolerance);
}
