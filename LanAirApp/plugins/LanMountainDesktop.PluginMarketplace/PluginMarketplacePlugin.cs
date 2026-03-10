using LanMountainDesktop.PluginSdk;

namespace LanMountainDesktop.PluginMarketplace;

[PluginEntrance]
public sealed class PluginMarketplacePlugin : PluginBase, IDisposable
{
    private AirAppMarketIndexService? _indexService;
    private AirAppMarketInstallService? _installService;

    public override void Initialize(IPluginContext context)
    {
        Directory.CreateDirectory(context.DataDirectory);

        var localizer = PluginLocalizer.Create(context);
        var packageManager = context.GetService<IPluginPackageManager>()
            ?? throw new InvalidOperationException(
                "The host does not expose IPluginPackageManager. LanMountainDesktop.PluginMarketplace requires a newer host build.");

        var cacheService = new AirAppMarketCacheService(context.DataDirectory);
        _indexService = new AirAppMarketIndexService(cacheService);
        _installService = new AirAppMarketInstallService(packageManager, context.DataDirectory);

        context.RegisterService(cacheService);
        context.RegisterService(_indexService);
        context.RegisterService(_installService);

        context.RegisterSettingsPage(new PluginSettingsPageRegistration(
            "marketplace",
            localizer.GetString("market.page_title", "插件市场"),
            () => new PluginMarketplaceSettingsView(
                context,
                localizer,
                packageManager,
                _indexService,
                _installService),
            sortOrder: -100));
    }

    public void Dispose()
    {
        _installService?.Dispose();
        _indexService?.Dispose();
        _installService = null;
        _indexService = null;
    }
}
