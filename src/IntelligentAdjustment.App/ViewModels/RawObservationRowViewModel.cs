using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using IntelligentAdjustment.Domain;
using IntelligentAdjustment.App.Services;

namespace IntelligentAdjustment.App.ViewModels;

public partial class RawObservationRowViewModel : ObservableObject, IDataErrorInfo
{
    public long Id { get; init; }

    [ObservableProperty]
    private long lineId;

    [ObservableProperty]
    private int sequence;

    [ObservableProperty]
    private string fromPoint = string.Empty;

    [ObservableProperty]
    private string toPoint = string.Empty;

    [ObservableProperty]
    private double? b1;

    [ObservableProperty]
    private double? b2;

    [ObservableProperty]
    private double? f1;

    [ObservableProperty]
    private double? f2;

    [ObservableProperty]
    private double? distanceB1;

    [ObservableProperty]
    private double? distanceB2;

    [ObservableProperty]
    private double? distanceF1;

    [ObservableProperty]
    private double? distanceF2;

    [ObservableProperty]
    private MeasurementMode measurementMode = MeasurementMode.BFFB;

    [ObservableProperty]
    private ObservationOrder observationOrder = ObservationOrder.BFFB;

    [ObservableProperty]
    private bool isValid = true;

    [ObservableProperty]
    private string? invalidReason;

    [ObservableProperty]
    private DateTimeOffset? measuredAt;

    [ObservableProperty]
    private double? temperatureCelsius;

    [ObservableProperty]
    private string? sourceFileName;

    [ObservableProperty]
    private string? comment;

    public string Error => string.Empty;

    public string this[string columnName] => columnName switch
    {
        nameof(FromPoint) when string.IsNullOrWhiteSpace(FromPoint) => LocalizationService.Text("Loc.Validation.FromRequired"),
        nameof(ToPoint) when string.IsNullOrWhiteSpace(ToPoint) => LocalizationService.Text("Loc.Validation.ToRequired"),
        nameof(ToPoint) when string.Equals(FromPoint.Trim(), ToPoint.Trim(), StringComparison.Ordinal) => LocalizationService.Text("Loc.Validation.EndpointsDifferent"),
        nameof(B1) when B1 is not null && !double.IsFinite(B1.Value) => LocalizationService.Text("Loc.Validation.B1Finite"),
        nameof(B2) when B2 is not null && !double.IsFinite(B2.Value) => LocalizationService.Text("Loc.Validation.B2Finite"),
        nameof(F1) when F1 is not null && !double.IsFinite(F1.Value) => LocalizationService.Text("Loc.Validation.F1Finite"),
        nameof(F2) when F2 is not null && !double.IsFinite(F2.Value) => LocalizationService.Text("Loc.Validation.F2Finite"),
        nameof(DistanceB1) when DistanceB1 is not null && DistanceB1 <= 0 => LocalizationService.Text("Loc.Validation.BackDistancePositive"),
        nameof(DistanceB2) when DistanceB2 is not null && DistanceB2 <= 0 => LocalizationService.Text("Loc.Validation.BackDistancePositive"),
        nameof(DistanceF1) when DistanceF1 is not null && DistanceF1 <= 0 => LocalizationService.Text("Loc.Validation.ForeDistancePositive"),
        nameof(DistanceF2) when DistanceF2 is not null && DistanceF2 <= 0 => LocalizationService.Text("Loc.Validation.ForeDistancePositive"),
        nameof(TemperatureCelsius) when TemperatureCelsius is not null && !double.IsFinite(TemperatureCelsius.Value) => LocalizationService.Text("Loc.Validation.TemperatureFinite"),
        _ => string.Empty
    };

    public static RawObservationRowViewModel FromDomain(RawObservation source) => new()
    {
        Id = source.Id,
        LineId = source.LineId,
        Sequence = source.Sequence,
        FromPoint = source.FromPoint,
        ToPoint = source.ToPoint,
        B1 = source.B1,
        B2 = source.B2,
        F1 = source.F1,
        F2 = source.F2,
        DistanceB1 = source.DistanceB1,
        DistanceB2 = source.DistanceB2,
        DistanceF1 = source.DistanceF1,
        DistanceF2 = source.DistanceF2,
        MeasurementMode = source.MeasurementMode,
        ObservationOrder = source.ObservationOrder,
        IsValid = source.IsValid,
        InvalidReason = source.InvalidReason,
        MeasuredAt = source.MeasuredAt,
        TemperatureCelsius = source.TemperatureCelsius,
        SourceFileName = source.SourceFileName,
        Comment = source.Comment
    };

    public RawObservation ToDomain() => new()
    {
        Id = Id,
        LineId = LineId,
        Sequence = Sequence,
        FromPoint = FromPoint.Trim(),
        ToPoint = ToPoint.Trim(),
        B1 = B1,
        B2 = B2,
        F1 = F1,
        F2 = F2,
        DistanceB1 = DistanceB1,
        DistanceB2 = DistanceB2,
        DistanceF1 = DistanceF1,
        DistanceF2 = DistanceF2,
        MeasurementMode = MeasurementMode,
        ObservationOrder = ObservationOrder,
        IsValid = IsValid,
        InvalidReason = InvalidReason,
        MeasuredAt = MeasuredAt,
        TemperatureCelsius = TemperatureCelsius,
        SourceFileName = SourceFileName,
        Comment = Comment
    };
}
