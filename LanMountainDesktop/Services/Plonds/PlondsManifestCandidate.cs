namespace LanMountainDesktop.Services.Plonds;

internal sealed record PlondsManifestCandidate(
    PlondsSourceDescriptor Source,
    PlondsClientManifest Manifest);
