using System;

using LanMountainDesktop.Services;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 设置快照缓存的判据钉（家在 <see cref="SettingsSnapshotCache{T}"/>，宿主与启动器的设置服务共用）。
///
/// 这里最要紧的是最后两条：读出来与存进去的都必须是<b>克隆</b>。缓存本体漏出去的话，
/// 调用方改一份快照就等于改了缓存，下一次 <c>Load()</c> 会还回一个"盘上根本没有"的状态——
/// 现场症状是"改设置没保存却像是保存了"，且重启后又变回去。
/// </summary>
public sealed class SettingsSnapshotCacheTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(400);
    private const string PathA = "/tmp/settings.json";
    private const string PathB = "/tmp/launcher-settings.json";

    [Fact]
    public void WithinProbeWindow_ReusesWithoutTouchingDisk()
    {
        var cache = NewCache();
        var t0 = new DateTime(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);
        cache.Update(PathA, new Box(7), writeTimeUtc: t0, probeTimeUtc: t0);

        Assert.True(cache.TryGetWithinProbeWindow(PathA, t0.AddMilliseconds(399), out var cached));
        Assert.Equal(7, cached.Value);

        Assert.False(cache.TryGetWithinProbeWindow(PathA, t0.Add(Interval), out _));
    }

    [Fact]
    public void AfterProbe_OnlyReusesWhileTheDiskWriteTimeIsUnchanged()
    {
        var cache = NewCache();
        var t0 = new DateTime(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);
        cache.Update(PathA, new Box(3), writeTimeUtc: t0, probeTimeUtc: t0);

        Assert.True(cache.TryGetAtCachedWriteTime(PathA, t0, out var same));
        Assert.Equal(3, same.Value);

        // 别的进程/另一次保存把文件写了：写时间一变，缓存就不许再顶。
        Assert.False(cache.TryGetAtCachedWriteTime(PathA, t0.AddSeconds(1), out _));
    }

    [Fact]
    public void ADifferentSettingsFile_IsNotThisFilesCache()
    {
        var cache = NewCache();
        var t0 = new DateTime(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);
        cache.Update(PathA, new Box(1), writeTimeUtc: t0, probeTimeUtc: t0);

        Assert.False(cache.TryGetWithinProbeWindow(PathB, t0, out _));
        Assert.False(cache.TryGetAtCachedWriteTime(PathB, t0, out _));
    }

    [Fact]
    public void MarkProbed_RecordsTheProbeWithoutDisturbingTheWriteTime()
    {
        var cache = NewCache();
        var t0 = new DateTime(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);
        cache.Update(PathA, new Box(5), writeTimeUtc: t0, probeTimeUtc: t0.AddSeconds(-30));

        cache.MarkProbed(t0);

        Assert.True(cache.TryGetWithinProbeWindow(PathA, t0.AddMilliseconds(100), out _));
        Assert.True(cache.TryGetAtCachedWriteTime(PathA, t0, out _));
    }

    [Fact]
    public void ReadingOutAClone_LeavesTheCacheIntact()
    {
        var cache = NewCache();
        var t0 = new DateTime(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);
        cache.Update(PathA, new Box(2), writeTimeUtc: t0, probeTimeUtc: t0);

        Assert.True(cache.TryGetWithinProbeWindow(PathA, t0, out var handedOut));
        handedOut.Value = 99;

        Assert.True(cache.TryGetWithinProbeWindow(PathA, t0, out var again));
        Assert.Equal(2, again.Value);
    }

    [Fact]
    public void StoringAClone_SevicesTheCallersReference()
    {
        var cache = NewCache();
        var t0 = new DateTime(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);
        var stored = new Box(4);
        cache.Update(PathA, stored, writeTimeUtc: t0, probeTimeUtc: t0);

        stored.Value = 77;

        Assert.True(cache.TryGetAtCachedWriteTime(PathA, t0, out var read));
        Assert.Equal(4, read.Value);
    }

    private static SettingsSnapshotCache<Box> NewCache() => new(static box => box.Clone(), Interval);

    private sealed class Box
    {
        public Box(int value)
        {
            Value = value;
        }

        public int Value { get; set; }

        public Box Clone() => new(Value);
    }
}
