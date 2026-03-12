using System.Globalization;
using System.Text.Json;

namespace LanMountainDesktop.PluginSdk;

public sealed class PluginLocalizer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly Dictionary<string, Dictionary<string, string>> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    public PluginLocalizer(string pluginDirectory, string? languageCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginDirectory);

        PluginDirectory = pluginDirectory;
        LanguageCode = NormalizeLanguageCode(languageCode);
    }

    public string PluginDirectory { get; }

    public string LanguageCode { get; }

    public static PluginLocalizer Create(IPluginRuntimeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new PluginLocalizer(context.PluginDirectory, ResolveLanguageCode(context.Properties));
    }

    public static PluginLocalizer Create(PluginDesktopComponentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new PluginLocalizer(context.PluginDirectory, ResolveLanguageCode(context.Properties));
    }

    public string GetString(string key, string fallback)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var primaryTable = LoadLanguageTable(LanguageCode);
        if (primaryTable.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        if (!string.Equals(LanguageCode, "en-US", StringComparison.OrdinalIgnoreCase))
        {
            var fallbackTable = LoadLanguageTable("en-US");
            if (fallbackTable.TryGetValue(key, out value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return fallback;
    }

    public string Format(string key, string fallback, params object[] args)
    {
        return string.Format(CultureInfo.CurrentCulture, GetString(key, fallback), args);
    }

    public static string NormalizeLanguageCode(string? languageCode)
    {
        return string.Equals(languageCode, "en-US", StringComparison.OrdinalIgnoreCase)
            ? "en-US"
            : "zh-CN";
    }

    public static string ResolveLanguageCode(IReadOnlyDictionary<string, object?> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        return properties.TryGetValue(PluginHostPropertyKeys.HostLanguageCode, out var rawValue) &&
               rawValue is string languageCode
            ? NormalizeLanguageCode(languageCode)
            : NormalizeLanguageCode(CultureInfo.CurrentUICulture.Name);
    }

    private Dictionary<string, string> LoadLanguageTable(string languageCode)
    {
        if (_cache.TryGetValue(languageCode, out var table))
        {
            return table;
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var filePath = Path.Combine(PluginDirectory, "Localization", $"{languageCode}.json");
            if (File.Exists(filePath))
            {
                var json = File.ReadAllText(filePath).TrimStart('\uFEFF');
                var data = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions);
                if (data is not null)
                {
                    result = new Dictionary<string, string>(data, StringComparer.OrdinalIgnoreCase);
                }
            }
        }
        catch
        {
            // Keep empty localization table for plugin resilience.
        }

        _cache[languageCode] = result;
        return result;
    }
}
