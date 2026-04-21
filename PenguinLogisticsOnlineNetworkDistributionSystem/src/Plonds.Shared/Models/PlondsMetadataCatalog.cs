namespace Plonds.Shared.Models;

public sealed record PlondsMetadataCatalog(
    string ProtocolName,
    string ProtocolVersion,
    string StorageRoot,
    string MetaRoot,
    IReadOnlyList<PlondsChannelPointer> Latest,
    IReadOnlyDictionary<string, string>? Metadata = null);

