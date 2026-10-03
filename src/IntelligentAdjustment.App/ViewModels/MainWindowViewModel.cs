using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IntelligentAdjustment.App.Services;
using IntelligentAdjustment.Application.Models;
using IntelligentAdjustment.Application.Services;
using IntelligentAdjustment.Core.Routes;
using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly ProjectSessionService session;
    private readonly IUserDialogService dialogs;
    private ProjectWorkspace? basisWorkspace;
    private AdjustmentResultsTabViewModel? adjustmentResultsTab;

    [ObservableProperty]
    private WorkspaceTabViewModel? selectedTab;

    [ObservableProperty]
    private string? currentProjectPath;

    [ObservableProperty]
    private string statusMessage = "就绪";

    [ObservableProperty]
    private bool isBusy;

    public MainWindowViewModel(ProjectSessionService session, IUserDialogService dialogs)
    {
        this.session = session;
        this.dialogs = dialogs;
        Document.PropertyChanged += Document_PropertyChanged;
        OpenDashboard();
    }

    public event EventHandler? RequestClose;

    public ProjectDocumentViewModel Document { get; } = new();
    public ObservableCollection<WorkspaceTabViewModel> Tabs { get; } = new();

    public string WindowTitle
    {
        get
        {
            string project = basisWorkspace is null
                ? "未打开工程"
                : string.IsNullOrWhiteSpace(Document.ProjectName)
                    ? Path.GetFileNameWithoutExtension(CurrentProjectPath)
                    : Document.ProjectName;

            return $"{project}{(Document.IsDirty ? " *" : string.Empty)} - IntelligentAdjustment";
        }
    }

    public bool HasProject => basisWorkspace is not null;

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
            ProjectWorkspace workspace = await session.CreateAsync(filePath);
            LoadWorkspace(workspace, filePath);
            StatusMessage = $"已新建工程：{Path.GetFileName(filePath)}";
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
            ProjectWorkspace workspace = await session.OpenAsync(filePath);
            LoadWorkspace(workspace, filePath);
            StatusMessage = $"已打开工程：{Path.GetFileName(filePath)}";
        });
    }

    [RelayCommand]
    private async Task SaveProjectAsync()
    {
        if (!HasProject)
        {
            dialogs.Info("请先新建或打开工程。");
            return;
        }

        await RunBusyAsync(SaveCurrentProjectCoreAsync);
    }

    [RelayCommand]
    private async Task SaveProjectAsAsync()
    {
        if (!HasProject || basisWorkspace is null)
        {
            dialogs.Info("请先新建或打开工程。");
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
            ProjectWorkspace saved = await session.SaveAsAsync(workspace, target);
            LoadWorkspace(saved, target);
            StatusMessage = $"工程已另存为：{Path.GetFileName(target)}";
        });
    }

    [RelayCommand]
    private async Task ImportOutAsync()
    {
        if (!HasProject)
        {
            dialogs.Info("请先新建或打开工程，再导入观测数据。");
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
            ProjectWorkspace workspace = await session.ImportOutFilesAsync(files);
            LoadWorkspace(workspace, CurrentProjectPath!);
            OpenLevelDifferences();
            StatusMessage = $"已导入 {files.Count} 个 OUT 文件；每个文件建立一条线路。";
        });
    }

    [RelayCommand]
    private async Task SearchRoutesAsync()
    {
        if (!await EnsureSavedProjectAsync())
        {
            return;
        }

        await RunBusyAsync(() =>
        {
            var engine = new RouteSearchEngine();
            IReadOnlyList<NetworkRoute> routes = engine.Search(
                basisWorkspace!.LevelDifferences,
                basisWorkspace.KnownHeights,
                basisWorkspace.Settings);

            RoutesTabViewModel tab = GetOrCreateRoutesTab();
            tab.Load(routes, basisWorkspace.Settings);
            SelectedTab = tab;
            StatusMessage = $"线路搜索完成，共 {routes.Count} 条闭合/附合路线。";
            return Task.CompletedTask;
        });
    }

    [RelayCommand]
    private async Task CalculateAdjustmentAsync()
    {
        if (!await EnsureSavedProjectAsync())
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            CalculationBundle bundle = await session.CalculateAsync(basisWorkspace!);

            RoutesTabViewModel routeTab = GetOrCreateRoutesTab();
            routeTab.Load(bundle.Routes, basisWorkspace!.Settings);

            adjustmentResultsTab ??= new AdjustmentResultsTabViewModel(CalculateAdjustmentAsync);
            if (!Tabs.Contains(adjustmentResultsTab))
            {
                Tabs.Add(adjustmentResultsTab);
            }

            adjustmentResultsTab.Load(bundle.AdjustmentResult);
            basisWorkspace = basisWorkspace with { Revision = bundle.Revision };
            Document.AcceptChanges(bundle.Revision);
            SelectedTab = adjustmentResultsTab;
            StatusMessage = "高程平差计算完成。";
            OnPropertyChanged(nameof(WindowTitle));
        });
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
    private void OpenRawObservations() =>
        SelectedTab = GetOrCreateTab(
            "raw-observations",
            () => new PlaceholderTabViewModel(
                "raw-observations",
                "原始观测",
                "仪器原始观测页面将在各厂商 Parser 接入后启用。"));

    [RelayCommand]
    private void OpenLevelDifferences() =>
        SelectedTab = GetOrCreateTab(
            "differences",
            () => new LevelDifferencesTabViewModel(Document));

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
    private void OpenMapSketch() =>
        SelectedTab = GetOrCreateTab(
            "map-sketch",
            () => new PlaceholderTabViewModel(
                "map-sketch",
                "网形草图",
                "下一阶段将加入节点拖动、框选、平移、滚轮缩放、Fit to View 与坐标持久化。"));

    [RelayCommand]
    private void OpenNetworkGraph() =>
        SelectedTab = GetOrCreateTab(
            "network-graph",
            () => new PlaceholderTabViewModel(
                "network-graph",
                "水准网图",
                "水准网图将使用草图逻辑坐标与高差网络拓扑生成，并支持 PNG/SVG 导出。"));

    [RelayCommand]
    private void OpenSettings() =>
        SelectedTab = GetOrCreateTab(
            "settings",
            () => new PlaceholderTabViewModel(
                "settings",
                "工程设置",
                "工程设置 UI 将绑定到当前 .iap 项目的 ProjectSettings。"));

    [RelayCommand]
    private void About() =>
        dialogs.Info(
            "IntelligentAdjustment\n.NET 8 / WPF\n当前版本：WPF 主程序基础链路",
            "关于");

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
            dialogs.Info("请先新建或打开工程。");
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
            StatusMessage = "工程没有需要保存的修改。";
            return;
        }

        if (!ValidateDocument())
        {
            return;
        }

        ProjectWorkspace workspace = Document.ToWorkspace(basisWorkspace);
        ProjectWorkspace saved = await session.SaveAsync(workspace);
        LoadWorkspace(saved, CurrentProjectPath!);
        StatusMessage = "工程已保存。";
    }

    private bool ValidateDocument()
    {
        var duplicateKnown = Document.KnownHeights
            .GroupBy(x => x.PointName.Trim(), StringComparer.Ordinal)
            .FirstOrDefault(x => x.Key.Length > 0 && x.Count() > 1);
        if (duplicateKnown is not null)
        {
            dialogs.Error($"已知高程点“{duplicateKnown.Key}”重复输入，请先修正。");
            return false;
        }

        foreach (var item in Document.LevelDifferences)
        {
            if (string.IsNullOrWhiteSpace(item.FromPoint) || string.IsNullOrWhiteSpace(item.ToPoint))
            {
                dialogs.Error("高差观测的起点名和终点名不能为空。");
                return false;
            }

            if (item.DistanceMeters <= 0)
            {
                dialogs.Error($"测段 {item.FromPoint} → {item.ToPoint} 的距离必须大于 0。");
                return false;
            }

            if (item.StationCount <= 0)
            {
                dialogs.Error($"测段 {item.FromPoint} → {item.ToPoint} 的测站数必须大于 0。");
                return false;
            }
        }

        return true;
    }

    private void LoadWorkspace(ProjectWorkspace workspace, string filePath)
    {
        basisWorkspace = workspace;
        CurrentProjectPath = filePath;
        Document.Load(workspace);

        foreach (WorkspaceTabViewModel tab in Tabs.ToArray())
        {
            if (tab.Key is not "dashboard")
            {
                Tabs.Remove(tab);
            }
        }

        adjustmentResultsTab = null;
        OpenDashboard();
        OnPropertyChanged(nameof(HasProject));
        OnPropertyChanged(nameof(WindowTitle));
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
            if (Document.IsDirty && adjustmentResultsTab is not null && adjustmentResultsTab.Heights.Count > 0)
            {
                adjustmentResultsTab.IsStale = true;
            }

            OnPropertyChanged(nameof(WindowTitle));
        }
    }

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
            StatusMessage = "操作失败。";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
