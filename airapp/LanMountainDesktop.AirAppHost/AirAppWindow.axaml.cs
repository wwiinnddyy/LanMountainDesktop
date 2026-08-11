using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using FluentAvalonia.UI.Windowing;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.Shared.IPC;
using LanMountainDesktop.Shared.IPC.Abstractions.Services;
using LanMountainDesktop.Views.Components;

namespace LanMountainDesktop.AirAppHost;

public sealed partial class AirAppWindow : FAAppWindow
{
    private readonly AirAppLaunchOptions _options;
    private readonly AirAppWindowDescriptor _descriptor;
    private WhiteboardWidget? _whiteboardWidget;
    private string _instanceKey = string.Empty;

    public AirAppWindow()
        : this(AirAppLaunchOptions.Parse([]))
    {
    }

    public AirAppWindow(AirAppLaunchOptions options)
    {
        _options = options;
        _descriptor = LocalizeDescriptor(AirAppWindowDescriptor.Create(options), options);
        InitializeComponent();
        ConfigureWindow();
    }

    private static AirAppWindowDescriptor LocalizeDescriptor(
        AirAppWindowDescriptor descriptor,
        AirAppLaunchOptions options)
    {
        if (!string.Equals(options.AppId, AirAppLaunchOptions.RssReaderAppId, StringComparison.OrdinalIgnoreCase))
            return descriptor;

        var localization = new LocalizationService();
        string languageCode;
        try
        {
            languageCode = localization.NormalizeLanguageCode(new AppSettingsService().Load().LanguageCode);
        }
        catch
        {
            languageCode = "zh-CN";
        }

        var title = localization.GetString(languageCode, "component.rss_reader", "RSS Reader");
        var airApp = localization.GetString(languageCode, "rss.air_app", "Air APP");
        return descriptor with
        {
            WindowTitle = $"{title} - {airApp}",
            TitleBarTitle = title,
            TitleBarSubtitle = airApp
        };
    }

    private void ConfigureWindow()
    {
        ApplyWindowDescriptor(_descriptor);

        if (string.Equals(_options.AppId, AirAppLaunchOptions.WorldClockAppId, StringComparison.OrdinalIgnoreCase))
        {
            ContentHost.Content = new ClockAirAppView(_options);
            return;
        }

        if (string.Equals(_options.AppId, AirAppLaunchOptions.WhiteboardAppId, StringComparison.OrdinalIgnoreCase))
        {
            ConfigureWhiteboardWindow();
            return;
        }

        if (string.Equals(_options.AppId, AirAppLaunchOptions.RssReaderAppId, StringComparison.OrdinalIgnoreCase))
        {
            ContentHost.Content = new RssReaderAirAppView(_options);
            return;
        }

        ContentHost.Content = new TextBlock
        {
            Text = $"Unsupported Air APP: {_options.AppId}",
            Margin = new Avalonia.Thickness(18)
        };
    }

    private void ApplyWindowDescriptor(AirAppWindowDescriptor descriptor)
    {
        Title = descriptor.Title;
        Width = descriptor.Width;
        Height = descriptor.Height;
        MinWidth = descriptor.MinWidth;
        MinHeight = descriptor.MinHeight;
        ShowInTaskbar = descriptor.ShowInTaskbar;
        CanResize = descriptor.CanResize;
        ShowAsDialog = descriptor.ShowAsDialog;
        WindowState = WindowState.Normal;
        WindowRoot.Background = this.TryFindResource("AirAppWindowBackgroundBrush", out var brush) && brush is IBrush backgroundBrush
            ? backgroundBrush
            : Brushes.White;
        ConfigureTitleBar(descriptor);

        switch (descriptor.ChromeMode)
        {
            case AirAppWindowChromeMode.Standard:
                WindowDecorations = WindowDecorations.Full;
                TitleBar.ExtendsContentIntoTitleBar = false;
                break;

            case AirAppWindowChromeMode.Borderless:
                WindowDecorations = WindowDecorations.None;
                TitleBar.ExtendsContentIntoTitleBar = true;
                break;

            case AirAppWindowChromeMode.FullScreen:
                WindowDecorations = WindowDecorations.None;
                TitleBar.ExtendsContentIntoTitleBar = true;
                ShowAsDialog = false;
                WindowState = WindowState.FullScreen;
                break;

            case AirAppWindowChromeMode.Tool:
                WindowDecorations = WindowDecorations.Full;
                TitleBar.ExtendsContentIntoTitleBar = false;
                ShowInTaskbar = false;
                CanResize = false;
                break;

            case AirAppWindowChromeMode.BackgroundOnly:
                // Reserved for future background-only Air APPs. Keep a normal window for now
                // so accidental launches remain visible and debuggable.
                break;
        }
    }

    private void ConfigureTitleBar(AirAppWindowDescriptor descriptor)
    {
        TitleBar.Height = descriptor.ChromeMode == AirAppWindowChromeMode.Tool ? 36 : 40;
        TitleBar.BackgroundColor = Colors.Transparent;
        TitleBar.ForegroundColor = Color.FromRgb(32, 32, 32);
        TitleBar.InactiveBackgroundColor = Colors.Transparent;
        TitleBar.InactiveForegroundColor = Color.FromRgb(96, 96, 96);
        TitleBar.ButtonBackgroundColor = Colors.Transparent;
        TitleBar.ButtonHoverBackgroundColor = Color.FromArgb(23, 0, 0, 0);
        TitleBar.ButtonPressedBackgroundColor = Color.FromArgb(52, 0, 0, 0);
        TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        TitleBar.ButtonInactiveForegroundColor = Colors.Gray;
    }

    private void ConfigureWhiteboardWindow()
    {
        var componentId = string.IsNullOrWhiteSpace(_options.SourceComponentId)
            ? BuiltInComponentIds.DesktopWhiteboard
            : _options.SourceComponentId.Trim();
        var baseWidthCells = string.Equals(componentId, BuiltInComponentIds.DesktopBlackboardLandscape, StringComparison.OrdinalIgnoreCase)
            ? 4
            : 2;
        var widget = new WhiteboardWidget(baseWidthCells);
        _whiteboardWidget = widget;
        widget.SetComponentPlacementContext(componentId, _options.SourcePlacementId);
        widget.SetSurfaceMode(
            WhiteboardWidgetSurfaceMode.AirApp,
            () =>
            {
                widget.ForceSaveNote();
                Close();
            });

        ContentHost.Content = widget;
        AppLogger.Info(
            "AirAppWindow",
            $"Whiteboard content created. ComponentId='{componentId}'; PlacementId='{_options.SourcePlacementId ?? string.Empty}'.");
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _ = RegisterWithLauncherAsync();
        AppLogger.Info(
            "AirAppWindow",
            $"Opened. WindowRole=AirApp; AppId='{_options.AppId}'; ForegroundActivationRequested=True.");
        Dispatcher.UIThread.Post(() =>
        {
            Activate();
        }, DispatcherPriority.Background);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        SaveWhiteboard();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        SaveAndDisposeWhiteboard();
        _ = UnregisterWithLauncherAsync();
        base.OnClosed(e);
    }

    private void SaveAndDisposeWhiteboard()
    {
        var widget = _whiteboardWidget;
        if (widget is null)
        {
            return;
        }

        SaveWhiteboard();
        if (ContentHost.Content == widget)
        {
            ContentHost.Content = null;
        }

        widget.Dispose();
        _whiteboardWidget = null;
    }

    private void SaveWhiteboard()
    {
        if (_whiteboardWidget is null)
        {
            return;
        }

        try
        {
            _whiteboardWidget.ForceSaveNote();
        }
        catch (Exception ex)
        {
            AppLogger.Warn("AirAppWindow", "Failed to force-save whiteboard before closing Air APP.", ex);
        }
    }

    private async Task RegisterWithLauncherAsync()
    {
        if (string.IsNullOrWhiteSpace(_options.LauncherPipeName))
        {
            return;
        }

        _instanceKey = ResolveInstanceKey();
        try
        {
            using var client = new LanMountainDesktopIpcClient();
            await client.ConnectAsync(_options.LauncherPipeName).ConfigureAwait(false);
            var proxy = client.CreateProxy<IAirAppLifecycleService>();
            _ = await proxy.RegisterAsync(new AirAppRegistrationRequest(
                _instanceKey,
                _options.AppId,
                _options.SessionId,
                Environment.ProcessId,
                Title ?? "Air APP",
                _options.SourceComponentId,
                _options.SourcePlacementId)).ConfigureAwait(false);
        }
        catch
        {
            // Registration is best-effort; Launcher also tracks the process it started.
        }
    }

    private async Task UnregisterWithLauncherAsync()
    {
        if (string.IsNullOrWhiteSpace(_options.LauncherPipeName))
        {
            return;
        }

        var instanceKey = string.IsNullOrWhiteSpace(_instanceKey) ? ResolveInstanceKey() : _instanceKey;
        try
        {
            using var client = new LanMountainDesktopIpcClient();
            await client.ConnectAsync(_options.LauncherPipeName).ConfigureAwait(false);
            var proxy = client.CreateProxy<IAirAppLifecycleService>();
            _ = await proxy.UnregisterAsync(instanceKey, Environment.ProcessId).ConfigureAwait(false);
        }
        catch
        {
            // Unregister is best-effort; Launcher prunes dead processes.
        }
    }

    private string ResolveInstanceKey()
    {
        if (!string.IsNullOrWhiteSpace(_options.InstanceKey))
        {
            return _options.InstanceKey.Trim();
        }

        if (string.Equals(_options.AppId, AirAppLaunchOptions.WorldClockAppId, StringComparison.OrdinalIgnoreCase))
        {
            return $"{AirAppLaunchOptions.WorldClockAppId}:clock-suite:global";
        }

        if (string.Equals(_options.AppId, AirAppLaunchOptions.RssReaderAppId, StringComparison.OrdinalIgnoreCase))
        {
            return $"{AirAppLaunchOptions.RssReaderAppId}:global";
        }

        var componentId = string.IsNullOrWhiteSpace(_options.SourceComponentId)
            ? "none"
            : _options.SourceComponentId.Trim();
        var placementId = string.IsNullOrWhiteSpace(_options.SourcePlacementId)
            ? "none"
            : _options.SourcePlacementId.Trim();
        return $"{_options.AppId}:{componentId}:{placementId}";
    }
}
