using CommunityToolkit.Mvvm.ComponentModel;
using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.App.ViewModels;

public sealed class SettingsTabViewModel : WorkspaceTabViewModel
{
    private readonly ProjectDocumentViewModel document;

    public SettingsTabViewModel(ProjectDocumentViewModel document)
        : base("settings", "工程设置")
    {
        this.document = document;
    }

    public bool IsDistanceTolerance
    {
        get => document.Settings.ToleranceMode == ClosureToleranceMode.Distance;
        set
        {
            if (value && !IsDistanceTolerance)
            {
                document.Settings = document.Settings with { ToleranceMode = ClosureToleranceMode.Distance };
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsStationTolerance));
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
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsDistanceTolerance));
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
                OnPropertyChanged();
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
                OnPropertyChanged();
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
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsQuasiStable));
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
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsClassical));
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
                OnPropertyChanged();
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
                OnPropertyChanged();
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
                OnPropertyChanged();
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
                OnPropertyChanged();
            }
        }
    }
}
