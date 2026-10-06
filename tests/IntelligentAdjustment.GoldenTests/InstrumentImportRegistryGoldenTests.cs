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
    public async Task GeoMaxImport_DoesNotInventTerminalKnownHeight_AndRemovesEmptyDefaultLine()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string projectFile = Path.Combine(Path.GetTempPath(), $"ia-geomax-{Guid.NewGuid():N}.iap");
        string mdt = Path.Combine(Path.GetTempPath(), $"GeoMax_ZDL-{Guid.NewGuid():N}.mdt");

        try
        {
            // Exact contents of the legacy Sample/GeoMax_ZDL.mdt shipped with
            // AdjustLevel. ReducedLevel is the current station's starting elevation;
            // foresight rows repeat it and therefore must not create a terminal
            // control point.
            const string source = """
                PtID       Order       Height      Distance    StaffType   ReducedLevel Type
                @0         2           0.0000      0.000       2           0.0000      107
                1          B           1.24193     7.4414      2           30.00000    107
                2          F           0.82182     7.7296      2           30.00000    107
                2          F           0.82189     7.7293      2           30.00000    107
                1          B           1.24166     7.4540      2           30.00000    107
                3          F           1.14182     8.7067      2           30.41994    107
                2          B           0.88865     8.8756      2           30.41994    107
                2          B           0.88855     8.8780      2           30.41994    107
                3          F           1.14180     8.7210      2           30.41994    107
                3          B           1.16185     19.0175     2           30.16673    107
                4          F           0.90928     19.0166     2           30.16673    107
                4          F           0.90928     19.0243     2           30.16673    107
                3          B           1.16189     19.0186     2           30.16673    107
                5          F           1.36908     21.9415     2           30.41933    107
                4          B           0.91373     21.9373     2           30.41933    107
                4          B           0.91373     21.9394     2           30.41933    107
                5          F           1.36903     21.9430     2           30.41933    107
                5          B           1.41232     3.7342      2           29.96400    107
                6          F           0.96281     4.0967      2           29.96400    107
                6          F           0.96294     4.0902      2           29.96400    107
                5          B           1.41237     3.7338      2           29.96400    107
                7          F           0.95646     5.3243      2           30.41347    107
                6          B           0.95408     5.7204      2           30.41347    107
                6          B           0.95410     5.7148      2           30.41347    107
                7          F           0.95644     5.3226      2           30.41347    107
                7          B           0.93953     16.0873     2           30.41111    107
                8          F           1.16521     16.3633     2           30.41111    107
                8          F           1.16517     16.3644     2           30.41111    107
                7          B           0.93934     16.0981     2           30.41111    107
                9          F           0.96435     16.9408     2           30.18536    107
                8          B           1.19537     17.5108     2           30.18536    107
                8          B           1.19529     17.5059     2           30.18536    107
                9          F           0.96420     16.9529     2           30.18536    107
                9          B           0.94785     18.0920     2           30.41641    107
                10         F           1.19213     18.1860     2           30.41641    107
                10         F           1.19225     18.1964     2           30.41641    107
                9          B           0.94750     18.1134     2           30.41641    107
                11         F           1.24577     19.6183     2           30.17190    107
                10         B           1.07289     19.6978     2           30.17190    107
                10         B           1.07289     19.6978     2           30.17190    107
                11         F           1.24518     19.6307     2           30.17190    107
                """;

            await File.WriteAllTextAsync(mdt, source, cancellationToken);

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

            KnownHeight start = Assert.Single(workspace.KnownHeights);
            Assert.Equal("1", start.PointName);
            Assert.Equal(30.0, start.Height);
            Assert.DoesNotContain(workspace.KnownHeights, x => x.PointName == "11");

            // With only one actual control point the open chain must not be presented
            // as an attached route with a fabricated closure.
            Assert.Empty(
                new RouteSearchEngine().Search(
                    workspace.LevelDifferences,
                    workspace.KnownHeights,
                    workspace.Settings));

            // When the real terminal control height is supplied, the sample becomes a
            // valid attached route and its closure is approximately -0.695 mm.
            KnownHeight[] knownWithTerminal =
                workspace.KnownHeights.Append(new KnownHeight("11", 30.0)).ToArray();

            NetworkRoute route = Assert.Single(
                new RouteSearchEngine().Search(
                    workspace.LevelDifferences,
                    knownWithTerminal,
                    workspace.Settings)
                .Where(x => x.RouteType == RouteType.Attached));

            Assert.Equal(10, route.EdgeCount);
            Assert.Equal("1", route.Points[0]);
            Assert.Equal("11", route.Points[^1]);
            Assert.InRange(route.ClosureMeters, -0.000695 - 1e-12, -0.000695 + 1e-12);
            Assert.True(Math.Abs(route.ClosureMeters) <= route.LengthToleranceMeters);
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
