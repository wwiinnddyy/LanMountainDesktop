namespace Plonds.Shared.Models;

public sealed record PlondsDistributionInfo(
    string DistributionId,
    string Version,
    string Channel,
    string Platform,
    DateTimeOffset PublishedAt,
    IReadOnlyList<PlondsComponent> Components,
    IReadOnlyList<PlondsMirrorAsset> InstallerMirrors,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<PlondsSignatureDescriptor> Signatures,
    IReadOnlyDictionary<string, string>? Metadata = null);

