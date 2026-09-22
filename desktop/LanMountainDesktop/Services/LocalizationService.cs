using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using LanMountainDesktop.Shared.Contracts.Localization;

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

    /// <summary>宿主默认语言，也是读不到设置快照时的退路。取值集合与归一化表在 <see cref="LanguageCodes"/>。</summary>
    public const string DefaultLanguageCode = LanguageCodes.Default;

    /// <summary>
    /// 组件/视图取当前语言码的唯一入口：映射交给 <see cref="NormalizeLanguageCode"/>，
    /// 读设置快照失败时退回默认语言，而不是让组件在刷新时抛出来。
    /// 此前 12 个组件各抄了一份同样的 try/catch + "zh-CN" 兜底。
    /// </summary>
    public string ResolveLanguageCode(Func<string?> readRawLanguageCode)
    {
        try
        {
            return NormalizeLanguageCode(readRawLanguageCode());
        }
        catch
        {
            return DefaultLanguageCode;
        }
    }

    /// <summary>
    /// 只给键名的取法：当前语言没有这个键时退回**源语言（zh-CN）文案**，两边都没有才退回键名。
    /// 2026-09-22 实测的必要性：ja-JP / ko-KR 各缺 300 多个键（见 <c>LocalizationParityRatchetTests</c>），
    /// 而 <see cref="GetString"/> 不做跨语言回退——设置页里有 15 处 <c>L(key)</c> 直接把键名当兜底，
    /// 其中 7 个键（<c>settings.search.placeholder</c>、<c>settings.window.back</c> 等）在日/韩表里就没有，
    /// 于是日语/韩语用户的设置页上显示的是 <c>settings.search.placeholder</c> 这种标识符。
    /// 这里不新增任何译文，只是把已有的源语言文案接上；补翻译仍是单独的决定。
    /// </summary>
    public string GetStringWithSourceFallback(string languageCode, string key)
    {
        var text = GetString(languageCode, key, string.Empty);
        if (!string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var sourceText = GetString(DefaultLanguageCode, key, string.Empty);
        return string.IsNullOrWhiteSpace(sourceText) ? key : sourceText;
    }

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

    public string NormalizeLanguageCode(string? languageCode) => LanguageCodes.Normalize(languageCode);

    /// <summary>
    /// 当前界面是不是中文。组件里 8 处各自写了 <c>string.Equals(_languageCode, "zh-CN", ...)</c>，
    /// 判定口径（是否先归一化）散在各处，收这一处。
    /// </summary>
    public bool IsChineseLanguage(string? languageCode) => LanguageCodes.IsChinese(languageCode);

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
