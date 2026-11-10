using Aprillz.MewUI;
using LanDesktop.MewInstaller.Installer;
using LanDesktopPLONDS.Installer;
using LanDesktopPLONDS.Installer.Services;
using LanMountainDesktop.Shared.Contracts.Privacy;

namespace LanDesktop.MewInstaller;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        InstallerStartupDiagnostics.Log($"MewInstaller starting. args=[{string.Join(' ', args)}]");

        var installPathArg = ParseOption(args, "--install-path");
        var localZipArg = ParseOption(args, "--local", hasValue: true);
        var isLocalMode = HasFlag(args, "--local");

        var privacyIdentity = new PrivacyDeviceIdentityProvider();
        IOnlineInstallService installService;
        string modeLabel;
        string? localZipPath = null;

        if (isLocalMode)
        {
            localZipPath = localZipArg ?? Path.Combine(AppContext.BaseDirectory, "Files.zip");
            installService = new LocalInstallService(localZipPath);
            modeLabel = "本地包安装程序";
        }
        else
        {
            installService = OnlineInstallService.CreateDefault(privacyIdentity);
            modeLabel = "在线安装程序";
        }

        var state = new InstallerState(
            installService,
            privacyIdentity,
            new InstallerPrivacyConsentStore(),
            modeLabel,
            isLocalMode,
            localZipPath);

        if (!string.IsNullOrWhiteSpace(installPathArg))
        {
            state.InstallPath.Value = installPathArg;
        }

        if (isLocalMode && !File.Exists(localZipPath))
        {
            state.ErrorMessage.Value = $"未找到本地安装包：{localZipPath}。请确认 Files.zip 与安装程序在同一目录。";
        }

        Application
            .Create()
            .UseWin32()
            .UseDirect2D()
            .UseTheme(ThemeVariant.System)
            .BuildMainWindow(() => new MainWindow(state))
            .Run();

        state.Dispose();
        return 0;
    }

    private static bool HasFlag(string[] args, string name) =>
        args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>读取 <c>--name value</c> 形式的选项；值可为空串。</summary>
    private static string? ParseOption(string[] args, string name, bool hasValue = false)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (i + 1 < args.Length && (!hasValue || !args[i + 1].StartsWith("--", StringComparison.Ordinal)))
            {
                var value = args[i + 1].Trim('"');
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }

            return hasValue ? null : string.Empty;
        }

        return null;
    }
}
