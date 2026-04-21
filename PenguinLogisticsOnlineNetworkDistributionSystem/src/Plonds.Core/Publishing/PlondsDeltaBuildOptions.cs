namespace Plonds.Core.Publishing;

public sealed record PlondsDeltaBuildOptions(
    string Platform,
    string CurrentVersion,
    string CurrentTag,
    string CurrentPayloadZip,
    string OutputRoot,
    string PrivateKeyPath,
    string Channel = "stable",
    string? BaselineVersion = null,
    string? BaselineTag = null,
    string? BaselinePayloadZip = null,
    bool IsFullPayload = false);
