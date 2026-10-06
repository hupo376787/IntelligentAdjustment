using IntelligentAdjustment.Application.Import;
using IntelligentAdjustment.Core.Routes;
using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.GoldenTests;

public sealed class InstrumentSampleParserGoldenTests
{
    [Fact]
    public async Task LeicaMdt_ParsesAlternatingBffbAndKnownHeight()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string file = TempFile(".mdt");
        try
        {
            await File.WriteAllTextAsync(
                file,
                """
                aBFFB,ZHNL,2026-10-04,10:00
                StartPt,ZHD,3.5000,1
                B1,ZHD,39.7339,1.5715
                F1,1,40.5335,1.8685
                F2,1,40.5229,1.8682
                B2,ZHD,39.7242,1.5715
                Results,1,-0.296823,3.2032
                """,
                cancellationToken);

            var importer = new LeicaDnaInstrumentDataImporter();
            InstrumentImportResult result = await importer.ParseAsync(file, cancellationToken);

            KnownHeight known = Assert.Single(result.KnownHeights);
            Assert.Equal("ZHD", known.PointName);
            Assert.Equal(3.5, known.Height);

            RawObservation raw = Assert.Single(result.RawObservations);
            Assert.Equal(MeasurementMode.AlternatingBFFB, raw.MeasurementMode);
            Assert.Equal(ObservationOrder.BFFB, raw.ObservationOrder);

            LevelDifference diff = Assert.Single(result.LevelDifferences);
            Assert.InRange(diff.HeightDifference, -0.29685 - 1e-12, -0.29685 + 1e-12);
            Assert.InRange(diff.DistanceMeters, 80.25725 - 1e-12, 80.25725 + 1e-12);
        }
        finally
        {
            Delete(file);
        }
    }

    [Fact]
    public async Task LeicaGsi_ParsesFixedWidthCodes()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string file = TempFile(".gsi");
        try
        {
            await File.WriteAllTextAsync(
                file,
                """
                410001+?......4
                110002+00000ZHD 83...6+00035000
                110003+00000ZHD 32...6+00397339 331.26+00015715
                110004+00000001 32...6+00405335 332.26+00018685
                110005+00000001 32...6+00405229 336.26+00018682
                110006+00000ZHD 32...6+00397242 335.26+00015715
                """,
                cancellationToken);

            var importer = new LeicaDnaInstrumentDataImporter();
            InstrumentImportResult result = await importer.ParseAsync(file, cancellationToken);

            Assert.Equal(3.5, Assert.Single(result.KnownHeights).Height);
            RawObservation raw = Assert.Single(result.RawObservations);
            Assert.Equal("ZHD", raw.FromPoint);
            Assert.Equal("1", raw.ToPoint);
            Assert.Equal(MeasurementMode.AlternatingBFFB, raw.MeasurementMode);
            Assert.InRange(
                Assert.Single(result.LevelDifferences).HeightDifference,
                -0.29685 - 1e-12,
                -0.29685 + 1e-12);
        }
        finally
        {
            Delete(file);
        }
    }

    [Fact]
    public async Task GeoMaxMdt_ParsesBffbWithoutInventingTerminalControlPoint()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string file = TempFile(".mdt");
        try
        {
            await File.WriteAllTextAsync(
                file,
                """
                PtID Order Height Distance StaffType ReducedLevel Type
                @0 2 0.0000 0.000 2 0.0000 107
                1 B 1.24193 7.4414 2 30.00000 107
                2 F 0.82182 7.7296 2 30.00000 107
                2 F 0.82189 7.7293 2 30.00000 107
                1 B 1.24166 7.4540 2 30.00000 107
                """,
                cancellationToken);

            var importer = new GeoMaxZdlInstrumentDataImporter();
            InstrumentImportResult result = await importer.ParseAsync(file, cancellationToken);

            KnownHeight known = Assert.Single(result.KnownHeights);
            Assert.Equal("1", known.PointName);
            Assert.Equal(30.0, known.Height);
            Assert.DoesNotContain(result.KnownHeights, x => x.PointName == "2");

            RawObservation raw = Assert.Single(result.RawObservations);
            Assert.Equal(ObservationOrder.BFFB, raw.ObservationOrder);
            Assert.Equal(MeasurementMode.AlternatingBFFB, raw.MeasurementMode);

            Assert.Empty(
                new RouteSearchEngine().Search(
                    result.LevelDifferences,
                    result.KnownHeights,
                    new ProjectSettings()));
        }
        finally
        {
            Delete(file);
        }
    }

    [Fact]
    public async Task SokkiaCsv_ParsesTypeOneAsBackAndTypeTwoAsFore()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string file = TempFile(".csv");
        try
        {
            await File.WriteAllTextAsync(
                file,
                """
                SDL30,15X4,010108,JOB03,0,73,2,,
                0001,0001,0,1,1,40.53,1.40786,20.00000,
                0002,0002,0,1,2,39.68,1.70199,19.70587,
                0003,0002,0,1,2,39.69,1.70185,19.70572,
                0004,0001,0,1,1,40.54,1.40757,20.00000,
                """,
                cancellationToken);

            var importer = new SokkiaSdlInstrumentDataImporter();
            InstrumentImportResult result = await importer.ParseAsync(file, cancellationToken);

            Assert.Equal("1", Assert.Single(result.KnownHeights).PointName);
            RawObservation raw = Assert.Single(result.RawObservations);
            Assert.Equal("1", raw.FromPoint);
            Assert.Equal("2", raw.ToPoint);
            Assert.Equal(ObservationOrder.BFFB, raw.ObservationOrder);
        }
        finally
        {
            Delete(file);
        }
    }

    [Fact]
    public async Task TopconDat_ParsesFourRowsAsOneStation()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string file = TempFile(".dat");
        try
        {
            await File.WriteAllTextAsync(
                file,
                """
                DL-100 TEXT
                Leveling
                JOB#:0823A START BM#:A 06/08/23 15:09
                GH(m): 100.0000
                Dist.(m) Staf.(m)
                A 3.37 0.1602 15:09
                1 3.37 0.1604 99.9998 15:09
                1 3.37 0.1603 15:10
                A 3.37 0.1603 100.0000 15:10
                END BM#:B 06/08/23 15:13
                """,
                cancellationToken);

            var importer = new TopconDlInstrumentDataImporter();
            InstrumentImportResult result = await importer.ParseAsync(file, cancellationToken);

            KnownHeight known = Assert.Single(result.KnownHeights);
            Assert.Equal("A", known.PointName);
            Assert.Equal(100.0, known.Height);

            RawObservation raw = Assert.Single(result.RawObservations);
            Assert.Equal(MeasurementMode.BFFB, raw.MeasurementMode);
            Assert.Equal("A", raw.FromPoint);
            Assert.Equal("1", raw.ToPoint);
        }
        finally
        {
            Delete(file);
        }
    }

    [Fact]
    public async Task TrimbleDat_RepeatedMeasurementAndStationUseFinalRemeasurement()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string file = TempFile(".dat");
        try
        {
            await File.WriteAllTextAsync(
                file,
                """
                For M5|Adr 1|TO Start-Line BFFB DC11| | | |
                For M5|Adr 2|KD1 175 DC11| | |Z 2.66220 m |
                For M5|Adr 3|KD1 175 01:00:003DC11|Rb 1.50000 m |HD 40.000 m | |
                For M5|Adr 4|KD1 A1 01:00:103DC11|Rf 1.70000 m |HD 40.000 m | |
                For M5|Adr 5|KD1 A1 01:00:203DC11|Rf 1.70000 m |HD 40.000 m | |
                For M5|Adr 6|KD1 175 01:00:303DC11|Rb 1.50000 m |HD 40.000 m | |
                For M5|Adr 7|TO Station repeated DC11| | | |
                For M5|Adr 8|KD1 175 01:01:003DC11|Rb 1.51000 m |HD 40.100 m | |
                For M5|Adr 9|KD1 A1 01:01:103DC11|Rf 1.71000 m |HD 40.200 m | |
                For M5|Adr 10|KD1 A1 01:01:203DC11|Rf 1.71000 m |HD 40.200 m | |
                For M5|Adr 11|KD1 175 01:01:303DC11|Rb 1.51000 m |HD 40.100 m | |
                For M5|Adr 12|KD1 A1 01:02:003DC11|Rb 1.30000 m |HD 41.000 m | |
                For M5|Adr 13|KD1 A2 01:02:103DC11|Rf 1.40000 m |HD 41.000 m | |
                For M5|Adr 14|TO Measurement repeated DC11| | | |
                For M5|Adr 15|KD1 A2 01:02:203DC11|Rf 1.41000 m |HD 41.100 m | |
                For M5|Adr 16|TO Station repeated DC11| | | |
                For M5|Adr 17|KD1 A1 01:03:003DC11|Rb 1.32000 m |HD 41.200 m | |
                For M5|Adr 18|KD1 A2 01:03:103DC11|Rf 1.42000 m |HD 41.300 m | |
                For M5|Adr 19|KD1 A2 01:03:203DC11|Rf 1.42100 m |HD 41.300 m | |
                For M5|Adr 20|KD1 A1 01:03:303DC11|Rb 1.32100 m |HD 41.200 m | |
                """,
                cancellationToken);

            var importer = new TrimbleDiNiInstrumentDataImporter();
            InstrumentImportResult result = await importer.ParseAsync(file, cancellationToken);

            Assert.Equal(2.66220, Assert.Single(result.KnownHeights).Height);
            Assert.Equal(2, result.RawObservations.Count);
            Assert.Equal(1.51000, result.RawObservations[0].B1);
            Assert.Equal("A1", result.RawObservations[1].FromPoint);
            Assert.Equal("A2", result.RawObservations[1].ToPoint);
        }
        finally
        {
            Delete(file);
        }
    }

    private static string TempFile(string extension) =>
        Path.Combine(Path.GetTempPath(), $"ia-parser-{Guid.NewGuid():N}{extension}");

    private static void Delete(string file)
    {
        if (File.Exists(file))
        {
            File.Delete(file);
        }
    }
}
