using IntelligentAdjustment.Core.Adjustment;
using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.GoldenTests;

public sealed class QuasiStableAdjustmentGoldenTests
{
    [Fact]
    public void FourPointLoop_WithAAndCStable_MatchesAdjustLevelGolden()
    {
        var solver = new QuasiStableAdjustmentSolver();
        var result = solver.Solve(
            GoldenData.FourPointLoop(),
            [new KnownHeight("A", 100.00000), new KnownHeight("C", 102.01000)]);

        AssertClose(4.0, result.QuasiStableLambda!.Value, 1e-12);
        AssertClose(100.00450, Height(result, "A"), 1e-10);
        AssertClose(101.00500, Height(result, "B"), 1e-10);
        AssertClose(102.00550, Height(result, "C"), 1e-10);
        AssertClose(103.00600, Height(result, "D"), 1e-10);

        double correctionA = Height(result, "A") - 100.00000;
        double correctionC = Height(result, "C") - 102.01000;
        AssertClose(0, correctionA + correctionC, 1e-10);

        foreach (var difference in result.Differences)
        {
            AssertClose(0.00050, difference.Residual, 1e-10);
        }

        AssertClose(0.00223606797749979, result.UnitWeightStandardDeviation, 1e-12);
        AssertClose(0.000661437827766148, Error(result, "A"), 1e-12);
        AssertClose(0.000750000000000000, Error(result, "B"), 1e-12);
        AssertClose(0.000661437827766148, Error(result, "C"), 1e-12);
        AssertClose(0.000750000000000000, Error(result, "D"), 1e-12);
        Assert.Equal(2, result.DegreesOfFreedom);
        Assert.True(result.Converged);
        Assert.InRange(result.Iterations, 1, 3);
    }

    [Fact]
    public void ScalingAllDistancesByTen_ScalesLambdaButPreservesReportedHeightErrors()
    {
        var solver = new QuasiStableAdjustmentSolver();
        var result = solver.Solve(
            GoldenData.FourPointLoop(distanceMeters: 1000),
            [new KnownHeight("A", 100.00000), new KnownHeight("C", 102.01000)]);

        AssertClose(0.4, result.QuasiStableLambda!.Value, 1e-12);
        AssertClose(0.000661437827766148, Error(result, "A"), 1e-12);
        AssertClose(0.000750000000000000, Error(result, "B"), 1e-12);
    }

    [Fact]
    public void ExtremeInitialStableHeights_StillPreserveStablePointCentroid()
    {
        var solver = new QuasiStableAdjustmentSolver();
        var result = solver.Solve(
            GoldenData.FourPointLoop(),
            [new KnownHeight("A", 100.00000), new KnownHeight("C", 152.01000)]);

        AssertClose(252.01000, Height(result, "A") + Height(result, "C"), 1e-9);
        Assert.True(result.Converged);
    }

    private static double Height(AdjustmentResult result, string name) => result.Heights.Single(x => x.PointName == name).Height;
    private static double Error(AdjustmentResult result, string name) => result.Heights.Single(x => x.PointName == name).StandardError;
    private static void AssertClose(double expected, double actual, double tolerance) => Assert.InRange(actual, expected - tolerance, expected + tolerance);
}
