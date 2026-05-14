using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Services;
using LanMountainDesktop.Shared.IPC;
using LanMountainDesktop.Shared.IPC.Abstractions.Services;
using LanMountainDesktop.Views.Components;

namespace LanMountainDesktop.AirAppHost;

public sealed partial class AirAppWindow : Window
{
    private readonly AirAppLaunchOptions _options;
    private readonly AirAppWindowDescriptor _descriptor;
    private string _instanceKey = string.Empty;

    public AirAppWindow()
        : this(AirAppLaunchOptions.Parse([]))
    {
    }

    public AirAppWindow(AirAppLaunchOptions options)
    {
        _options = options;
        _descriptor = AirAppWindowDescriptor.Create(options);
        InitializeComponent();
        ConfigureWindow();
    }

    private void ConfigureWindow()
    {
        ApplyWindowDescriptor(_descriptor);

        if (string.Equals(_options.AppId, AirAppLaunchOptions.WorldClockAppId, StringComparison.OrdinalIgnoreCase))
        {
            ContentHost.Content = new WorldClockAirAppView(_options);
            return;
        }

        if (string.Equals(_options.AppId, AirAppLaunchOptions.WhiteboardAppId, StringComparison.OrdinalIgnoreCase))
        {
            ConfigureWhiteboardWindow();
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
        TitleTextBlock.Text = descriptor.TitleText;
        SubtitleTextBlock.Text = descriptor.SubtitleText;
        Width = descriptor.Width;
        Height = descriptor.Height;
        MinWidth = descriptor.MinWidth;
        MinHeight = descriptor.MinHeight;
        ShowInTaskbar = descriptor.ShowInTaskbar;
        CanResize = descriptor.CanResize;
        WindowDecorations = WindowDecorations.None;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = -1;

        TitleBar.IsVisible = true;
        Grid.SetRow(ContentHost, 1);
        Grid.SetRowSpan(ContentHost, 1);
        WindowState = WindowState.Normal;

        switch (descriptor.ChromeMode)
        {
            case AirAppWindowChromeMode.Standard:
                break;

            case AirAppWindowChromeMode.Borderless:
                HideCustomTitleBar();
                break;

            case AirAppWindowChromeMode.FullScreen:
                HideCustomTitleBar();
                WindowShell.CornerRadius = new Avalonia.CornerRadius(0);
                WindowShell.BorderThickness = new Avalonia.Thickness(0);
                WindowShell.BoxShadow = default;
                WindowState = WindowState.FullScreen;
                break;

            case AirAppWindowChromeMode.Tool:
                ShowInTaskbar = false;
                CanResize = false;
                break;

            case AirAppWindowChromeMode.BackgroundOnly:
                // Reserved for future background-only Air APPs. Keep a normal window for now
                // so accidental launches remain visible and debuggable.
                break;
        }
    }

    private void HideCustomTitleBar()
    {
        TitleBar.IsVisible = false;
        Grid.SetRow(ContentHost, 0);
        Grid.SetRowSpan(ContentHost, 2);
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
        widget.SetComponentPlacementContext(componentId, _options.SourcePlacementId);
        widget.SetSurfaceMode(
            WhiteboardWidgetSurfaceMode.AirApp,
            () =>
            {
                widget.ForceSaveNote();
                Close();
            });

        ContentHost.Content = widget;
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

    protected override void OnClosed(EventArgs e)
    {
        _ = UnregisterWithLauncherAsync();
        base.OnClosed(e);
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
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

        var componentId = string.IsNullOrWhiteSpace(_options.SourceComponentId)
            ? "none"
            : _options.SourceComponentId.Trim();
        var placementId = string.IsNullOrWhiteSpace(_options.SourcePlacementId)
            ? "none"
            : _options.SourcePlacementId.Trim();
        return $"{_options.AppId}:{componentId}:{placementId}";
    }
}
