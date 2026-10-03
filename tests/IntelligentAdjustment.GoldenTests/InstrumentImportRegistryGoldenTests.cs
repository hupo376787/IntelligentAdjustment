using IntelligentAdjustment.Application.Import;
using IntelligentAdjustment.Application.Models;
using IntelligentAdjustment.Application.Services;
using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.GoldenTests;

public sealed class InstrumentImportRegistryGoldenTests
{
    [Fact]
    public void Catalog_ContainsAllPlannedVendors_AndOnlyOutIsEnabledForNow()
    {
        var registry = new InstrumentImportRegistry();

        Assert.Equal(7, registry.Catalog.Count);
        Assert.Contains(registry.Catalog, x => x.Vendor == InstrumentVendor.LeicaDna);
        Assert.Contains(registry.Catalog, x => x.Vendor == InstrumentVendor.GeoMaxZdl);
        Assert.Contains(registry.Catalog, x => x.Vendor == InstrumentVendor.SokkiaSdl);
        Assert.Contains(registry.Catalog, x => x.Vendor == InstrumentVendor.TopconDl);
        Assert.Contains(registry.Catalog, x => x.Vendor == InstrumentVendor.TrimbleDiNi);
        Assert.Contains(registry.Catalog, x => x.Vendor == InstrumentVendor.LevNet);

        InstrumentImporterDescriptor outDescriptor =
            Assert.Single(registry.Catalog, x => x.Vendor == InstrumentVendor.GenericOut);

        Assert.True(outDescriptor.IsImplemented);
        Assert.IsType<OutInstrumentDataImporter>(
            registry.GetRequiredImporter(InstrumentVendor.GenericOut));

        Assert.Throws<NotSupportedException>(
            () => registry.GetRequiredImporter(InstrumentVendor.LeicaDna));
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
