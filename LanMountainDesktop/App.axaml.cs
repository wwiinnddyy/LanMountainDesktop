using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System;
using System.Linq;
using Avalonia.Markup.Xaml;
using LanMountainDesktop.Services;
using LanMountainDesktop.ViewModels;
using LanMountainDesktop.Views;
using AvaloniaWebView;

namespace LanMountainDesktop;

public partial class App : Application
{
    public override void Initialize()
    {
        ConfigureWebViewUserDataFolder();
        AvaloniaWebViewBuilder.Initialize(default);
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(),
            };
        }

        base.OnFrameworkInitializationCompleted();
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
