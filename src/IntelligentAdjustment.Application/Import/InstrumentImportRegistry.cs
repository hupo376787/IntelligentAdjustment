using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.Application.Import;

public sealed class InstrumentImportRegistry
{
    private readonly IReadOnlyDictionary<InstrumentVendor, IInstrumentDataImporter> importers;

    public InstrumentImportRegistry()
    {
        IInstrumentDataImporter[] available =
        [
            new LeicaDnaInstrumentDataImporter(),
            new GeoMaxZdlInstrumentDataImporter(),
            new SokkiaSdlInstrumentDataImporter(),
            new TopconDlInstrumentDataImporter(),
            new TrimbleDiNiInstrumentDataImporter(),
            new LevNetInstrumentDataImporter(),
            new OutInstrumentDataImporter()
        ];

        importers = available.ToDictionary(x => x.Vendor);
        Catalog = available
            .Select(x => new InstrumentImporterDescriptor(
                x.Vendor,
                x.DisplayName,
                x.SupportedExtensions,
                IsImplemented: true))
            .ToArray();
    }

    public IReadOnlyList<InstrumentImporterDescriptor> Catalog { get; }

    public bool TryGetImporter(
        InstrumentVendor vendor,
        out IInstrumentDataImporter? importer) =>
        importers.TryGetValue(vendor, out importer);

    public IInstrumentDataImporter GetRequiredImporter(InstrumentVendor vendor)
    {
        if (!importers.TryGetValue(vendor, out IInstrumentDataImporter? importer))
        {
            throw new NotSupportedException($"“{vendor}”解析器尚未接入。");
        }

        return importer;
    }
}

public sealed record InstrumentImporterDescriptor(
    InstrumentVendor Vendor,
    string DisplayName,
    IReadOnlyList<string> Extensions,
    bool IsImplemented);
