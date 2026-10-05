using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IntelligentAdjustment.App.Services;
using IntelligentAdjustment.App.Views;
using IntelligentAdjustment.Application.Models;
using IntelligentAdjustment.Application.Export;
using IntelligentAdjustment.Application.Services;
using IntelligentAdjustment.Core.Routes;
using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly ProjectSessionService session;
    private readonly IUserDialogService dialogs;
    private readonly ApplicationPreferencesService preferences;
    private readonly Stack<DatabaseUndoEntry> databaseUndoStack = new();
    private readonly Stack<DatabaseUndoEntry> databaseRedoStack = new();
    private bool initialized;
    private ProjectWorkspace? basisWorkspace;
    private AdjustmentResultsTabViewModel? adjustmentResultsTab;
    private LineManagementTabViewModel? lineManagementTab;
    private RawObservationsTabViewModel? rawObservationsTab;
    private InstrumentImportTabViewModel? instrumentImportTab;
    private MapSketchTabViewModel? mapSketchTab;
    private NetworkGraphTabViewModel? networkGraphTab;
    private ReportTabViewModel? reportTab;

    [ObservableProperty]
    private WorkspaceTabViewModel? selectedTab;

    [ObservableProperty]
    private string? currentProjectPath;

    [ObservableProperty]
    private string statusMessage = LocalizationService.Text("Loc.Status.Ready");

    [ObservableProperty]
    private bool isBusy;

    public MainWindowViewModel(
        ProjectSessionService session,
        IUserDialogService dialogs)
        : this(session, dialogs, new ApplicationPreferencesService())
    {
    }

    public MainWindowViewModel(
        ProjectSessionService session,
        IUserDialogService dialogs,
        ApplicationPreferencesService preferences)
    {
        this.session = session;
        this.dialogs = dialogs;
        this.preferences = preferences;
        Document.PropertyChanged += Document_PropertyChanged;
        Document.UndoStateChanged += (_, _) => RefreshUndoCommands();
        LocalizationService.LanguageChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(WindowTitle));
            OnPropertyChanged(nameof(CurrentProjectPathDisplay));
        };
        OpenDashboard();
    }

    public event EventHandler? RequestClose;

    public ProjectDocumentViewModel Document { get; } = new();
    public ObservableCollection<WorkspaceTabViewModel> Tabs { get; } = new();

    public string WindowTitle
    {
        get
        {
            string fallbackProjectName = string.IsNullOrWhiteSpace(CurrentProjectPath)
                ? LocalizationService.Text("Loc.Project.Unnamed")
                : Path.GetFileNameWithoutExtension(CurrentProjectPath)
                    ?? LocalizationService.Text("Loc.Project.Unnamed");

            string project = basisWorkspace is null
                ? LocalizationService.Text("Loc.Status.NotOpened")
                : string.IsNullOrWhiteSpace(Document.ProjectName)
                    ? fallbackProjectName
                    : Document.ProjectName;

            return $"{project}{(Document.IsDirty ? " *" : string.Empty)} - Intelligent Adjustment";
        }
    }

    public bool HasProject => basisWorkspace is not null;

    public string CurrentProjectPathDisplay =>
        string.IsNullOrWhiteSpace(CurrentProjectPath)
            ? LocalizationService.Text("Loc.Status.NotOpened")
            : CurrentProjectPath;

    partial void OnCurrentProjectPathChanged(string? value) =>
        OnPropertyChanged(nameof(CurrentProjectPathDisplay));

    public async Task InitializeAsync(Action<StartupProgress>? reportProgress = null)
    {
        if (initialized)
        {
            reportProgress?.Invoke(new StartupProgress(90, LocalizationService.Text("Loc.Startup.AlreadyInitialized")));
            return;
        }

        initialized = true;
        reportProgress?.Invoke(new StartupProgress(36, LocalizationService.Text("Loc.Startup.CheckingOptions")));

        if (!preferences.OpenLastProjectOnStartup)
        {
            reportProgress?.Invoke(new StartupProgress(82, LocalizationService.Text("Loc.Startup.BlankWorkspace")));
            return;
        }

        string? path = preferences.LastProjectPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            reportProgress?.Invoke(new StartupProgress(82, LocalizationService.Text("Loc.Startup.BlankWorkspace")));
            return;
        }

        reportProgress?.Invoke(new StartupProgress(46, LocalizationService.Text("Loc.Startup.CheckingLastProject")));
        if (!File.Exists(path))
        {
            preferences.ClearMissingLastProject();
            StatusMessage = LocalizationService.Text("Loc.Status.ProjectMissing");
            reportProgress?.Invoke(new StartupProgress(82, LocalizationService.Text("Loc.Startup.BlankWorkspace")));
            return;
        }

        await RunBusyAsync(async () =>
        {
            reportProgress?.Invoke(new StartupProgress(56, LocalizationService.Format("Loc.Startup.OpeningProject", Path.GetFileName(path))));
            ClearDatabaseUndoHistory();
            ProjectWorkspace workspace = await session.OpenAsync(path);

            reportProgress?.Invoke(new StartupProgress(70, LocalizationService.Text("Loc.Startup.LoadingProject")));
            LoadWorkspace(workspace, path);

            reportProgress?.Invoke(new StartupProgress(80, LocalizationService.Text("Loc.Startup.RestoringResults")));
            await RestoreLatestCalculationAsync();

            reportProgress?.Invoke(new StartupProgress(88, LocalizationService.Text("Loc.Startup.SyncingWorkspace")));
            StatusMessage = LocalizationService.Format("Loc.Status.AutoOpened", Path.GetFileName(path));
        });
    }

    [RelayCommand]
    private async Task NewProjectAsync()
    {
        if (!await EnsureCanLeaveCurrentProjectAsync())
        {
            return;
        }

        string? filePath = dialogs.PickNewProjectPath();
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            ClearDatabaseUndoHistory();
            ProjectWorkspace workspace = await session.CreateAsync(
                filePath,
                preferences.DefaultProjectSettings);
            LoadWorkspace(workspace, filePath);
            StatusMessage = LocalizationService.Format("Loc.Status.Created", Path.GetFileName(filePath));
        });
    }

    [RelayCommand]
    private async Task OpenProjectAsync()
    {
        if (!await EnsureCanLeaveCurrentProjectAsync())
        {
            return;
        }

        string? filePath = dialogs.PickProjectToOpen();
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            ClearDatabaseUndoHistory();
            ProjectWorkspace workspace = await session.OpenAsync(filePath);
            LoadWorkspace(workspace, filePath);
            await RestoreLatestCalculationAsync();
            StatusMessage = LocalizationService.Format("Loc.Status.Opened", Path.GetFileName(filePath));
        });
    }

    [RelayCommand]
    private async Task SaveProjectAsync()
    {
        if (!HasProject)
        {
            dialogs.Info(LocalizationService.Text("Loc.Message.ProjectRequired"));
            return;
        }

        await RunBusyAsync(SaveCurrentProjectCoreAsync);
    }

    [RelayCommand]
    private async Task SaveProjectAsAsync()
    {
        if (!HasProject || basisWorkspace is null)
        {
            dialogs.Info(LocalizationService.Text("Loc.Message.ProjectRequired"));
            return;
        }

        string? target = dialogs.PickSaveAsProjectPath(CurrentProjectPath);
        if (string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        if (!ValidateDocument())
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            ProjectWorkspace workspace = Document.ToWorkspace(basisWorkspace);
            ProjectWorkspace saved = await session.SaveAsAsync(
                workspace,
                target,
                Document.CalculationInputsChanged);
            ClearDatabaseUndoHistory();
            LoadWorkspace(saved, target);
            await RestoreLatestCalculationAsync();
            StatusMessage = LocalizationService.Format("Loc.Status.SavedAs", Path.GetFileName(target));
        });
    }

    [RelayCommand]
    private async Task ImportOutAsync()
    {
        if (!HasProject)
        {
            dialogs.Info(LocalizationService.Text("Loc.Message.ProjectRequiredImport"));
            return;
        }

        IReadOnlyList<string> files = dialogs.PickOutFiles();
        if (files.Count == 0)
        {
            return;
        }

        if (Document.IsDirty)
        {
            await RunBusyAsync(SaveCurrentProjectCoreAsync);
            if (Document.IsDirty)
            {
                return;
            }
        }

        await RunBusyAsync(async () =>
        {
            ClearDatabaseUndoHistory();
            ProjectWorkspace workspace = await session.ImportOutFilesAsync(files);
            LoadWorkspace(workspace, CurrentProjectPath!, resetTabs: false);
            OpenLevelDifferences();
            StatusMessage = LocalizationService.Format("Loc.Status.ImportedOut", files.Count);
        });
    }

    [RelayCommand]
    private async Task ExportOutAsync()
    {
        if (!HasProject)
        {
            dialogs.Info(LocalizationService.Text("Loc.Message.ProjectRequired"));
            return;
        }

        if (Document.LevelDifferences.Count == 0)
        {
            dialogs.Info(LocalizationService.Text("Loc.Message.NoDifferencesToExport"));
            return;
        }

        if (!ValidateDocument())
        {
            return;
        }

        string projectName = string.IsNullOrWhiteSpace(Document.ProjectName)
            ? Path.GetFileNameWithoutExtension(CurrentProjectPath)
                ?? LocalizationService.Text("Loc.Project.Unnamed")
            : Document.ProjectName;

        string? target = dialogs.PickOutExportPath(projectName);
        if (string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        LevelDifference[] differences = Document.LevelDifferences
            .Select(x => x.ToDomain())
            .ToArray();
        KnownHeight[] knownHeights = Document.KnownHeights
            .Select(x => x.ToDomain())
            .ToArray();

        await RunBusyAsync(async () =>
        {
            var exporter = new OutFileExporter();
            await exporter.ExportAsync(target, differences, knownHeights);
            StatusMessage = LocalizationService.Format(
                "Loc.Status.ExportedOut",
                Path.GetFileName(target));
        });
    }

    [RelayCommand]
    private async Task SearchRoutesAsync()
    {
        if (!await EnsureSavedProjectAsync())
        {
            return;
        }

        int routeCount = 0;
        bool completed = false;

        await RunBusyAsync(() =>
        {
            var engine = new RouteSearchEngine();
            IReadOnlyList<NetworkRoute> routes = engine.Search(
                basisWorkspace!.LevelDifferences,
                basisWorkspace.KnownHeights,
                basisWorkspace.Settings);

            routeCount = routes.Count;
            RoutesTabViewModel tab = GetOrCreateRoutesTab();
            tab.Load(routes, basisWorkspace.Settings);
            SelectedTab = tab;
            StatusMessage = LocalizationService.Format("Loc.Status.RoutesDone", routeCount);
            completed = true;
            return Task.CompletedTask;
        });

        if (completed)
        {
            dialogs.Info(
                LocalizationService.Format("Loc.Message.SearchRoutesDone", routeCount),
                LocalizationService.Text("Loc.Message.SearchRoutesTitle"));
        }
    }

    [RelayCommand]
    private async Task CalculateAdjustmentAsync()
    {
        if (!await EnsureSavedProjectAsync())
        {
            return;
        }

        WorkspaceTabViewModel? returnToTab = SelectedTab;
        bool completed = false;

        await RunBusyAsync(async () =>
        {
            ClearDatabaseUndoHistory();
            CalculationBundle bundle = await session.CalculateAsync(basisWorkspace!);

            RoutesTabViewModel routeTab = GetOrCreateRoutesTab();
            routeTab.Load(bundle.Routes, basisWorkspace!.Settings);

            adjustmentResultsTab ??= new AdjustmentResultsTabViewModel(CalculateAdjustmentAsync);
            if (!Tabs.Contains(adjustmentResultsTab))
            {
                Tabs.Add(adjustmentResultsTab);
            }

            adjustmentResultsTab.Load(bundle.AdjustmentResult, basisWorkspace.Settings);
            basisWorkspace = basisWorkspace with { Revision = bundle.Revision };
            Document.AcceptChanges(bundle.Revision);

            // “执行高程平差”是计算动作；“平差结果”才负责打开成果页面。
            // 计算结束后保持用户当前页面，避免两个入口表现成同一个功能。
            SelectedTab = returnToTab ?? GetOrCreateTab(
                "dashboard",
                () => new DashboardTabViewModel(Document));

            StatusMessage = LocalizationService.Text("Loc.Status.AdjustmentDone");
            OnPropertyChanged(nameof(WindowTitle));
            completed = true;
        });

        if (completed)
        {
            dialogs.Info(
                LocalizationService.Text("Loc.Message.AdjustmentDone"),
                LocalizationService.Text("Loc.Message.AdjustmentTitle"));
        }
    }

    [RelayCommand]
    private void OpenDashboard()
    {
        WorkspaceTabViewModel tab = GetOrCreateTab(
            "dashboard",
            () => new DashboardTabViewModel(Document));
        SelectedTab = tab;
    }

    [RelayCommand]
    private void OpenRawObservations()
    {
        rawObservationsTab ??= new RawObservationsTabViewModel(Document, dialogs);
        if (!Tabs.Contains(rawObservationsTab))
        {
            Tabs.Add(rawObservationsTab);
        }

        rawObservationsTab.Refresh();
        SelectedTab = rawObservationsTab;
    }

    [RelayCommand]
    private void OpenInstrumentImport()
    {
        instrumentImportTab ??= new InstrumentImportTabViewModel(
            session,
            dialogs,
            EnsureSavedProjectAsync,
            workspace =>
            {
                if (string.IsNullOrWhiteSpace(CurrentProjectPath))
                {
                    return;
                }

                ClearDatabaseUndoHistory();
                LoadWorkspace(workspace, CurrentProjectPath, resetTabs: false);
                StatusMessage = LocalizationService.Text("Loc.Status.ImportDone");
            });

        if (!Tabs.Contains(instrumentImportTab))
        {
            Tabs.Add(instrumentImportTab);
        }

        SelectedTab = instrumentImportTab;
    }

    [RelayCommand]
    private void OpenLineManagement()
    {
        lineManagementTab ??= new LineManagementTabViewModel(
            Document,
            session,
            dialogs,
            EnsureSavedProjectAsync,
            workspace =>
            {
                if (string.IsNullOrWhiteSpace(CurrentProjectPath))
                {
                    return;
                }

                LoadWorkspace(workspace, CurrentProjectPath, resetTabs: false);
                StatusMessage = LocalizationService.Text("Loc.Status.LineUpdated");
            },
            ExecuteUndoableDatabaseActionAsync);

        if (!Tabs.Contains(lineManagementTab))
        {
            Tabs.Add(lineManagementTab);
        }

        lineManagementTab.Refresh();
        SelectedTab = lineManagementTab;
    }

    [RelayCommand]
    private void OpenLevelDifferences() =>
        SelectedTab = GetOrCreateTab(
            "differences",
            () => new LevelDifferencesTabViewModel(Document, dialogs));

    [RelayCommand]
    private void OpenKnownHeights() =>
        SelectedTab = GetOrCreateTab(
            "known-heights",
            () => new KnownHeightsTabViewModel(Document, dialogs));

    [RelayCommand]
    private void OpenRoutes() => SelectedTab = GetOrCreateRoutesTab();

    [RelayCommand]
    private void OpenAdjustmentResults()
    {
        adjustmentResultsTab ??= new AdjustmentResultsTabViewModel(CalculateAdjustmentAsync);
        if (!Tabs.Contains(adjustmentResultsTab))
        {
            Tabs.Add(adjustmentResultsTab);
        }

        SelectedTab = adjustmentResultsTab;
    }

    [RelayCommand]
    private void OpenMapSketch()
    {
        mapSketchTab ??= new MapSketchTabViewModel(Document);
        if (!Tabs.Contains(mapSketchTab))
        {
            Tabs.Add(mapSketchTab);
        }

        mapSketchTab.Refresh();
        SelectedTab = mapSketchTab;
    }

    [RelayCommand]
    private void OpenNetworkGraph()
    {
        IReadOnlyList<NetworkRoute> routes = BuildCurrentRoutes();

        networkGraphTab ??= new NetworkGraphTabViewModel(Document, routes);
        if (!Tabs.Contains(networkGraphTab))
        {
            Tabs.Add(networkGraphTab);
        }
        else
        {
            networkGraphTab.LoadRoutes(routes);
        }

        networkGraphTab.Refresh();
        SelectedTab = networkGraphTab;
    }

    [RelayCommand]
    private void OpenReport()
    {
        reportTab ??= new ReportTabViewModel(
            Document,
            session,
            dialogs,
            EnsureSavedProjectAsync);

        if (!Tabs.Contains(reportTab))
        {
            Tabs.Add(reportTab);
        }

        SelectedTab = reportTab;
    }

    [RelayCommand]
    private void About() => dialogs.ShowAbout();

    [RelayCommand]
    private async Task CloseProjectAsync()
    {
        if (!HasProject)
        {
            return;
        }

        if (!await EnsureCanLeaveCurrentProjectAsync())
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(CurrentProjectPath) && basisWorkspace is not null)
        {
            preferences.RememberProject(CurrentProjectPath);
        }

        session.Close();
        basisWorkspace = null;
        CurrentProjectPath = null;
        Document.Clear();
        ClearDatabaseUndoHistory();

        foreach (WorkspaceTabViewModel tab in Tabs.ToArray())
        {
            if (tab.Key is not "dashboard")
            {
                Tabs.Remove(tab);
            }
        }

        adjustmentResultsTab = null;
        lineManagementTab = null;
        rawObservationsTab = null;
        instrumentImportTab = null;
        mapSketchTab = null;
        networkGraphTab = null;
        reportTab = null;
        OpenDashboard();

        StatusMessage = LocalizationService.Text("Loc.Status.ProjectClosed");
        OnPropertyChanged(nameof(HasProject));
        OnPropertyChanged(nameof(WindowTitle));
    }

    [RelayCommand]
    private void Exit() => RequestClose?.Invoke(this, EventArgs.Empty);

    public async Task<bool> CanCloseAsync()
    {
        if (!Document.IsDirty)
        {
            return true;
        }

        UnsavedChangesChoice choice = dialogs.AskUnsavedChanges();
        if (choice == UnsavedChangesChoice.Cancel)
        {
            return false;
        }

        if (choice == UnsavedChangesChoice.Discard)
        {
            return true;
        }

        await RunBusyAsync(SaveCurrentProjectCoreAsync);
        return !Document.IsDirty;
    }

    private async Task<bool> EnsureCanLeaveCurrentProjectAsync()
    {
        if (!Document.IsDirty)
        {
            return true;
        }

        UnsavedChangesChoice choice = dialogs.AskUnsavedChanges();
        if (choice == UnsavedChangesChoice.Cancel)
        {
            return false;
        }

        if (choice == UnsavedChangesChoice.Save)
        {
            await RunBusyAsync(SaveCurrentProjectCoreAsync);
            return !Document.IsDirty;
        }

        return true;
    }

    private async Task<bool> EnsureSavedProjectAsync()
    {
        if (!HasProject)
        {
            dialogs.Info(LocalizationService.Text("Loc.Message.ProjectRequired"));
            return false;
        }

        if (Document.IsDirty)
        {
            await RunBusyAsync(SaveCurrentProjectCoreAsync);
        }

        return !Document.IsDirty && basisWorkspace is not null;
    }

    private async Task SaveCurrentProjectCoreAsync()
    {
        if (!HasProject || basisWorkspace is null)
        {
            return;
        }

        if (!Document.IsDirty)
        {
            StatusMessage = LocalizationService.Text("Loc.Status.NothingToSave");
            return;
        }

        if (!ValidateDocument())
        {
            return;
        }

        ProjectWorkspace workspace = Document.ToWorkspace(basisWorkspace);
        ClearDatabaseUndoHistory();
        ProjectWorkspace saved = await session.SaveAsync(
            workspace,
            Document.CalculationInputsChanged);
        LoadWorkspace(saved, CurrentProjectPath!, resetTabs: false);
        StatusMessage = LocalizationService.Text("Loc.Status.Saved");
    }

    private bool ValidateDocument()
    {
        var duplicateKnown = Document.KnownHeights
            .GroupBy(x => x.PointName.Trim(), StringComparer.Ordinal)
            .FirstOrDefault(x => x.Key.Length > 0 && x.Count() > 1);
        if (duplicateKnown is not null)
        {
            dialogs.Error(LocalizationService.Format("Loc.Validation.DuplicateKnown", duplicateKnown.Key));
            return false;
        }

        foreach (var item in Document.LevelDifferences)
        {
            if (string.IsNullOrWhiteSpace(item.FromPoint) || string.IsNullOrWhiteSpace(item.ToPoint))
            {
                dialogs.Error(LocalizationService.Text("Loc.Validation.EmptyDifferenceEndpoints"));
                return false;
            }

            if (item.DistanceMeters <= 0)
            {
                dialogs.Error(LocalizationService.Format("Loc.Validation.DistancePositive", item.FromPoint, item.ToPoint));
                return false;
            }

            if (item.StationCount <= 0)
            {
                dialogs.Error(LocalizationService.Format("Loc.Validation.StationsPositive", item.FromPoint, item.ToPoint));
                return false;
            }
        }

        return true;
    }

    private void LoadWorkspace(
        ProjectWorkspace workspace,
        string filePath,
        bool resetTabs = true)
    {
        basisWorkspace = workspace;
        CurrentProjectPath = filePath;
        Document.Load(workspace);
        preferences.RememberProject(filePath);

        if (resetTabs)
        {
            foreach (WorkspaceTabViewModel tab in Tabs.ToArray())
            {
                if (tab.Key is not "dashboard")
                {
                    Tabs.Remove(tab);
                }
            }

            adjustmentResultsTab = null;
            lineManagementTab = null;
            rawObservationsTab = null;
            instrumentImportTab = null;
            mapSketchTab = null;
            networkGraphTab = null;
            reportTab = null;
            OpenDashboard();
        }
        else
        {
            if (adjustmentResultsTab is not null && adjustmentResultsTab.Heights.Count > 0)
            {
                adjustmentResultsTab.IsStale = workspace.Revision.ResultsAreStale;
            }

            RoutesTabViewModel? routesTab = Tabs.OfType<RoutesTabViewModel>().FirstOrDefault();
            if (routesTab is not null && routesTab.Items.Count > 0)
            {
                routesTab.IsStale = workspace.Revision.ResultsAreStale;
            }

            lineManagementTab?.Refresh();
            rawObservationsTab?.Refresh();
            mapSketchTab?.Refresh();
            if (networkGraphTab is not null)
            {
                networkGraphTab.LoadRoutes(BuildCurrentRoutes());
            }
        }

        OnPropertyChanged(nameof(HasProject));
        OnPropertyChanged(nameof(WindowTitle));
    }

    private async Task<ProjectWorkspace> ExecuteUndoableDatabaseActionAsync(
        Func<Task<ProjectWorkspace>> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        byte[] before = await session.CaptureProjectSnapshotAsync();
        ProjectWorkspace workspace = await action();
        byte[] after = await session.CaptureProjectSnapshotAsync();

        databaseUndoStack.Push(new DatabaseUndoEntry(before, after));
        databaseRedoStack.Clear();
        RefreshUndoCommands();
        return workspace;
    }

    private void ClearDatabaseUndoHistory()
    {
        databaseUndoStack.Clear();
        databaseRedoStack.Clear();
        RefreshUndoCommands();
    }

    private void RefreshUndoCommands()
    {
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    private async Task RestoreLatestCalculationAsync()
    {
        CalculationBundle? bundle = await session.LoadLatestCalculationAsync();
        if (bundle is null || basisWorkspace is null)
        {
            return;
        }

        bool isStale = bundle.Revision.ResultsAreStale;

        RoutesTabViewModel routeTab = GetOrCreateRoutesTab();
        routeTab.Load(bundle.Routes, basisWorkspace.Settings, isStale);

        adjustmentResultsTab ??= new AdjustmentResultsTabViewModel(CalculateAdjustmentAsync);
        if (!Tabs.Contains(adjustmentResultsTab))
        {
            Tabs.Add(adjustmentResultsTab);
        }

        adjustmentResultsTab.Load(bundle.AdjustmentResult, basisWorkspace.Settings, isStale);
    }

    private IReadOnlyList<NetworkRoute> BuildCurrentRoutes()
    {
        if (basisWorkspace is null)
        {
            return Array.Empty<NetworkRoute>();
        }

        try
        {
            ProjectWorkspace current = Document.ToWorkspace(basisWorkspace);
            var routeSearch = new RouteSearchEngine();
            return routeSearch.Search(
                current.LevelDifferences,
                current.KnownHeights,
                current.Settings);
        }
        catch
        {
            return Array.Empty<NetworkRoute>();
        }
    }

    private RoutesTabViewModel GetOrCreateRoutesTab()
    {
        var existing = Tabs.OfType<RoutesTabViewModel>().FirstOrDefault();
        if (existing is not null)
        {
            return existing;
        }

        var created = new RoutesTabViewModel();
        Tabs.Add(created);
        return created;
    }

    private WorkspaceTabViewModel GetOrCreateTab(string key, Func<WorkspaceTabViewModel> factory)
    {
        WorkspaceTabViewModel? existing = Tabs.FirstOrDefault(x => x.Key == key);
        if (existing is not null)
        {
            return existing;
        }

        WorkspaceTabViewModel created = factory();
        Tabs.Add(created);
        return created;
    }

    private void Document_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProjectDocumentViewModel.IsDirty))
        {
            if (Document.IsDirty)
            {
                if (adjustmentResultsTab is not null && adjustmentResultsTab.Heights.Count > 0)
                {
                    adjustmentResultsTab.IsStale = true;
                }

                RoutesTabViewModel? routesTab = Tabs.OfType<RoutesTabViewModel>().FirstOrDefault();
                if (routesTab is not null && routesTab.Items.Count > 0)
                {
                    routesTab.IsStale = true;
                }
            }

            OnPropertyChanged(nameof(WindowTitle));
        }
    }

    private sealed record DatabaseUndoEntry(byte[] Before, byte[] After);

    private async Task RunBusyAsync(Func<Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            dialogs.Error(ex.Message);
            StatusMessage = LocalizationService.Text("Loc.Message.OperationFailed");
        }
        finally
        {
            IsBusy = false;
        }
    }
}
