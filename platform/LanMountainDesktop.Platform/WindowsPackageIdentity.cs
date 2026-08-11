using System.Runtime.InteropServices;
using System.Text;

namespace LanMountainDesktop.Platform.Windows;

/// <summary>
/// Windows 包标识查询（MSIX/UWP 打包检测）。
/// </summary>
public static class WindowsPackageIdentity
{
    private const int AppmodelErrorNoPackage = 15700;

    /// <summary>
    /// 检测当前进程是否具有包标识（以 MSIX 打包运行）。
    /// 非 Windows 平台返回 false。
    /// </summary>
    public static bool HasPackageIdentity()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var length = 0;
        var hr = GetCurrentPackageFullName(ref length, null);
        if (hr == AppmodelErrorNoPackage)
        {
            return false;
        }

        if (length <= 0)
        {
            return hr == 0;
        }

        var builder = new StringBuilder(length);
        hr = GetCurrentPackageFullName(ref length, builder);
        return hr == 0;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, StringBuilder? packageFullName);
}
