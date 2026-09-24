using System.Text.RegularExpressions;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 组件生命周期配对棘轮：Attach/构造里起来的东西，Detach/Dispose 时有没有收回去。
///
/// 为什么值得钉成闸门：组件控件是短命的、服务与计时器是长命的。控件忘退订一次，长命对象就替
/// 一棵已经分离的 visual tree 一直持有引用——本仓真发生过两次（组件库预览换选中项、组件浮窗关停），
/// 两次都是"起的地方有、收的地方没有"，而且都不报错。
/// 重复方法体的那两把尺子（<c>dump-dup-methods.py</c> / <c>dump-drift-methods.py</c>）看不见这类问题：
/// 它们比的是方法体，这里比的是动词配对。
///
/// 判据与 <c>scripts/check-component-pairs.py</c> 一致，但两处刻意收窄了脚本的粗判：
/// ① 只查同时出现 <c>AttachedToVisualTree</c> 或 <c>DetachedFromVisualTree</c> 的文件（组件才有 detach 时机）；
/// ② 监视租约<b>不在</b>这张表里：它走 <c>StudyMonitoringLease.Sync(ref …, 状态位)</c>，
///    取/放由家按状态对账，按动词数只会得出假结论；
/// ③ "起"与"收"两侧都要认家：snapshot 族认 <c>StudyComponentLifecycle.Attach</c> /
///    <c>StudyComponentLifecycle.Detach</c>、timer 族认 <c>ComponentRefreshLifetime.Detach</c>
///    （表是它内部停的）。不认的话这条判据会反过来逼代码保留逐字复制——本末倒置。
/// 每族的"起/收"条数还各钉一个下限：把 <c>Stop()</c> 删掉、或者把某一族的正则改窄，
/// 都会让"未配对 0 处"照样成立，那种静默收窄比红灯贵。
/// 两道哨兵各管一半：2026-09-23 实测摘掉一个组件的 <c>Detach(...)</c> 调用，
/// "未配对"那条<b>没报</b>（该文件在别处还有一句 <c>_refreshTimer.Stop()</c>，按 key 配对确实成立），
/// 是覆盖面下限先红的（收 58/59）——所以"整批收尾交给家"之后，族计数才是那条真正兜得住的哨兵。
/// </summary>
public sealed class ComponentLifecyclePairingRatchetTests
{
    private static readonly string[] ComponentDirectories =
        [Path.Combine("desktop", "LanMountainDesktop", "Views", "Components")];

    private sealed record Family(
        string Name,
        Regex Start,
        Regex Stop,
        bool PairByKey,
        int StartFloor,
        int StopFloor);

    private static readonly Family[] Families =
    [
        new("timer",
            // 起与收都认"把表整个交给生命周期家"这种写法（新增一个家只往这个名字列表里加一条，别复制正则）。
            // 两侧认的方法**不一样**，这是刻意的：Reschedule / Apply 按设置既可能起也可能停，
            // Detach 只停不起——把它算进"起"的一侧会把 11 处 detach 虚报成 11 个起点（实测 35 → 46）。
            new Regex(@"\b(?<key>[\w]*[Tt]imer\w*)\s*\??\.\s*Start\s*\(" +
                      @"|\b(?:ComponentRefreshLifetime|DailyWordAutoRefresh)\s*\.\s*(?:Reschedule|Apply)\s*\([^;]*?(?<key>[\w]*[Tt]imer\w*)",
                  RegexOptions.Compiled),
            new Regex(@"\b(?<key>[\w]*[Tt]imer\w*)\s*\??\.\s*Stop\s*\(" +
                      @"|\b(?:ComponentRefreshLifetime|DailyWordAutoRefresh)\s*\.\s*(?:Detach|Reschedule|Apply)\s*\([^;]*?(?<key>[\w]*[Tt]imer\w*)",
                  RegexOptions.Compiled),
            true, 35, 59),
        new("subscription",
            new Regex(@"\b(?<key>[\w.]*[Ss]ervice\w*)\s*\??\.\s*(?<h>\w+)\s*\+=\s*\w+", RegexOptions.Compiled),
            new Regex(@"\b(?<key>[\w.]*[Ss]ervice\w*)\s*\??\.\s*(?<h>\w+)\s*-=\s*\w+", RegexOptions.Compiled),
            true, 5, 5),
        new("property-changed",
            new Regex(@"\b(?<key>[\w.]+)\.(?<h>PropertyChanged|CollectionChanged|CanExecuteChanged)\s*\+=\s*\w+",
                RegexOptions.Compiled),
            new Regex(@"\b(?<key>[\w.]+)\.(?<h>PropertyChanged|CollectionChanged|CanExecuteChanged)\s*-=\s*\w+",
                RegexOptions.Compiled),
            true, 1, 1),
        new("snapshot",
            // 与下面"收的一侧认家"完全对称：2026-09-25 把 7 个学习组件挂载那五步收进
            // StudyComponentLifecycle.Attach（那五步里才是真 Subscribe），起的一侧必须也认这个家，
            // 否则这条判据会反过来把刚收掉的抄本再逼回来。下限 8 不动：现在是 1 处裸 Subscribe
            // （StudySessionHistoryWidget 没有租约那一步，形状不同，没收）+ 7 处走家 = 8。
            new Regex(@"StudySnapshotSubscription\s*\.\s*Subscribe|StudyComponentLifecycle\s*\.\s*Attach",
                RegexOptions.Compiled),
            // 收的一侧也算上"整批交给家"的写法：学习组件的 detach 四步已收进
            // StudyComponentLifecycle.Detach（里面才是真 Unsubscribe）。
            // 判据必须认这个家，否则它会反过来逼代码保留逐字复制——那是本末倒置。
            new Regex(@"StudySnapshotSubscription\s*\.\s*Unsubscribe|StudyComponentLifecycle\s*\.\s*Detach",
                RegexOptions.Compiled),
            false, 8, 13),
        new("timezone",
            new Regex(@"\bSet\s*TimeZoneService\s*\(", RegexOptions.Compiled),
            new Regex(@"\bClear\s*TimeZoneService\s*\(", RegexOptions.Compiled),
            false, 10, 10),
    ];

    [Fact]
    public void EveryLifecycleStartSite_HasAMatchingStopSite()
    {
        var repoRoot = RepoRoot();
        var unpaired = new List<string>();
        var checkedFiles = 0;
        var startCounts = new int[Families.Length];
        var stopCounts = new int[Families.Length];

        for (var familyIndex = 0; familyIndex < Families.Length; familyIndex++)
        {
            var family = Families[familyIndex];

            foreach (var path in EnumerateCSharpFiles(repoRoot, ComponentDirectories))
            {
                var text = File.ReadAllText(path);
                if (!text.Contains("AttachedToVisualTree", StringComparison.Ordinal) &&
                    !text.Contains("DetachedFromVisualTree", StringComparison.Ordinal))
                {
                    continue;
                }

                checkedFiles++;
                var relative = Relative(repoRoot, path);
                var starts = ScanSites(path, family.Start);
                var stops = ScanSites(path, family.Stop);
                startCounts[familyIndex] += starts.Count;
                stopCounts[familyIndex] += stops.Count;

                if (starts.Count == 0)
                {
                    continue;
                }

                foreach (var (line, key, raw) in starts)
                {
                    var paired = family.PairByKey
                        ? stops.Count(item => string.Equals(item.Key, key, StringComparison.Ordinal)) >= 1
                        : stops.Count >= starts.Count;
                    if (!paired)
                    {
                        unpaired.Add($"{relative}:{line} [{family.Name}] {raw}");
                    }
                }
            }
        }

        var shrunk = new List<string>();
        for (var index = 0; index < Families.Length; index++)
        {
            if (startCounts[index] < Families[index].StartFloor || stopCounts[index] < Families[index].StopFloor)
            {
                shrunk.Add($"{Families[index].Name} 起 {startCounts[index]}/{Families[index].StartFloor}" +
                           $"、收 {stopCounts[index]}/{Families[index].StopFloor}");
            }
        }

        Assert.True(
            checkedFiles >= 46,
            $"只有 {checkedFiles} 个组件文件被这条判据覆盖（2026-09-22 实测基线是 46）：" +
            "要么扫描范围被改窄，要么组件的 attach/detach 写法整体变了，先查清再放行");

        Assert.True(
            shrunk.Count == 0,
            "这几族的起/收条数低于基线，说明判据覆盖面在静默收窄（正则改窄、或整批收尾被删）：" +
            string.Join(" | ", shrunk));

        Assert.True(
            unpaired.Count == 0,
            "这些动作起来了却没有对应的收回（长命对象会替已分离的控件持有整棵 visual tree）：" +
            string.Join(" | ", unpaired.Order(StringComparer.Ordinal)));
    }

    private sealed record Site(int Line, string Key, string Raw);

    private static List<Site> ScanSites(string path, Regex regex)
    {
        var sites = new List<Site>();
        var number = 0;
        foreach (var raw in File.ReadLines(path))
        {
            number++;
            var line = raw.Trim();
            if (line.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (Match match in regex.Matches(line))
            {
                var key = string.Join('|', match.Groups.Values
                    .Skip(1)
                    .Where(group => group.Success)
                    .Select(group => group.Value));
                sites.Add(new Site(number, key, line[..Math.Min(line.Length, 110)]));
            }
        }

        return sites;
    }

    private static IEnumerable<string> EnumerateCSharpFiles(string root, IEnumerable<string> topDirs)
    {
        foreach (var directory in topDirs)
        {
            var full = Path.Combine(root, directory);
            if (!Directory.Exists(full))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(full, "*.cs", SearchOption.AllDirectories))
            {
                yield return file;
            }
        }
    }

    private static string Relative(string root, string path) => Path.GetRelativePath(root, path)
        .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

    private static string RepoRoot()
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
