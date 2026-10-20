using System.Runtime.InteropServices;

namespace LanMountainDesktop.Shared.Contracts.Deployment;

internal static class DeploymentPlatform
{
    internal static string ArchitectureToken(Architecture architecture) => architecture switch
    {
        Architecture.X86 => "x86",
        Architecture.Arm64 => "arm64",
        _ => "x64"
    };

    internal static string Current => (OperatingSystem.IsWindows() ? "windows" :
        OperatingSystem.IsLinux() ? "linux" : OperatingSystem.IsMacOS() ? "macos" : "unknown") +
        "-" + ArchitectureToken(RuntimeInformation.ProcessArchitecture);
}
