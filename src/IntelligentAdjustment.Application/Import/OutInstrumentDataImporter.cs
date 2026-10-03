using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.Application.Import;

public sealed class OutInstrumentDataImporter : IInstrumentDataImporter
{
    private readonly OutFileImporter importer = new();

    public InstrumentVendor Vendor => InstrumentVendor.GenericOut;
    public string DisplayName => "高差 OUT";
    public IReadOnlyList<string> SupportedExtensions { get; } = [".out"];

    public async Task<InstrumentImportResult> ParseAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        OutImportResult parsed = await importer.ParseAsync(filePath, cancellationToken);
        return new InstrumentImportResult(
            RawObservations: Array.Empty<RawObservation>(),
            parsed.LevelDifferences,
            parsed.KnownHeights);
    }
}
