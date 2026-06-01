namespace LanMountainDesktop.Services.Plonds;

internal sealed record PlondsSourceDescriptor(
    string Id,
    string Kind,
    string ManifestUrl,
    int Priority = 0);
