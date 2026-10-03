using IntelligentAdjustment.Application.Models;
using IntelligentAdjustment.Application.Services;
using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.GoldenTests;

public sealed class ProjectSessionGoldenTests
{
    [Fact]
    public async Task NewProject_CreatesEditableManualLine()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string file = Path.Combine(Path.GetTempPath(), $"ia-session-{Guid.NewGuid():N}.iap");

        try
        {
            var session = new ProjectSessionService();
            ProjectWorkspace workspace = await session.CreateAsync(file, cancellationToken);

            var line = Assert.Single(workspace.Lines);
            Assert.Equal(0, line.DisplayOrder);
            Assert.Equal("线路 0", line.Name);
            Assert.Equal("MANUAL", line.InstrumentType);
            Assert.Equal(0, workspace.Revision.InputRevision);
        }
        finally
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }

    [Fact]
    public async Task MetadataOnlySave_DoesNotInvalidateCalculationInputRevision()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string file = Path.Combine(Path.GetTempPath(), $"ia-revision-{Guid.NewGuid():N}.iap");

        try
        {
            var session = new ProjectSessionService();
            ProjectWorkspace workspace = await session.CreateAsync(file, cancellationToken);

            ProjectWorkspace metadataOnly = workspace with
            {
                Metadata = workspace.Metadata with { ProjectName = "仅修改项目名称" }
            };

            ProjectWorkspace afterMetadata = await session.SaveAsync(
                metadataOnly,
                incrementInputRevision: false,
                cancellationToken);

            Assert.Equal(0, afterMetadata.Revision.InputRevision);

            ProjectWorkspace changedInput = afterMetadata with
            {
                KnownHeights = [new KnownHeight("A", 100.0)]
            };

            ProjectWorkspace afterInput = await session.SaveAsync(
                changedInput,
                incrementInputRevision: true,
                cancellationToken);

            Assert.Equal(1, afterInput.Revision.InputRevision);
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
