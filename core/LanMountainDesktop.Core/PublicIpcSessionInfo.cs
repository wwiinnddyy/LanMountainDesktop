namespace LanMountainDesktop.Shared.IPC;

public sealed record PublicIpcSessionInfo(
    string PipeName,
    string ProtocolVersion,
    string[] Capabilities,
    DateTimeOffset StartedAt);
