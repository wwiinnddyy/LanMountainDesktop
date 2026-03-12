namespace LanMountainDesktop.PluginSdk;

public interface IPluginExportRegistry
{
    IReadOnlyList<PluginServiceExportDescriptor> GetExports();

    IReadOnlyList<PluginServiceExportDescriptor> GetExports(Type contractType);

    PluginServiceExportDescriptor? GetExport(Type contractType, string providerPluginId);

    TContract? GetExport<TContract>(string providerPluginId)
        where TContract : class;
}
