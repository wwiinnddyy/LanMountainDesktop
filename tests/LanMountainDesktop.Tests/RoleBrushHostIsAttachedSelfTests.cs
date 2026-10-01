using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 角色画笔只许向<b>已经挂树的宿主</b>要键（2026-10-01 现量：桌面侧 79 处全是 <c>(this)</c>，0 处例外）。
///
/// 为什么这条值得钉：<c>TimerConstructTimeInkTests</c> 与 <c>ThemeKeyScopeLayerProbeTests</c> 量到
/// 一个未挂树、自家也没资源的控件<b>一支 Adaptive 键都查不到</b>（0/4），于是
/// <c>ComponentRoleBrushes.PrimaryText(那个控件)</c> 只会拿到 <c>ComponentRoleBrushes</c> 家里那支兜底中性灰。
/// 而灰是<b>当场定色</b>的：控件随后挂上去也不会自己变回去——标记面那行
/// <c>Foreground="{DynamicResource …}"</c> 能自己重解析，代码面赋值不能（#115 三条事实）。
/// 所以"向刚造出来的控件要键"是昼档里会一直停在灰的真缺陷形状，动态条目、扇出到记录字段的那些站点最容易踩。
///
/// 覆盖面下限与等值一起钉：只数非 <c>this</c> 的站点会把"整条正则没匹配上"也数成 0，
/// 所以另加一条现量下限（79），少一族就是尺子坏了而不是代码干净了。
/// </summary>
public sealed class RoleBrushHostIsAttachedSelfTests
{
    // 2026-10-01 现量：PrimaryText 22 + SecondaryText 25 + MutedText 12 + RaisedSurface 10 + OverlaySurface 10。
    private const int RoleBrushSiteFloor = 75;

    private static readonly Regex CallShape = new(
        @"ComponentRoleBrushes\.(?<role>[A-Za-z]+)\(\s*(?<host>[^)]*)\)",
        RegexOptions.Compiled);

    [Fact]
    public void RoleBrushes_AreRequestedFromTheAttachedWidgetItself()
    {
        var viewsDir = Path.Combine(ResolveRepositoryRoot(), "desktop", "LanMountainDesktop", "Views");
        var offenders = new List<string>();
        var sites = 0;

        foreach (var path in Directory.GetFiles(viewsDir, "*.cs", SearchOption.AllDirectories)
                     .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                              && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (Match match in CallShape.Matches(lines[i]))
                {
                    var host = match.Groups["host"].Value.Trim();
                    if (host.StartsWith("//", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    sites++;
                    if (host != "this")
                    {
                        offenders.Add($"{Path.GetFileName(path)}:{i + 1} 角色 {match.Groups["role"].Value} 的宿主是 `{host}`");
                    }
                }
            }
        }

        Assert.True(
            sites >= RoleBrushSiteFloor,
            $"这一跑只认到 {sites} 处角色画笔取色站点（下限 {RoleBrushSiteFloor}）——" +
            "方法改名、正则退化或目录挪走都会让它安静地少认一族，那种 0 不是干净");
        Assert.True(
            offenders.Count == 0,
            "向未挂树的控件要角色画笔＝当场拿到兜底灰且不会自己变回来（见 ThemeKeyScopeLayerProbeTests 的 0/4 与" +
            $"TimerConstructTimeInkTests 的三条事实）。现量 {offenders.Count} 处：" + string.Join(Environment.NewLine, offenders));
    }

    private static string ResolveRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LanMountainDesktop.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"从 {AppContext.BaseDirectory} 往上没找到 LanMountainDesktop.slnx");
    }
}
