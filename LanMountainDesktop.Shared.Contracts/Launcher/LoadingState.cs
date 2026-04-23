namespace LanMountainDesktop.Shared.Contracts.Launcher;

public enum LoadingItemType
{
    System,
    Settings,
    Plugin,
    Component,
    Resource,
    Data,
    Network,
    Other
}

public enum LoadingState
{
    Pending,
    InProgress,
    Completed,
    Delayed,
    Failed,
    Cancelled,
    Timeout
}

public record LoadingItem
{
    public required string Id { get; init; }

    public LoadingItemType Type { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public LoadingState State { get; init; }

    public int ProgressPercent { get; init; }

    public string? Message { get; init; }

    public string? ErrorMessage { get; init; }

    public DateTimeOffset? StartTime { get; init; }

    public DateTimeOffset? EndTime { get; init; }

    public int? EstimatedRemainingSeconds { get; init; }

    public List<LoadingItem>? Children { get; init; }

    public Dictionary<string, string>? Metadata { get; init; }

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

public record LoadingStateMessage
{
    public StartupStage Stage { get; init; }

    public int OverallProgressPercent { get; init; }

    public List<LoadingItem> ActiveItems { get; init; } = new();

    public int CompletedCount { get; init; }

    public int TotalCount { get; init; }

    public string? Message { get; init; }

    public bool HasErrors { get; init; }

    public List<string>? ErrorMessages { get; init; }

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

public record DetailedProgressMessage : StartupProgressMessage
{
    public LoadingItem? CurrentItem { get; init; }

    public List<LoadingItem>? AllItems { get; init; }

    public bool IsMajorUpdate { get; init; }
}
