using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 设置变更事件的"订了就退"配对守卫。钉的是**数量差**而不是清单：
/// 全仓 <c>Settings.Changed +=</c> 的处数减去 <c>-=</c> 的处数不许超过今天实测的 1（那一处是
/// <c>SettingsWindowService</c>，理由见下面"核实过"那段；先不让第二处混进来）。
///
/// 为什么值得数：设置页 VM 是每开一次窗口现造的（<c>ActivatorUtilities.CreateInstance</c>，容器不追踪也不释放），
/// 订了不退就是每开一次永久挂一份 —— 症状不是当场报错，而是"改了设置后旧页面还在响应"，
/// 加上 VM 与它的本地化服务被事件钉住回收不掉。<c>SettingsWindow.DropCachedPages</c> 已经补上丢弃时释放，
/// 这个守卫守的是"以后又加一处只订不退"。
///
/// 只数不判类型是有意的：静态判"这个类是否会被丢弃"要跨文件追构造与容器注册，代价高且容易假红；
/// 数量差是廉价但真的能红的（新加一处只订不退 → 立刻红），而且不锁死名单，
/// 门：<c>SettingsWindowService</c> 那一处经核实**不是泄漏**，所以豁免是有据的而不是"先放过去"：
/// 全仓只有 <c>App.axaml.cs:800</c> 一处 <c>_settingsWindowService ??= new SettingsWindowService(...)</c>
/// （<c>new App(</c> 在应用代码里也没有第二处），它订的三条（<c>Settings.Changed</c>、
/// <c>_appearanceThemeService.Changed</c>、静态的 <c>AppSettingsService.SettingsSaved</c>）宿主都与进程同寿，
/// 订一次就一直在——没有"每开一次窗口留一份"的累积。<c>SettingsWindowViewModel</c>（每次开窗重建的那个）
/// 经核实在任何地方都不订事件，所以也不在这条账上。
/// 要收紧到 0：只有当有人把 <c>SettingsWindowService</c> 变成可重复创建（例如改成工厂/多次 new）时，
/// 才需要它实现 <c>IDisposable</c> 并在释放处退订 —— 那时这个守卫会先红，不会静默。
/// </summary>
public sealed class SettingsSubscriptionPairingTests
{
    private static readonly Regex Subscribe = new(@"Settings\.Changed\s*\+=", RegexOptions.Compiled);

    private static readonly Regex Unsubscribe = new(@"Settings\.Changed\s*\-=", RegexOptions.Compiled);

    [Fact]
    public void SettingsChangedSubscriptions_ArePairedWithinOneKnownSite()
    {
        var root = RepositoryRoot();
        var offenders = new List<string>();
        var subscribes = 0;
        var unsubscribes = 0;

        foreach (var path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            if (relative.Contains("/obj/", StringComparison.Ordinal)
                || relative.Contains("/bin/", StringComparison.Ordinal)
                || relative.StartsWith("tests/", StringComparison.Ordinal))
            {
                continue;
            }

            var text = File.ReadAllText(path);
            var adds = Subscribe.Matches(text).Count;
            var removes = Unsubscribe.Matches(text).Count;
            subscribes += adds;
            unsubscribes += removes;
            if (adds > removes)
            {
                offenders.Add($"{relative}（订 {adds} 退 {removes}）");
            }
        }

        Assert.True(subscribes > 0, "一条订阅都没扫到：判据失效，这次的 0 不可信");
        Assert.Equal(1, subscribes - unsubscribes);
        var offender = Assert.Single(offenders);
        Assert.Contains("SettingsWindowService.cs", offender);
        Assert.Contains("退 0", offender);
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "LanMountainDesktop.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("找不到仓库根（LanMountainDesktop.slnx）");
    }
}
