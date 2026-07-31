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
            var vm = new MainWindowViewModel(installService, privacyIdentity, consentStore);

            // Task 2: 解析 --install-path 参数（提权重启后由管理员进程传入）
            var installPath = MainWindowViewModel.ParseInstallPath(desktop.Args);
            if (!string.IsNullOrWhiteSpace(installPath))
            {
                vm.InstallPath = installPath;
            }

            var mainWindow = new MainWindow
            {
                DataContext = vm
            };
            desktop.MainWindow = mainWindow;
            mainWindow.Show();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
