using LanMountainDesktop.Models;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 学习分析参数合法区间的行为钉。此前这份"钳一遍"在流水线与服务层各抄一份逐字 23 行，
/// 改一边忘一边的症状不报错：同一份设置里同一个数，采集按一套区间跑、界面按另一套区间显示与回写。
/// 十个字段全部按"越界→贴边、界内→原样"钉住，动任何一条区间都会红对应那一格。
/// </summary>
public sealed class StudyAnalyticsConfigRangeTests
{
    private static StudyAnalyticsConfig AllTooLow() => new()
    {
        FrameMs = 1,
        UiPublishIntervalMs = 1,
        SliceSec = 1,
        ScoreThresholdDbfs = -140,
        SegmentMergeGapMs = 1,
        MaxSegmentsPerMin = 0,
        SilenceFloorDbfs = -120,
        BaselineDb = 5,
        AvgWindowSec = 0,
        RealtimeBufferCapacity = 1,
    };

    private static StudyAnalyticsConfig AllTooHigh() => new()
    {
        FrameMs = 10_000,
        UiPublishIntervalMs = 10_000,
        SliceSec = 10_000,
        ScoreThresholdDbfs = 0,
        SegmentMergeGapMs = 100_000,
        MaxSegmentsPerMin = 4000,
        SilenceFloorDbfs = 0,
        BaselineDb = 500,
        AvgWindowSec = 900,
        RealtimeBufferCapacity = 100_000,
    };

    [Fact]
    public void ClampedToLegalRanges_PullsEveryFieldUpToItsFloor()
    {
        var clamped = AllTooLow().ClampedToLegalRanges();

        Assert.Equal(20, clamped.FrameMs);
        Assert.Equal(50, clamped.UiPublishIntervalMs);
        Assert.Equal(5, clamped.SliceSec);
        Assert.Equal(-100d, clamped.ScoreThresholdDbfs, 6);
        Assert.Equal(100, clamped.SegmentMergeGapMs);
        Assert.Equal(1, clamped.MaxSegmentsPerMin);
        Assert.Equal(-100d, clamped.SilenceFloorDbfs, 6);
        Assert.Equal(20d, clamped.BaselineDb, 6);
        Assert.Equal(1, clamped.AvgWindowSec);
        Assert.Equal(60, clamped.RealtimeBufferCapacity);
    }

    [Fact]
    public void ClampedToLegalRanges_PushesEveryFieldDownToItsCeiling()
    {
        var clamped = AllTooHigh().ClampedToLegalRanges();

        Assert.Equal(250, clamped.FrameMs);
        Assert.Equal(500, clamped.UiPublishIntervalMs);
        Assert.Equal(600, clamped.SliceSec);
        Assert.Equal(-5d, clamped.ScoreThresholdDbfs, 6);
        Assert.Equal(4000, clamped.SegmentMergeGapMs);
        Assert.Equal(40, clamped.MaxSegmentsPerMin);
        Assert.Equal(-20d, clamped.SilenceFloorDbfs, 6);
        Assert.Equal(90d, clamped.BaselineDb, 6);
        Assert.Equal(8, clamped.AvgWindowSec);
        Assert.Equal(1200, clamped.RealtimeBufferCapacity);
    }

    [Fact]
    public void ClampedToLegalRanges_LeavesALegalConfigByteForByteAlone()
    {
        // 界内的值不许被动过（"顺手改默认值"就是这里红）。
        var config = new StudyAnalyticsConfig();
        Assert.Equal(config, config.ClampedToLegalRanges());

        var nudged = AllTooLow() with { FrameMs = 33, SliceSec = 45, BaselineDb = 61 };
        var clampedNudged = nudged.ClampedToLegalRanges();
        Assert.Equal(33, clampedNudged.FrameMs);
        Assert.Equal(45, clampedNudged.SliceSec);
        Assert.Equal(61d, clampedNudged.BaselineDb, 6);
    }

    [Fact]
    public void ClampedToLegalRanges_KeepsTheFieldsItDoesNotOwn()
    {
        var source = new StudyAnalyticsConfig { ShowRelativeDb = false, AlertSoundEnabled = true };

        var clamped = source.ClampedToLegalRanges();

        Assert.Equal(source.ShowRelativeDb, clamped.ShowRelativeDb);
        Assert.Equal(source.AlertSoundEnabled, clamped.AlertSoundEnabled);
    }
}
