using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System;
using System.Linq;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using LanMountainDesktop.Services;
using LanMountainDesktop.ViewModels;
using LanMountainDesktop.Views;
using AvaloniaWebView;

namespace LanMountainDesktop;

public partial class App : Application
{
    private readonly AppSettingsService _appSettingsService = new();
    private readonly LocalizationService _localizationService = new();

    private SettingsWindow? _traySettingsWindow;
    private TrayIcons? _trayIcons;
    private PluginRuntimeService? _pluginRuntimeService;

    public PluginRuntimeService? PluginRuntimeService => _pluginRuntimeService;

    public override void Initialize()
    {
        AppLogger.Info("App", "Initializing application resources.");
        ConfigureWebViewUserDataFolder();
        AvaloniaWebViewBuilder.Initialize(default);
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        AppLogger.Info("App", "Framework initialization completed.");
        LinuxDesktopEntryInstaller.EnsureInstalled();
        InitializePluginRuntime();
        AppSettingsService.SettingsSaved += OnAppSettingsSaved;
        InitializeTrayIcon();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit.
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;
            desktop.Exit += (_, _) =>
            {
                AppLogger.Info("App", "Desktop lifetime exit triggered.");
                AppSettingsService.SettingsSaved -= OnAppSettingsSaved;
                DisposeTrayIcon();
            };
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(),
            };
            AppLogger.Info("App", $"Main window created. LogFile={AppLogger.LogFilePath}");
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnTrayExitClick(object? sender, EventArgs e)
    {
        DisposeTrayIcon();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    private void OnTraySettingsClick(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (_traySettingsWindow is { } existingWindow && existingWindow.IsVisible)
                {
                    existingWindow.WindowState = Avalonia.Controls.WindowState.Normal;
                    existingWindow.Activate();
                    return;
                }

                var settingsWindow = new SettingsWindow();
                settingsWindow.Closed += (_, _) =>
                {
                    if (ReferenceEquals(_traySettingsWindow, settingsWindow))
                    {
                        _traySettingsWindow = null;
                    }
                };

                _traySettingsWindow = settingsWindow;
                settingsWindow.Show();
                settingsWindow.Activate();
            }
            catch (Exception ex)
            {
                AppLogger.Warn("TraySettings", "Failed to open settings window.", ex);
            }
        }, DispatcherPriority.Normal);
    }

    private void OnTrayRestartClick(object? sender, EventArgs e)
    {
        AppRestartService.TryRestartApplication();
    }

    private void OnAppSettingsSaved(string _)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_trayIcons is not null)
            {
                InitializeTrayIcon();
            }
        }, DispatcherPriority.Background);
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }

    private static void ConfigureWebViewUserDataFolder()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        const string userDataFolderEnvVar = "WEBVIEW2_USER_DATA_FOLDER";
        try
        {
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(userDataFolderEnvVar)))
            {
                return;
            }

            var userDataFolder = WebView2RuntimeProbe.ResolveUserDataFolder();
            Environment.SetEnvironmentVariable(
                userDataFolderEnvVar,
                userDataFolder,
                EnvironmentVariableTarget.Process);
        }
        catch (Exception ex)
        {
            // Keep startup resilient if user profile folders are unavailable.
            AppLogger.Warn("WebView2", "Failed to configure WebView2 user data folder.", ex);
        }
    }

    private void InitializePluginRuntime()
    {
        try
        {
            _pluginRuntimeService?.Dispose();
            _pluginRuntimeService = new PluginRuntimeService();
            _pluginRuntimeService.LoadInstalledPlugins();
        }
        catch (Exception ex)
        {
            AppLogger.Warn("PluginRuntime", "Failed to initialize plugin runtime.", ex);
        }
    }

    private void InitializeTrayIcon()
    {
        try
        {
            DisposeTrayIcon();

            using var iconStream = AssetLoader.Open(new Uri("avares://LanMountainDesktop/Assets/avalonia-logo.ico"));
            var trayIcon = new TrayIcon
            {
                Icon = new WindowIcon(iconStream),
                ToolTipText = L("tray.tooltip", "LanMountainDesktop"),
                Menu = BuildTrayMenu(),
                IsVisible = true
            };

            _trayIcons = [trayIcon];
            TrayIcon.SetIcons(this, _trayIcons);
        }
        catch (Exception ex)
        {
            AppLogger.Warn("TrayIcon", "Failed to initialize tray icon.", ex);
        }
    }

    private NativeMenu BuildTrayMenu()
    {
        var menu = new NativeMenu();

        var settingsItem = new NativeMenuItem(L("tray.menu.settings", "设置"));
        settingsItem.Click += OnTraySettingsClick;
        menu.Items.Add(settingsItem);

        menu.Items.Add(new NativeMenuItemSeparator());

        var restartItem = new NativeMenuItem(L("tray.menu.restart", "重启应用"));
        restartItem.Click += OnTrayRestartClick;
        menu.Items.Add(restartItem);

        menu.Items.Add(new NativeMenuItemSeparator());

        var exitItem = new NativeMenuItem(L("tray.menu.exit", "退出应用"));
        exitItem.Click += OnTrayExitClick;
        menu.Items.Add(exitItem);

        return menu;
    }

    private void DisposeTrayIcon()
    {
        if (_trayIcons is null)
        {
            return;
        }

        TrayIcon.SetIcons(this, null);
        foreach (var trayIcon in _trayIcons)
        {
            trayIcon.Dispose();
        }

        _trayIcons = null;
    }

    private string L(string key, string fallback)
    {
        var snapshot = _appSettingsService.Load();
        var languageCode = _localizationService.NormalizeLanguageCode(snapshot.LanguageCode);
        return _localizationService.GetString(languageCode, key, fallback);
    }
}
