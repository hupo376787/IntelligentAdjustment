using IntelligentAdjustment.Infrastructure;
using Microsoft.Data.Sqlite;

namespace IntelligentAdjustment.GoldenTests;

public sealed class ProjectDatabaseGoldenTests
{
    [Fact]
    public async Task NewProject_IsSingleSqliteIap_WithExpectedDefaults()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string file = Path.Combine(Path.GetTempPath(), $"ia-{Guid.NewGuid():N}.iap");
        try
        {
            var db = new ProjectDatabase(file);
            await db.CreateAsync(cancellationToken);

            Assert.True(File.Exists(file));
            Assert.True(ProjectDatabase.HasProjectExtension(file));

            await using (SqliteConnection connection = db.CreateConnection())
            {
                await connection.OpenAsync(cancellationToken);
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    SELECT ToleranceMode,
                           DistanceToleranceCoefficientMm,
                           StationToleranceCoefficientMm,
                           AdjustmentMethod,
                           AutoMergeTransitionPoints,
                           AutoUpdateLevelDifferences,
                           DistanceDecimals,
                           HeightDecimals
                    FROM ProjectSettings WHERE Id = 1;
                    """;

                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                Assert.True(await reader.ReadAsync(cancellationToken));
                Assert.Equal(0L, reader.GetInt64(0));
                Assert.Equal(4.0, reader.GetDouble(1));
                Assert.Equal(0.3, reader.GetDouble(2));
                Assert.Equal(0L, reader.GetInt64(3));
                Assert.Equal(0L, reader.GetInt64(4));
                Assert.Equal(1L, reader.GetInt64(5));
                Assert.Equal(4L, reader.GetInt64(6));
                Assert.Equal(5L, reader.GetInt64(7));
            }
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
    public async Task KnownHeight_CanBeStoredBeforePointExistsInNetwork()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string file = Path.Combine(Path.GetTempPath(), $"ia-{Guid.NewGuid():N}.iap");
        try
        {
            var db = new ProjectDatabase(file);
            await db.CreateAsync(cancellationToken);

            await using (SqliteConnection connection = db.CreateConnection())
            {
                await connection.OpenAsync(cancellationToken);

                await using var command = connection.CreateCommand();
                command.CommandText = "INSERT INTO KnownHeight(PointName, Height) VALUES ('X_NOT_IN_NETWORK', 123.456);";
                Assert.Equal(1, await command.ExecuteNonQueryAsync(cancellationToken));
            }
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
