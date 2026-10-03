using IntelligentAdjustment.Domain;
using Microsoft.Data.Sqlite;

namespace IntelligentAdjustment.Infrastructure;

public sealed class ProjectRepository
{
    private readonly ProjectDatabase _database;

    public ProjectRepository(ProjectDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public async Task<ProjectMetadata> LoadMetadataAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ProjectName, ProjectNumber, UnitName, ProjectLeader, Reviewer,
                   CreatedAtUtc, UpdatedAtUtc
            FROM ProjectInfo
            WHERE Id = 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("ProjectInfo record is missing.");
        }

        return new ProjectMetadata(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            DateTimeOffset.Parse(reader.GetString(5)),
            DateTimeOffset.Parse(reader.GetString(6)));
    }

    public async Task<ProjectSettings> LoadSettingsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
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
            FROM ProjectSettings
            WHERE Id = 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("ProjectSettings record is missing.");
        }

        return new ProjectSettings
        {
            ToleranceMode = (ClosureToleranceMode)reader.GetInt32(0),
            DistanceToleranceCoefficientMm = reader.GetDouble(1),
            StationToleranceCoefficientMm = reader.GetDouble(2),
            AdjustmentMethod = (AdjustmentMethod)reader.GetInt32(3),
            AutoMergeTransitionPoints = reader.GetInt32(4) != 0,
            AutoUpdateLevelDifferences = reader.GetInt32(5) != 0,
            DistanceDecimals = reader.GetInt32(6),
            HeightDecimals = reader.GetInt32(7)
        };
    }

    public async Task<ProjectRevisionState> LoadRevisionAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT InputRevision, ResultRevision, LastCalculatedAtUtc
            FROM ProjectState
            WHERE Id = 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("ProjectState record is missing.");
        }

        DateTimeOffset? calculatedAt = reader.IsDBNull(2)
            ? null
            : DateTimeOffset.Parse(reader.GetString(2));

        return new ProjectRevisionState(reader.GetInt64(0), reader.GetInt64(1), calculatedAt);
    }

    public async Task<IReadOnlyList<ObservationLineInfo>> LoadLinesAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<ObservationLineInfo>();
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, DisplayOrder, Name, SourceFileName, InstrumentType, CreatedAtUtc
            FROM ObservationLine
            ORDER BY DisplayOrder, Id;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new ObservationLineInfo(
                reader.GetInt64(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                DateTimeOffset.Parse(reader.GetString(5))));
        }

        return result;
    }

    public async Task<IReadOnlyList<LevelDifference>> LoadLevelDifferencesAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<LevelDifference>();
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, ObservationLineId, Sequence, FromPoint, ToPoint,
                   HeightDifference, DistanceMeters, StationCount,
                   ToPointRole, IsRoleManuallySpecified, Comment
            FROM LevelDifference
            ORDER BY ObservationLineId, Sequence, Id;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new LevelDifference(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetDouble(5),
                reader.GetDouble(6),
                reader.GetInt32(7),
                (PointRole)reader.GetInt32(8),
                reader.GetInt32(9) != 0,
                reader.IsDBNull(10) ? null : reader.GetString(10)));
        }

        return result;
    }

    public async Task<IReadOnlyList<KnownHeight>> LoadKnownHeightsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<KnownHeight>();
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT PointName, Height, Comment
            FROM KnownHeight
            ORDER BY PointName COLLATE NOCASE;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new KnownHeight(
                reader.GetString(0),
                reader.GetDouble(1),
                reader.IsDBNull(2) ? null : reader.GetString(2)));
        }

        return result;
    }

    public async Task SaveProjectInputsAsync(
        ProjectMetadata metadata,
        ProjectSettings settings,
        IReadOnlyList<LevelDifference> levelDifferences,
        IReadOnlyList<KnownHeight> knownHeights,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();

        await UpdateMetadataAsync(connection, transaction, metadata, cancellationToken);
        await UpdateSettingsAsync(connection, transaction, settings, cancellationToken);

        await ExecuteAsync(connection, transaction, "DELETE FROM LevelDifference;", cancellationToken);
        foreach (var difference in levelDifferences.OrderBy(x => x.LineId).ThenBy(x => x.Sequence))
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO LevelDifference(
                    ObservationLineId, Sequence, FromPoint, ToPoint,
                    HeightDifference, DistanceMeters, StationCount,
                    ToPointRole, IsRoleManuallySpecified, Comment)
                VALUES($lineId, $sequence, $fromPoint, $toPoint,
                       $heightDifference, $distanceMeters, $stationCount,
                       $toPointRole, $manual, $comment);
                """;
            command.Parameters.AddWithValue("$lineId", difference.LineId);
            command.Parameters.AddWithValue("$sequence", difference.Sequence);
            command.Parameters.AddWithValue("$fromPoint", difference.FromPoint);
            command.Parameters.AddWithValue("$toPoint", difference.ToPoint);
            command.Parameters.AddWithValue("$heightDifference", difference.HeightDifference);
            command.Parameters.AddWithValue("$distanceMeters", difference.DistanceMeters);
            command.Parameters.AddWithValue("$stationCount", difference.StationCount);
            command.Parameters.AddWithValue("$toPointRole", (int)difference.ToPointRole);
            command.Parameters.AddWithValue("$manual", difference.IsRoleManuallySpecified ? 1 : 0);
            command.Parameters.AddWithValue("$comment", (object?)difference.Comment ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await ExecuteAsync(connection, transaction, "DELETE FROM KnownHeight;", cancellationToken);
        foreach (var known in knownHeights)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO KnownHeight(PointName, Height, Comment)
                VALUES($name, $height, $comment);
                """;
            command.Parameters.AddWithValue("$name", known.PointName);
            command.Parameters.AddWithValue("$height", known.Height);
            command.Parameters.AddWithValue("$comment", (object?)known.Comment ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await ExecuteAsync(
            connection,
            transaction,
            """
            UPDATE ProjectState
            SET InputRevision = InputRevision + 1
            WHERE Id = 1;
            """,
            cancellationToken);

        transaction.Commit();
    }

    public async Task<long> ImportOutAsync(
        string sourceFileName,
        byte[] sourceBytes,
        string sha256,
        IReadOnlyList<LevelDifference> differences,
        IReadOnlyList<KnownHeight> knownHeights,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();

        int displayOrder;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT COALESCE(MAX(DisplayOrder), -1) + 1 FROM ObservationLine;";
            displayOrder = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        }

        long lineId;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO ObservationLine(DisplayOrder, Name, SourceFileName, InstrumentType, CreatedAtUtc)
                VALUES($order, $name, $source, 'OUT', $created);
                SELECT last_insert_rowid();
                """;
            command.Parameters.AddWithValue("$order", displayOrder);
            command.Parameters.AddWithValue("$name", Path.GetFileNameWithoutExtension(sourceFileName));
            command.Parameters.AddWithValue("$source", Path.GetFileName(sourceFileName));
            command.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
            lineId = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
        }

        foreach (var difference in differences.OrderBy(x => x.Sequence))
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO LevelDifference(
                    ObservationLineId, Sequence, FromPoint, ToPoint,
                    HeightDifference, DistanceMeters, StationCount,
                    ToPointRole, IsRoleManuallySpecified, Comment)
                VALUES($lineId, $sequence, $fromPoint, $toPoint,
                       $heightDifference, $distanceMeters, $stationCount,
                       $role, $manual, $comment);
                """;
            command.Parameters.AddWithValue("$lineId", lineId);
            command.Parameters.AddWithValue("$sequence", difference.Sequence);
            command.Parameters.AddWithValue("$fromPoint", difference.FromPoint);
            command.Parameters.AddWithValue("$toPoint", difference.ToPoint);
            command.Parameters.AddWithValue("$heightDifference", difference.HeightDifference);
            command.Parameters.AddWithValue("$distanceMeters", difference.DistanceMeters);
            command.Parameters.AddWithValue("$stationCount", difference.StationCount);
            command.Parameters.AddWithValue("$role", (int)difference.ToPointRole);
            command.Parameters.AddWithValue("$manual", difference.IsRoleManuallySpecified ? 1 : 0);
            command.Parameters.AddWithValue("$comment", (object?)difference.Comment ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var known in knownHeights)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT OR IGNORE INTO KnownHeight(PointName, Height, Comment)
                VALUES($name, $height, $comment);
                """;
            command.Parameters.AddWithValue("$name", known.PointName);
            command.Parameters.AddWithValue("$height", known.Height);
            command.Parameters.AddWithValue("$comment", (object?)known.Comment ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO ImportSource(ObservationLineId, FileName, InstrumentType, ImportedAtUtc, Sha256, OriginalData)
                VALUES($lineId, $fileName, 'OUT', $importedAt, $sha256, $data);
                """;
            command.Parameters.AddWithValue("$lineId", lineId);
            command.Parameters.AddWithValue("$fileName", Path.GetFileName(sourceFileName));
            command.Parameters.AddWithValue("$importedAt", DateTimeOffset.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$sha256", sha256);
            command.Parameters.Add("$data", SqliteType.Blob).Value = sourceBytes;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await ExecuteAsync(
            connection,
            transaction,
            "UPDATE ProjectState SET InputRevision = InputRevision + 1 WHERE Id = 1;",
            cancellationToken);

        transaction.Commit();
        return lineId;
    }

    public async Task SaveCalculationAsync(
        IReadOnlyList<NetworkRoute> routes,
        AdjustmentResult result,
        ProjectSettings settings,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();

        long inputRevision;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT InputRevision FROM ProjectState WHERE Id = 1;";
            inputRevision = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
        }

        long runId;
        string now = DateTimeOffset.UtcNow.ToString("O");
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO CalculationRun(
                    InputRevision, AdjustmentMethod, ToleranceMode,
                    StartedAtUtc, CompletedAtUtc, IsSuccessful,
                    UnitWeightStandardDeviation, DegreesOfFreedom, Iterations, QuasiStableLambda)
                VALUES($revision, $method, $toleranceMode,
                       $now, $now, 1,
                       $sigma0, $dof, $iterations, $lambda);
                SELECT last_insert_rowid();
                """;
            command.Parameters.AddWithValue("$revision", inputRevision);
            command.Parameters.AddWithValue("$method", (int)result.Method);
            command.Parameters.AddWithValue("$toleranceMode", (int)settings.ToleranceMode);
            command.Parameters.AddWithValue("$now", now);
            command.Parameters.AddWithValue("$sigma0", result.UnitWeightStandardDeviation);
            command.Parameters.AddWithValue("$dof", result.DegreesOfFreedom);
            command.Parameters.AddWithValue("$iterations", result.Iterations);
            command.Parameters.AddWithValue("$lambda", (object?)result.QuasiStableLambda ?? DBNull.Value);
            runId = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
        }

        foreach (var route in routes)
        {
            long routeId;
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO ClosureRoute(
                        CalculationRunId, RouteIndex, RouteType, EdgeCount,
                        LengthMeters, StationCount, ClosureMeters,
                        LengthToleranceMeters, StationToleranceMeters)
                    VALUES($runId, $routeIndex, $routeType, $edgeCount,
                           $lengthMeters, $stationCount, $closureMeters,
                           $lengthTolerance, $stationTolerance);
                    SELECT last_insert_rowid();
                    """;
                command.Parameters.AddWithValue("$runId", runId);
                command.Parameters.AddWithValue("$routeIndex", route.Index);
                command.Parameters.AddWithValue("$routeType", (int)route.RouteType);
                command.Parameters.AddWithValue("$edgeCount", route.EdgeCount);
                command.Parameters.AddWithValue("$lengthMeters", route.LengthMeters);
                command.Parameters.AddWithValue("$stationCount", route.StationCount);
                command.Parameters.AddWithValue("$closureMeters", route.ClosureMeters);
                command.Parameters.AddWithValue("$lengthTolerance", route.LengthToleranceMeters);
                command.Parameters.AddWithValue("$stationTolerance", route.StationToleranceMeters);
                routeId = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
            }

            for (int i = 0; i < route.Points.Count; i++)
            {
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO ClosureRoutePoint(ClosureRouteId, Sequence, PointName)
                    VALUES($routeId, $sequence, $pointName);
                    """;
                command.Parameters.AddWithValue("$routeId", routeId);
                command.Parameters.AddWithValue("$sequence", i);
                command.Parameters.AddWithValue("$pointName", route.Points[i]);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        foreach (var height in result.Heights)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO AdjustmentResultHeight(
                    CalculationRunId, PointName, Height, StandardError, IsReferencePoint)
                VALUES($runId, $pointName, $height, $error, $reference);
                """;
            command.Parameters.AddWithValue("$runId", runId);
            command.Parameters.AddWithValue("$pointName", height.PointName);
            command.Parameters.AddWithValue("$height", height.Height);
            command.Parameters.AddWithValue("$error", height.StandardError);
            command.Parameters.AddWithValue("$reference", height.IsReferencePoint ? 1 : 0);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var difference in result.Differences)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO AdjustmentResultDifference(
                    CalculationRunId, LevelDifferenceId,
                    FromPoint, ToPoint, ObservedDifference, AdjustedDifference,
                    Residual, DistanceMeters, StationCount, StandardError)
                VALUES($runId, $levelDifferenceId,
                       $fromPoint, $toPoint, $observed, $adjusted,
                       $residual, $distance, $stations, $error);
                """;
            command.Parameters.AddWithValue("$runId", runId);
            command.Parameters.AddWithValue("$levelDifferenceId", difference.ObservationId);
            command.Parameters.AddWithValue("$fromPoint", difference.FromPoint);
            command.Parameters.AddWithValue("$toPoint", difference.ToPoint);
            command.Parameters.AddWithValue("$observed", difference.ObservedDifference);
            command.Parameters.AddWithValue("$adjusted", difference.AdjustedDifferenceValue);
            command.Parameters.AddWithValue("$residual", difference.Residual);
            command.Parameters.AddWithValue("$distance", difference.DistanceMeters);
            command.Parameters.AddWithValue("$stations", difference.StationCount);
            command.Parameters.AddWithValue("$error", difference.StandardError);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE ProjectState
                SET ResultRevision = InputRevision,
                    LastCalculatedAtUtc = $now
                WHERE Id = 1;
                """;
            command.Parameters.AddWithValue("$now", now);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        transaction.Commit();
    }

    private static async Task UpdateMetadataAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ProjectMetadata metadata,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE ProjectInfo
            SET ProjectName = $name,
                ProjectNumber = $number,
                UnitName = $unit,
                ProjectLeader = $leader,
                Reviewer = $reviewer,
                UpdatedAtUtc = $updated
            WHERE Id = 1;
            """;
        command.Parameters.AddWithValue("$name", metadata.ProjectName);
        command.Parameters.AddWithValue("$number", metadata.ProjectNumber);
        command.Parameters.AddWithValue("$unit", metadata.UnitName);
        command.Parameters.AddWithValue("$leader", metadata.ProjectLeader);
        command.Parameters.AddWithValue("$reviewer", metadata.Reviewer);
        command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpdateSettingsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ProjectSettings settings,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE ProjectSettings
            SET ToleranceMode = $toleranceMode,
                DistanceToleranceCoefficientMm = $distanceCoefficient,
                StationToleranceCoefficientMm = $stationCoefficient,
                AdjustmentMethod = $method,
                AutoMergeTransitionPoints = $autoMerge,
                AutoUpdateLevelDifferences = $autoUpdate,
                DistanceDecimals = $distanceDecimals,
                HeightDecimals = $heightDecimals
            WHERE Id = 1;
            """;
        command.Parameters.AddWithValue("$toleranceMode", (int)settings.ToleranceMode);
        command.Parameters.AddWithValue("$distanceCoefficient", settings.DistanceToleranceCoefficientMm);
        command.Parameters.AddWithValue("$stationCoefficient", settings.StationToleranceCoefficientMm);
        command.Parameters.AddWithValue("$method", (int)settings.AdjustmentMethod);
        command.Parameters.AddWithValue("$autoMerge", settings.AutoMergeTransitionPoints ? 1 : 0);
        command.Parameters.AddWithValue("$autoUpdate", settings.AutoUpdateLevelDifferences ? 1 : 0);
        command.Parameters.AddWithValue("$distanceDecimals", settings.DistanceDecimals);
        command.Parameters.AddWithValue("$heightDecimals", settings.HeightDecimals);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
