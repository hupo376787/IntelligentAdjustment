using IntelligentAdjustment.Core.Adjustment;
using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.GoldenTests;

public sealed class ClassicalAdjustmentGoldenTests
{
    [Fact]
    public void FourPointLoop_WithAAndCFixed_MatchesAdjustLevelGolden()
    {
        var solver = new ClassicalAdjustmentSolver();
        var result = solver.Solve(
            GoldenData.FourPointLoop(),
            [new KnownHeight("A", 100.00000), new KnownHeight("C", 102.01000)]);

        Assert.Equal(AdjustmentMethod.Classical, result.Method);
        AssertClose(101.00500, Height(result, "B"), 1e-10);
        AssertClose(103.00600, Height(result, "D"), 1e-10);
        AssertClose(100.00000, Height(result, "A"), 1e-12);
        AssertClose(102.01000, Height(result, "C"), 1e-12);

        double[] expectedResiduals = [0.00500, 0.00500, -0.00400, -0.00400];
        for (int i = 0; i < expectedResiduals.Length; i++)
        {
            AssertClose(expectedResiduals[i], result.Differences[i].Residual, 1e-10);
        }

        AssertClose(0.0202484567313166, result.UnitWeightStandardDeviation, 1e-12);
        AssertClose(0.00452769256906871, Error(result, "B"), 1e-12);
        AssertClose(0.00452769256906871, Error(result, "D"), 1e-12);
        AssertClose(0, Error(result, "A"), 1e-12);
        AssertClose(0, Error(result, "C"), 1e-12);
        Assert.Equal(2, result.DegreesOfFreedom);
    }


    [Fact]
    public void LeicaGsiClosedLoop_MatchesVerifiedAdjustLevelGolden()
    {
        LevelDifference[] observations =
        [
            new(1, 1, 0, "ZHD",  "I350",  0.10815, 296.5545, 4),
            new(2, 1, 1, "I350", "I351", -0.96835, 257.1199, 4),
            new(3, 1, 2, "I351", "I352",  1.69395, 277.0288, 4),
            new(4, 1, 3, "I352", "I353", -0.72565, 533.9245, 8),
            new(5, 1, 4, "I353", "ZHD",  -0.10840, 296.5348, 4)
        ];

        var result = new ClassicalAdjustmentSolver().Solve(
            observations,
            [new KnownHeight("ZHD", 3.50000)]);

        AssertClose(3.60820355668094, Height(result, "I350"), 1e-12);
        AssertClose(2.63989999161431, Height(result, "I351"), 1e-12);
        AssertClose(4.33390002202373, Height(result, "I352"), 1e-12);
        AssertClose(3.60834644687681, Height(result, "I353"), 1e-12);

        AssertClose(0.000232763669636806, result.UnitWeightStandardDeviation, 1e-12);
        AssertClose(0.000114885535244691, Error(result, "I350"), 1e-12);
        AssertClose(0.000141418391167147, Error(result, "I351"), 1e-12);
        AssertClose(0.000149999998383181, Error(result, "I352"), 1e-12);
        AssertClose(0.000114882548515723, Error(result, "I353"), 1e-12);

        double[] expectedResiduals =
        [
            0.0000535566809387322,
            0.0000464349333673120,
            0.0000500304094277482,
            0.0000964248530773704,
            0.0000535531231888320
        ];
        for (int i = 0; i < expectedResiduals.Length; i++)
        {
            AssertClose(expectedResiduals[i], result.Differences[i].Residual, 1e-12);
        }
    }

    private static double Height(AdjustmentResult result, string name) => result.Heights.Single(x => x.PointName == name).Height;
    private static double Error(AdjustmentResult result, string name) => result.Heights.Single(x => x.PointName == name).StandardError;
    private static void AssertClose(double expected, double actual, double tolerance) => Assert.InRange(actual, expected - tolerance, expected + tolerance);
}
