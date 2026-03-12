using LanMountainDesktop.PluginSdk;

namespace LanMountainDesktop.SamplePlugin;

[PluginEntrance]
public sealed class SamplePlugin : PluginBase, IDisposable
{
    private SamplePluginRuntimeStateService? _stateService;
    private SamplePluginClockService? _clockService;

    public override void Initialize(IPluginContext context)
    {
        Directory.CreateDirectory(context.DataDirectory);
        var localizer = PluginLocalizer.Create(context);

        var hostName = GetHostProperty(context, PluginHostPropertyKeys.HostApplicationName, "UnknownHost");
        var hostVersion = GetHostProperty(context, PluginHostPropertyKeys.HostVersion, "UnknownVersion");
        var sdkApiVersion = GetHostProperty(context, PluginHostPropertyKeys.PluginSdkApiVersion, "UnknownApiVersion");
        var messageBus = context.GetService<IPluginMessageBus>()
            ?? throw new InvalidOperationException("Plugin message bus is not available.");

        _stateService = new SamplePluginRuntimeStateService(
            context.Manifest,
            context.PluginDirectory,
            context.DataDirectory,
            hostName,
            hostVersion,
            sdkApiVersion,
            messageBus,
            localizer);
        context.RegisterService(_stateService);

        _clockService = new SamplePluginClockService(context.DataDirectory, _stateService, messageBus, localizer);
        context.RegisterService(_clockService);
        _stateService.AttachClockService(_clockService);

        var logPath = Path.Combine(context.DataDirectory, "sample-plugin.log");
        var initMessage =
            $"[{DateTimeOffset.UtcNow:O}] {context.Manifest.Name} initialized in {hostName} (plugin version {context.Manifest.Version ?? "dev"}).";

        try
        {
            File.AppendAllText(logPath, initMessage + Environment.NewLine);
            _stateService.MarkBackendReady(localizer.Format(
                "status.backend.detail.log_written",
                "Initialization log written: {0}",
                logPath));
        }
        catch (Exception ex)
        {
            _stateService.MarkBackendFaulted(localizer.Format(
                "status.backend.detail.log_write_failed",
                "Initialization log failed: {0}",
                ex.Message));
            throw;
        }

        _clockService.Start();

        context.RegisterDesktopComponent(new PluginDesktopComponentRegistration(
            "LanMountainDesktop.SamplePlugin.StatusClock",
            localizer.GetString("widget.display_name", "Sample Plugin Status Clock"),
            widgetContext => new SamplePluginStatusClockWidget(widgetContext),
            iconKey: "PuzzlePiece",
            category: localizer.GetString("widget.category", "Plugins"),
            minWidthCells: 4,
            minHeightCells: 4,
            allowDesktopPlacement: true,
            allowStatusBarPlacement: false,
            resizeMode: PluginDesktopComponentResizeMode.Proportional,
            cornerRadiusResolver: cellSize => Math.Clamp(cellSize * 0.34, 18, 34)));

        context.RegisterDesktopComponent(new PluginDesktopComponentRegistration(
            "LanMountainDesktop.SamplePlugin.CloseDesktop",
            localizer.GetString("widget.close_desktop.display_name", "Close Desktop"),
            widgetContext => new SamplePluginCloseDesktopWidget(widgetContext),
            iconKey: "DismissCircle",
            category: localizer.GetString("widget.category", "Plugins"),
            minWidthCells: 2,
            minHeightCells: 1,
            allowDesktopPlacement: true,
            allowStatusBarPlacement: false,
            resizeMode: PluginDesktopComponentResizeMode.Free,
            cornerRadiusResolver: cellSize => Math.Clamp(cellSize * 0.28, 14, 22)));
    }

    public void Dispose()
    {
        _clockService?.Dispose();
        _clockService = null;
        _stateService = null;
    }

    private static string GetHostProperty(IPluginContext context, string key, string fallback)
    {
        return context.TryGetProperty<string>(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;
    }
}
