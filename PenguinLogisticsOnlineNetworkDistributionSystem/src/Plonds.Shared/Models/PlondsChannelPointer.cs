namespace Plonds.Shared.Models;

public sealed record PlondsChannelPointer(
    string Channel,
    string Platform,
    string DistributionId,
    string Version,
    DateTimeOffset PublishedAt,
    string? DistributionPath = null,
    string? FileMapPath = null);

