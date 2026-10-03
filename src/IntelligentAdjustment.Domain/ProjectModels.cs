namespace IntelligentAdjustment.Domain;

public sealed record ProjectMetadata(
    string ProjectName,
    string ProjectNumber,
    string UnitName,
    string ProjectLeader,
    string Reviewer,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record ObservationLineInfo(
    long Id,
    int DisplayOrder,
    string Name,
    string? SourceFileName,
    string? InstrumentType,
    DateTimeOffset CreatedAtUtc);

public sealed record ProjectRevisionState(
    long InputRevision,
    long ResultRevision,
    DateTimeOffset? LastCalculatedAtUtc)
{
    public bool ResultsAreStale => ResultRevision != InputRevision;
}
