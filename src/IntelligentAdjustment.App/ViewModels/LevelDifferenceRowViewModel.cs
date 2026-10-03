using CommunityToolkit.Mvvm.ComponentModel;
using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.App.ViewModels;

public partial class LevelDifferenceRowViewModel : ObservableObject
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
