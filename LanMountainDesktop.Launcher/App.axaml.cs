using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LanMountainDesktop.Launcher.Models;
using LanMountainDesktop.Launcher.Shell;
using LanMountainDesktop.Launcher.Shell.EntryHandlers;
using LanMountainDesktop.Launcher.Views;

namespace LanMountainDesktop.Launcher;

public partial class App : Application
{
    public override void Initialize()
    {
        if (Design.IsDesignMode)
        {
            AvaloniaXamlLoader.Load(this);
            return;
        }

        Logger.Initialize();
        var context = LauncherRuntimeContext.Current;
        var execution = LauncherExecutionContext.Capture();
        Logger.Info(
            $"Launcher App initialize. Command='{context.Command}'; IsGuiMode={context.IsGuiCommand}; " +
            $"IsPreview={context.IsPreviewCommand}; IsDebugMode={context.IsDebugMode}; " +
            $"LaunchSource='{context.LaunchSource}'; IsElevated={execution.IsElevated}; " +
            $"UserSid='{execution.UserSid ?? string.Empty}'; ExplicitAppRoot='{context.ExplicitAppRoot ?? "<none>"}'.");

        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (Design.IsDesignMode)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var context = LauncherRuntimeContext.Current;
        var execution = LauncherExecutionContext.Capture();
        Logger.Info(
            $"Framework initialization completed. Command='{context.Command}'; IsPreview={context.IsPreviewCommand}; " +
            $"IsDebugMode={context.IsDebugMode}; LaunchSource='{context.LaunchSource}'; " +
            $"IsElevated={execution.IsElevated}; UserSid='{execution.UserSid ?? string.Empty}'.");

        if (PreviewEntryHandler.TryHandle(context, desktop))
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        if (context.IsAirAppBrokerCommand)
        {
            _ = AirAppBrokerEntryHandler.RunAsync(desktop, context);
            base.OnFrameworkInitializationCompleted();
            return;
        }

        if (context.IsDebugMode && !context.IsPreviewCommand)
        {
            Logger.Info("Debug mode active; showing DevDebugWindow instead of normal launch flow.");
            new DevDebugWindow().Show();
            base.OnFrameworkInitializationCompleted();
            return;
        }

        var splashWindow = LaunchEntryHandler.CreateSplashWindow();
        splashWindow.Show();
        _ = LauncherCompositionRoot.RunOrchestratorWithSplashAsync(desktop, context, splashWindow);

        base.OnFrameworkInitializationCompleted();
    }
}
