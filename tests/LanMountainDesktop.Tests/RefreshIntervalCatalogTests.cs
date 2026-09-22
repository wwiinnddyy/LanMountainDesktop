using LanMountainDesktop.Models;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 自动刷新档位归一化的行为钉。
///
/// 为什么现在要有：6 个组件各自抄了一份同款算法、绕开这个家，谁都没被测过（2026-09-23 收口时实测
/// 测试工程里对 <see cref="RefreshIntervalCatalog"/> 零引用）。收口之后它是 7 个调用点
/// （6 个组件 + 组件编辑器面板）唯一的口径，平局往哪一档、越界怎么处理这类规则一旦改错，
/// 会同时影响设置面板显示与组件实际刷新间隔，而不报错。
/// </summary>
public sealed class RefreshIntervalCatalogTests
{
    [Theory]
    // 命中档位表：照用
    [InlineData(20, 15, 20)]
    [InlineData(1440, 15, 1440)]
    // 表外的值：取距离最近的一档
    [InlineData(22, 15, 20)]
    [InlineData(55, 15, 60)]
    // 低于最小档 / 高于最大档：收到端点
    [InlineData(1, 15, 5)]
    [InlineData(9999, 15, 1440)]
    // 距离并列时取表里靠前的那一档（10 与 12 距 11 都是 1）——钉住收口前后行为一致
    [InlineData(11, 15, 10)]
    // 非正数（设置里从没填过值时的 0）：退回该组件的默认档
    [InlineData(0, 360, 360)]
    [InlineData(-5, 20, 20)]
    public void Normalize_LandsOnTheNearestSupportedInterval(
        int minutes,
        int fallbackMinutes,
        int expectedMinutes)
    {
        Assert.Equal(
            expectedMinutes,
            RefreshIntervalCatalog.Normalize(minutes, fallbackMinutes));
    }

    [Fact]
    public void Normalize_DefaultsMustThemselvesBeSupportedIntervals()
    {
        // 6 个组件传的默认档（15、20、360）本身就是档位表里的值：
        // 若哪天有人加了个表外的默认档，"非正数退回默认档"会产出一个月历上根本没有的间隔。
        foreach (var fallback in new[] { 15, 20, 360 })
        {
            Assert.Equal(
                fallback,
                RefreshIntervalCatalog.Normalize(0, fallback));
            Assert.Contains(
                fallback,
                RefreshIntervalCatalog.SupportedIntervalsMinutes);
        }
    }
}
