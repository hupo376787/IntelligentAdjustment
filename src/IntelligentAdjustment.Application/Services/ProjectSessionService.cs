using System.Security.Cryptography;
using IntelligentAdjustment.Application.Import;
using IntelligentAdjustment.Application.Models;
using IntelligentAdjustment.Core.Adjustment;
using IntelligentAdjustment.Core.Routes;
using IntelligentAdjustment.Domain;
using IntelligentAdjustment.Infrastructure;

namespace IntelligentAdjustment.Application.Services;

public sealed class ProjectSessionService
{
    private readonly OutFileImporter _outImporter = new();
    private ProjectDatabase? _database;
    private ProjectRepository? _repository;

    public string? CurrentProjectPath { get; private set; }

    public bool HasOpenProject => _repository is not null;

    public async Task<ProjectWorkspace> CreateAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (File.Exists(filePath))
        {
            throw new IOException($"项目文件已经存在：{filePath}");
        }

        _database = new ProjectDatabase(filePath);
        await _database.CreateAsync(cancellationToken);
        _repository = new ProjectRepository(_database);
        CurrentProjectPath = filePath;

        var workspace = await LoadAsync(cancellationToken);
        string defaultName = Path.GetFileNameWithoutExtension(filePath);
        var metadata = workspace.Metadata with
        {
            ProjectName = defaultName,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        await _repository.SaveProjectInputsAsync(
            metadata,
            workspace.Settings,
            workspace.LevelDifferences,
            workspace.KnownHeights,
            cancellationToken);

        return await LoadAsync(cancellationToken);
    }

    public async Task<ProjectWorkspace> OpenAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("项目文件不存在。", filePath);
        }

        if (!ProjectDatabase.HasProjectExtension(filePath))
        {
            throw new InvalidOperationException("IntelligentAdjustment 项目文件必须使用 .iap 扩展名。");
        }

        _database = new ProjectDatabase(filePath);
        _repository = new ProjectRepository(_database);
        CurrentProjectPath = filePath;
        return await LoadAsync(cancellationToken);
    }

    public async Task<ProjectWorkspace> LoadAsync(CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        return new ProjectWorkspace(
            await _repository!.LoadMetadataAsync(cancellationToken),
            await _repository.LoadSettingsAsync(cancellationToken),
            await _repository.LoadRevisionAsync(cancellationToken),
            await _repository.LoadLinesAsync(cancellationToken),
            await _repository.LoadLevelDifferencesAsync(cancellationToken),
            await _repository.LoadKnownHeightsAsync(cancellationToken));
    }

    public async Task<ProjectWorkspace> SaveAsync(
        ProjectWorkspace workspace,
        CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        await _repository!.SaveProjectInputsAsync(
            workspace.Metadata,
            workspace.Settings,
            workspace.LevelDifferences,
            workspace.KnownHeights,
            cancellationToken);
        return await LoadAsync(cancellationToken);
    }

    public async Task<ProjectWorkspace> SaveAsAsync(
        ProjectWorkspace workspace,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        _ = await SaveAsync(workspace, cancellationToken);

        if (File.Exists(destinationPath))
        {
            throw new IOException($"目标项目文件已经存在：{destinationPath}");
        }

        File.Copy(CurrentProjectPath!, destinationPath);
        return await OpenAsync(destinationPath, cancellationToken);
    }

    public async Task<ProjectWorkspace> ImportOutFilesAsync(
        IEnumerable<string> filePaths,
        CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        foreach (string filePath in filePaths)
        {
            OutImportResult parsed = await _outImporter.ParseAsync(filePath, cancellationToken);
            byte[] bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
            string sha256 = Convert.ToHexString(SHA256.HashData(bytes));

            await _repository!.ImportOutAsync(
                filePath,
                bytes,
                sha256,
                parsed.LevelDifferences,
                parsed.KnownHeights,
                cancellationToken);
        }

        return await LoadAsync(cancellationToken);
    }

    public async Task<CalculationBundle> CalculateAsync(
        ProjectWorkspace workspace,
        CancellationToken cancellationToken = default)
    {
        EnsureOpen();

        var routeSearch = new RouteSearchEngine();
        IReadOnlyList<NetworkRoute> routes = routeSearch.Search(
            workspace.LevelDifferences,
            workspace.KnownHeights,
            workspace.Settings);

        IAdjustmentSolver solver = workspace.Settings.AdjustmentMethod switch
        {
            AdjustmentMethod.Classical => new ClassicalAdjustmentSolver(),
            AdjustmentMethod.QuasiStable => new QuasiStableAdjustmentSolver(),
            _ => throw new NotSupportedException($"Unsupported adjustment method: {workspace.Settings.AdjustmentMethod}")
        };

        AdjustmentResult result = solver.Solve(workspace.LevelDifferences, workspace.KnownHeights);
        await _repository!.SaveCalculationAsync(routes, result, workspace.Settings, cancellationToken);

        ProjectRevisionState revision = await _repository.LoadRevisionAsync(cancellationToken);
        return new CalculationBundle(routes, result, revision);
    }

    public void Close()
    {
        _repository = null;
        _database = null;
        CurrentProjectPath = null;
    }

    private void EnsureOpen()
    {
        if (_repository is null || string.IsNullOrWhiteSpace(CurrentProjectPath))
        {
            throw new InvalidOperationException("当前没有打开项目。");
        }
    }
}
