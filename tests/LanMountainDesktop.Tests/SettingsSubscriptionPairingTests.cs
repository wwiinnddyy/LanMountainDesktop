using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 设置变更事件的"订了就退"配对守卫。钉的是**数量差**而不是清单：
/// 判据是**按文件的配对**：一个文件里 <c>Settings.Changed +=</c> 的处数不许超过同文件里的退订处数
/// ——退订写在本地、或改走 <c>SettingsChangedSubscription.UnsubscribeOnce</c>（收口后的家）都算数。
/// 今天实测唯一越界的是 <c>SettingsWindowService</c>，理由见下面"核实过"那段；第二处混进来就红。
///
/// 为什么值得数：设置页 VM 是每开一次窗口现造的（<c>ActivatorUtilities.CreateInstance</c>，容器不追踪也不释放），
/// 订了不退就是每开一次永久挂一份 —— 症状不是当场报错，而是"改了设置后旧页面还在响应"，
/// 加上 VM 与它的本地化服务被事件钉住回收不掉。<c>SettingsWindow.DropCachedPages</c> 已经补上丢弃时释放，
/// 这个守卫守的是"以后又加一处只订不退"。
///
/// 退订可以写在本地、也可以走 SettingsChangedSubscription.UnsubscribeOnce（收口后是家），
/// 两种都算退过一次 —— 不然一次正常的收口会把守卫判成泄漏。
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
    private const RegexOptions Scan = RegexOptions.Compiled | RegexOptions.IgnoreCase;

    private static readonly Regex Subscribe = new(@"Settings\.Changed\s*\+=", Scan);

    private static readonly Regex Unsubscribe = new(@"Settings\.Changed\s*\-=", Scan);

    /// <summary>退订搬进家之后，"家调用"与"字面退订"等价，所以按同一份账算。</summary>
    private static readonly Regex UnsubscribeOnce = new(@"SettingsChangedSubscription\.UnsubscribeOnce\(", Scan);

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
            var removes = Unsubscribe.Matches(text).Count + UnsubscribeOnce.Matches(text).Count;
            subscribes += adds;
            unsubscribes += removes;
            if (adds > removes)
            {
                offenders.Add($"{relative}（订 {adds} 退 {removes}）");
            }
        }

        Assert.True(subscribes > 0, "一条订阅都没扫到：判据失效，这次的 0 不可信");
        // 总量差不再是判据：退订收进 SettingsChangedSubscription 之后，家那一条 settings.Changed -= 服务多个
        // 订阅点，"订的处数 - 退的处数"不再是 1:1（这是改判据，不是放松：改成按文件配对 + 认家的额度，
        // 少一对就红——把 Dev VM 那行退订删掉试一次即知）。
        Assert.True(unsubscribes > 0, "一条退订都没扫到：判据失效");
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
