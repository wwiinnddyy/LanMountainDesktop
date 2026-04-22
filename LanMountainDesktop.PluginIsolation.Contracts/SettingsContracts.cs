using System.Text.Json;

namespace LanMountainDesktop.PluginIsolation.Contracts;

public sealed record PluginSettingsSnapshotRequest(
    string Scope,
    string? SectionId = null,
    string? ComponentInstanceId = null);

public sealed record PluginSettingsSnapshotResponse(
    string Scope,
    JsonElement Snapshot,
    string? ETag = null);

public sealed record PluginSettingsWriteRequest(
    string Scope,
    JsonElement Value,
    string? SectionId = null,
    string? ComponentInstanceId = null,
    string? ETag = null);

public sealed record PluginSettingsWriteResponse(
    bool Accepted,
    string? ETag = null,
    string? ErrorCode = null,
    string? ErrorMessage = null);

public sealed record PluginSettingsChangedNotification(
    string Scope,
    JsonElement Value,
    string? SectionId = null,
    string? ComponentInstanceId = null,
    string? ETag = null);
