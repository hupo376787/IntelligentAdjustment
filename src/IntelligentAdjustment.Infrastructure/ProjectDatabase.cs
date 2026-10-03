using System.Reflection;
using Microsoft.Data.Sqlite;

namespace IntelligentAdjustment.Infrastructure;

public sealed class ProjectDatabase
{
    private readonly string _filePath;

    public ProjectDatabase(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
    }

    public string FilePath => _filePath;

    public static bool HasProjectExtension(string filePath) =>
        string.Equals(Path.GetExtension(filePath), ".iap", StringComparison.OrdinalIgnoreCase);

    public async Task CreateAsync(CancellationToken cancellationToken = default)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(_filePath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        string sql = await LoadSchemaSqlAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);

        string now = DateTimeOffset.UtcNow.ToString("O");
        await using var projectCommand = connection.CreateCommand();
        projectCommand.CommandText = """
            INSERT OR IGNORE INTO ProjectInfo(Id, CreatedAtUtc, UpdatedAtUtc)
            VALUES (1, $now, $now);
            """;
        projectCommand.Parameters.AddWithValue("$now", now);
        await projectCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    public SqliteConnection CreateConnection()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _filePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            ForeignKeys = true,
            Pooling = false
        };

        return new SqliteConnection(builder.ToString());
    }

    private static async Task<string> LoadSchemaSqlAsync(CancellationToken cancellationToken)
    {
        string assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? AppContext.BaseDirectory;
        string path = Path.Combine(assemblyDirectory, "Sql", "schema.sql");
        return await File.ReadAllTextAsync(path, cancellationToken);
    }
}
