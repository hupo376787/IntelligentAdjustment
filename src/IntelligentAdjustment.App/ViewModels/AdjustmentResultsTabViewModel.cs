using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.App.ViewModels;

public partial class AdjustmentResultsTabViewModel : WorkspaceTabViewModel
{
    private readonly Func<Task> recalculate;

    [ObservableProperty]
    private bool isStale;

    [ObservableProperty]
    private string summary = "尚未执行高程平差。";

    public AdjustmentResultsTabViewModel(Func<Task> recalculate)
        : base("adjustment-results", "平差结果")
    {
        this.recalculate = recalculate;
    }

    public ObservableCollection<AdjustedHeight> Heights { get; } = new();
    public ObservableCollection<AdjustedDifference> Differences { get; } = new();

    public void Load(AdjustmentResult result, bool isStale = false)
    {
        Heights.Clear();
        Differences.Clear();

        foreach (var height in result.Heights)
        {
            Heights.Add(height);
        }

        foreach (var difference in result.Differences)
        {
            Differences.Add(difference);
        }

        string method = result.Method == AdjustmentMethod.Classical ? "经典平差" : "拟稳平差";
        Summary = $"{method}；单位权中误差 {result.UnitWeightStandardDeviation:F6} m；自由度 {result.DegreesOfFreedom}；迭代 {result.Iterations} 次";
        IsStale = isStale;
    }

    [RelayCommand]
    private Task RecalculateAsync() => recalculate();
}
