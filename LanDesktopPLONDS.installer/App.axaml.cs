using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LanDesktopPLONDS.Installer.Services;
using LanDesktopPLONDS.Installer.ViewModels;
using LanDesktopPLONDS.Installer.Views;
using LanMountainDesktop.Shared.Contracts.Privacy;

namespace LanDesktopPLONDS.Installer;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var privacyIdentity = new PrivacyDeviceIdentityProvider();
            var installService = OnlineInstallService.CreateDefault(privacyIdentity);
            var consentStore = new InstallerPrivacyConsentStore();
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(installService, privacyIdentity, consentStore)
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
