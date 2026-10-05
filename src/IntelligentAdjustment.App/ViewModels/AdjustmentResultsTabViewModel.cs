using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IntelligentAdjustment.Domain;
using IntelligentAdjustment.App.Services;

namespace IntelligentAdjustment.App.ViewModels;

public partial class AdjustmentResultsTabViewModel : WorkspaceTabViewModel
{
    private readonly Func<Task> recalculate;

    [ObservableProperty]
    private bool isStale;

    [ObservableProperty]
    private string summary = LocalizationService.Text("Loc.Adjust.NotRun");

    public AdjustmentResultsTabViewModel(Func<Task> recalculate)
        : base("adjustment-results", LocalizationService.Text("Loc.Nav.AdjustmentResults"))
    {
        this.recalculate = recalculate;
    }

    public ObservableCollection<AdjustedHeight> Heights { get; } = new();
    public ObservableCollection<AdjustedDifference> Differences { get; } = new();

    public void Load(AdjustmentResult result, bool isStale = false) =>
        Load(result, new ProjectSettings { HeightDecimals = 6 }, isStale);

    public void Load(
        AdjustmentResult result,
        ProjectSettings settings,
        bool isStale = false)
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

        string method = result.Method == AdjustmentMethod.Classical
            ? LocalizationService.Text("Loc.Adjust.Classical")
            : LocalizationService.Text("Loc.Adjust.QuasiStable");
        int decimals = Math.Clamp(settings.HeightDecimals, 0, 10);
        string sigma0 = result.UnitWeightStandardDeviation.ToString($"F{decimals}");
        Summary = LocalizationService.Format(
            "Loc.Adjust.Summary",
            method,
            sigma0,
            result.DegreesOfFreedom,
            result.Iterations);
        IsStale = isStale;
    }

    [RelayCommand]
    private Task RecalculateAsync() => recalculate();
}
