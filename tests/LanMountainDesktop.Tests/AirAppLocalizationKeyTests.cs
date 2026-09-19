using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// AirApp 命名族本地化键的守卫。改名之前必须先有这四条：LocalizationService.GetString 找不到键时
/// 直接返回调用方传进来的 fallback（Services/LocalizationService.cs:57-59），所以"代码改了、语言文件
/// 漏一处"或反过来的唯一症状，是界面静默退回默认文案——不抛异常、不写日志、测试也照样绿。
/// 只约束 airapp 命名的键，不接管全仓库既有的一千多个孤儿键。
/// </summary>
public sealed class AirAppLocalizationKeyTests
{
    private static readonly string[] Languages = ["zh-CN", "en-US", "ja-JP", "ko-KR"];

    private static readonly string[] ProductionDirectories = ["core", "desktop", "airapp", "install", "mobile"];

    // 与 LocalizationService 同款解析选项：语言文件允许注释和尾逗号。
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly Regex KeyShape = new("^\"([a-z][a-z0-9_]*(?:\\.[a-z0-9_]+)+)\"$");

    [Fact]
    public void LanguageFiles_HoldNoLegacyPluginNamedKeys()
    {
        var offenders = Languages
            .SelectMany(lang => LoadKeys(lang)
                .Where(key => ContainsPluginNaming(key))
                .Select(key => $"{lang}.json:{key}"))
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void AirAppNamedKeys_ExistInEveryLanguage()
    {
        var offenders = Languages
            .Select(lang => (lang, missing: AirAppNamedKeys()
                .Where(key => !LoadKeys(lang).Contains(key))
                .ToArray()))
            .Where(pair => pair.missing.Length > 0)
            .Select(pair => $"{pair.lang}.json is missing {string.Join(", ", pair.missing)}")
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void AirAppNamedKeys_AreAllReferencedByProductionSource()
    {
        var referenced = ReferencedKeys();
        var orphans = AirAppNamedKeys()
            .Where(key => !referenced.Contains(key))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(orphans);
    }

    [Fact]
    public void AirAppNamedKeyLiterals_InSource_ExistInEveryLanguage()
    {
        var offenders = ReferencedKeys()
            .Where(ContainsAirAppNaming)
            .SelectMany(key => Languages
                .Where(lang => !LoadKeys(lang).Contains(key))
                .Select(lang => $"\"{key}\" (used in source) is absent from {lang}.json"))
            .ToArray();

        Assert.Empty(offenders);
    }

    private static IEnumerable<string> AirAppNamedKeys() =>
        Languages.SelectMany(LoadKeys).Distinct(StringComparer.Ordinal).Where(ContainsAirAppNaming);

    private static HashSet<string> LoadKeys(string language)
    {
        var path = Path.Combine(RepoRoot, "desktop", "LanMountainDesktop", "Localization", $"{language}.json");
        var json = File.ReadAllText(path).TrimStart('\uFEFF');
        var table = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions);
        Assert.NotEmpty(table!);
        return table!.Keys.ToHashSet(StringComparer.Ordinal);
    }

    private static HashSet<string> ReferencedKeys()
    {
        // 只看首段落在语言文件命名空间里的字面量：源码里 "airapp.json"、"manifest.json" 这类
        // 点分字符串形状上和键无法区分，但它们的首段没有任何语言文件用过。
        var namespaces = Languages
            .SelectMany(LoadKeys)
            .Select(key => key.Split('.')[0])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in ProductionSourceFiles())
        {
            foreach (var line in File.ReadLines(file))
            {
                var trimmed = line.TrimStart();
                // 注释里出现的键名是在解释历史，不算引用点。
                if (trimmed.StartsWith("//", StringComparison.Ordinal)
                    || trimmed.StartsWith("///", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (Match match in Regex.Matches(line, "\"[a-z][a-z0-9_]*(?:\\.[a-z0-9_]+)+\""))
                {
                    var key = KeyShape.Match(match.Value);
                    if (key.Success && namespaces.Contains(key.Groups[1].Value.Split('.')[0]))
                    {
                        keys.Add(key.Groups[1].Value);
                    }
                }
            }
        }

        return keys;
    }

    private static IEnumerable<string> ProductionSourceFiles() => ProductionDirectories
        .Select(part => Path.Combine(RepoRoot, part))
        .Where(Directory.Exists)
        .SelectMany(dir => Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
        .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

    private static bool ContainsPluginNaming(string value) =>
        value.Contains("plugin", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsAirAppNaming(string value) =>
        value.Contains("airapp", StringComparison.OrdinalIgnoreCase);

    private static string RepoRoot
    {
        get
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "LanMountainDesktop.slnx")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new InvalidOperationException("Unable to locate repository root.");
        }
    }
}
