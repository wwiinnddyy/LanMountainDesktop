using LanMountainDesktop.PluginSdk;

namespace LanMountainDesktop.SamplePlugin;

[PluginEntrance]
public sealed class SamplePlugin : PluginBase, IDisposable
{
    private SamplePluginHeartbeatService? _heartbeatService;

    public override void Initialize(IPluginContext context)
    {
        Directory.CreateDirectory(context.DataDirectory);

        var hostName = context.TryGetProperty<string>("HostApplicationName", out var configuredHostName) &&
                       !string.IsNullOrWhiteSpace(configuredHostName)
            ? configuredHostName
            : "UnknownHost";

        var version = context.Manifest.Version ?? "dev";
        SamplePluginRuntimeStatus.Reset(hostName, version, context.DataDirectory);

        var message =
            $"[{DateTimeOffset.UtcNow:O}] {context.Manifest.Name} initialized in {hostName} (plugin version {version}).";

        try
        {
            File.AppendAllText(
                Path.Combine(context.DataDirectory, "sample-plugin.log"),
                message + Environment.NewLine);
            SamplePluginRuntimeStatus.MarkBackendReady(
                $"Plugin entry initialized successfully. Host: {hostName}; Version: {version}");
        }
        catch (Exception ex)
        {
            SamplePluginRuntimeStatus.MarkBackendFaulted($"Initialization log write failed: {ex.Message}");
            throw;
        }

        _heartbeatService = new SamplePluginHeartbeatService(context.DataDirectory);
        _heartbeatService.Start();

        context.RegisterSettingsPage(new PluginSettingsPageRegistration(
            "status",
            "Plugin Status",
            () => new SamplePluginSettingsView(context)));

        context.RegisterDesktopComponent(new PluginDesktopComponentRegistration(
            "LanMountainDesktop.SamplePlugin.StatusClock",
            "Sample Plugin Status Clock",
            widgetContext => new SamplePluginStatusClockWidget(widgetContext),
            iconKey: "PuzzlePiece",
            category: "Plugins",
            minWidthCells: 4,
            minHeightCells: 4,
            allowDesktopPlacement: true,
            allowStatusBarPlacement: false,
            resizeMode: PluginDesktopComponentResizeMode.Proportional,
            cornerRadiusResolver: cellSize => Math.Clamp(cellSize * 0.34, 18, 34)));
    }

    public void Dispose()
    {
        _heartbeatService?.Dispose();
        _heartbeatService = null;
    }
}
