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

        var hostName = GetHostProperty(context, "HostApplicationName", "UnknownHost");
        var hostVersion = GetHostProperty(context, "HostVersion", "UnknownVersion");
        var sdkApiVersion = GetHostProperty(context, "PluginSdkApiVersion", "UnknownApiVersion");
        var messageBus = context.GetService<IPluginMessageBus>()
            ?? throw new InvalidOperationException("Plugin message bus is not available.");

        _stateService = new SamplePluginRuntimeStateService(
            context.Manifest,
            context.PluginDirectory,
            context.DataDirectory,
            hostName,
            hostVersion,
            sdkApiVersion,
            messageBus);
        context.RegisterService(_stateService);

        _clockService = new SamplePluginClockService(context.DataDirectory, _stateService, messageBus);
        context.RegisterService(_clockService);
        _stateService.AttachClockService(_clockService);

        var logPath = Path.Combine(context.DataDirectory, "sample-plugin.log");
        var initMessage =
            $"[{DateTimeOffset.UtcNow:O}] {context.Manifest.Name} initialized in {hostName} (plugin version {context.Manifest.Version ?? "dev"}).";

        try
        {
            File.AppendAllText(logPath, initMessage + Environment.NewLine);
            _stateService.MarkBackendReady($"Initialization log written to {logPath}.");
        }
        catch (Exception ex)
        {
            _stateService.MarkBackendFaulted($"Initialization log write failed: {ex.Message}");
            throw;
        }

        _clockService.Start();

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
