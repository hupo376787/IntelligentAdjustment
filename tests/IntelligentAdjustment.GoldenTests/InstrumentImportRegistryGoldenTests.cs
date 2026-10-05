using System.Text;
using IntelligentAdjustment.Core.Routes;
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

            // Once imported data exists, the untouched default manual line is removed.
            Assert.Equal(2, workspace.Lines.Count);
            Assert.DoesNotContain(workspace.Lines, x => x.InstrumentType == "MANUAL");
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

    [Fact]
    public async Task GeoMaxImport_CreatesAttachedRoute_AndRemovesEmptyDefaultLine()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string projectFile = Path.Combine(Path.GetTempPath(), $"ia-geomax-{Guid.NewGuid():N}.iap");
        string mdt = Path.Combine(Path.GetTempPath(), $"GeoMax_ZDL-{Guid.NewGuid():N}.mdt");

        try
        {
            var source = new StringBuilder();
            source.AppendLine("PtID Order Height Distance StaffType ReducedLevel Type");
            source.AppendLine("@0 2 0.0000 0.000 2 0.0000 107");

            for (int station = 1; station <= 10; station++)
            {
                double fromHeight = 30.0 + (station - 1) * 0.1;
                double toHeight = fromHeight + 0.1;
                string from = station.ToString();
                string to = (station + 1).ToString();

                source.AppendLine(FormattableString.Invariant(
                    $"{from} B 1.50000 40.0000 2 {fromHeight:F5} 107"));
                source.AppendLine(FormattableString.Invariant(
                    $"{to} F 1.40000 40.0000 2 {toHeight:F5} 107"));
                source.AppendLine(FormattableString.Invariant(
                    $"{to} F 1.40000 40.0000 2 {toHeight:F5} 107"));
                source.AppendLine(FormattableString.Invariant(
                    $"{from} B 1.50000 40.0000 2 {fromHeight:F5} 107"));
            }

            await File.WriteAllTextAsync(mdt, source.ToString(), cancellationToken);

            var session = new ProjectSessionService();
            _ = await session.CreateAsync(projectFile, cancellationToken);

            ProjectWorkspace workspace = await session.ImportInstrumentFilesAsync(
                InstrumentVendor.GeoMaxZdl,
                [mdt],
                cancellationToken);

            ObservationLineInfo line = Assert.Single(workspace.Lines);
            Assert.Equal("GeoMax ZDL", line.InstrumentType);
            Assert.Equal(10, workspace.RawObservations.Count);
            Assert.Equal(10, workspace.LevelDifferences.Count);
            Assert.DoesNotContain(workspace.Lines, x => x.InstrumentType == "MANUAL");

            Assert.Contains(workspace.KnownHeights, x => x.PointName == "1");
            Assert.Contains(workspace.KnownHeights, x => x.PointName == "11");

            NetworkRoute route = Assert.Single(
                new RouteSearchEngine().Search(
                    workspace.LevelDifferences,
                    workspace.KnownHeights,
                    workspace.Settings)
                .Where(x => x.RouteType == RouteType.Attached));

            Assert.Equal(10, route.EdgeCount);
            Assert.Equal("1", route.Points[0]);
            Assert.Equal("11", route.Points[^1]);
        }
        finally
        {
            foreach (string file in new[] { mdt, projectFile })
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
        }
    }
}
