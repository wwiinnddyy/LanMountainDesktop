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

    // 语言表是进程级共享的：语言文件一张就是 60-75KB、上千键，而组件/视图各自 new 实例（现量 40+ 处），
    // 实例级缓存会让同一份 JSON 在每个实例上重复读盘+解析一遍，页面重排重建组件时再涨一轮。
    // 实例 API 保持不变，调用方无感；ClearCache 语义随之上移为"全进程清表"，正是语言切换想要的。
    private static readonly object CacheGate = new();
    private static readonly Dictionary<string, Dictionary<string, string>> SharedCache =
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
            lock (CacheGate)
            {
                SharedCache.Clear();
            }
        }
        else
        {
            var normalizedCode = NormalizeLanguageCode(languageCode);
            lock (CacheGate)
            {
                SharedCache.Remove(normalizedCode);
            }
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
        lock (CacheGate)
        {
            if (SharedCache.TryGetValue(languageCode, out var cachedTable))
            {
                return cachedTable;
            }
        }

        var result = LoadLanguageTableFromDisk(languageCode);

        // 只有当语言表非空时才缓存，这样如果加载失败可以下次重试
        if (result.Count > 0)
        {
            lock (CacheGate)
            {
                // 双检：并发首载时晚到的那份直接复用先到者，避免互相覆盖。
                if (SharedCache.TryGetValue(languageCode, out var winner))
                {
                    return winner;
                }

                SharedCache[languageCode] = result;
            }
        }

        return result;
    }

    private static Dictionary<string, string> LoadLanguageTableFromDisk(string languageCode)
    {
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

        return result;
    }

    private static string? TryLoadFromFileSystem(string languageCode)
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

    private static string? TryLoadFromEmbeddedResource(string languageCode)
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
