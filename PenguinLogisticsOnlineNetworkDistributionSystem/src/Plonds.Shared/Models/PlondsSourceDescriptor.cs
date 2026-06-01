namespace Plonds.Shared.Models;

public sealed record PlondsSourceDescriptor(
    string Id,
    string Kind,
    string ManifestUrl,
    int Priority = 0);
