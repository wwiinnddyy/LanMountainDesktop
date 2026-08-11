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

    /// <summary>
    /// 清除指定语言代码的缓存，强制下次重新加载。
    /// 在语言切换时调用此方法以确保加载最新的语言文件。
    /// </summary>
    public void ClearCache(string? languageCode = null)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            _cache.Clear();
        }
        else
        {
            var normalizedCode = NormalizeLanguageCode(languageCode);
            _cache.Remove(normalizedCode);
        }
    }

    public string NormalizeLanguageCode(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return "zh-CN";
        }

        return languageCode.ToLowerInvariant() switch
        {
            "en-us" or "en" => "en-US",
            "ja-jp" or "ja" => "ja-JP",
            "ko-kr" or "ko" => "ko-KR",
            _ => "zh-CN"
        };
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
                if (data is not null && data.Count > 0)
                {
                    result = new Dictionary<string, string>(data, StringComparer.OrdinalIgnoreCase);
                }
            }
        }
        catch
        {
            // Keep empty table for resilience.
        }

        // 只有当语言表非空时才缓存，这样如果加载失败可以下次重试
        if (result.Count > 0)
        {
            _cache[languageCode] = result;
        }
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
