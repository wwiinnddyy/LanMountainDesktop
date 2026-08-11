using System.Text.Json;

namespace LanMountainDesktop.AirAppIsolation.Contracts;

public sealed record AirAppSettingsSnapshotRequest(
    string Scope,
    string? SectionId = null,
    string? ComponentInstanceId = null);

public sealed record AirAppSettingsSnapshotResponse(
    string Scope,
    JsonElement Snapshot,
    string? ETag = null);

public sealed record AirAppSettingsWriteRequest(
    string Scope,
    JsonElement Value,
    string? SectionId = null,
    string? ComponentInstanceId = null,
    string? ETag = null);

public sealed record AirAppSettingsWriteResponse(
    bool Accepted,
    string? ETag = null,
    string? ErrorCode = null,
    string? ErrorMessage = null);

public sealed record AirAppSettingsChangedNotification(
    string Scope,
    JsonElement Value,
    string? SectionId = null,
    string? ComponentInstanceId = null,
    string? ETag = null);
