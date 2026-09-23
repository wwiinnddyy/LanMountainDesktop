using System.Runtime.InteropServices;

namespace LanMountainDesktop.Services;

/// <summary>
/// 本机平台标识只认这一处。收口前四份：宿主两处 <c>ResolveCurrentPlatform</c>
/// （<c>SettingsDomainServices</c> 与 <c>UpdateOrchestrator</c>，逐字相同的 14 行）各算一遍"os-架构"，
/// 另两处（<c>UpdateManifestMapper</c> 与 <c>GitHubReleaseUpdateService</c> 的 <c>SelectPreferredInstallerAsset</c>）
/// 又各自把"架构 token"那段 <c>switch</c> 抄了一遍——同一个值在一条链上算两次，是"清单挑中哪个安装包"
/// 与"更新清单能不能对上"两边各错一半的那种错法。
///
/// 这套 token 是**对外契约**，不是内部命名：发布资产名里就写着 <c>files-windows-x64.zip</c>
/// （见 <c>PlondsWireFormat.LegacyWindowsX64PackageFileName</c>），所以
/// ① 全小写、② 架构默认档给 <c>x64</c>（不是 <c>amd64</c>、也不是 <c>x86_64</c>）、③ 认不出的系统写
/// <c>unknown</c> 而不是抛——三条都是资产名对得上的前提。要改得连同发布侧一起改，别在这一处单方改口径。
/// </summary>
internal static class PlatformIdentifiers
{
    /// <summary>资产名里的那个架构段：<c>arm64</c> / <c>x86</c>，其余一律落到 <c>x64</c>。</summary>
    public static string ArchitectureToken(Architecture architecture)
    {
        return architecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            _ => "x64"
        };
    }

    public static string CurrentArchitectureToken => ArchitectureToken(RuntimeInformation.OSArchitecture);

    /// <summary>更新清单用的"os-架构"：如 <c>windows-x64</c>。</summary>
    public static string CurrentPlatformId
    {
        get
        {
            var os = OperatingSystem.IsWindows()
                ? "windows"
                : OperatingSystem.IsLinux()
                    ? "linux"
                    : OperatingSystem.IsMacOS()
                        ? "macos"
                        : "unknown";
            return $"{os}-{CurrentArchitectureToken}";
        }
    }
}
