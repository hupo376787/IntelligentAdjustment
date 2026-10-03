using IntelligentAdjustment.Application.Models;
using IntelligentAdjustment.Application.Services;

namespace IntelligentAdjustment.GoldenTests;

public sealed class PersistedCalculationGoldenTests
{
    [Fact]
    public async Task ReopenProject_RestoresLatestAdjustmentAndRoutes()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string projectFile = Path.Combine(Path.GetTempPath(), $"ia-result-{Guid.NewGuid():N}.iap");
        string outFile = Path.Combine(Path.GetTempPath(), $"ia-result-{Guid.NewGuid():N}.out");

        try
        {
            await File.WriteAllTextAsync(
                outFile,
                """
                From To Hei_Diff Distance Station
                A B 1.00000 100.0000 1
                B C 1.00000 100.0000 1
                C D 1.00000 100.0000 1
                D A -3.00200 100.0000 1
                Height
                A 100.00000
                C 102.01000
                """,
                cancellationToken);

            var firstSession = new ProjectSessionService();
            _ = await firstSession.CreateAsync(projectFile, cancellationToken);
            ProjectWorkspace imported = await firstSession.ImportOutFilesAsync([outFile], cancellationToken);
            CalculationBundle calculated = await firstSession.CalculateAsync(imported, cancellationToken);

            Assert.Equal(4, calculated.AdjustmentResult.Heights.Count);
            Assert.NotEmpty(calculated.Routes);

            firstSession.Close();

            var reopenedSession = new ProjectSessionService();
            ProjectWorkspace reopened = await reopenedSession.OpenAsync(projectFile, cancellationToken);
            CalculationBundle? restored = await reopenedSession.LoadLatestCalculationAsync(cancellationToken);

            Assert.NotNull(restored);
            Assert.False(reopened.Revision.ResultsAreStale);
            Assert.Equal(4, restored.AdjustmentResult.Heights.Count);
            Assert.Equal(calculated.AdjustmentResult.Method, restored.AdjustmentResult.Method);
            Assert.InRange(
                restored.AdjustmentResult.UnitWeightStandardDeviation,
                calculated.AdjustmentResult.UnitWeightStandardDeviation - 1e-12,
                calculated.AdjustmentResult.UnitWeightStandardDeviation + 1e-12);
            Assert.Equal(calculated.Routes.Count, restored.Routes.Count);
        }
        finally
        {
            if (File.Exists(outFile))
            {
                File.Delete(outFile);
            }

            if (File.Exists(projectFile))
            {
                File.Delete(projectFile);
            }
        }
    }
}
