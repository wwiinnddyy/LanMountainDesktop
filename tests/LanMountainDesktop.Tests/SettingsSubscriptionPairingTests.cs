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
/// <c>SettingsWindowService</c> —— 它自己要不要退订还没判，先不让第二处混进来）。
///
/// 为什么值得数：设置页 VM 是每开一次窗口现造的（<c>ActivatorUtilities.CreateInstance</c>，容器不追踪也不释放），
/// 订了不退就是每开一次永久挂一份 —— 症状不是当场报错，而是"改了设置后旧页面还在响应"，
/// 加上 VM 与它的本地化服务被事件钉住回收不掉。<c>SettingsWindow.DropCachedPages</c> 已经补上丢弃时释放，
/// 这个守卫守的是"以后又加一处只订不退"。
///
/// 只数不判类型是有意的：静态判"这个类是否会被丢弃"要跨文件追构造与容器注册，代价高且容易假红；
/// 数量差是廉价但真的能红的（新加一处只订不退 → 立刻红），而且不锁死名单，
/// 等 <c>SettingsWindowService</c> 那一处判完可以把上限收到 0。
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
