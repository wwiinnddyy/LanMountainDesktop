using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 翻译量棘轮：以 zh-CN 为源语言，量出其余三种语言还缺多少条文案，只许降不许升。
/// 2026-09-21 死键清理后实测：en-US 缺 25 条、ja-JP 缺 313 条、ko-KR 缺 275 条（zh-CN 共 1123 条），
/// 也就是说日/韩界面里仍有相当一部分文案在走代码里的兜底字符串。这是已知欠账，不是回归，
/// 所以这里用棘轮而不是严格相等——新增键却忘了补翻译会立刻被拦下，
/// 补了翻译就把数字改小。
/// </summary>
public sealed class LocalizationParityRatchetTests
{
    private const string SourceLanguage = "zh-CN";

    private static readonly Dictionary<string, int> KnownGaps = new()
    {
        ["en-US"] = 25,
        ["ja-JP"] = 313,
        ["ko-KR"] = 275,
    };

    /// <summary>
    /// 同一个 json 里同名键写两遍时，<see cref="Services.LocalizationService"/> 走
    /// <c>JsonSerializer.Deserialize&lt;Dictionary&lt;string, string&gt;&gt;</c>：重复键不抛异常，
    /// 用索引器覆盖，所以前一条永远读不到——它是死文案，而且改它的人以为改了。
    /// 2026-09-21 实测四份词表里共 182 条这种被覆盖的键（en-US 独占 103 条，
    /// 整页 <c>settings.update.*</c> 被粘了两遍），其中 zh/ja/ko 的
    /// <c>settings.update.preferences_description</c> 两份内容不同，界面上看到的是后一份。
    /// 顺带管住"一键一行"：一行塞两个键会让按行取键的工具（清理脚本、grep）静默漏掉第二个。
    /// </summary>
    [Theory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    [InlineData("ja-JP")]
    [InlineData("ko-KR")]
    public void LocaleFile_DeclaresEachKeyOnceOnItsOwnLine(string language)
    {
        var path = Path.Combine(
            RepoRoot, "desktop", "LanMountainDesktop", "Localization", $"{language}.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));

        var properties = document.RootElement.EnumerateObject()
            .Select(property => property.Name)
            .ToArray();
        var duplicated = properties
            .GroupBy(name => name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key} x{group.Count()}")
            .OrderBy(entry => entry, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            duplicated.Length == 0,
            $"{language}.json 里 {duplicated.Length} 个键被声明了不止一次，前一份运行时永远读不到："
            + $"{string.Join(", ", duplicated.Take(10))}");

        var keyLines = File.ReadAllLines(path)
            .Count(line => Regex.IsMatch(line, @"^\s*""[^""]+""\s*:"));

        Assert.True(
            keyLines == properties.Length,
            $"{language}.json 有 {properties.Length} 个键却只有 {keyLines} 行以键开头——"
            + "一行塞了两个键，按行取键的工具会漏掉后一个。");
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("ja-JP")]
    [InlineData("ko-KR")]
    public void MissingTranslations_HaveNotGrown(string language)
    {
        var directory = Path.Combine(RepoRoot, "desktop", "LanMountainDesktop", "Localization");
        var source = ReadKeys(Path.Combine(directory, $"{SourceLanguage}.json"));
        var target = ReadKeys(Path.Combine(directory, $"{language}.json"));
        var missing = source.Except(target).ToList();
        var allowance = KnownGaps[language];

        Assert.True(
            missing.Count <= allowance,
            $"{language} 缺 {missing.Count} 条文案，基线是 {allowance} 条。补上翻译；若是有意为之，"
            + $"请把基线改成 {missing.Count} 并在提交说明里写清理由。缺失示例："
            + string.Join(", ", missing.OrderBy(key => key, StringComparer.Ordinal).Take(8)));
    }

    private static HashSet<string> ReadKeys(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            keys.Add(property.Name);
        }

        return keys;
    }

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
