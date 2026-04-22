namespace LanMountainDesktop.Shared.IPC;

public static class IpcConstants
{
    public const string DefaultPipeName = "LanMountainDesktop.IPC.v1.Server";

    public const string ProtocolVersion = "external-ipc-public-api.v1";

    public static class Routes
    {
        public const string SessionGetInfo = "lanmountain.session.get-info";
        public const string CatalogGet = "lanmountain.catalog.get";
    }
}
