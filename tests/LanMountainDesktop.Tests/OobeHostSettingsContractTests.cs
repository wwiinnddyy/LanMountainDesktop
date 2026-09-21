using System;
using System.Linq;
using System.Reflection;

using LanMountainDesktop.Launcher.Oobe;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 首启向导往宿主的 settings.json 里写字段，两个二进制之间没有共享类型，全靠字符串对齐：
/// 向导写 <c>"ShowInTaskbar"</c>，宿主读 <see cref="AppSettingsSnapshot"/>.<c>ShowInTaskbar</c>。
/// 拼错一个字母不会报错，只会让那个开关静默失效——用户在向导里勾了，宿主从来没读到过。
/// <c>HostAppSettingsOobeMerger</c> 的注释一直说这件事由 <c>HostThemeModeContractTests</c> 钉住，
/// 而那个类从来没存在过（2026-09-21 量出来），这里补上它该钉的两件事。
/// </summary>
public sealed class OobeHostSettingsContractTests
{
    [Fact]
    public void EveryKeyOobeWrites_IsAPropertyTheHostReads()
    {
        var hostProperties = typeof(AppSettingsSnapshot)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        var writtenKeys = typeof(HostAppSettingsOobeMerger)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.Name.EndsWith("Key", StringComparison.Ordinal))
            .Select(field => (Const: field.Name, Key: (string?)field.GetRawConstantValue()))
            .ToArray();

        // 取键的反射规则一旦失效（改名、换成属性），这条测试就会静默地什么都不检查。
        Assert.True(
            writtenKeys.Length >= 8,
            $"只量到 {writtenKeys.Length} 个 *Key 常量，取键的反射规则失效了，这条守卫不能算通过。");

        var orphans = writtenKeys
            .Where(entry => entry.Key is null || !hostProperties.Contains(entry.Key))
            .Select(entry => $"{entry.Const} = \"{entry.Key}\"")
            .OrderBy(entry => entry, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            orphans.Length == 0,
            $"{orphans.Length} 个向导写进 settings.json 的键名在宿主 AppSettingsSnapshot 里没有对应属性，"
            + $"宿主干脆不认这个字段：{Environment.NewLine}{string.Join(Environment.NewLine, orphans)}");
    }

    [Fact]
    public void ThemeModeValues_MatchTheHostVocabulary()
    {
        Assert.Equal(ThemeAppearanceValues.ThemeModeLight, HostAppSettingsOobeMerger.ThemeModeLightValue);
        Assert.Equal(ThemeAppearanceValues.ThemeModeDark, HostAppSettingsOobeMerger.ThemeModeDarkValue);
    }
}
