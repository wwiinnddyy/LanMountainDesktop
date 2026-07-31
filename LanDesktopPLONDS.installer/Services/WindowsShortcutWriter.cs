using System.Runtime.InteropServices;
using System.Text;

namespace LanDesktopPLONDS.Installer.Services;

/// <summary>
/// NativeAOT 安全的 .lnk 快捷方式创建工具。
/// 通过直接操作 COM vtable 函数指针调用 IShellLinkW/IPersistFile 接口，
/// 完全兼容 NativeAOT 编译（不依赖 ComImport 或源生成 COM 包装器）。
/// 运行时失败时回退到 .url 格式。
/// </summary>
internal static partial class WindowsShortcutWriter
{
    // CLSID_ShellLink = {00021401-0000-0000-C000-000000000046}
    private static readonly Guid s_clsidShellLink = new("00021401-0000-0000-C000-000000000046");
    // IID_IShellLinkW = {000214F9-0000-0000-C000-000000000046}
    private static readonly Guid s_iidShellLinkW = new("000214F9-0000-0000-C000-000000000046");
    // IID_IPersistFile = {0000010b-0000-0000-C000-000000000046}
    private static readonly Guid s_iidPersistFile = new("0000010b-0000-0000-C000-000000000046");

    [LibraryImport("ole32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int CoCreateInstance(
        in Guid rclsid,
        IntPtr pUnkOuter,
        uint dwClsContext,
        in Guid riid,
        out IntPtr ppv);

    /// <summary>
    /// 尝试创建 .lnk 快捷方式文件。COM 互操作失败时回退到 .url 格式。
    /// </summary>
    public static bool TryCreateShortcut(string lnkPath, string targetPath, string workingDirectory, string? iconPath)
    {
        try
        {
            CreateShortcut(lnkPath, targetPath, workingDirectory, iconPath);
            return true;
        }
        catch
        {
            // .lnk 创建失败时回退到 .url 格式，保证快捷方式始终可用
            try
            {
                var urlPath = Path.ChangeExtension(lnkPath, ".url");
                WriteUrlShortcut(urlPath, targetPath);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// 创建 .lnk 快捷方式文件。COM 互操作失败时抛出异常。
    /// </summary>
    public static unsafe void CreateShortcut(string lnkPath, string targetPath, string workingDirectory, string? iconPath)
    {
        var pShellLink = IntPtr.Zero;
        var pPersistFile = IntPtr.Zero;
        try
        {
            const uint CLSCTX_INPROC_SERVER = 0x1;
            var hr = CoCreateInstance(
                in s_clsidShellLink, IntPtr.Zero, CLSCTX_INPROC_SERVER, in s_iidShellLinkW, out pShellLink);
            Marshal.ThrowExceptionForHR(hr);

            // IShellLinkW vtable 布局（IUnknown 占 [0-2]）：
            // [3] GetPath … [7] SetDescription, [8] GetWorkingDirectory,
            // [9] SetWorkingDirectory … [17] SetIconLocation … [20] SetPath
            var vtable = *(nint**)pShellLink;

            // IShellLinkW::SetPath (vtable[20])
            CallComStringMethod(vtable[20], pShellLink, targetPath);

            // IShellLinkW::SetWorkingDirectory (vtable[9])
            CallComStringMethod(vtable[9], pShellLink, workingDirectory);

            // IShellLinkW::SetIconLocation (vtable[17])
            if (!string.IsNullOrEmpty(iconPath))
            {
                CallComStringIntMethod(vtable[17], pShellLink, iconPath, 0);
            }

            // QueryInterface → IPersistFile
            var iidPersistFile = s_iidPersistFile;
            hr = Marshal.QueryInterface(pShellLink, ref iidPersistFile, out pPersistFile);
            Marshal.ThrowExceptionForHR(hr);

            // IPersistFile vtable: [0-2] IUnknown, [3] GetClassID, [4] IsDirty, [5] Load, [6] Save
            var persistVtable = *(nint**)pPersistFile;
            CallComStringBoolMethod(persistVtable[6], pPersistFile, lnkPath, fRemember: true);
        }
        finally
        {
            if (pPersistFile != IntPtr.Zero)
            {
                Marshal.Release(pPersistFile);
            }

            if (pShellLink != IntPtr.Zero)
            {
                Marshal.Release(pShellLink);
            }
        }
    }

    /// <summary>
    /// 通过 vtable 函数指针调用单字符串参数的 COM 方法。
    /// 等效签名：HRESULT Method(LPCWSTR param)。
    /// </summary>
    private static unsafe void CallComStringMethod(nint fnPtr, IntPtr pObj, string? value)
    {
        var pStr = Marshal.StringToCoTaskMemUni(value);
        try
        {
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, nint, int>)fnPtr;
            var hr = fn(pObj, pStr);
            Marshal.ThrowExceptionForHR(hr);
        }
        finally
        {
            Marshal.FreeCoTaskMem(pStr);
        }
    }

    /// <summary>
    /// 通过 vtable 函数指针调用字符串+整数参数的 COM 方法。
    /// 等效签名：HRESULT Method(LPCWSTR param, int value)。
    /// </summary>
    private static unsafe void CallComStringIntMethod(nint fnPtr, IntPtr pObj, string? value, int intValue)
    {
        var pStr = Marshal.StringToCoTaskMemUni(value);
        try
        {
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, nint, int, int>)fnPtr;
            var hr = fn(pObj, pStr, intValue);
            Marshal.ThrowExceptionForHR(hr);
        }
        finally
        {
            Marshal.FreeCoTaskMem(pStr);
        }
    }

    /// <summary>
    /// 通过 vtable 函数指针调用字符串+布尔参数的 COM 方法。
    /// 等效签名：HRESULT Method(LPCWSTR param, BOOL flag)。
    /// </summary>
    private static unsafe void CallComStringBoolMethod(nint fnPtr, IntPtr pObj, string? value, bool fRemember)
    {
        var pStr = Marshal.StringToCoTaskMemUni(value);
        try
        {
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, nint, int, int>)fnPtr;
            var hr = fn(pObj, pStr, fRemember ? 1 : 0);
            Marshal.ThrowExceptionForHR(hr);
        }
        finally
        {
            Marshal.FreeCoTaskMem(pStr);
        }
    }

    /// <summary>
    /// 回退方案：写入 .url 快捷方式文件。
    /// </summary>
    internal static void WriteUrlShortcut(string shortcutPath, string targetPath)
    {
        File.WriteAllText(
            shortcutPath,
            $"[InternetShortcut]{Environment.NewLine}URL=file:///{targetPath.Replace('\\', '/')}{Environment.NewLine}");
    }
}
