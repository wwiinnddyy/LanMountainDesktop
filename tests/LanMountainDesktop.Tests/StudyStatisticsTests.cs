using System.Text.RegularExpressions;

using LanMountainDesktop.Services;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 分位数这家的算法。三份抄本（两个学习组件 + 分析服务）的线性插值部分逐字相同，
/// 唯一分歧是"空数组返回什么"，所以那一格按调用方各钉一次，而不是统一改死。
/// </summary>
public sealed class StudyStatisticsTests
{
    [Theory]
    [InlineData(0.0, 10d)]
    [InlineData(0.5, 25d)]          // 落在 20 与 30 中间：线性插值，不是取其中一个
    [InlineData(0.95, 38.5d)]       // position = 3 * 0.95 = 2.85 → 30 + (40-30)*0.85
    [InlineData(1.0, 40d)]
    [InlineData(-3.0, 10d)]         // 越界钳到端点，不抛也不 NaN
    [InlineData(5.0, 40d)]
    public void Percentile_InterpolatesOnSortedValues(double percentile, double expected) =>
        Assert.Equal(expected, StudyStatistics.Percentile([10, 20, 30, 40], percentile, emptySentinel: 0), 6);

    [Fact]
    public void Percentile_OfASingleValue_IgnoresThePosition()
    {
        Assert.Equal(-7d, StudyStatistics.Percentile([-7], 0.5, emptySentinel: 0));
        Assert.Equal(-7d, StudyStatistics.Percentile([-7], 1.0, emptySentinel: 0));
    }

    [Fact]
    public void Percentile_OfEmpty_UsesTheCallersOwnSentinel()
    {
        // 既有分歧：面板把"没有测量值"压到刻度底端，分析服务给 0。统一成哪个都要先拍（G1-BK）。
        Assert.Equal(-100d, StudyStatistics.Percentile([], 0.95, emptySentinel: -100));
        Assert.Equal(0d, StudyStatistics.Percentile([], 0.95, emptySentinel: 0));
    }

    /// <summary>
    /// 把"哪个调用方用哪个哨兵"钉在源码上。G1-BK 拍板之前，这条分歧不许被顺手统一掉——
    /// 收口后它只剩一个字面量的距离：把某个调用点的 <c>-100</c> 改成 <c>0</c>，
    /// 上面的算法测试照样全绿，因为只有这里在核对调用方给的数。
    /// 站点数也钉住：少一条断言就是静默变窄（判据的输入集合会随调用点消失而缩小）。
    /// </summary>
    [Theory]
    [InlineData(@"desktop\LanMountainDesktop\Views\Components\StudyDeductionReasonsWidget.axaml.cs", 1, "-100")]
    [InlineData(@"desktop\LanMountainDesktop\Views\Components\StudyScoreOverviewWidget.axaml.cs", 1, "-100")]
    [InlineData(@"desktop\LanMountainDesktop\Services\StudyAnalyticsInternals.cs", 3, "0")]
    public void PercentileCallSites_KeepTheirOwnSentinel(string relative, int expectedSites, string expectedSentinel)
    {
        var path = Path.Combine(RepoRoot(), relative);
        var hits = new List<string>();

        foreach (var line in File.ReadLines(path))
        {
            var at = line.IndexOf("StudyStatistics.Percentile(", StringComparison.Ordinal);
            while (at >= 0)
            {
                var close = line.IndexOf(')', at);
                Assert.True(close > at, $"{relative} 里有一条跨行的 Percentile 调用，这条判据是按行取的，先扩它再动调用点");
                hits.Add(line.Substring(at, close - at));
                at = line.IndexOf("StudyStatistics.Percentile(", close, StringComparison.Ordinal);
            }
        }

        Assert.Equal(expectedSites, hits.Count);
        foreach (var call in hits)
        {
            var actual = Regex.Match(call, @"emptySentinel:\s*(-?\d+)");
            Assert.True(actual.Success, $"{call} 没显式给 emptySentinel（G1-BK 未拍板，不许靠默认值）");
            Assert.Equal(expectedSentinel, actual.Groups[1].Value);
        }
    }

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
