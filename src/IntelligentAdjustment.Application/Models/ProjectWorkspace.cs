using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.Application.Models;

public sealed record ProjectWorkspace(
    ProjectMetadata Metadata,
    ProjectSettings Settings,
    ProjectRevisionState Revision,
    IReadOnlyList<ObservationLineInfo> Lines,
    IReadOnlyList<LevelDifference> LevelDifferences,
    IReadOnlyList<KnownHeight> KnownHeights);

public sealed record CalculationBundle(
    IReadOnlyList<NetworkRoute> Routes,
    AdjustmentResult AdjustmentResult,
    ProjectRevisionState Revision);

public sealed record OutImportResult(
    IReadOnlyList<LevelDifference> LevelDifferences,
    IReadOnlyList<KnownHeight> KnownHeights);
