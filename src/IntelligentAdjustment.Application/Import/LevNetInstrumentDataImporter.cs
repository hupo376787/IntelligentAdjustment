using IntelligentAdjustment.Application.Models;
using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.Application.Import;

public sealed class LevNetInstrumentDataImporter : IInstrumentDataImporter
{
    private readonly OutFileImporter outImporter = new();

    public InstrumentVendor Vendor => InstrumentVendor.LevNet;
    public string DisplayName => "Lev_Net";
    public IReadOnlyList<string> SupportedExtensions { get; } = [".out", ".mdt"];

    public async Task<InstrumentImportResult> ParseAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        string extension = Path.GetExtension(filePath);

        if (extension.Equals(".mdt", StringComparison.OrdinalIgnoreCase))
        {
            return await MdtBffbFileParser.ParseAsync(filePath, cancellationToken);
        }

        if (extension.Equals(".out", StringComparison.OrdinalIgnoreCase))
        {
            OutImportResult parsed = await outImporter.ParseAsync(filePath, cancellationToken);
            return new InstrumentImportResult(
                Array.Empty<RawObservation>(),
                parsed.LevelDifferences,
                parsed.KnownHeights);
        }

        throw new NotSupportedException(
            $"Lev_Net 不支持扩展名“{extension}”；支持 .out / .mdt。");
    }
}
