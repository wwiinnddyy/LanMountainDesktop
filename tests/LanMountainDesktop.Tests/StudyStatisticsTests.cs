using LanMountainDesktop.Services;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 分位数这家的算法。三份抄本（两个学习组件 + 分析服务）的线性插值部分逐字相同，
/// 唯一分歧是"空数组返回什么"——2026-09-26 拍板统一成 <see cref="StudyStatistics.NoMeasurementDbfs"/>，
/// 参数从签名上拿掉了，所以"谁又给自己挑了一档"这件事从此由编译器拦，这里只钉调用点数量与形状。
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
        Assert.Equal(expected, StudyStatistics.Percentile([10, 20, 30, 40], percentile), 6);

    [Fact]
    public void Percentile_OfASingleValue_IgnoresThePosition()
    {
        Assert.Equal(-7d, StudyStatistics.Percentile([-7], 0.5));
        Assert.Equal(-7d, StudyStatistics.Percentile([-7], 1.0));
    }

    /// <summary>刻度方向决定的那一档：dBFS 的 0 是满刻度（最响），不能拿来表示"没测到"。</summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(0.95)]
    [InlineData(1.0)]
    public void Percentile_OfEmpty_IsBelowTheSilenceFloor(double percentile)
    {
        // -100 也在研究配置的钳位下沿（StudyAnalyticsModels.cs:68 把静音档钳在 -100..-20），
        // 所以它落在"任何读数档之外"；0 不是——0 是满刻度。
        Assert.Equal(-100d, StudyStatistics.Percentile([], percentile));
        Assert.Equal(StudyStatistics.NoMeasurementDbfs, StudyStatistics.Percentile([], percentile));
    }

    /// <summary>
    /// 站点名单：统一之后剩下的价值是"别悄悄地少一条调用"。
    /// 每处调用必须只带两个实参——哨兵再回到调用点，这条就红。
    /// </summary>
    [Theory]
    [InlineData(@"desktop\LanMountainDesktop\Views\Components\StudyDeductionReasonsWidget.axaml.cs", 1)]
    [InlineData(@"desktop\LanMountainDesktop\Views\Components\StudyScoreOverviewWidget.axaml.cs", 1)]
    [InlineData(@"desktop\LanMountainDesktop\Services\StudyAnalyticsInternals.cs", 3)]
    public void PercentileCallSites_TakeNoSentinel(string relative, int expectedSites)
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
            Assert.False(call.Contains("emptySentinel", StringComparison.Ordinal), $"{call} 又把哨兵拿回了调用点");
            var commas = call.Split(',').Length - 1;
            Assert.True(commas <= 1, $"{call} 的参数个数变了（分位数只要有序数组与位置两个）");
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
