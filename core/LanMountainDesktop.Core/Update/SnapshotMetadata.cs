using System;
using System.Text.Json.Serialization;

namespace LanMountainDesktop.Shared.Contracts.Update;

/// <summary>
/// <c>{数据根}/update/snapshots/*.json</c> 里的快照元数据：宿主应用更新时写，
/// 宿主与启动器两侧在"保留哪些旧版本可回滚"时各自读它。两个二进制之间没有共享类型，
/// 全靠这几个属性名逐字对齐，所以模型与落盘键名都得钉在这一个地方。
/// </summary>
/// <remarks>
/// 键名写成显式 <see cref="JsonPropertyNameAttribute"/>：两侧源生成上下文各自设了 camelCase 策略，
/// 但"读得到"不该依赖两个程序集的序列化开关是否恰好一致（两边都开了大小写不敏感，所以现在没出事）。
/// </remarks>
public sealed class SnapshotMetadata
{
    /// <summary>刚建快照、还没切活动部署时的状态值。</summary>
    public const string PendingStatus = "pending";

    [JsonPropertyName("snapshotId")]
    public string SnapshotId { get; set; } = string.Empty;

    [JsonPropertyName("sourceVersion")]
    public string SourceVersion { get; set; } = string.Empty;

    [JsonPropertyName("targetVersion")]
    public string? TargetVersion { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>更新前的活动部署目录，回滚时按它决定"这个版本还要不要留"。</summary>
    [JsonPropertyName("sourceDirectory")]
    public string SourceDirectory { get; set; } = string.Empty;

    [JsonPropertyName("targetDirectory")]
    public string? TargetDirectory { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = PendingStatus;
}
