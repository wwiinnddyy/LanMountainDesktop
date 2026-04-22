using System.Timers;
using LanMountainDesktop.Shared.Contracts.Launcher;
using LanMountainDesktop.Shared.IPC;

namespace LanMountainDesktop.Services.Loading;

/// <summary>
/// 加载状态上报器 - 将加载状态实时上报给 Launcher
/// </summary>
public class LoadingStateReporter : IDisposable
{
    private readonly LoadingStateManager _manager;
    private readonly IExternalIpcNotificationPublisher? _notificationPublisher;
    private readonly System.Timers.Timer _reportTimer;
    private readonly object _lock = new();
    private bool _isDisposed;
    
    /// <summary>
    /// 上报间隔（毫秒）
    /// </summary>
    public int ReportIntervalMs { get; set; } = 100;
    
    /// <summary>
    /// 是否启用批量上报优化
    /// </summary>
    public bool EnableBatching { get; set; } = true;
    
    /// <summary>
    /// 最小上报间隔（毫秒），用于限制高频更新
    /// </summary>
    public int MinReportIntervalMs { get; set; } = 50;
    
    private DateTimeOffset _lastReportTime = DateTimeOffset.MinValue;
    private DetailedProgressMessage? _pendingMessage;
    private bool _hasPendingMessage;

    public LoadingStateReporter(
        LoadingStateManager manager, 
        IExternalIpcNotificationPublisher? notificationPublisher = null)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _notificationPublisher = notificationPublisher;
        
        // 创建定时上报定时器
        _reportTimer = new System.Timers.Timer(ReportIntervalMs);
        _reportTimer.Elapsed += OnReportTimerElapsed;
        _reportTimer.AutoReset = true;
        
        // 订阅状态变更事件
        _manager.StateChanged += OnStateChanged;
        _manager.OverallProgressChanged += OnOverallProgressChanged;
    }

    /// <summary>
    /// 启动上报
    /// </summary>
    public void Start()
    {
        if (_isDisposed) return;
        
        _reportTimer.Start();
        AppLogger.Info("LoadingStateReporter", "Loading state reporter started");
    }

    /// <summary>
    /// 停止上报
    /// </summary>
    public void Stop()
    {
        _reportTimer.Stop();
        
        // 发送任何待处理的消息
        FlushPendingMessage();
        
        AppLogger.Info("LoadingStateReporter", "Loading state reporter stopped");
    }

    /// <summary>
    /// 立即上报当前状态
    /// </summary>
    public async Task ReportImmediatelyAsync()
    {
        if (_isDisposed || _notificationPublisher == null) return;
        
        var message = CreateDetailedProgressMessage();
        await SendMessageAsync(message);
    }

    /// <summary>
    /// 上报单个加载项的进度
    /// </summary>
    public async Task ReportItemProgressAsync(string itemId, int percent, string? message = null)
    {
        if (_isDisposed || _notificationPublisher == null) return;
        
        var item = _manager.GetAllItems().FirstOrDefault(i => i.Id == itemId);
        if (item == null) return;
        
        var updatedItem = item with
        {
            ProgressPercent = percent,
            Message = message ?? item.Message,
            Timestamp = DateTimeOffset.UtcNow
        };
        
        var progressMessage = new DetailedProgressMessage
        {
            Stage = _manager.CurrentStage,
            ProgressPercent = _manager.OverallProgressPercent,
            CurrentItem = updatedItem,
            AllItems = _manager.GetAllItems().ToList(),
            Message = message,
            IsMajorUpdate = false
        };
        
        await SendMessageAsync(progressMessage);
    }

    /// <summary>
    /// 上报阶段变更
    /// </summary>
    public async Task ReportStageChangeAsync(StartupStage stage, string? message = null)
    {
        if (_isDisposed || _notificationPublisher == null) return;
        
        var progressMessage = new DetailedProgressMessage
        {
            Stage = stage,
            ProgressPercent = _manager.OverallProgressPercent,
            AllItems = _manager.GetAllItems().ToList(),
            Message = message ?? $"进入阶段: {stage}",
            IsMajorUpdate = true
        };
        
        await SendMessageAsync(progressMessage);
    }

    /// <summary>
    /// 上报错误
    /// </summary>
    public async Task ReportErrorAsync(string errorMessage, string? details = null)
    {
        if (_isDisposed || _notificationPublisher == null) return;
        
        var fullMessage = string.IsNullOrEmpty(details) 
            ? errorMessage 
            : $"{errorMessage}: {details}";
        
        var progressMessage = new DetailedProgressMessage
        {
            Stage = _manager.CurrentStage,
            ProgressPercent = _manager.OverallProgressPercent,
            AllItems = _manager.GetAllItems().ToList(),
            Message = fullMessage,
            IsMajorUpdate = true
        };
        
        await SendMessageAsync(progressMessage);
    }

    /// <summary>
    /// 状态变更事件处理
    /// </summary>
    private void OnStateChanged(object? sender, LoadingStateChangedEventArgs e)
    {
        if (_isDisposed) return;
        
        // 重要状态变更立即上报
        if (e.CurrentState is LoadingState.Completed or LoadingState.Failed or LoadingState.Timeout)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await ReportImmediatelyAsync();
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("LoadingStateReporter", $"Failed to report state change: {ex.Message}");
                }
            });
        }
        else
        {
            // 其他状态变更标记为待处理
            QueueMessage(CreateDetailedProgressMessage());
        }
    }

    /// <summary>
    /// 整体进度变更事件处理
    /// </summary>
    private void OnOverallProgressChanged(object? sender, OverallProgressChangedEventArgs e)
    {
        if (_isDisposed) return;
        
        QueueMessage(CreateDetailedProgressMessage(e.Message));
    }

    /// <summary>
    /// 定时上报处理
    /// </summary>
    private void OnReportTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        FlushPendingMessage();
    }

    /// <summary>
    /// 将消息加入待处理队列
    /// </summary>
    private void QueueMessage(DetailedProgressMessage message)
    {
        if (!EnableBatching)
        {
            // 如果不启用批量，立即发送
            _ = Task.Run(async () => await SendMessageAsync(message));
            return;
        }
        
        lock (_lock)
        {
            _pendingMessage = message;
            _hasPendingMessage = true;
        }
    }

    /// <summary>
    /// 刷新待处理消息
    /// </summary>
    private void FlushPendingMessage()
    {
        DetailedProgressMessage? message;
        
        lock (_lock)
        {
            if (!_hasPendingMessage) return;
            
            message = _pendingMessage;
            _pendingMessage = null;
            _hasPendingMessage = false;
        }
        
        if (message != null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await SendMessageAsync(message);
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("LoadingStateReporter", $"Failed to flush pending message: {ex.Message}");
                }
            });
        }
    }

    /// <summary>
    /// 创建详细的进度消息
    /// </summary>
    private DetailedProgressMessage CreateDetailedProgressMessage(string? message = null)
    {
        var activeItems = _manager.GetActiveItems().ToList();
        var currentItem = activeItems.FirstOrDefault();
        
        return new DetailedProgressMessage
        {
            Stage = _manager.CurrentStage,
            ProgressPercent = _manager.OverallProgressPercent,
            CurrentItem = currentItem,
            AllItems = _manager.GetAllItems().ToList(),
            Message = message ?? currentItem?.Message,
            IsMajorUpdate = false
        };
    }

    /// <summary>
    /// 发送消息
    /// </summary>
    private async Task SendMessageAsync(DetailedProgressMessage message)
    {
        if (_notificationPublisher == null) return;
        
        // 检查最小上报间隔
        var now = DateTimeOffset.UtcNow;
        var elapsed = now - _lastReportTime;
        if (elapsed.TotalMilliseconds < MinReportIntervalMs)
        {
            await Task.Delay(MinReportIntervalMs - (int)elapsed.TotalMilliseconds);
        }
        
        try
        {
            // 转换为 StartupProgressMessage 以保持兼容性
            var loadingStateMessage = _manager.GetLoadingStateMessage() with
            {
                Stage = message.Stage,
                OverallProgressPercent = message.ProgressPercent,
                Message = FormatMessage(message),
                Timestamp = DateTimeOffset.UtcNow
            };

            await _notificationPublisher.NotifyAsync(IpcRoutedNotifyIds.LauncherLoadingState, loadingStateMessage);
            _lastReportTime = DateTimeOffset.UtcNow;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("LoadingStateReporter", $"Failed to send message: {ex.Message}");
        }
    }

    /// <summary>
    /// 格式化消息
    /// </summary>
    private string FormatMessage(DetailedProgressMessage message)
    {
        var parts = new List<string>();
        
        if (message.CurrentItem != null)
        {
            parts.Add($"[{message.CurrentItem.Type}] {message.CurrentItem.Name}");
            
            if (message.CurrentItem.ProgressPercent > 0)
            {
                parts.Add($"{message.CurrentItem.ProgressPercent}%");
            }
        }
        
        if (!string.IsNullOrEmpty(message.Message))
        {
            parts.Add(message.Message);
        }
        
        var completedCount = message.AllItems?.Count(i => i.State == LoadingState.Completed) ?? 0;
        var totalCount = message.AllItems?.Count ?? 0;
        
        if (totalCount > 0)
        {
            parts.Add($"({completedCount}/{totalCount})");
        }
        
        return string.Join(" - ", parts);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        
        _isDisposed = true;
        
        Stop();
        
        _reportTimer.Elapsed -= OnReportTimerElapsed;
        _reportTimer.Dispose();
        
        _manager.StateChanged -= OnStateChanged;
        _manager.OverallProgressChanged -= OnOverallProgressChanged;
    }
}
