using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using IntelligentAdjustment.Application.Models;
using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.App.ViewModels;

public partial class ProjectDocumentViewModel : ObservableObject
{
    private bool suppressDirty;

    [ObservableProperty]
    private string projectName = string.Empty;

    [ObservableProperty]
    private string projectNumber = string.Empty;

    [ObservableProperty]
    private string unitName = string.Empty;

    [ObservableProperty]
    private string projectLeader = string.Empty;

    [ObservableProperty]
    private string reviewer = string.Empty;

    [ObservableProperty]
    private ProjectSettings settings = new();

    [ObservableProperty]
    private ProjectRevisionState revision = new(0, -1, null);

    [ObservableProperty]
    private bool isDirty;

    public ObservableCollection<ObservationLineInfo> Lines { get; } = new();
    public ObservableCollection<LevelDifferenceRowViewModel> LevelDifferences { get; } = new();
    public ObservableCollection<KnownHeightRowViewModel> KnownHeights { get; } = new();

    public ProjectDocumentViewModel()
    {
        LevelDifferences.CollectionChanged += LevelDifferences_CollectionChanged;
        KnownHeights.CollectionChanged += KnownHeights_CollectionChanged;
        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(ProjectName) or nameof(ProjectNumber) or nameof(UnitName) or nameof(ProjectLeader) or nameof(Reviewer) or nameof(Settings))
            {
                MarkDirty();
            }
        };
    }

    public void Load(ProjectWorkspace workspace)
    {
        suppressDirty = true;
        try
        {
            ProjectName = workspace.Metadata.ProjectName;
            ProjectNumber = workspace.Metadata.ProjectNumber;
            UnitName = workspace.Metadata.UnitName;
            ProjectLeader = workspace.Metadata.ProjectLeader;
            Reviewer = workspace.Metadata.Reviewer;
            Settings = workspace.Settings;
            Revision = workspace.Revision;

            UnsubscribeRows(LevelDifferences);
            UnsubscribeRows(KnownHeights);

            Lines.Clear();
            foreach (var line in workspace.Lines)
            {
                Lines.Add(line);
            }

            LevelDifferences.Clear();
            foreach (var item in workspace.LevelDifferences)
            {
                LevelDifferences.Add(LevelDifferenceRowViewModel.FromDomain(item));
            }

            KnownHeights.Clear();
            foreach (var item in workspace.KnownHeights)
            {
                KnownHeights.Add(KnownHeightRowViewModel.FromDomain(item));
            }

            IsDirty = false;
        }
        finally
        {
            suppressDirty = false;
        }
    }

    public ProjectWorkspace ToWorkspace(ProjectWorkspace basis)
    {
        var metadata = basis.Metadata with
        {
            ProjectName = ProjectName.Trim(),
            ProjectNumber = ProjectNumber.Trim(),
            UnitName = UnitName.Trim(),
            ProjectLeader = ProjectLeader.Trim(),
            Reviewer = Reviewer.Trim(),
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        return new ProjectWorkspace(
            metadata,
            Settings,
            Revision,
            Lines.ToArray(),
            LevelDifferences.Select(x => x.ToDomain()).ToArray(),
            KnownHeights.Select(x => x.ToDomain()).ToArray());
    }

    public void AcceptChanges(ProjectRevisionState revision)
    {
        suppressDirty = true;
        try
        {
            Revision = revision;
            IsDirty = false;
        }
        finally
        {
            suppressDirty = false;
        }
    }

    public void MarkDirty()
    {
        if (!suppressDirty)
        {
            IsDirty = true;
        }
    }

    partial void OnSettingsChanged(ProjectSettings value) => MarkDirty();

    private void LevelDifferences_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateRowSubscriptions(e);
        MarkDirty();
    }

    private void KnownHeights_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateRowSubscriptions(e);
        MarkDirty();
    }

    private void UpdateRowSubscriptions(NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (object item in e.OldItems)
            {
                if (item is INotifyPropertyChanged notify)
                {
                    notify.PropertyChanged -= Row_PropertyChanged;
                }
            }
        }

        if (e.NewItems is not null)
        {
            foreach (object item in e.NewItems)
            {
                if (item is INotifyPropertyChanged notify)
                {
                    notify.PropertyChanged += Row_PropertyChanged;
                }
            }
        }
    }

    private void Row_PropertyChanged(object? sender, PropertyChangedEventArgs e) => MarkDirty();

    private void UnsubscribeRows<T>(IEnumerable<T> rows)
        where T : INotifyPropertyChanged
    {
        foreach (var row in rows)
        {
            row.PropertyChanged -= Row_PropertyChanged;
        }
    }
}
