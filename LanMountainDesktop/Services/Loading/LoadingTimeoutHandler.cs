using System.Timers;
using LanMountainDesktop.Shared.Contracts.Launcher;

namespace LanMountainDesktop.Services.Loading;

/// <summary>
/// 加载超时处理器 - 监控加载项超时并执行相应处理
/// </summary>
public class LoadingTimeoutHandler : IDisposable
{
    private readonly LoadingStateManager _manager;
    private readonly System.Timers.Timer _checkTimer;
    private readonly Dictionary<string, TimeSpan> _itemTimeouts = new();
    private readonly Dictionary<string, int> _retryCounts = new();
    private readonly object _lock = new();
    private bool _isDisposed;

    /// <summary>
    /// 默认超时时间
    /// </summary>
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 最大重试次数
    /// </summary>
    public int MaxRetryCount { get; set; } = 3;

    /// <summary>
    /// 检查间隔
    /// </summary>
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 超时事件
    /// </summary>
    public event EventHandler<LoadingTimeoutEventArgs>? ItemTimeout;

    /// <summary>
    /// 重试事件
    /// </summary>
    public event EventHandler<LoadingRetryEventArgs>? ItemRetry;

    /// <summary>
    /// 最终失败事件（超过最大重试次数）
    /// </summary>
    public event EventHandler<LoadingTimeoutEventArgs>? ItemFailed;

    public LoadingTimeoutHandler(LoadingStateManager manager)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));

        _checkTimer = new System.Timers.Timer(CheckInterval.TotalMilliseconds);
        _checkTimer.Elapsed += OnCheckTimerElapsed;
        _checkTimer.AutoReset = true;

        // 订阅状态变更事件
        _manager.StateChanged += OnStateChanged;
    }

    /// <summary>
    /// 启动监控
    /// </summary>
    public void Start()
    {
        if (_isDisposed) return;
        _checkTimer.Start();
        AppLogger.Info("LoadingTimeoutHandler", "Timeout handler started");
    }

    /// <summary>
    /// 停止监控
    /// </summary>
    public void Stop()
    {
        _checkTimer.Stop();
        AppLogger.Info("LoadingTimeoutHandler", "Timeout handler stopped");
    }

    /// <summary>
    /// 为特定加载项设置超时
    /// </summary>
    public void SetItemTimeout(string itemId, TimeSpan timeout)
    {
        lock (_lock)
        {
            _itemTimeouts[itemId] = timeout;
        }
    }

    /// <summary>
    /// 获取加载项的超时时间
    /// </summary>
    public TimeSpan GetItemTimeout(string itemId)
    {
        lock (_lock)
        {
            return _itemTimeouts.TryGetValue(itemId, out var timeout) ? timeout : DefaultTimeout;
        }
    }

    /// <summary>
    /// 重置重试计数
    /// </summary>
    public void ResetRetryCount(string itemId)
    {
        lock (_lock)
        {
            _retryCounts[itemId] = 0;
        }
    }

    /// <summary>
    /// 定时检查超时
    /// </summary>
    private void OnCheckTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        if (_isDisposed) return;

        try
        {
            var activeItems = _manager.GetActiveItems().ToList();
            var now = DateTimeOffset.UtcNow;

            foreach (var item in activeItems)
            {
                if (!item.StartTime.HasValue) continue;

                var timeout = GetItemTimeout(item.Id);
                var elapsed = now - item.StartTime.Value;

                if (elapsed > timeout)
                {
                    HandleTimeout(item.Id, elapsed);
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("LoadingTimeoutHandler", $"Error checking timeouts: {ex.Message}");
        }
    }

    /// <summary>
    /// 处理超时
    /// </summary>
    private void HandleTimeout(string itemId, TimeSpan elapsed)
    {
        lock (_lock)
        {
            var retryCount = _retryCounts.GetValueOrDefault(itemId, 0);

            if (retryCount < MaxRetryCount)
            {
                // 重试
                _retryCounts[itemId] = retryCount + 1;

                var item = _manager.GetAllItems().FirstOrDefault(i => i.Id == itemId);
                if (item != null)
                {
                    AppLogger.Warn("LoadingTimeoutHandler",
                        $"Item '{item.Name}' timed out after {elapsed.TotalSeconds}s, retrying ({retryCount + 1}/{MaxRetryCount})...");

                    ItemRetry?.Invoke(this, new LoadingRetryEventArgs
                    {
                        ItemId = itemId,
                        ItemName = item.Name,
                        RetryCount = retryCount + 1,
                        MaxRetryCount = MaxRetryCount,
                        ElapsedTime = elapsed
                    });

                    // 重新启动该项
                    _manager.StartItem(itemId, $"第 {retryCount + 1} 次重试...");
                }
            }
            else
            {
                // 最终失败
                _retryCounts.Remove(itemId);

                var item = _manager.GetAllItems().FirstOrDefault(i => i.Id == itemId);
                if (item != null)
                {
                    AppLogger.Error("LoadingTimeoutHandler",
                        $"Item '{item.Name}' failed after {MaxRetryCount} retries ({elapsed.TotalSeconds}s)");

                    var args = new LoadingTimeoutEventArgs
                    {
                        ItemId = itemId,
                        ItemName = item.Name,
                        ElapsedTime = elapsed,
                        RetryCount = MaxRetryCount,
                        IsFinalFailure = true
                    };

                    ItemTimeout?.Invoke(this, args);
                    ItemFailed?.Invoke(this, args);

                    // 标记为失败
                    _manager.FailItem(itemId,
                        $"加载超时（超过 {elapsed.TotalSeconds:F0} 秒）",
                        $"已重试 {MaxRetryCount} 次但仍失败");
                }
            }
        }
    }

    /// <summary>
    /// 状态变更事件处理
    /// </summary>
    private void OnStateChanged(object? sender, LoadingStateChangedEventArgs e)
    {
        // 当项完成或失败时，清除重试计数
        if (e.CurrentState is LoadingState.Completed or LoadingState.Failed or LoadingState.Cancelled)
        {
            lock (_lock)
            {
                _retryCounts.Remove(e.Item.Id);
            }
        }

        // 当项开始时，如果是第一次开始，初始化重试计数
        if (e.CurrentState == LoadingState.InProgress &&
            (e.PreviousState == null || e.PreviousState == LoadingState.Pending))
        {
            lock (_lock)
            {
                if (!_retryCounts.ContainsKey(e.Item.Id))
                {
                    _retryCounts[e.Item.Id] = 0;
                }
            }
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        Stop();

        _checkTimer.Elapsed -= OnCheckTimerElapsed;
        _checkTimer.Dispose();

        _manager.StateChanged -= OnStateChanged;

        _itemTimeouts.Clear();
        _retryCounts.Clear();
    }
}

/// <summary>
/// 加载超时事件参数
/// </summary>
public class LoadingTimeoutEventArgs : EventArgs
{
    public required string ItemId { get; init; }
    public required string ItemName { get; init; }
    public required TimeSpan ElapsedTime { get; init; }
    public int RetryCount { get; init; }
    public bool IsFinalFailure { get; init; }
}

/// <summary>
/// 加载重试事件参数
/// </summary>
public class LoadingRetryEventArgs : EventArgs
{
    public required string ItemId { get; init; }
    public required string ItemName { get; init; }
    public required int RetryCount { get; init; }
    public required int MaxRetryCount { get; init; }
    public required TimeSpan ElapsedTime { get; init; }
}
