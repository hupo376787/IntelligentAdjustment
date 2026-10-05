using System.IO;
using System.Text.Json;
using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.App.Services;

public sealed class ApplicationPreferencesService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string filePath;
    private ApplicationPreferences state;

    public ApplicationPreferencesService()
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IntelligentAdjustment");
        filePath = Path.Combine(directory, "settings.json");
        state = Load(filePath);
    }

    public ProjectSettings DefaultProjectSettings => state.DefaultProjectSettings ?? new ProjectSettings();

    public string? LastProjectPath => state.LastProjectPath;

    public bool OpenLastProjectOnStartup => state.OpenLastProjectOnStartup;

    public double UiFontSize => Math.Clamp(state.UiFontSize, 11.0, 18.0);

    public void SaveDefaultProjectSettings(ProjectSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        state = state with
        {
            DefaultProjectSettings = settings
        };
        Save();
    }

    public void RememberProject(string? projectPath)
    {
        state = state with
        {
            LastProjectPath = string.IsNullOrWhiteSpace(projectPath)
                ? state.LastProjectPath
                : Path.GetFullPath(projectPath)
        };
        Save();
    }

    public void SetOpenLastProjectOnStartup(bool value)
    {
        if (state.OpenLastProjectOnStartup == value)
        {
            return;
        }

        state = state with { OpenLastProjectOnStartup = value };
        Save();
    }

    public void ClearMissingLastProject()
    {
        state = state with { LastProjectPath = null };
        Save();
    }

    public void SaveUiFontSize(double fontSize)
    {
        state = state with { UiFontSize = Math.Clamp(fontSize, 11.0, 18.0) };
        Save();
    }

    private static ApplicationPreferences Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new ApplicationPreferences();
            }

            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<ApplicationPreferences>(json, JsonOptions)
                ?? new ApplicationPreferences();
        }
        catch (JsonException)
        {
            return new ApplicationPreferences();
        }
        catch (IOException)
        {
            return new ApplicationPreferences();
        }
        catch (UnauthorizedAccessException)
        {
            return new ApplicationPreferences();
        }
    }

    private void Save()
    {
        try
        {
            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string temporaryPath = filePath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(state, JsonOptions));
            File.Move(temporaryPath, filePath, true);
        }
        catch (IOException)
        {
            // Preferences are convenience state only; project data remains authoritative.
        }
        catch (UnauthorizedAccessException)
        {
            // Keep the in-memory preference if the local profile cannot be written.
        }
    }

    private sealed record ApplicationPreferences(
        ProjectSettings? DefaultProjectSettings = null,
        string? LastProjectPath = null,
        bool OpenLastProjectOnStartup = false,
        double UiFontSize = 13.0);
}
