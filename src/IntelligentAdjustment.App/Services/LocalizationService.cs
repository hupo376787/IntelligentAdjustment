using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace IntelligentAdjustment.App.Services;

public static class LocalizationService
{
    public const string DefaultLanguageCode = "zh-CN";
    public const string EnglishLanguageCode = "en-US";

    private static IReadOnlyDictionary<string, string> strings =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public static event EventHandler? LanguageChanged;

    public static string CurrentLanguageCode { get; private set; } = DefaultLanguageCode;

    public static IReadOnlyList<string> SupportedLanguageCodes { get; } =
        [DefaultLanguageCode, EnglishLanguageCode];

    public static void ApplyLanguage(string? languageCode)
    {
        string normalized = NormalizeLanguageCode(languageCode);
        Dictionary<string, string> loaded = LoadLanguage(normalized);

        if (loaded.Count == 0 && !string.Equals(normalized, DefaultLanguageCode, StringComparison.Ordinal))
        {
            normalized = DefaultLanguageCode;
            loaded = LoadLanguage(normalized);
        }

        strings = loaded;
        CurrentLanguageCode = normalized;

        var culture = CultureInfo.GetCultureInfo(normalized);
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        if (System.Windows.Application.Current is not null)
        {
            foreach ((string key, string value) in loaded)
            {
                System.Windows.Application.Current.Resources[key] = value;
            }
        }

        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    public static string Text(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return strings.TryGetValue(key, out string? value) ? value : key;
    }

    public static string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Text(key), args);

    public static string NormalizeLanguageCode(string? languageCode) =>
        string.Equals(languageCode, EnglishLanguageCode, StringComparison.OrdinalIgnoreCase)
            ? EnglishLanguageCode
            : DefaultLanguageCode;

    private static Dictionary<string, string> LoadLanguage(string languageCode)
    {
        try
        {
            string path = Path.Combine(
                AppContext.BaseDirectory,
                "Languages",
                $"{languageCode}.json");

            if (!File.Exists(path))
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }

            string json = File.ReadAllText(path);
            Dictionary<string, string> loaded =
                JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                ?? new Dictionary<string, string>(StringComparer.Ordinal);

            return loaded.ToDictionary(
                pair => pair.Key,
                pair => NormalizeEscapedLineBreaks(pair.Value),
                StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (IOException)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (UnauthorizedAccessException)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private static string NormalizeEscapedLineBreaks(string value) =>
        value
            .Replace("\\r\\n", Environment.NewLine, StringComparison.Ordinal)
            .Replace("\\n", Environment.NewLine, StringComparison.Ordinal)
            .Replace("\\r", Environment.NewLine, StringComparison.Ordinal);
}
