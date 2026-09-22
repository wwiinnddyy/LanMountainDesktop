using System;
using Avalonia.Threading;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.Components;

internal sealed class StudySnapshotRenderGate : IDisposable
{
    private readonly object _syncRoot = new();
    private readonly Func<bool> _canRender;
    private readonly Action<StudyAnalyticsSnapshot> _renderSnapshot;
    private readonly Action? _afterRender;

    private StudyAnalyticsSnapshot? _pendingSnapshot;
    private bool _hasPendingSnapshot;
    private bool _dispatchQueued;
    private bool _isDisposed;

    public StudySnapshotRenderGate(
        Func<bool> canRender,
        Action<StudyAnalyticsSnapshot> renderSnapshot,
        Action? afterRender = null)
    {
        _canRender = canRender ?? throw new ArgumentNullException(nameof(canRender));
        _renderSnapshot = renderSnapshot ?? throw new ArgumentNullException(nameof(renderSnapshot));
        _afterRender = afterRender;
    }

    internal bool HasPendingSnapshot
    {
        get
        {
            lock (_syncRoot)
            {
                return _hasPendingSnapshot;
            }
        }
    }

    public void Queue(StudyAnalyticsSnapshot snapshot)
    {
        lock (_syncRoot)
        {
            if (_isDisposed)
            {
                return;
            }

            _pendingSnapshot = snapshot;
            _hasPendingSnapshot = true;
            if (_dispatchQueued)
            {
                return;
            }

            _dispatchQueued = true;
        }

        Dispatcher.UIThread.Post(() => ProcessPending(), DispatcherPriority.Background);
    }

    /// <summary>
    /// 订阅 <c>StudyAnalyticsService</c> 时直接把这个方法当回调：不可见（组件已分离或不在当前页）时连排队都不做。
    /// 这条判断原来在 8 个学习组件里各抄了一遍（每份 9 行、逐字相同，只有一份多一句 <c>_ = sender;</c> 压警告），
    /// 而 <c>_canRender</c> 本来就是传进这里来的——回调 shape 该由门自己管。
    /// </summary>
    public void HandleSnapshotUpdated(object? sender, StudyAnalyticsSnapshotChangedEventArgs e)
    {
        if (!_canRender())
        {
            return;
        }

        Queue(e.Snapshot);
    }

    public bool ProcessPending()
    {
        StudyAnalyticsSnapshot? snapshot;
        lock (_syncRoot)
        {
            _dispatchQueued = false;
            if (_isDisposed || !_hasPendingSnapshot)
            {
                return false;
            }

            snapshot = _pendingSnapshot;
            _pendingSnapshot = null;
            _hasPendingSnapshot = false;
        }

        if (snapshot is null || !_canRender())
        {
            return false;
        }

        _renderSnapshot(snapshot);
        _afterRender?.Invoke();
        return true;
    }

    public void Clear()
    {
        lock (_syncRoot)
        {
            _pendingSnapshot = null;
            _hasPendingSnapshot = false;
        }
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            _pendingSnapshot = null;
            _hasPendingSnapshot = false;
            _isDisposed = true;
        }
    }
}
