using System;

namespace LanMountainDesktop.Services;

/// <summary>
/// 设置快照缓存只认这一处：四条判据与四个状态字段都在这里，宿主设置服务与启动器设置服务共用。
/// </summary>
/// <remarks>
/// 收口前两边各抄了一份逐字相同的三个方法（<c>TryGetCachedWithoutProbe</c> /
/// <c>TryGetCachedAfterProbe</c> / <c>UpdateCache</c>）加同一组静态字段：同一个 settings.json
/// 的"多久不再问磁盘"、"磁盘写时间没变就复用"、"读出去的是克隆还是缓存本体"这三件事有两处说法。
/// 漂开的症状不报错，是"改了设置某一边读到旧值"或"每次读都打一次盘"。
///
/// 锁故意留在外面：两个服务各自 <c>lock (CacheGate)</c> 罩住的是"读盘 → 落盘 → 写缓存"整段，
/// 比这里的单次读写粗得多，收进来会改变临界区大小（那是行为变更，不是收口）。
/// 键也一样留在调用方：缓存按"哪份 settings.json"分，路径变了就当没有缓存。
/// </remarks>
internal sealed class SettingsSnapshotCache<T>
    where T : class
{
    private readonly Func<T, T> _clone;
    private readonly TimeSpan _probeInterval;

    private string? _cachedPath;
    private T? _cachedSnapshot;
    private DateTime _cachedWriteTimeUtc = DateTime.MinValue;
    private DateTime _lastProbeUtc = DateTime.MinValue;

    public SettingsSnapshotCache(Func<T, T> clone, TimeSpan probeInterval)
    {
        _clone = clone;
        _probeInterval = probeInterval;
    }

    /// <summary>刚问过磁盘、还在探针窗口里：直接复用，不去 <c>GetLastWriteTimeUtc</c>。</summary>
    public bool TryGetWithinProbeWindow(string path, DateTime nowUtc, out T snapshot)
    {
        if (string.Equals(_cachedPath, path, StringComparison.Ordinal) &&
            _cachedSnapshot is not null &&
            nowUtc - _lastProbeUtc < _probeInterval)
        {
            snapshot = _clone(_cachedSnapshot);
            return true;
        }

        snapshot = null!;
        return false;
    }

    /// <summary>问过磁盘、且磁盘写时间与缓存里那份一致：复用（没写过的话两边都是 <c>MinValue</c>，也算一致）。</summary>
    public bool TryGetAtCachedWriteTime(string path, DateTime writeTimeUtc, out T snapshot)
    {
        if (string.Equals(_cachedPath, path, StringComparison.Ordinal) &&
            _cachedSnapshot is not null &&
            writeTimeUtc == _cachedWriteTimeUtc)
        {
            snapshot = _clone(_cachedSnapshot);
            return true;
        }

        snapshot = null!;
        return false;
    }

    /// <summary>只记"这一刻问过磁盘"，不动快照与写时间。</summary>
    public void MarkProbed(DateTime nowUtc) => _lastProbeUtc = nowUtc;

    /// <summary>把这次读到的快照连同它的磁盘写时间与探针时刻一起记下来（存的是克隆）。</summary>
    public void Update(string path, T snapshot, DateTime writeTimeUtc, DateTime probeTimeUtc)
    {
        _cachedPath = path;
        _cachedSnapshot = _clone(snapshot);
        _cachedWriteTimeUtc = writeTimeUtc;
        _lastProbeUtc = probeTimeUtc;
    }
}
