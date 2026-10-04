using IntelligentAdjustment.Application.Models;
using IntelligentAdjustment.Application.Services;
using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.GoldenTests;

public sealed class MapAndReportPersistenceGoldenTests
{
    [Fact]
    public async Task SaveAndReopen_PreservesSketchCoordinatesAndReportText_WithoutInvalidatingCalculationInputs()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string file = Path.Combine(Path.GetTempPath(), $"ia-map-report-{Guid.NewGuid():N}.iap");

        try
        {
            var session = new ProjectSessionService();
            ProjectWorkspace workspace = await session.CreateAsync(file, cancellationToken);

            ProjectWorkspace edited = workspace with
            {
                MapPoints =
                [
                    new NetworkMapPoint("A", 100, 200, DateTimeOffset.UtcNow),
                    new NetworkMapPoint("B", 260, 340, DateTimeOffset.UtcNow)
                ],
                ReportText = new ReportTextContent(
                    TaskOverview: "任务概述",
                    NaturalGeography: "自然地理",
                    ExistingData: "已有资料",
                    ReferencedStandards: "项目采用规范",
                    TechnicalIndicators: "技术指标",
                    FieldWorkSummary: "外业工作量",
                    ConclusionAndRecommendations: "结论与建议")
            };

            ProjectWorkspace saved = await session.SaveAsync(
                edited,
                incrementInputRevision: false,
                cancellationToken);

            Assert.Equal(0, saved.Revision.InputRevision);
            Assert.Equal(2, saved.MapPoints.Count);
            Assert.Equal("任务概述", saved.ReportText.TaskOverview);

            session.Close();

            var reopenedSession = new ProjectSessionService();
            ProjectWorkspace reopened = await reopenedSession.OpenAsync(file, cancellationToken);

            Assert.Equal(2, reopened.MapPoints.Count);
            Assert.Contains(reopened.MapPoints, x => x.PointName == "A" && x.X == 100 && x.Y == 200);
            Assert.Equal("项目采用规范", reopened.ReportText.ReferencedStandards);
            Assert.Equal("结论与建议", reopened.ReportText.ConclusionAndRecommendations);
            Assert.Equal(0, reopened.Revision.InputRevision);
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
