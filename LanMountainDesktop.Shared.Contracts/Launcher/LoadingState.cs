namespace LanMountainDesktop.Shared.Contracts.Launcher;

/// <summary>
/// 加载项类型
/// </summary>
public enum LoadingItemType
{
    /// <summary>
    /// 系统初始化
    /// </summary>
    System,
    
    /// <summary>
    /// 设置加载
    /// </summary>
    Settings,
    
    /// <summary>
    /// 插件
    /// </summary>
    Plugin,
    
    /// <summary>
    /// 组件
    /// </summary>
    Component,
    
    /// <summary>
    /// 资源
    /// </summary>
    Resource,
    
    /// <summary>
    /// 数据
    /// </summary>
    Data,
    
    /// <summary>
    /// 网络请求
    /// </summary>
    Network,
    
    /// <summary>
    /// 其他
    /// </summary>
    Other
}

/// <summary>
/// 加载状态
/// </summary>
public enum LoadingState
{
    /// <summary>
    /// 等待中
    /// </summary>
    Pending,
    
    /// <summary>
    /// 进行中
    /// </summary>
    InProgress,
    
    /// <summary>
    /// 已完成
    /// </summary>
    Completed,
    
    /// <summary>
    /// 失败
    /// </summary>
    Failed,
    
    /// <summary>
    /// 已取消
    /// </summary>
    Cancelled,
    
    /// <summary>
    /// 超时
    /// </summary>
    Timeout
}

/// <summary>
/// 加载项信息
/// </summary>
public record LoadingItem
{
    /// <summary>
    /// 加载项唯一标识
    /// </summary>
    public required string Id { get; init; }
    
    /// <summary>
    /// 加载项类型
    /// </summary>
    public LoadingItemType Type { get; init; }
    
    /// <summary>
    /// 加载项名称
    /// </summary>
    public required string Name { get; init; }
    
    /// <summary>
    /// 加载项描述
    /// </summary>
    public string? Description { get; init; }
    
    /// <summary>
    /// 当前状态
    /// </summary>
    public LoadingState State { get; init; }
    
    /// <summary>
    /// 进度百分比 (0-100)
    /// </summary>
    public int ProgressPercent { get; init; }
    
    /// <summary>
    /// 状态消息
    /// </summary>
    public string? Message { get; init; }
    
    /// <summary>
    /// 错误信息（当 State 为 Failed 时）
    /// </summary>
    public string? ErrorMessage { get; init; }
    
    /// <summary>
    /// 开始时间
    /// </summary>
    public DateTimeOffset? StartTime { get; init; }
    
    /// <summary>
    /// 结束时间
    /// </summary>
    public DateTimeOffset? EndTime { get; init; }
    
    /// <summary>
    /// 预计剩余时间（秒）
    /// </summary>
    public int? EstimatedRemainingSeconds { get; init; }
    
    /// <summary>
    /// 子加载项
    /// </summary>
    public List<LoadingItem>? Children { get; init; }
    
    /// <summary>
    /// 额外数据
    /// </summary>
    public Dictionary<string, string>? Metadata { get; init; }
    
    /// <summary>
    /// 时间戳
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// 加载状态更新消息
/// </summary>
public record LoadingStateMessage
{
    /// <summary>
    /// 当前启动阶段
    /// </summary>
    public StartupStage Stage { get; init; }
    
    /// <summary>
    /// 整体进度百分比 (0-100)
    /// </summary>
    public int OverallProgressPercent { get; init; }
    
    /// <summary>
    /// 当前活动的加载项
    /// </summary>
    public List<LoadingItem> ActiveItems { get; init; } = new();
    
    /// <summary>
    /// 已完成的加载项数量
    /// </summary>
    public int CompletedCount { get; init; }
    
    /// <summary>
    /// 总加载项数量
    /// </summary>
    public int TotalCount { get; init; }
    
    /// <summary>
    /// 状态消息
    /// </summary>
    public string? Message { get; init; }
    
    /// <summary>
    /// 是否有错误
    /// </summary>
    public bool HasErrors { get; init; }
    
    /// <summary>
    /// 错误消息列表
    /// </summary>
    public List<string>? ErrorMessages { get; init; }
    
    /// <summary>
    /// 时间戳
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// 详细的加载进度消息（用于实时更新）
/// </summary>
public record DetailedProgressMessage : StartupProgressMessage
{
    /// <summary>
    /// 当前加载项
    /// </summary>
    public LoadingItem? CurrentItem { get; init; }
    
    /// <summary>
    /// 所有加载项
    /// </summary>
    public List<LoadingItem>? AllItems { get; init; }
    
    /// <summary>
    /// 是否为主要更新
    /// </summary>
    public bool IsMajorUpdate { get; init; }
}
