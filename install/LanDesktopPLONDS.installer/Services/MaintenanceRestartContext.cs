using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace LanDesktopPLONDS.Installer.Services;

internal sealed class MaintenanceRestartContext : IDisposable
{
    private readonly SafeAccessTokenHandle? _token;
    private MaintenanceRestartContext(SafeAccessTokenHandle? token) => _token = token;

    internal static MaintenanceRestartContext Capture(Process parent)
    {
        if (!OperatingSystem.IsWindows() || !InstallerElevation.IsRunningElevated()) return new(null);
        if (!OpenProcessToken(parent.Handle, 0x0002 | 0x0008, out var original)) return new(null);
        using (original)
            return DuplicateTokenEx(original, 0x02000000, IntPtr.Zero, 2, 1, out var token) ? new(token) : new(null);
    }

    internal void Restart(string executable, string directory)
    {
        if (_token is null)
        {
            if (OperatingSystem.IsWindows() && InstallerElevation.IsRunningElevated())
            {
                InstallerStartupDiagnostics.Log("Update completed. Restart the desktop from its normal shortcut; the original user token was unavailable.");
                return;
            }
            Process.Start(new ProcessStartInfo(executable) { WorkingDirectory = directory, UseShellExecute = true });
            return;
        }
        var startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfo>() };
        if (!CreateProcessWithTokenW(_token, 1, executable, null, 0, IntPtr.Zero, directory, ref startup, out var information))
        {
            InstallerStartupDiagnostics.Log($"Update completed, but restarting as the original user failed: {Marshal.GetLastWin32Error()}.");
            return;
        }
        CloseHandle(information.Process);
        CloseHandle(information.Thread);
    }

    public void Dispose() => _token?.Dispose();

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public int X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        public short ShowWindow, Reserved2Length;
        public IntPtr Reserved2, Input, Output, Error;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation { public IntPtr Process, Thread; public int ProcessId, ThreadId; }

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr process, int access, out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateTokenEx(SafeAccessTokenHandle original, int access, IntPtr attributes, int level, int type, out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessWithTokenW(SafeAccessTokenHandle token, int flags, string executable, string? command,
        int creationFlags, IntPtr environment, string directory, ref StartupInfo startup, out ProcessInformation information);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
