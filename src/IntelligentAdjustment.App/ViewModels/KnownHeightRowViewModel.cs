using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.App.ViewModels;

public partial class KnownHeightRowViewModel : ObservableObject, IDataErrorInfo
{
    [ObservableProperty]
    private string pointName = string.Empty;

    [ObservableProperty]
    private double height;

    [ObservableProperty]
    private string? comment;

    public string Error => string.Empty;

    public string this[string columnName] => columnName switch
    {
        nameof(PointName) when string.IsNullOrWhiteSpace(PointName) => "点名不能为空。",
        nameof(Height) when !double.IsFinite(Height) => "高程必须是有效数值。",
        _ => string.Empty
    };

    public static KnownHeightRowViewModel FromDomain(KnownHeight source) => new()
    {
        PointName = source.PointName,
        Height = source.Height,
        Comment = source.Comment
    };

    public KnownHeight ToDomain() => new(PointName.Trim(), Height, Comment);
}
