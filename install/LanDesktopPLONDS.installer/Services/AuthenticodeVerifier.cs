using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace LanDesktopPLONDS.Installer.Services;

/// <summary>
/// Authenticode 签名验证结果。
/// </summary>
internal enum AuthenticodeStatus
{
    /// <summary>文件具有有效的 Authenticode 签名。</summary>
    Signed,

    /// <summary>文件未签名。</summary>
    Unsigned,

    /// <summary>签名无效或验证过程出错。</summary>
    Invalid
}

/// <summary>
/// Authenticode 验证结果数据。
/// </summary>
internal sealed class AuthenticodeResult
{
    /// <summary>验证状态。</summary>
    public AuthenticodeStatus Status { get; init; }

    /// <summary>签名者主体名称（仅在 Status == Signed 时有效）。</summary>
    public string? SignerSubject { get; init; }

    /// <summary>是否要求强制签名验证。</summary>
    public bool EnforcementEnabled { get; init; }

    public override string ToString() => Status switch
    {
        AuthenticodeStatus.Signed => $"Signed ({SignerSubject ?? "unknown"})",
        AuthenticodeStatus.Unsigned => "Unsigned",
        AuthenticodeStatus.Invalid => "Invalid",
        _ => "Unknown"
    };
}

/// <summary>
/// Windows Authenticode（WinVerifyTrust）签名验证器。
/// 使用 Win32 P/Invoke 验证 PE 文件的 Authenticode 签名，
/// 并通过 X509Certificate 提取签名者信息。
/// 默认为仅报告模式；设置 LANMOUNTAIN_INSTALLER_REQUIRE_SIGNED=1 启用强制验证。
/// </summary>
internal static class AuthenticodeVerifier
{
    private const string RequireSignedEnvVar = "LANMOUNTAIN_INSTALLER_REQUIRE_SIGNED";

    /// <summary>
    /// 当前是否启用了强制签名验证。
    /// </summary>
    public static bool EnforcementEnabled =>
        string.Equals(
            Environment.GetEnvironmentVariable(RequireSignedEnvVar),
            "1",
            StringComparison.Ordinal);

    /// <summary>
    /// 对指定 PE 文件执行 Authenticode 签名验证。
    /// </summary>
    /// <param name="path">要验证的文件路径。</param>
    /// <returns>验证结果，包含状态和签名者信息。</returns>
    public static AuthenticodeResult VerifyFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            InstallerStartupDiagnostics.Log($"[Authenticode] 文件不存在：{path}");
            return new AuthenticodeResult { Status = AuthenticodeStatus.Invalid };
        }

        if (!OperatingSystem.IsWindows())
        {
            InstallerStartupDiagnostics.Log("[Authenticode] 非 Windows 平台，跳过 Authenticode 验证。");
            return new AuthenticodeResult { Status = AuthenticodeStatus.Unsigned };
        }

        // 第一步：通过 Win32 WinVerifyTrust 验证签名有效性
        var trustStatus = WinVerifyTrustNative(path);
        if (trustStatus != 0)
        {
            // TRUST_E_NOSIGNATURE (0x800B0100) = 文件未签名
            if (trustStatus == unchecked((int)0x800B0100))
            {
                InstallerStartupDiagnostics.Log($"[Authenticode] 文件未签名：{path}");
                return new AuthenticodeResult { Status = AuthenticodeStatus.Unsigned };
            }

            InstallerStartupDiagnostics.Log(
                $"[Authenticode] WinVerifyTrust 返回错误 0x{trustStatus:X8}：{path}");
            return new AuthenticodeResult { Status = AuthenticodeStatus.Invalid };
        }

        // 第二步：提取签名者信息
        string? signerSubject = null;
        try
        {
#pragma warning disable SYSLIB0057 // X509Certificate.CreateFromSignedFile 已过时
            using var cert = X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
            signerSubject = cert.Subject;
        }
        catch (CryptographicException ex)
        {
            // WinVerifyTrust 通过但无法读取证书——记录但不视为失败
            InstallerStartupDiagnostics.Log(
                $"[Authenticode] 签名有效但无法读取证书信息：{ex.Message}");
        }

        InstallerStartupDiagnostics.Log(
            $"[Authenticode] 签名验证通过：{path}，签名者={signerSubject ?? "unknown"}");

        return new AuthenticodeResult
        {
            Status = AuthenticodeStatus.Signed,
            SignerSubject = signerSubject
        };
    }

    // WinVerifyTrust 相关常量
    private const int WTD_UI_NONE = 2;
    private const int WTD_REVOKE_NONE = 0;
    private const int WTD_CHOICE_FILE = 1;
    private const int WTD_STATEACTION_VERIFY = 1;
    private const int WTD_STATEACTION_CLOSE = 2;
    private const int WTD_SAFER_FLAG = 0x100;
    private const string WinTrustDll = "wintrust.dll";

    // WINTRUST_ACTION_GENERIC_VERIFY_V2 = {00AAC56B-CD44-11d0-8CC2-00C04FC295EE}
    private static readonly Guid s_winTrustActionGenericVerifyV2 =
        new(0x00AAC56B, 0xCD44, 0x11d0, 0x8C, 0xC2, 0x00, 0xC0, 0x4F, 0xC2, 0x95, 0xEE);

    /// <summary>
    /// 调用 WinVerifyTrust 的简化封装。
    /// </summary>
    private static int WinVerifyTrustNative(string filePath)
    {
        var fileInfo = new WINTRUST_FILE_INFO
        {
            cbStruct = Marshal.SizeOf<WINTRUST_FILE_INFO>(),
            pcwszFilePath = filePath,
            hFile = IntPtr.Zero,
            pgKnownSubject = IntPtr.Zero
        };

        var pFileInfo = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
        try
        {
            Marshal.StructureToPtr(fileInfo, pFileInfo, false);

            var data = new WINTRUST_DATA
            {
                cbStruct = Marshal.SizeOf<WINTRUST_DATA>(),
                pPolicyCallbackData = IntPtr.Zero,
                pSIPClientData = IntPtr.Zero,
                dwUIChoice = WTD_UI_NONE,
                fdwRevocationChecks = WTD_REVOKE_NONE,
                dwUnionChoice = WTD_CHOICE_FILE,
                pFile = pFileInfo,
                dwStateAction = WTD_STATEACTION_VERIFY,
                hWVTStateData = IntPtr.Zero,
                pwszURLReference = IntPtr.Zero,
                dwProvFlags = WTD_SAFER_FLAG,
                dwUIContext = 0
            };

            // 使用局部副本传递 ref 参数（static readonly 字段不能用于 ref）
            var actionId = s_winTrustActionGenericVerifyV2;
            var result = WinVerifyTrustCore(IntPtr.Zero, ref actionId, ref data);

            // 清理状态句柄
            data.dwStateAction = WTD_STATEACTION_CLOSE;
            actionId = s_winTrustActionGenericVerifyV2;
            WinVerifyTrustCore(IntPtr.Zero, ref actionId, ref data);

            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(pFileInfo);
        }
    }

    /// <summary>
    /// Win32 WinVerifyTrust P/Invoke 核心调用。
    /// AOT 安全的 DllImport 声明。
    /// </summary>
    [DllImport(WinTrustDll, EntryPoint = "WinVerifyTrust", SetLastError = false)]
    private static extern int WinVerifyTrustCore(
        IntPtr hwnd,
        ref Guid pgActionID,
        ref WINTRUST_DATA pWVTData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_FILE_INFO
    {
        public int cbStruct;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_DATA
    {
        public int cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public int dwUIChoice;
        public int fdwRevocationChecks;
        public int dwUnionChoice;
        public IntPtr pFile;
        public int dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference; // LPWSTR 作为 IntPtr 传递更安全
        public int dwProvFlags;
        public int dwUIContext;
    }
}
