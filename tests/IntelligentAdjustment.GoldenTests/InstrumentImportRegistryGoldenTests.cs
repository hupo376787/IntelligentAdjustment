using IntelligentAdjustment.Application.Import;
using IntelligentAdjustment.Application.Models;
using IntelligentAdjustment.Application.Services;
using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.GoldenTests;

public sealed class InstrumentImportRegistryGoldenTests
{
    [Fact]
    public void Catalog_ContainsAndEnablesAllLegacySampleImporters()
    {
        var registry = new InstrumentImportRegistry();

        Assert.Equal(7, registry.Catalog.Count);
        Assert.All(registry.Catalog, descriptor => Assert.True(descriptor.IsImplemented));

        Assert.IsType<LeicaDnaInstrumentDataImporter>(
            registry.GetRequiredImporter(InstrumentVendor.LeicaDna));
        Assert.IsType<GeoMaxZdlInstrumentDataImporter>(
            registry.GetRequiredImporter(InstrumentVendor.GeoMaxZdl));
        Assert.IsType<SokkiaSdlInstrumentDataImporter>(
            registry.GetRequiredImporter(InstrumentVendor.SokkiaSdl));
        Assert.IsType<TopconDlInstrumentDataImporter>(
            registry.GetRequiredImporter(InstrumentVendor.TopconDl));
        Assert.IsType<TrimbleDiNiInstrumentDataImporter>(
            registry.GetRequiredImporter(InstrumentVendor.TrimbleDiNi));
        Assert.IsType<LevNetInstrumentDataImporter>(
            registry.GetRequiredImporter(InstrumentVendor.LevNet));
        Assert.IsType<OutInstrumentDataImporter>(
            registry.GetRequiredImporter(InstrumentVendor.GenericOut));
    }

    [Fact]
    public async Task GenericOutImport_UsesSharedInstrumentPipeline_AndCreatesOneLinePerFile()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string projectFile = Path.Combine(Path.GetTempPath(), $"ia-import-{Guid.NewGuid():N}.iap");
        string first = Path.Combine(Path.GetTempPath(), $"line-a-{Guid.NewGuid():N}.out");
        string second = Path.Combine(Path.GetTempPath(), $"line-b-{Guid.NewGuid():N}.out");

        try
        {
            const string contentA = """
                From To Hei_Diff Distance Station
                A B 1.00000 100.0000 1
                Height
                A 100.00000
                """;

            const string contentB = """
                From To Hei_Diff Distance Station
                B C 0.50000 120.0000 1
                """;

            await File.WriteAllTextAsync(first, contentA, cancellationToken);
            await File.WriteAllTextAsync(second, contentB, cancellationToken);

            var session = new ProjectSessionService();
            _ = await session.CreateAsync(projectFile, cancellationToken);

            ProjectWorkspace workspace = await session.ImportInstrumentFilesAsync(
                InstrumentVendor.GenericOut,
                [first, second],
                cancellationToken);

            // New projects retain the default manual line, then each imported file creates one line.
            Assert.Equal(3, workspace.Lines.Count);
            Assert.Equal(2, workspace.Lines.Count(x => x.InstrumentType == "高差 OUT"));
            Assert.Equal(2, workspace.LevelDifferences.Count);
            Assert.Single(workspace.KnownHeights);
            Assert.Equal(2, workspace.Revision.InputRevision);
        }
        finally
        {
            foreach (string file in new[] { first, second, projectFile })
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
        }
    }
}
