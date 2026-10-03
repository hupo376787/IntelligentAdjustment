using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.Application.Import;

public sealed class InstrumentImportRegistry
{
    private readonly IReadOnlyDictionary<InstrumentVendor, IInstrumentDataImporter> importers;

    public InstrumentImportRegistry()
    {
        IInstrumentDataImporter[] available =
        [
            new OutInstrumentDataImporter()
        ];

        importers = available.ToDictionary(x => x.Vendor);
    }

    public IReadOnlyList<InstrumentImporterDescriptor> Catalog { get; } =
    [
        new(InstrumentVendor.LeicaDna, "Leica DNA", [".gsi", ".mdt"], false),
        new(InstrumentVendor.GeoMaxZdl, "GeoMax ZDL", [".mdt"], false),
        new(InstrumentVendor.SokkiaSdl, "Sokkia SDL", [".csv"], false),
        new(InstrumentVendor.TopconDl, "Topcon DL", [".dat"], false),
        new(InstrumentVendor.TrimbleDiNi, "Trimble DiNi", [".dat"], false),
        new(InstrumentVendor.LevNet, "Lev_Net", [".out", ".mdt"], false),
        new(InstrumentVendor.GenericOut, "高差 OUT", [".out"], true)
    ];

    public bool TryGetImporter(
        InstrumentVendor vendor,
        out IInstrumentDataImporter? importer) =>
        importers.TryGetValue(vendor, out importer);

    public IInstrumentDataImporter GetRequiredImporter(InstrumentVendor vendor)
    {
        if (!importers.TryGetValue(vendor, out IInstrumentDataImporter? importer))
        {
            InstrumentImporterDescriptor? descriptor = Catalog.FirstOrDefault(x => x.Vendor == vendor);
            string name = descriptor?.DisplayName ?? vendor.ToString();
            throw new NotSupportedException($"“{name}”解析器尚未接入。");
        }

        return importer;
    }
}

public sealed record InstrumentImporterDescriptor(
    InstrumentVendor Vendor,
    string DisplayName,
    IReadOnlyList<string> Extensions,
    bool IsImplemented);
