using IntelligentAdjustment.Application.Services;

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
            var workspace = await session.CreateAsync(file, cancellationToken);

            var line = Assert.Single(workspace.Lines);
            Assert.Equal(0, line.DisplayOrder);
            Assert.Equal("线路 0", line.Name);
            Assert.Equal("MANUAL", line.InstrumentType);
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
