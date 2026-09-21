using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 翻译量棘轮：以 zh-CN 为源语言，量出其余三种语言还缺多少条文案，只许降不许升。
/// 2026-09-20 实测：en-US 缺 35 条、ja-JP 缺 333 条、ko-KR 缺 289 条（zh-CN 共 1468 条），
/// 也就是说日/韩界面里约两成文案在走代码里的兜底字符串。这是已知欠账，不是回归，
/// 所以这里用棘轮而不是严格相等——新增键却忘了补翻译会立刻被拦下，
/// 补了翻译就把数字改小。
/// </summary>
public sealed class LocalizationParityRatchetTests
{
    private const string SourceLanguage = "zh-CN";

    private static readonly Dictionary<string, int> KnownGaps = new()
    {
        ["en-US"] = 35,
        ["ja-JP"] = 333,
        ["ko-KR"] = 289,
    };

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
