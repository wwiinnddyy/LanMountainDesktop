using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace LanMountainDesktop.Services;

public sealed class LocalizationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly Dictionary<string, Dictionary<string, string>> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    public string NormalizeLanguageCode(string? languageCode)
    {
        return string.Equals(languageCode, "en-US", StringComparison.OrdinalIgnoreCase)
            ? "en-US"
            : "zh-CN";
    }

    public string GetString(string languageCode, string key, string fallback)
    {
        var normalizedLanguage = NormalizeLanguageCode(languageCode);
        var table = LoadLanguageTable(normalizedLanguage);
        return table.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;
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
            var json = TryLoadFromFileSystem(languageCode);
            if (string.IsNullOrEmpty(json))
            {
                json = TryLoadFromEmbeddedResource(languageCode);
            }

            if (!string.IsNullOrEmpty(json))
            {
                json = json.TrimStart('\uFEFF');
                var data = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions);
                if (data is not null)
                {
                    result = new Dictionary<string, string>(data, StringComparer.OrdinalIgnoreCase);
                }
            }
        }
        catch
        {
            // Keep empty table for resilience.
        }

        _cache[languageCode] = result;
        return result;
    }

    private string? TryLoadFromFileSystem(string languageCode)
    {
        try
        {
            var filePath = Path.Combine(AppContext.BaseDirectory, "Localization", $"{languageCode}.json");
            if (File.Exists(filePath))
            {
                return File.ReadAllText(filePath);
            }
        }
        catch
        {
            // Continue to next method
        }
        return null;
    }

    private string? TryLoadFromEmbeddedResource(string languageCode)
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = $"LanMountainDesktop.Localization.{languageCode}.json";
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
        }
        catch
        {
            // Continue to next method
        }
        return null;
    }
}
