using System;

namespace LanMountainDesktop.Shared.Contracts.Update;

public sealed record DeploymentLock(
    int SchemaVersion,
    string Kind,
    string TargetVersion,
    string PayloadPath,
    string? PayloadSha256,
    DateTimeOffset CreatedAtUtc);
