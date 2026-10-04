using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.App.ViewModels;

public partial class NetworkMapPointRowViewModel : ObservableObject, IDataErrorInfo
{
    [ObservableProperty]
    private string pointName = string.Empty;

    [ObservableProperty]
    private double x;

    [ObservableProperty]
    private double y;

    [ObservableProperty]
    private DateTimeOffset updatedAtUtc = DateTimeOffset.UtcNow;

    public string Error => string.Empty;

    public string this[string columnName] => columnName switch
    {
        nameof(PointName) when string.IsNullOrWhiteSpace(PointName) => "点名不能为空。",
        nameof(X) when !double.IsFinite(X) => "X 坐标必须是有效数值。",
        nameof(Y) when !double.IsFinite(Y) => "Y 坐标必须是有效数值。",
        _ => string.Empty
    };

    public static NetworkMapPointRowViewModel FromDomain(NetworkMapPoint source) => new()
    {
        PointName = source.PointName,
        X = source.X,
        Y = source.Y,
        UpdatedAtUtc = source.UpdatedAtUtc
    };

    public NetworkMapPoint ToDomain() => new(
        PointName.Trim(),
        X,
        Y,
        UpdatedAtUtc);
}
