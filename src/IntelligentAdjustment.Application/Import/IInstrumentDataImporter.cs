using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.Application.Import;

public interface IInstrumentDataImporter
{
    InstrumentVendor Vendor { get; }
    string DisplayName { get; }
    IReadOnlyList<string> SupportedExtensions { get; }

    Task<InstrumentImportResult> ParseAsync(
        string filePath,
        CancellationToken cancellationToken = default);
}

public sealed record InstrumentImportResult(
    IReadOnlyList<RawObservation> RawObservations,
    IReadOnlyList<LevelDifference> LevelDifferences,
    IReadOnlyList<KnownHeight> KnownHeights);
