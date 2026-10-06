using IntelligentAdjustment.Application.Models;
using IntelligentAdjustment.Application.Services;
using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.GoldenTests;

public sealed class RawObservationPersistenceGoldenTests
{
    [Fact]
    public async Task SaveAndReopen_PreservesRawObservationFields()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string file = Path.Combine(Path.GetTempPath(), $"ia-raw-{Guid.NewGuid():N}.iap");

        try
        {
            var session = new ProjectSessionService();
            ProjectWorkspace workspace = await session.CreateAsync(file, cancellationToken);
            Assert.Empty(workspace.Lines);

            workspace = await session.CreateLineAsync("线路 A", cancellationToken);
            long lineId = Assert.Single(workspace.Lines).Id;

            var raw = new RawObservation
            {
                Id = -1,
                LineId = lineId,
                Sequence = 0,
                FromPoint = "A",
                ToPoint = "B",
                B1 = 1.50000,
                B2 = 1.70000,
                F1 = 1.20000,
                F2 = 1.30000,
                DistanceB1 = 40.0,
                DistanceB2 = 40.2,
                DistanceF1 = 39.8,
                DistanceF2 = 40.0,
                MeasurementMode = MeasurementMode.BFFB,
                ObservationOrder = ObservationOrder.BFFB,
                IsValid = true,
                MeasuredAt = new DateTimeOffset(2026, 10, 4, 5, 30, 0, TimeSpan.FromHours(8)),
                TemperatureCelsius = 21.5,
                Comment = "golden"
            };

            ProjectWorkspace saved = await session.SaveAsync(
                workspace with { RawObservations = [raw] },
                incrementInputRevision: true,
                cancellationToken);

            Assert.Equal(1, saved.Revision.InputRevision);
            session.Close();

            var reopenedSession = new ProjectSessionService();
            ProjectWorkspace reopened = await reopenedSession.OpenAsync(file, cancellationToken);
            RawObservation actual = Assert.Single(reopened.RawObservations);

            Assert.Equal("A", actual.FromPoint);
            Assert.Equal("B", actual.ToPoint);
            Assert.Equal(MeasurementMode.BFFB, actual.MeasurementMode);
            Assert.Equal(ObservationOrder.BFFB, actual.ObservationOrder);
            Assert.Equal(21.5, actual.TemperatureCelsius);
            Assert.Equal("golden", actual.Comment);
            Assert.Equal(raw.MeasuredAt, actual.MeasuredAt);
            Assert.Equal(raw.B1, actual.B1);
            Assert.Equal(raw.DistanceF2, actual.DistanceF2);
        }
        finally
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }
}
