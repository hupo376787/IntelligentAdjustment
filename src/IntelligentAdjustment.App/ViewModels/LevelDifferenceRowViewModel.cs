using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.App.ViewModels;

public partial class LevelDifferenceRowViewModel : ObservableObject, IDataErrorInfo
{
    public long Id { get; init; }
    public long LineId { get; init; }

    [ObservableProperty]
    private int sequence;

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
        nameof(FromPoint) when string.IsNullOrWhiteSpace(FromPoint) => "起点点名不能为空。",
        nameof(ToPoint) when string.IsNullOrWhiteSpace(ToPoint) => "终点点名不能为空。",
        nameof(ToPoint) when string.Equals(FromPoint.Trim(), ToPoint.Trim(), StringComparison.Ordinal) => "起点和终点不能相同。",
        nameof(HeightDifference) when !double.IsFinite(HeightDifference) => "高差必须是有效数值。",
        nameof(DistanceMeters) when !double.IsFinite(DistanceMeters) || DistanceMeters <= 0 => "距离必须大于 0。",
        nameof(StationCount) when StationCount <= 0 => "测站数必须大于 0。",
        _ => string.Empty
    };

    public static LevelDifferenceRowViewModel FromDomain(LevelDifference source) => new()
    {
        Id = source.Id,
        LineId = source.LineId,
        Sequence = source.Sequence,
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
