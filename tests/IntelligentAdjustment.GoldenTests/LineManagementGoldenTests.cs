using IntelligentAdjustment.Application.Models;
using IntelligentAdjustment.Application.Services;
using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.GoldenTests;

public sealed class LineManagementGoldenTests
{
    [Fact]
    public async Task CreateRenameMoveDeleteLine_PreservesOrderAndInvalidatesOnlyWhenDataIsDeleted()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string file = Path.Combine(Path.GetTempPath(), $"ia-lines-{Guid.NewGuid():N}.iap");

        try
        {
            var session = new ProjectSessionService();
            ProjectWorkspace workspace = await session.CreateAsync(file, cancellationToken);
            long firstId = Assert.Single(workspace.Lines).Id;

            workspace = await session.CreateLineAsync("线路 B", cancellationToken);
            Assert.Equal(2, workspace.Lines.Count);
            long secondId = workspace.Lines.Single(x => x.Id != firstId).Id;
            Assert.Equal(0, workspace.Revision.InputRevision);

            workspace = await session.RenameLineAsync(secondId, "线路 B-重命名", cancellationToken);
            Assert.Equal("线路 B-重命名", workspace.Lines.Single(x => x.Id == secondId).Name);
            Assert.Equal(0, workspace.Revision.InputRevision);

            workspace = await session.MoveLineAsync(secondId, -1, cancellationToken);
            Assert.Equal(secondId, workspace.Lines.OrderBy(x => x.DisplayOrder).First().Id);
            Assert.Equal(0, workspace.Revision.InputRevision);

            workspace = await session.SaveAsync(
                workspace with
                {
                    LevelDifferences =
                    [
                        new LevelDifference(
                            -1,
                            secondId,
                            0,
                            "A",
                            "B",
                            1.0,
                            100.0,
                            1)
                    ]
                },
                incrementInputRevision: true,
                cancellationToken);

            Assert.Equal(1, workspace.Revision.InputRevision);

            workspace = await session.DeleteLineAsync(secondId, cancellationToken);
            Assert.Single(workspace.Lines);
            Assert.DoesNotContain(workspace.LevelDifferences, x => x.LineId == secondId);
            Assert.Equal(2, workspace.Revision.InputRevision);
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
