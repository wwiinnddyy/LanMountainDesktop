using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System;
using System.Diagnostics;
using System.Linq;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using LanMountainDesktop.Services;
using LanMountainDesktop.ViewModels;
using LanMountainDesktop.Views;
using AvaloniaWebView;

namespace LanMountainDesktop;

public partial class App : Application
{
    private SettingsWindow? _traySettingsWindow;

    public override void Initialize()
    {
        ConfigureWebViewUserDataFolder();
        AvaloniaWebViewBuilder.Initialize(default);
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        LinuxDesktopEntryInstaller.EnsureInstalled();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnTrayExitClick(object? sender, EventArgs e)
    {
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
                Debug.WriteLine($"[TraySettings] Failed to open settings window: {ex}");
            }
        }, DispatcherPriority.Normal);
    }

    private void OnTrayRestartClick(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return;
        }

        if (TryStartCurrentProcess())
        {
            desktop.Shutdown();
        }
    }

    private static bool TryStartCurrentProcess()
    {
        try
        {
            var args = Environment.GetCommandLineArgs();
            if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
            {
                return false;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = args[0],
                UseShellExecute = false
            };

            for (var i = 1; i < args.Length; i++)
            {
                startInfo.ArgumentList.Add(args[i]);
            }

            Process.Start(startInfo);
            return true;
        }
        catch
        {
            return false;
        }
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
        catch
        {
            // Keep startup resilient if user profile folders are unavailable.
        }
    }
}
