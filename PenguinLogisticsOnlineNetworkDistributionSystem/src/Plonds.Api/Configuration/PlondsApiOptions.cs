namespace Plonds.Api.Configuration;

public sealed class PlondsApiOptions
{
    public string StorageRoot { get; set; } = Plonds.Shared.PlondsConstants.DefaultStorageRoot;

    public string MetaRoot { get; set; } = Plonds.Shared.PlondsConstants.DefaultMetaRoot;

    public string ApiBasePath { get; set; } = Plonds.Shared.PlondsConstants.DefaultApiBasePath;
}

