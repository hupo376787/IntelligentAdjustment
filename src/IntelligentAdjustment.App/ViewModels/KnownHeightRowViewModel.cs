using CommunityToolkit.Mvvm.ComponentModel;
using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.App.ViewModels;

public partial class KnownHeightRowViewModel : ObservableObject
{
    [ObservableProperty]
    private string pointName = string.Empty;

    [ObservableProperty]
    private double height;

    [ObservableProperty]
    private string? comment;

    public static KnownHeightRowViewModel FromDomain(KnownHeight source) => new()
    {
        PointName = source.PointName,
        Height = source.Height,
        Comment = source.Comment
    };

    public KnownHeight ToDomain() => new(PointName.Trim(), Height, Comment);
}
