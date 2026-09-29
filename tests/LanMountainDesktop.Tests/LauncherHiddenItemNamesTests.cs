using System;
using System.IO;
using System.Linq;

using LanMountainDesktop.Models;
using LanMountainDesktop.Services;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 启动台隐藏项的键与兜底显示名，都只认这一家。
///
/// 兜底名此前是设置页与桌面叠加层各抄一份逐字相同的 9 行：只改一份的结果是同一个隐藏项在两处
/// 显示成两个名字（都不报错，只是看起来像两个条目）。
/// 键的清洗此前是**三份**：写盘侧一份（去空白/去重/排序）、设置页读侧又排一遍再去重、
/// 外加 <c>NormalizeLauncherHiddenKey</c> 两份同语义不同写法——一份表达式体一份块体，
/// 所以"逐字相同"那把尺子按语句数 ≥2 的口径**根本看不见这一族**（收益不在族数上，在"改一处就两边都改"）。
/// 这一族的键是**磁盘上的值**（<c>launcher-settings.json</c> 里的列表）：写的时候怎么去重、按什么排，
/// 决定读侧两行会不会长成同一条；两份口径漂开的症状不是崩，而是"隐藏过的项目又在启动台出现一次"。
/// </summary>
[Collection("AppDataPath")]
public sealed class LauncherHiddenItemNamesTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(
        Path.GetTempPath(),
        "LanMountainDesktop.Tests",
        nameof(LauncherHiddenItemNamesTests),
        Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(@"C:\Start Menu\Games.lnk", "Games")]
    [InlineData("/usr/share/applications/foo.desktop", "foo")]
    [InlineData("Calendar", "Calendar")]
    [InlineData(null, "Unknown")]
    [InlineData("", "Unknown")]
    [InlineData("   ", "Unknown")]
    [InlineData("/", "/")]                 // 末段取不出名字：退回原键，别显示成空标签
    [InlineData(@"x\y\  ", @"x\y\  ")]     // 同上：尾部空白不算名字
    public void FallbackDisplayName_FollowsTheSingleRule(string? key, string expected) =>
        Assert.Equal(expected, LauncherHiddenItemNames.FallbackDisplayName(key));

    [Theory]
    [InlineData("  C:\\Apps\\X  ", @"C:\Apps\X")]   // 只切首尾空白：键中间的空格是名字的一部分
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void NormalizeKey_TrimsAndGivesBlankTheEmptyKey(string? key, string expected) =>
        Assert.Equal(expected, LauncherHiddenItemNames.NormalizeKey(key));

    [Fact]
    public void NormalizeKeys_DeduplicatesWithoutCasingAndOrders()
    {
        var normalized = LauncherHiddenItemNames.NormalizeKeys(
            ["C:\\Apps\\b", "  C:\\Apps\\A  ", "C:\\apps\\a", "", "   ", "C:\\Apps\\b"]);

        // 大小写不敏感的去重是必须的：Windows 上 C:\Apps\A 与 C:\apps\a 是同一个目录，
        // 两条都留下就是"同一个项目显示两行"。
        Assert.Equal(["C:\\Apps\\A", "C:\\Apps\\b"], normalized);
    }

    [Fact]
    public void NormalizeKeys_AcceptsAMissingList() =>
        Assert.Empty(LauncherHiddenItemNames.NormalizeKeys(null));

    [Fact]
    public void Save_WritesTheHiddenListAlreadyDeduplicatedToDisk()
    {
        AppDataPathProvider.Initialize(["--data-root", _dataRoot]);
        var service = new LauncherSettingsService();

        service.Save(new LauncherSettingsSnapshot
        {
            HiddenLauncherAppPaths = ["C:\\Apps\\b", "  C:\\apps\\a  ", "C:\\Apps\\A", "   ", "C:\\Apps\\b"],
        });

        // 直接读磁盘上那份 JSON：Load() 可能回内存缓存里的对象，证明不了"落盘的字节就是去重后的"。
        var json = File.ReadAllText(
            Path.Combine(AppDataPathProvider.GetSettingsDirectory(), "launcher-settings.json"));
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var written = document.RootElement.GetProperty("HiddenLauncherAppPaths")
            .EnumerateArray().Select(value => value.GetString()).ToArray();

        // 去重不认大小写、空的与纯空白的都不落地；同一组里留下的是**先出现那种写法**（这里 "C:\apps\a"），
        // 这条也一并钉住——它决定了旧文件与新写入会不会被看成两个键。
        Assert.Equal(["C:\\apps\\a", "C:\\Apps\\b"], written);
    }

    public void Dispose()
    {
        AppDataPathProvider.ResetForTests();
        try
        {
            if (Directory.Exists(_dataRoot))
            {
                Directory.Delete(_dataRoot, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
