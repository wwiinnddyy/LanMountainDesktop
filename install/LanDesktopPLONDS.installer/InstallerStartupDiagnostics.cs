using System.Runtime.InteropServices;
using System.Text;

namespace LanDesktopPLONDS.Installer;

internal static class InstallerStartupDiagnostics
{
    private const uint MessageBoxIconError = 0x00000010;
    private const uint MessageBoxOk = 0x00000000;

    private static int _initialized;
    private static int _fatalMessageShown;

    public static string LogPath => Path.Combine(GetLogDirectory(), "startup.log");

    public static void Initialize()
    {
        if (Interlocked.Exchange(ref _initialized, 1) != 0)
        {
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var exception = args.ExceptionObject as Exception;
            ReportFatal("The installer encountered an unhandled startup error.", exception);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            ReportFatal("The installer encountered an unobserved background error.", args.Exception);
            args.SetObserved();
        };

        Log("Startup diagnostics initialized.");
    }

    public static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(GetLogDirectory());
            File.AppendAllText(
                LogPath,
                $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}",
                Encoding.UTF8);
        }
        catch
        {
            // Diagnostics must never become the reason the installer cannot start.
        }
    }

    public static void ReportFatal(string message, Exception? exception)
    {
        Log(exception is null ? message : $"{message}{Environment.NewLine}{exception}");

        if (!OperatingSystem.IsWindows() || Interlocked.Exchange(ref _fatalMessageShown, 1) != 0)
        {
            return;
        }

        try
        {
            var details = exception is null
                ? message
                : $"{message}{Environment.NewLine}{Environment.NewLine}{exception.GetType().Name}: {exception.Message}";
            _ = MessageBox(
                IntPtr.Zero,
                $"{details}{Environment.NewLine}{Environment.NewLine}Log: {LogPath}",
                "LanDesktopPLONDS Installer",
                MessageBoxOk | MessageBoxIconError);
        }
        catch
        {
        }
    }

    private static string GetLogDirectory()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root))
        {
            root = AppContext.BaseDirectory;
        }

        return Path.Combine(root, "LanMountainDesktop", "Installer", "logs");
    }

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}
