namespace LanMountainDesktop.Shared.IPC;

public sealed record PublicAppInfoSnapshot(
    string ApplicationName,
    string Version,
    string Codename,
    string PipeName,
    int ProcessId,
    DateTimeOffset StartedAt);
