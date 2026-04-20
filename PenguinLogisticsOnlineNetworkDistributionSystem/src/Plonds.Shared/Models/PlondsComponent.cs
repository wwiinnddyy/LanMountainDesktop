namespace Plonds.Shared.Models;

public sealed record PlondsComponent(
    string Id,
    string Root,
    string Mode,
    IReadOnlyList<PlondsFileEntry> Files,
    IReadOnlyDictionary<string, string>? Metadata = null);

