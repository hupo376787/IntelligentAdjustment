using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IntelligentAdjustment.App.Services;
using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.App.ViewModels;

public sealed partial class SettingsTabViewModel : WorkspaceTabViewModel
{
    private readonly ProjectDocumentViewModel document;
    private readonly ApplicationPreferencesService preferences;
    private readonly IUserDialogService dialogs;
    private readonly Func<string?> currentProjectPathProvider;

    public SettingsTabViewModel(
        ProjectDocumentViewModel document,
        ApplicationPreferencesService preferences,
        IUserDialogService dialogs,
        Func<string?> currentProjectPathProvider)
        : base("settings", "工程设置")
    {
        this.document = document;
        this.preferences = preferences;
        this.dialogs = dialogs;
        this.currentProjectPathProvider = currentProjectPathProvider;
        document.PropertyChanged += Document_PropertyChanged;
    }

    public IReadOnlyList<HorizontalAlignmentOption> HorizontalAlignmentOptions { get; } =
    [
        new(PointNameHorizontalAlignmentMode.Left, "左侧"),
        new(PointNameHorizontalAlignmentMode.Center, "居中"),
        new(PointNameHorizontalAlignmentMode.Right, "右侧")
    ];

    public IReadOnlyList<VerticalAlignmentOption> VerticalAlignmentOptions { get; } =
    [
        new(PointNameVerticalAlignmentMode.Top, "上方"),
        new(PointNameVerticalAlignmentMode.Center, "居中"),
        new(PointNameVerticalAlignmentMode.Bottom, "下方")
    ];

    public bool IsDistanceTolerance
    {
        get => document.Settings.ToleranceMode == ClosureToleranceMode.Distance;
        set
        {
            if (value && !IsDistanceTolerance)
            {
                document.Settings = document.Settings with { ToleranceMode = ClosureToleranceMode.Distance };
            }
        }
    }

    public bool IsStationTolerance
    {
        get => document.Settings.ToleranceMode == ClosureToleranceMode.StationCount;
        set
        {
            if (value && !IsStationTolerance)
            {
                document.Settings = document.Settings with { ToleranceMode = ClosureToleranceMode.StationCount };
            }
        }
    }

    public double DistanceToleranceCoefficientMm
    {
        get => document.Settings.DistanceToleranceCoefficientMm;
        set
        {
            if (Math.Abs(value - document.Settings.DistanceToleranceCoefficientMm) > double.Epsilon)
            {
                document.Settings = document.Settings with { DistanceToleranceCoefficientMm = value };
            }
        }
    }

    public double StationToleranceCoefficientMm
    {
        get => document.Settings.StationToleranceCoefficientMm;
        set
        {
            if (Math.Abs(value - document.Settings.StationToleranceCoefficientMm) > double.Epsilon)
            {
                document.Settings = document.Settings with { StationToleranceCoefficientMm = value };
            }
        }
    }

    public bool IsClassical
    {
        get => document.Settings.AdjustmentMethod == AdjustmentMethod.Classical;
        set
        {
            if (value && !IsClassical)
            {
                document.Settings = document.Settings with { AdjustmentMethod = AdjustmentMethod.Classical };
            }
        }
    }

    public bool IsQuasiStable
    {
        get => document.Settings.AdjustmentMethod == AdjustmentMethod.QuasiStable;
        set
        {
            if (value && !IsQuasiStable)
            {
                document.Settings = document.Settings with { AdjustmentMethod = AdjustmentMethod.QuasiStable };
            }
        }
    }

    public bool AutoMergeTransitionPoints
    {
        get => document.Settings.AutoMergeTransitionPoints;
        set
        {
            if (value != document.Settings.AutoMergeTransitionPoints)
            {
                document.Settings = document.Settings with { AutoMergeTransitionPoints = value };
            }
        }
    }

    public bool AutoUpdateLevelDifferences
    {
        get => document.Settings.AutoUpdateLevelDifferences;
        set
        {
            if (value != document.Settings.AutoUpdateLevelDifferences)
            {
                document.Settings = document.Settings with { AutoUpdateLevelDifferences = value };
            }
        }
    }

    public int DistanceDecimals
    {
        get => document.Settings.DistanceDecimals;
        set
        {
            int normalized = Math.Clamp(value, 0, 10);
            if (normalized != document.Settings.DistanceDecimals)
            {
                document.Settings = document.Settings with { DistanceDecimals = normalized };
            }
        }
    }

    public int HeightDecimals
    {
        get => document.Settings.HeightDecimals;
        set
        {
            int normalized = Math.Clamp(value, 0, 10);
            if (normalized != document.Settings.HeightDecimals)
            {
                document.Settings = document.Settings with { HeightDecimals = normalized };
            }
        }
    }

    public PointNameHorizontalAlignmentMode PointNameHorizontalAlignment
    {
        get => document.Settings.PointNameHorizontalAlignment;
        set
        {
            if (value != document.Settings.PointNameHorizontalAlignment)
            {
                document.Settings = document.Settings with { PointNameHorizontalAlignment = value };
            }
        }
    }

    public PointNameVerticalAlignmentMode PointNameVerticalAlignment
    {
        get => document.Settings.PointNameVerticalAlignment;
        set
        {
            if (value != document.Settings.PointNameVerticalAlignment)
            {
                document.Settings = document.Settings with { PointNameVerticalAlignment = value };
            }
        }
    }

    public bool OpenLastProjectOnStartup
    {
        get => preferences.OpenLastProjectOnStartup;
        set
        {
            if (value == preferences.OpenLastProjectOnStartup)
            {
                return;
            }

            preferences.SetOpenLastProjectOnStartup(
                value,
                value ? currentProjectPathProvider() : null);
            OnPropertyChanged();
        }
    }

    public double UiFontSize
    {
        get => preferences.UiFontSize;
        set
        {
            double normalized = Math.Round(Math.Clamp(value, 11.0, 18.0), 1);
            if (Math.Abs(normalized - preferences.UiFontSize) < 0.01)
            {
                return;
            }

            preferences.SaveUiFontSize(normalized);
            UiAppearanceService.ApplyFontSize(normalized);
            OnPropertyChanged();
        }
    }

    [RelayCommand]
    private void SaveAsApplicationDefaults()
    {
        preferences.SaveDefaultProjectSettings(document.Settings);
        dialogs.Info("已将当前工程设置保存为应用默认值。以后新建工程将使用这些设置。", "应用默认设置");
    }

    [RelayCommand]
    private void ApplyApplicationDefaults()
    {
        document.Settings = preferences.DefaultProjectSettings;
        dialogs.Info("已将应用默认设置应用到当前工程。保存工程后生效。", "应用默认设置");
    }

    private void Document_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProjectDocumentViewModel.Settings))
        {
            NotifyAllSettingsChanged();
        }
    }

    private void NotifyAllSettingsChanged()
    {
        OnPropertyChanged(nameof(IsDistanceTolerance));
        OnPropertyChanged(nameof(IsStationTolerance));
        OnPropertyChanged(nameof(DistanceToleranceCoefficientMm));
        OnPropertyChanged(nameof(StationToleranceCoefficientMm));
        OnPropertyChanged(nameof(IsClassical));
        OnPropertyChanged(nameof(IsQuasiStable));
        OnPropertyChanged(nameof(AutoMergeTransitionPoints));
        OnPropertyChanged(nameof(AutoUpdateLevelDifferences));
        OnPropertyChanged(nameof(DistanceDecimals));
        OnPropertyChanged(nameof(HeightDecimals));
        OnPropertyChanged(nameof(PointNameHorizontalAlignment));
        OnPropertyChanged(nameof(PointNameVerticalAlignment));
        OnPropertyChanged(nameof(OpenLastProjectOnStartup));
        OnPropertyChanged(nameof(UiFontSize));
    }
}

public sealed record HorizontalAlignmentOption(
    PointNameHorizontalAlignmentMode Value,
    string Label);

public sealed record VerticalAlignmentOption(
    PointNameVerticalAlignmentMode Value,
    string Label);
