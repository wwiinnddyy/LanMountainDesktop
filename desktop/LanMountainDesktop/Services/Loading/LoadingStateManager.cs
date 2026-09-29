using System.Collections.Concurrent;
using LanMountainDesktop.Shared.Contracts.Launcher;
using LanMountainDesktop.Shared.Threading;

namespace LanMountainDesktop.Services.Loading;

/// <summary>
/// 加载状态管理器 - 管理所有加载项的状态
/// </summary>
public class LoadingStateManager : IDisposable
{
    private readonly ConcurrentDictionary<string, LoadingItem> _items = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _startTimes = new();
    private readonly object _lock = new();
    
    /// <summary>
    /// 状态变更事件
    /// </summary>
    public event EventHandler<LoadingStateChangedEventArgs>? StateChanged;
    
    /// <summary>
    /// 整体进度变了。**不带货**：原来那条事件参数里 <c>Stage</c> 永远是
    /// <c>StartupStage.Initializing</c>（写死的常量，#G1-AZ 之后这条模型根本没有"阶段推进"那一维）、
    /// <c>OverallProgressPercent</c> 没人读（订阅者自己从条目重算详单）、
    /// <c>Message</c> 反过来**从来没人写**（读了个恒 null）。三个字段两头都没对接上，
    /// 留着等于让"进度在哪一阶段"这句话在事件签名里冒充有答案。
    /// </summary>
    public event EventHandler? OverallProgressChanged;
    
    /// <summary>
    /// 整体进度百分比
    /// </summary>
    public int OverallProgressPercent { get; private set; }
    
    /// <summary>
    /// 是否正在加载
    /// </summary>
    public bool IsLoading => _items.Values.Any(i => i.State == LoadingState.InProgress);
    
    /// <summary>
    /// 是否有错误
    /// </summary>
    public bool HasErrors => _items.Values.Any(i => i.State == LoadingState.Failed);
    
    /// <summary>
    /// 获取所有加载项
    /// </summary>
    public IReadOnlyCollection<LoadingItem> GetAllItems() => _items.Values.ToList();
    
    /// <summary>
    /// 获取活动的加载项
    /// </summary>
    public IReadOnlyCollection<LoadingItem> GetActiveItems() => 
        _items.Values.Where(i => i.State is LoadingState.InProgress or LoadingState.Pending).ToList();
    
    /// <summary>
    /// 注册加载项
    /// </summary>
    public LoadingItem RegisterItem(
        string id, 
        LoadingItemType type, 
        string name, 
        string? description = null,
        Dictionary<string, string>? metadata = null)
    {
        var item = new LoadingItem
        {
            Id = id,
            Type = type,
            Name = name,
            Description = description,
            State = LoadingState.Pending,
            ProgressPercent = 0,
            Metadata = metadata,
            Timestamp = DateTimeOffset.UtcNow
        };
        
        _items[id] = item;
        
        StateChanged?.Invoke(this, new LoadingStateChangedEventArgs 
        { 
            Item = item, 
            PreviousState = null, 
            CurrentState = item.State 
        });
        
        return item;
    }
    
    /// <summary>
    /// 开始加载
    /// </summary>
    public void StartItem(string id, string? message = null)
    {
        if (!_items.TryGetValue(id, out var item))
            return;
        
        var previousState = item.State;
        var startTime = DateTimeOffset.UtcNow;
        
        _startTimes[id] = startTime;
        
        var updatedItem = item with
        {
            State = LoadingState.InProgress,
            StartTime = startTime,
            Message = message ?? $"正在加载 {item.Name}...",
            Timestamp = DateTimeOffset.UtcNow
        };
        
        _items[id] = updatedItem;
        
        StateChanged?.Invoke(this, new LoadingStateChangedEventArgs 
        { 
            Item = updatedItem, 
            PreviousState = previousState, 
            CurrentState = updatedItem.State 
        });
        
        UpdateOverallProgress();
    }
    
    /// <summary>
    /// 完成加载
    /// </summary>
    public void CompleteItem(string id, string? message = null)
    {
        if (!_items.TryGetValue(id, out var item))
            return;
        
        var previousState = item.State;
        var endTime = DateTimeOffset.UtcNow;
        
        _startTimes.TryRemove(id, out _);
        
        var updatedItem = item with
        {
            State = LoadingState.Completed,
            ProgressPercent = 100,
            EndTime = endTime,
            Message = message ?? $"{item.Name} 加载完成",
            Timestamp = DateTimeOffset.UtcNow
        };
        
        _items[id] = updatedItem;
        
        StateChanged?.Invoke(this, new LoadingStateChangedEventArgs 
        { 
            Item = updatedItem, 
            PreviousState = previousState, 
            CurrentState = updatedItem.State 
        });
        
        UpdateOverallProgress();
    }
    
    /// <summary>
    /// 标记失败
    /// </summary>
    public void FailItem(string id, string errorMessage, string? details = null)
    {
        if (!_items.TryGetValue(id, out var item))
            return;
        
        var previousState = item.State;
        var endTime = DateTimeOffset.UtcNow;
        
        _startTimes.TryRemove(id, out _);
        
        var fullErrorMessage = string.IsNullOrEmpty(details) 
            ? errorMessage 
            : $"{errorMessage}: {details}";
        
        var updatedItem = item with
        {
            State = LoadingState.Failed,
            ErrorMessage = fullErrorMessage,
            EndTime = endTime,
            Message = $"{item.Name} 加载失败",
            Timestamp = DateTimeOffset.UtcNow
        };
        
        _items[id] = updatedItem;
        
        StateChanged?.Invoke(this, new LoadingStateChangedEventArgs 
        { 
            Item = updatedItem, 
            PreviousState = previousState, 
            CurrentState = updatedItem.State 
        });
        
        UpdateOverallProgress();
    }
    
    /// <summary>
    /// 更新整体进度
    /// </summary>
    private void UpdateOverallProgress()
    {
        lock (_lock)
        {
            var items = _items.Values.ToList();
            if (items.Count == 0)
            {
                OverallProgressPercent = 0;
                return;
            }
            
            // 计算加权进度
            var totalWeight = items.Count;
            var completedWeight = items.Count(i => i.State == LoadingState.Completed);
            var inProgressWeight = items
                .Where(i => i.State == LoadingState.InProgress)
                .Sum(i => i.ProgressPercent / 100.0);
            
            // 阶段一律写 Initializing：这个模型只有条目状态、没有阶段推进那一维。启动阶段与百分比
            // 走的是另一条活路径（App.axaml.cs 的 ReportStartupProgress → LauncherStartupProgress）。
            var progress = (int)((completedWeight + inProgressWeight) / totalWeight * 100);
            OverallProgressPercent = Math.Clamp(progress, 0, 100);
            
            OverallProgressChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    
    /// <summary>
    /// 获取加载状态消息
    /// </summary>
    public LoadingStateMessage GetLoadingStateMessage()
    {
        var items = _items.Values.ToList();
        var activeItems = items.Where(i => i.State is LoadingState.InProgress or LoadingState.Pending).ToList();
        var errorItems = items.Where(i => i.State == LoadingState.Failed).ToList();
        
        return new LoadingStateMessage
        {
            Stage = StartupStage.Initializing,
            OverallProgressPercent = OverallProgressPercent,
            ActiveItems = activeItems,
            CompletedCount = items.Count(i => i.State == LoadingState.Completed),
            TotalCount = items.Count,
            HasErrors = errorItems.Any(),
            ErrorMessages = errorItems.Select(i => $"{i.Name}: {i.ErrorMessage}").ToList()
        };
    }
    
    /// <summary>
    /// 清理所有加载项
    /// </summary>
    public void Clear()
    {
        _items.Clear();
        _startTimes.Clear();
        OverallProgressPercent = 0;
    }
    
    public void Dispose()
    {
        _items.Clear();
        _startTimes.Clear();
    }
}

/// <summary>
/// 加载状态变更事件参数
/// </summary>
public class LoadingStateChangedEventArgs : EventArgs
{
    public required LoadingItem Item { get; init; }
    public LoadingState? PreviousState { get; init; }
    public required LoadingState CurrentState { get; init; }
}
