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

    public async Task<bool> RemoveEmptyDefaultManualLineAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();

        long? lineId = null;
        await using (var findCommand = connection.CreateCommand())
        {
            findCommand.Transaction = transaction;
            findCommand.CommandText = """
                SELECT l.Id
                FROM ObservationLine AS l
                WHERE l.Name = '线路 0'
                  AND COALESCE(l.InstrumentType, '') = 'MANUAL'
                  AND l.SourceFileName IS NULL
                  AND NOT EXISTS (
                      SELECT 1 FROM RawObservation AS r
                      WHERE r.ObservationLineId = l.Id)
                  AND NOT EXISTS (
                      SELECT 1 FROM LevelDifference AS d
                      WHERE d.ObservationLineId = l.Id)
                ORDER BY l.DisplayOrder, l.Id
                LIMIT 1;
                """;

            object? value = await findCommand.ExecuteScalarAsync(cancellationToken);
            if (value is not null && value != DBNull.Value)
            {
                lineId = Convert.ToInt64(value);
            }
        }

        if (lineId is null)
        {
            transaction.Commit();
            return false;
        }

        await using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = "DELETE FROM ObservationLine WHERE Id = $id;";
            deleteCommand.Parameters.AddWithValue("$id", lineId.Value);
            await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await NormalizeLineOrderAsync(connection, transaction, cancellationToken);
        transaction.Commit();
        return true;
    }

    public async Task<long> CreateLineAsync(
        string name,
        string? instrumentType = "MANUAL",
        CancellationToken cancellationToken = default)
    {
        string normalizedName = string.IsNullOrWhiteSpace(name) ? "新线路" : name.Trim();

        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();

        int displayOrder;
        await using (var orderCommand = connection.CreateCommand())
        {
            orderCommand.Transaction = transaction;
            orderCommand.CommandText = "SELECT COALESCE(MAX(DisplayOrder), -1) + 1 FROM ObservationLine;";
            displayOrder = Convert.ToInt32(await orderCommand.ExecuteScalarAsync(cancellationToken));
        }

        long id;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO ObservationLine(DisplayOrder, Name, SourceFileName, InstrumentType, CreatedAtUtc)
                VALUES($displayOrder, $name, NULL, $instrumentType, $createdAt);
                SELECT last_insert_rowid();
                """;
            command.Parameters.AddWithValue("$displayOrder", displayOrder);
            command.Parameters.AddWithValue("$name", normalizedName);
            command.Parameters.AddWithValue("$instrumentType", (object?)instrumentType ?? DBNull.Value);
            command.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O"));
            id = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
        }

        transaction.Commit();
        return id;
    }

    public async Task RenameLineAsync(
        long lineId,
        string name,
        CancellationToken cancellationToken = default)
    {
        string normalizedName = name.Trim();
        if (normalizedName.Length == 0)
        {
            throw new ArgumentException("线路名称不能为空。", nameof(name));
        }

        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE ObservationLine SET Name = $name WHERE Id = $id;";
        command.Parameters.AddWithValue("$name", normalizedName);
        command.Parameters.AddWithValue("$id", lineId);

        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
        {
            throw new InvalidOperationException("指定线路不存在。");
        }
    }

    public async Task DeleteLineAsync(
        long lineId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();

        long inputRowCount;
        await using (var countCommand = connection.CreateCommand())
        {
            countCommand.Transaction = transaction;
            countCommand.CommandText = """
                SELECT
                    (SELECT COUNT(*) FROM RawObservation WHERE ObservationLineId = $id) +
                    (SELECT COUNT(*) FROM LevelDifference WHERE ObservationLineId = $id);
                """;
            countCommand.Parameters.AddWithValue("$id", lineId);
            inputRowCount = Convert.ToInt64(await countCommand.ExecuteScalarAsync(cancellationToken));
        }

        await using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = "DELETE FROM ObservationLine WHERE Id = $id;";
            deleteCommand.Parameters.AddWithValue("$id", lineId);
            if (await deleteCommand.ExecuteNonQueryAsync(cancellationToken) == 0)
            {
                throw new InvalidOperationException("指定线路不存在。");
            }
        }

        await NormalizeLineOrderAsync(connection, transaction, cancellationToken);

        if (inputRowCount > 0)
        {
            await ExecuteAsync(
                connection,
                transaction,
                "UPDATE ProjectState SET InputRevision = InputRevision + 1 WHERE Id = 1;",
                cancellationToken);
        }

        transaction.Commit();
    }

    public async Task MoveLineAsync(
        long lineId,
        int direction,
        CancellationToken cancellationToken = default)
    {
        if (direction is not (-1 or 1))
        {
            throw new ArgumentOutOfRangeException(nameof(direction), "方向只能是 -1 或 1。");
        }

        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();

        var lines = new List<(long Id, int Order)>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT Id, DisplayOrder FROM ObservationLine ORDER BY DisplayOrder, Id;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                lines.Add((reader.GetInt64(0), reader.GetInt32(1)));
            }
        }

        int index = lines.FindIndex(x => x.Id == lineId);
        if (index < 0)
        {
            throw new InvalidOperationException("指定线路不存在。");
        }

        int targetIndex = index + direction;
        if (targetIndex < 0 || targetIndex >= lines.Count)
        {
            return;
        }

        (long Id, int Order) current = lines[index];
        (long Id, int Order) target = lines[targetIndex];

        await using (var tempCommand = connection.CreateCommand())
        {
            tempCommand.Transaction = transaction;
            tempCommand.CommandText = "UPDATE ObservationLine SET DisplayOrder = -1 WHERE Id = $id;";
            tempCommand.Parameters.AddWithValue("$id", current.Id);
            await tempCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var targetCommand = connection.CreateCommand())
        {
            targetCommand.Transaction = transaction;
            targetCommand.CommandText = "UPDATE ObservationLine SET DisplayOrder = $order WHERE Id = $id;";
            targetCommand.Parameters.AddWithValue("$order", current.Order);
            targetCommand.Parameters.AddWithValue("$id", target.Id);
            await targetCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var currentCommand = connection.CreateCommand())
        {
            currentCommand.Transaction = transaction;
            currentCommand.CommandText = "UPDATE ObservationLine SET DisplayOrder = $order WHERE Id = $id;";
            currentCommand.Parameters.AddWithValue("$order", target.Order);
            currentCommand.Parameters.AddWithValue("$id", current.Id);
            await currentCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        transaction.Commit();
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
                   HeightDecimals,
                   PointNameHorizontalAlignment,
                   PointNameVerticalAlignment,
                   OpenLastProjectOnStartup
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
            HeightDecimals = reader.GetInt32(7),
            PointNameHorizontalAlignment = (PointNameHorizontalAlignmentMode)reader.GetInt32(8),
            PointNameVerticalAlignment = (PointNameVerticalAlignmentMode)reader.GetInt32(9),
            OpenLastProjectOnStartup = reader.GetInt32(10) != 0
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
            SELECT d.Id, d.ObservationLineId, d.Sequence, d.FromPoint, d.ToPoint,
                   d.HeightDifference, d.DistanceMeters, d.StationCount,
                   d.ToPointRole, d.IsRoleManuallySpecified, d.Comment
            FROM LevelDifference AS d
            INNER JOIN ObservationLine AS l ON l.Id = d.ObservationLineId
            ORDER BY l.DisplayOrder, d.Sequence, d.Id;
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

    public async Task<IReadOnlyList<RawObservation>> LoadRawObservationsAsync(
        CancellationToken cancellationToken = default)
    {
        var result = new List<RawObservation>();
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT r.Id, r.ObservationLineId, r.Sequence, r.FromPoint, r.ToPoint,
                   r.B1, r.B2, r.F1, r.F2,
                   r.DistanceB1, r.DistanceB2, r.DistanceF1, r.DistanceF2,
                   r.MeasurementMode, r.ObservationOrder, r.IsValid, r.InvalidReason,
                   r.MeasuredAtUtc, r.TemperatureCelsius, l.SourceFileName, r.Comment
            FROM RawObservation AS r
            INNER JOIN ObservationLine AS l ON l.Id = r.ObservationLineId
            ORDER BY l.DisplayOrder, r.Sequence, r.Id;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new RawObservation
            {
                Id = reader.GetInt64(0),
                LineId = reader.GetInt64(1),
                Sequence = reader.GetInt32(2),
                FromPoint = reader.GetString(3),
                ToPoint = reader.GetString(4),
                B1 = reader.IsDBNull(5) ? null : reader.GetDouble(5),
                B2 = reader.IsDBNull(6) ? null : reader.GetDouble(6),
                F1 = reader.IsDBNull(7) ? null : reader.GetDouble(7),
                F2 = reader.IsDBNull(8) ? null : reader.GetDouble(8),
                DistanceB1 = reader.IsDBNull(9) ? null : reader.GetDouble(9),
                DistanceB2 = reader.IsDBNull(10) ? null : reader.GetDouble(10),
                DistanceF1 = reader.IsDBNull(11) ? null : reader.GetDouble(11),
                DistanceF2 = reader.IsDBNull(12) ? null : reader.GetDouble(12),
                MeasurementMode = (MeasurementMode)reader.GetInt32(13),
                ObservationOrder = (ObservationOrder)reader.GetInt32(14),
                IsValid = reader.GetInt32(15) != 0,
                InvalidReason = reader.IsDBNull(16) ? null : reader.GetString(16),
                MeasuredAt = reader.IsDBNull(17) ? null : DateTimeOffset.Parse(reader.GetString(17)),
                TemperatureCelsius = reader.IsDBNull(18) ? null : reader.GetDouble(18),
                SourceFileName = reader.IsDBNull(19) ? null : reader.GetString(19),
                Comment = reader.IsDBNull(20) ? null : reader.GetString(20)
            });
        }

        return result;
    }

    public async Task<IReadOnlyList<NetworkMapPoint>> LoadMapPointsAsync(
        CancellationToken cancellationToken = default)
    {
        var result = new List<NetworkMapPoint>();
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT PointName, X, Y, UpdatedAtUtc
            FROM NetworkMapPoint
            ORDER BY PointName COLLATE NOCASE;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new NetworkMapPoint(
                reader.GetString(0),
                reader.GetDouble(1),
                reader.GetDouble(2),
                DateTimeOffset.Parse(reader.GetString(3))));
        }

        return result;
    }

    public async Task<ReportTextContent> LoadReportTextAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TaskOverview,
                   NaturalGeography,
                   ExistingData,
                   ReferencedStandards,
                   TechnicalIndicators,
                   FieldWorkSummary,
                   ConclusionAndRecommendations
            FROM ReportText
            WHERE Id = 1;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new ReportTextContent();
        }

        return new ReportTextContent(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6));
    }

    public async Task SaveMapPointsAsync(
        IReadOnlyList<NetworkMapPoint> points,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();

        await ExecuteAsync(connection, transaction, "DELETE FROM NetworkMapPoint;", cancellationToken);

        foreach (var point in points.OrderBy(x => x.PointName, StringComparer.Ordinal))
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO NetworkMapPoint(PointName, X, Y, UpdatedAtUtc)
                VALUES($name, $x, $y, $updated);
                """;
            command.Parameters.AddWithValue("$name", point.PointName);
            command.Parameters.AddWithValue("$x", point.X);
            command.Parameters.AddWithValue("$y", point.Y);
            command.Parameters.AddWithValue("$updated", point.UpdatedAtUtc.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        transaction.Commit();
    }

    public async Task SaveReportTextAsync(
        ReportTextContent reportText,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ReportText
            SET TaskOverview = $taskOverview,
                NaturalGeography = $naturalGeography,
                ExistingData = $existingData,
                ReferencedStandards = $referencedStandards,
                TechnicalIndicators = $technicalIndicators,
                FieldWorkSummary = $fieldWorkSummary,
                ConclusionAndRecommendations = $conclusion
            WHERE Id = 1;
            """;
        command.Parameters.AddWithValue("$taskOverview", reportText.TaskOverview ?? string.Empty);
        command.Parameters.AddWithValue("$naturalGeography", reportText.NaturalGeography ?? string.Empty);
        command.Parameters.AddWithValue("$existingData", reportText.ExistingData ?? string.Empty);
        command.Parameters.AddWithValue("$referencedStandards", reportText.ReferencedStandards ?? string.Empty);
        command.Parameters.AddWithValue("$technicalIndicators", reportText.TechnicalIndicators ?? string.Empty);
        command.Parameters.AddWithValue("$fieldWorkSummary", reportText.FieldWorkSummary ?? string.Empty);
        command.Parameters.AddWithValue("$conclusion", reportText.ConclusionAndRecommendations ?? string.Empty);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<KnownHeight>> LoadKnownHeightsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<KnownHeight>();
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT kh.PointName, kh.Height, kh.Comment
            FROM KnownHeight AS kh
            ORDER BY
                COALESCE((
                    SELECT l.DisplayOrder
                    FROM LevelDifference AS d
                    INNER JOIN ObservationLine AS l ON l.Id = d.ObservationLineId
                    WHERE d.FromPoint = kh.PointName OR d.ToPoint = kh.PointName
                    ORDER BY l.DisplayOrder, d.Sequence, d.Id
                    LIMIT 1
                ), 2147483647),
                COALESCE((
                    SELECT d.Sequence
                    FROM LevelDifference AS d
                    INNER JOIN ObservationLine AS l ON l.Id = d.ObservationLineId
                    WHERE d.FromPoint = kh.PointName OR d.ToPoint = kh.PointName
                    ORDER BY l.DisplayOrder, d.Sequence, d.Id
                    LIMIT 1
                ), 2147483647),
                kh.PointName COLLATE NOCASE;
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

    public Task SaveProjectInputsAsync(
        ProjectMetadata metadata,
        ProjectSettings settings,
        IReadOnlyList<LevelDifference> levelDifferences,
        IReadOnlyList<KnownHeight> knownHeights,
        CancellationToken cancellationToken = default) =>
        SaveProjectInputsCoreAsync(
            metadata,
            settings,
            levelDifferences,
            knownHeights,
            rawObservations: null,
            incrementInputRevision: true,
            cancellationToken);

    public Task SaveProjectInputsAsync(
        ProjectMetadata metadata,
        ProjectSettings settings,
        IReadOnlyList<LevelDifference> levelDifferences,
        IReadOnlyList<KnownHeight> knownHeights,
        bool incrementInputRevision,
        CancellationToken cancellationToken = default) =>
        SaveProjectInputsCoreAsync(
            metadata,
            settings,
            levelDifferences,
            knownHeights,
            rawObservations: null,
            incrementInputRevision,
            cancellationToken);

    public Task SaveProjectInputsAsync(
        ProjectMetadata metadata,
        ProjectSettings settings,
        IReadOnlyList<LevelDifference> levelDifferences,
        IReadOnlyList<KnownHeight> knownHeights,
        IReadOnlyList<RawObservation> rawObservations,
        bool incrementInputRevision,
        CancellationToken cancellationToken = default) =>
        SaveProjectInputsCoreAsync(
            metadata,
            settings,
            levelDifferences,
            knownHeights,
            rawObservations,
            incrementInputRevision,
            cancellationToken);

    private async Task SaveProjectInputsCoreAsync(
        ProjectMetadata metadata,
        ProjectSettings settings,
        IReadOnlyList<LevelDifference> levelDifferences,
        IReadOnlyList<KnownHeight> knownHeights,
        IReadOnlyList<RawObservation>? rawObservations,
        bool incrementInputRevision,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();

        await UpdateMetadataAsync(connection, transaction, metadata, cancellationToken);
        await UpdateSettingsAsync(connection, transaction, settings, cancellationToken);

        if (rawObservations is not null)
        {
            await ExecuteAsync(connection, transaction, "DELETE FROM RawObservation;", cancellationToken);

            foreach (var observation in rawObservations.OrderBy(x => x.LineId).ThenBy(x => x.Sequence))
            {
                await using var rawCommand = connection.CreateCommand();
                rawCommand.Transaction = transaction;
                rawCommand.CommandText = """
                    INSERT INTO RawObservation(
                        ObservationLineId, Sequence, FromPoint, ToPoint,
                        B1, B2, F1, F2,
                        DistanceB1, DistanceB2, DistanceF1, DistanceF2,
                        MeasurementMode, ObservationOrder, IsValid, InvalidReason,
                        MeasuredAtUtc, TemperatureCelsius, Comment)
                    VALUES(
                        $lineId, $sequence, $fromPoint, $toPoint,
                        $b1, $b2, $f1, $f2,
                        $distanceB1, $distanceB2, $distanceF1, $distanceF2,
                        $measurementMode, $observationOrder, $isValid, $invalidReason,
                        $measuredAtUtc, $temperatureCelsius, $comment);
                    """;
                rawCommand.Parameters.AddWithValue("$lineId", observation.LineId);
                rawCommand.Parameters.AddWithValue("$sequence", observation.Sequence);
                rawCommand.Parameters.AddWithValue("$fromPoint", observation.FromPoint);
                rawCommand.Parameters.AddWithValue("$toPoint", observation.ToPoint);
                rawCommand.Parameters.AddWithValue("$b1", (object?)observation.B1 ?? DBNull.Value);
                rawCommand.Parameters.AddWithValue("$b2", (object?)observation.B2 ?? DBNull.Value);
                rawCommand.Parameters.AddWithValue("$f1", (object?)observation.F1 ?? DBNull.Value);
                rawCommand.Parameters.AddWithValue("$f2", (object?)observation.F2 ?? DBNull.Value);
                rawCommand.Parameters.AddWithValue("$distanceB1", (object?)observation.DistanceB1 ?? DBNull.Value);
                rawCommand.Parameters.AddWithValue("$distanceB2", (object?)observation.DistanceB2 ?? DBNull.Value);
                rawCommand.Parameters.AddWithValue("$distanceF1", (object?)observation.DistanceF1 ?? DBNull.Value);
                rawCommand.Parameters.AddWithValue("$distanceF2", (object?)observation.DistanceF2 ?? DBNull.Value);
                rawCommand.Parameters.AddWithValue("$measurementMode", (int)observation.MeasurementMode);
                rawCommand.Parameters.AddWithValue("$observationOrder", (int)observation.ObservationOrder);
                rawCommand.Parameters.AddWithValue("$isValid", observation.IsValid ? 1 : 0);
                rawCommand.Parameters.AddWithValue("$invalidReason", (object?)observation.InvalidReason ?? DBNull.Value);
                rawCommand.Parameters.AddWithValue(
                    "$measuredAtUtc",
                    observation.MeasuredAt is null ? DBNull.Value : observation.MeasuredAt.Value.ToString("O"));
                rawCommand.Parameters.AddWithValue(
                    "$temperatureCelsius",
                    (object?)observation.TemperatureCelsius ?? DBNull.Value);
                rawCommand.Parameters.AddWithValue("$comment", (object?)observation.Comment ?? DBNull.Value);
                await rawCommand.ExecuteNonQueryAsync(cancellationToken);
            }
        }

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

        if (incrementInputRevision)
        {
            await ExecuteAsync(
                connection,
                transaction,
                """
                UPDATE ProjectState
                SET InputRevision = InputRevision + 1
                WHERE Id = 1;
                """,
                cancellationToken);
        }

        transaction.Commit();
    }

    public Task<long> ImportOutAsync(
        string sourceFileName,
        byte[] sourceBytes,
        string sha256,
        IReadOnlyList<LevelDifference> differences,
        IReadOnlyList<KnownHeight> knownHeights,
        CancellationToken cancellationToken = default) =>
        ImportInstrumentFileAsync(
            sourceFileName,
            instrumentType: "OUT",
            sourceBytes,
            sha256,
            Array.Empty<RawObservation>(),
            differences,
            knownHeights,
            cancellationToken);

    public async Task<long> ImportInstrumentFileAsync(
        string sourceFileName,
        string instrumentType,
        byte[] sourceBytes,
        string sha256,
        IReadOnlyList<RawObservation> rawObservations,
        IReadOnlyList<LevelDifference> differences,
        IReadOnlyList<KnownHeight> knownHeights,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(instrumentType);
        ArgumentNullException.ThrowIfNull(sourceBytes);
        ArgumentNullException.ThrowIfNull(rawObservations);
        ArgumentNullException.ThrowIfNull(differences);
        ArgumentNullException.ThrowIfNull(knownHeights);

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
                VALUES($order, $name, $source, $instrumentType, $created);
                SELECT last_insert_rowid();
                """;
            command.Parameters.AddWithValue("$order", displayOrder);
            command.Parameters.AddWithValue("$name", Path.GetFileNameWithoutExtension(sourceFileName));
            command.Parameters.AddWithValue("$source", Path.GetFileName(sourceFileName));
            command.Parameters.AddWithValue("$instrumentType", instrumentType);
            command.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
            lineId = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
        }

        foreach (var observation in rawObservations.OrderBy(x => x.Sequence))
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO RawObservation(
                    ObservationLineId, Sequence, FromPoint, ToPoint,
                    B1, B2, F1, F2,
                    DistanceB1, DistanceB2, DistanceF1, DistanceF2,
                    MeasurementMode, ObservationOrder, IsValid, InvalidReason,
                    MeasuredAtUtc, TemperatureCelsius, Comment)
                VALUES(
                    $lineId, $sequence, $fromPoint, $toPoint,
                    $b1, $b2, $f1, $f2,
                    $distanceB1, $distanceB2, $distanceF1, $distanceF2,
                    $measurementMode, $observationOrder, $isValid, $invalidReason,
                    $measuredAtUtc, $temperatureCelsius, $comment);
                """;
            command.Parameters.AddWithValue("$lineId", lineId);
            command.Parameters.AddWithValue("$sequence", observation.Sequence);
            command.Parameters.AddWithValue("$fromPoint", observation.FromPoint);
            command.Parameters.AddWithValue("$toPoint", observation.ToPoint);
            command.Parameters.AddWithValue("$b1", (object?)observation.B1 ?? DBNull.Value);
            command.Parameters.AddWithValue("$b2", (object?)observation.B2 ?? DBNull.Value);
            command.Parameters.AddWithValue("$f1", (object?)observation.F1 ?? DBNull.Value);
            command.Parameters.AddWithValue("$f2", (object?)observation.F2 ?? DBNull.Value);
            command.Parameters.AddWithValue("$distanceB1", (object?)observation.DistanceB1 ?? DBNull.Value);
            command.Parameters.AddWithValue("$distanceB2", (object?)observation.DistanceB2 ?? DBNull.Value);
            command.Parameters.AddWithValue("$distanceF1", (object?)observation.DistanceF1 ?? DBNull.Value);
            command.Parameters.AddWithValue("$distanceF2", (object?)observation.DistanceF2 ?? DBNull.Value);
            command.Parameters.AddWithValue("$measurementMode", (int)observation.MeasurementMode);
            command.Parameters.AddWithValue("$observationOrder", (int)observation.ObservationOrder);
            command.Parameters.AddWithValue("$isValid", observation.IsValid ? 1 : 0);
            command.Parameters.AddWithValue("$invalidReason", (object?)observation.InvalidReason ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "$measuredAtUtc",
                observation.MeasuredAt is null ? DBNull.Value : observation.MeasuredAt.Value.ToString("O"));
            command.Parameters.AddWithValue(
                "$temperatureCelsius",
                (object?)observation.TemperatureCelsius ?? DBNull.Value);
            command.Parameters.AddWithValue("$comment", (object?)observation.Comment ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken);
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
                VALUES($lineId, $fileName, $instrumentType, $importedAt, $sha256, $data);
                """;
            command.Parameters.AddWithValue("$lineId", lineId);
            command.Parameters.AddWithValue("$fileName", Path.GetFileName(sourceFileName));
            command.Parameters.AddWithValue("$instrumentType", instrumentType);
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

    public async Task<AdjustmentResult?> LoadLatestAdjustmentResultAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        long runId;
        AdjustmentMethod method;
        double sigma0;
        int degreesOfFreedom;
        int iterations;
        double? lambda;

        await using (var runCommand = connection.CreateCommand())
        {
            runCommand.CommandText = """
                SELECT Id, AdjustmentMethod, UnitWeightStandardDeviation,
                       DegreesOfFreedom, Iterations, QuasiStableLambda
                FROM CalculationRun
                WHERE IsSuccessful = 1
                ORDER BY Id DESC
                LIMIT 1;
                """;

            await using var reader = await runCommand.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            runId = reader.GetInt64(0);
            method = (AdjustmentMethod)reader.GetInt32(1);
            sigma0 = reader.IsDBNull(2) ? 0 : reader.GetDouble(2);
            degreesOfFreedom = reader.IsDBNull(3) ? 0 : reader.GetInt32(3);
            iterations = reader.IsDBNull(4) ? 0 : reader.GetInt32(4);
            lambda = reader.IsDBNull(5) ? null : reader.GetDouble(5);
        }

        var heights = new List<AdjustedHeight>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT arh.PointName, arh.Height, arh.StandardError, arh.IsReferencePoint
                FROM AdjustmentResultHeight AS arh
                WHERE arh.CalculationRunId = $runId
                ORDER BY
                    COALESCE((
                        SELECT l.DisplayOrder
                        FROM LevelDifference AS d
                        INNER JOIN ObservationLine AS l ON l.Id = d.ObservationLineId
                        WHERE d.FromPoint = arh.PointName OR d.ToPoint = arh.PointName
                        ORDER BY l.DisplayOrder, d.Sequence, d.Id
                        LIMIT 1
                    ), 2147483647),
                    COALESCE((
                        SELECT d.Sequence
                        FROM LevelDifference AS d
                        INNER JOIN ObservationLine AS l ON l.Id = d.ObservationLineId
                        WHERE d.FromPoint = arh.PointName OR d.ToPoint = arh.PointName
                        ORDER BY l.DisplayOrder, d.Sequence, d.Id
                        LIMIT 1
                    ), 2147483647),
                    arh.Id;
                """;
            command.Parameters.AddWithValue("$runId", runId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                heights.Add(new AdjustedHeight(
                    reader.GetString(0),
                    reader.GetDouble(1),
                    reader.GetDouble(2),
                    reader.GetInt32(3) != 0));
            }
        }

        var differences = new List<AdjustedDifference>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT LevelDifferenceId, FromPoint, ToPoint,
                       ObservedDifference, AdjustedDifference, Residual,
                       DistanceMeters, StationCount, StandardError
                FROM AdjustmentResultDifference
                WHERE CalculationRunId = $runId
                ORDER BY Id;
                """;
            command.Parameters.AddWithValue("$runId", runId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                differences.Add(new AdjustedDifference(
                    reader.IsDBNull(0) ? 0 : reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetDouble(3),
                    reader.GetDouble(4),
                    reader.GetDouble(5),
                    reader.GetDouble(6),
                    reader.GetInt32(7),
                    reader.GetDouble(8)));
            }
        }

        return new AdjustmentResult(
            method,
            heights,
            differences,
            sigma0,
            degreesOfFreedom,
            iterations,
            Converged: true,
            lambda);
    }

    public async Task<IReadOnlyList<NetworkRoute>> LoadLatestRoutesAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        long? runId;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT Id
                FROM CalculationRun
                WHERE IsSuccessful = 1
                ORDER BY Id DESC
                LIMIT 1;
                """;
            object? value = await command.ExecuteScalarAsync(cancellationToken);
            runId = value is null || value == DBNull.Value ? null : Convert.ToInt64(value);
        }

        if (runId is null)
        {
            return Array.Empty<NetworkRoute>();
        }

        var routes = new List<NetworkRoute>();
        var rows = new List<(long Id, int Index, RouteType Type, double Length, int Stations, double Closure, double LengthTolerance, double StationTolerance)>();
        await using (var routeCommand = connection.CreateCommand())
        {
            routeCommand.CommandText = """
                SELECT Id, RouteIndex, RouteType, LengthMeters, StationCount,
                       ClosureMeters, LengthToleranceMeters, StationToleranceMeters
                FROM ClosureRoute
                WHERE CalculationRunId = $runId
                ORDER BY RouteIndex, Id;
                """;
            routeCommand.Parameters.AddWithValue("$runId", runId.Value);

            await using var routeReader = await routeCommand.ExecuteReaderAsync(cancellationToken);
            while (await routeReader.ReadAsync(cancellationToken))
            {
                rows.Add((
                    routeReader.GetInt64(0),
                    routeReader.GetInt32(1),
                    (RouteType)routeReader.GetInt32(2),
                    routeReader.GetDouble(3),
                    routeReader.GetInt32(4),
                    routeReader.GetDouble(5),
                    routeReader.GetDouble(6),
                    routeReader.GetDouble(7)));
            }
        }

        foreach (var row in rows)
        {
            var points = new List<string>();
            await using (var pointCommand = connection.CreateCommand())
            {
                pointCommand.CommandText = """
                    SELECT PointName
                    FROM ClosureRoutePoint
                    WHERE ClosureRouteId = $routeId
                    ORDER BY Sequence;
                    """;
                pointCommand.Parameters.AddWithValue("$routeId", row.Id);

                await using var pointReader = await pointCommand.ExecuteReaderAsync(cancellationToken);
                while (await pointReader.ReadAsync(cancellationToken))
                {
                    points.Add(pointReader.GetString(0));
                }
            }

            var edges = new List<RouteEdge>();
            for (int i = 0; i + 1 < points.Count; i++)
            {
                edges.Add(new RouteEdge(
                    ObservationId: 0,
                    FromPoint: points[i],
                    ToPoint: points[i + 1],
                    SignedHeightDifference: 0,
                    DistanceMeters: 0,
                    StationCount: 0));
            }

            routes.Add(new NetworkRoute(
                row.Index,
                row.Type,
                points,
                edges,
                row.Closure,
                row.Length,
                row.Stations,
                row.LengthTolerance,
                row.StationTolerance));
        }

        return routes;
    }

    private static async Task NormalizeLineOrderAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var ids = new List<long>();
        await using (var readCommand = connection.CreateCommand())
        {
            readCommand.Transaction = transaction;
            readCommand.CommandText = "SELECT Id FROM ObservationLine ORDER BY DisplayOrder, Id;";
            await using var reader = await readCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                ids.Add(reader.GetInt64(0));
            }
        }

        for (int i = 0; i < ids.Count; i++)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE ObservationLine SET DisplayOrder = $temporary WHERE Id = $id;";
            command.Parameters.AddWithValue("$temporary", -1000000 - i);
            command.Parameters.AddWithValue("$id", ids[i]);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        for (int i = 0; i < ids.Count; i++)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE ObservationLine SET DisplayOrder = $order WHERE Id = $id;";
            command.Parameters.AddWithValue("$order", i);
            command.Parameters.AddWithValue("$id", ids[i]);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
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
                HeightDecimals = $heightDecimals,
                PointNameHorizontalAlignment = $pointNameHorizontalAlignment,
                PointNameVerticalAlignment = $pointNameVerticalAlignment,
                OpenLastProjectOnStartup = $openLastProjectOnStartup
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
        command.Parameters.AddWithValue("$pointNameHorizontalAlignment", (int)settings.PointNameHorizontalAlignment);
        command.Parameters.AddWithValue("$pointNameVerticalAlignment", (int)settings.PointNameVerticalAlignment);
        command.Parameters.AddWithValue("$openLastProjectOnStartup", settings.OpenLastProjectOnStartup ? 1 : 0);
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
