using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using IntelligentAdjustment.Domain;
using IntelligentAdjustment.App.Services;

namespace IntelligentAdjustment.App.ViewModels;

public partial class LevelDifferenceRowViewModel : ObservableObject, IDataErrorInfo
{
    public long Id { get; init; }
    public long LineId { get; init; }

    [ObservableProperty]
    private int sequence;

    [ObservableProperty]
    private string lineName = string.Empty;

    [ObservableProperty]
    private string fromPoint = string.Empty;

    [ObservableProperty]
    private string toPoint = string.Empty;

    [ObservableProperty]
    private double heightDifference;

    [ObservableProperty]
    private double distanceMeters;

    [ObservableProperty]
    private int stationCount = 1;

    [ObservableProperty]
    private PointRole toPointRole;

    [ObservableProperty]
    private bool isRoleManuallySpecified;

    [ObservableProperty]
    private string? comment;

    public string Error => string.Empty;

    public string this[string columnName] => columnName switch
    {
        nameof(FromPoint) when string.IsNullOrWhiteSpace(FromPoint) => LocalizationService.Text("Loc.Validation.FromRequired"),
        nameof(ToPoint) when string.IsNullOrWhiteSpace(ToPoint) => LocalizationService.Text("Loc.Validation.ToRequired"),
        nameof(ToPoint) when string.Equals(FromPoint.Trim(), ToPoint.Trim(), StringComparison.Ordinal) => LocalizationService.Text("Loc.Validation.EndpointsDifferent"),
        nameof(HeightDifference) when !double.IsFinite(HeightDifference) => LocalizationService.Text("Loc.Validation.HeightDifferenceFinite"),
        nameof(DistanceMeters) when !double.IsFinite(DistanceMeters) || DistanceMeters <= 0 => LocalizationService.Text("Loc.Validation.DistancePositiveSimple"),
        nameof(StationCount) when StationCount <= 0 => LocalizationService.Text("Loc.Validation.StationCountPositive"),
        _ => string.Empty
    };

    public static LevelDifferenceRowViewModel FromDomain(
        LevelDifference source,
        string? lineName = null) => new()
    {
        Id = source.Id,
        LineId = source.LineId,
        Sequence = source.Sequence,
        LineName = lineName ?? source.LineId.ToString(),
        FromPoint = source.FromPoint,
        ToPoint = source.ToPoint,
        HeightDifference = source.HeightDifference,
        DistanceMeters = source.DistanceMeters,
        StationCount = source.StationCount,
        ToPointRole = source.ToPointRole,
        IsRoleManuallySpecified = source.IsRoleManuallySpecified,
        Comment = source.Comment
    };

    public LevelDifference ToDomain() => new(
        Id,
        LineId,
        Sequence,
        FromPoint.Trim(),
        ToPoint.Trim(),
        HeightDifference,
        DistanceMeters,
        StationCount,
        ToPointRole,
        IsRoleManuallySpecified,
        Comment);
}
