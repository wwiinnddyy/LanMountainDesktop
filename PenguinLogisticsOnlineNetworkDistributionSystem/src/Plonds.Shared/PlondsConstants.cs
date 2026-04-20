namespace Plonds.Shared;

public static class PlondsConstants
{
    public const string ProtocolName = "PLONDS";
    public const string ProtocolVersion = "1.0";

    public const string DefaultApiBasePath = "/api/plonds/v1";
    public const string DefaultStorageRoot = "sample-data";
    public const string DefaultMetaRoot = "meta";
    public const string DefaultRepoRoot = "repo";
    public const string DefaultInstallersRoot = "installers";

    public const string FileObjectMode = "file-object";
    public const string CompressedObjectMode = "compressed-object";
    public const string BinaryPatchMode = "binary-patch";

    public static readonly string[] SupportedFileModes =
    [
        FileObjectMode,
        CompressedObjectMode,
        BinaryPatchMode
    ];
}

