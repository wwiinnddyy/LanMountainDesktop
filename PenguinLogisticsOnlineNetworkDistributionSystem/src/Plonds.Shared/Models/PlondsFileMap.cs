namespace Plonds.Shared.Models;

public sealed record PlondsFileMap(
    string FormatVersion,
    string DistributionId,
    string SourceVersion,
    string TargetVersion,
    string Platform,
    IReadOnlyList<PlondsComponent> Components,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<PlondsSignatureDescriptor> Signatures,
    IReadOnlyDictionary<string, string>? Metadata = null);

