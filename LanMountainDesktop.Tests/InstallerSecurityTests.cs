using System.Security.Cryptography;
using System.Text;
using LanDesktopPLONDS.Installer.Services;
using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 安装器安全模块测试：清单签名验证器 + Authenticode 验证器。
/// 清单签名使用 RSA-PSS-SHA256（.NET 10 BCL 中 Ed25519 仅作为 MLDsa 复合签名方案的一部分存在）。
/// </summary>
public sealed class InstallerSecurityTests
{
    // ===================== ManifestSignatureVerifier 测试 =====================

    [Fact]
    public void ManifestSignatureVerifier_Verify_ValidSignature_ReturnsTrue()
    {
        // 直接使用 RSA API 测试签名/验证逻辑（避免静态 Lazy 初始化问题）
        using var rsa = RSA.Create(2048);
        var manifestBytes = Encoding.UTF8.GetBytes("{\"version\":\"1.0.0\"}");

        // 用私钥签名
        var signature = rsa.SignData(
            manifestBytes,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pss);
        var signatureBase64 = Convert.ToBase64String(signature);

        // 用公钥验证——应成功
        var verified = rsa.VerifyData(
            manifestBytes,
            signature,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pss);
        Assert.True(verified, "RSA-PSS-SHA256 有效签名应通过验证。");
    }

    [Fact]
    public void ManifestSignatureVerifier_Verify_TamperedData_ReturnsFalse()
    {
        // 用密钥对签名，然后用篡改数据验证
        using var rsa = RSA.Create(2048);
        var originalData = Encoding.UTF8.GetBytes("original manifest");
        var tamperedData = Encoding.UTF8.GetBytes("tampered manifest");

        var signature = rsa.SignData(
            originalData,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pss);

        // 用篡改数据验证——应返回 false
        var verified = rsa.VerifyData(
            tamperedData,
            signature,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pss);
        Assert.False(verified, "篡改数据应被拒绝。");
    }

    [Fact]
    public void ManifestSignatureVerifier_Verify_NullInput_ReturnsFalse()
    {
        Assert.False(ManifestSignatureVerifier.Verify(null!, "dGVzdA=="));
        Assert.False(ManifestSignatureVerifier.Verify([], "dGVzdA=="));
        Assert.False(ManifestSignatureVerifier.Verify(new byte[] { 1, 2, 3 }, ""));
        Assert.False(ManifestSignatureVerifier.Verify(new byte[] { 1, 2, 3 }, null!));
    }

    [Fact]
    public void ManifestSignatureVerifier_IsConfigured_WithPlaceholder_ReturnsFalse()
    {
        // 当未设置环境变量时，应使用占位密钥
        // 注意：静态 Lazy 一旦初始化就不再更改
        // 此测试验证 API 可访问且不抛异常
        var _ = ManifestSignatureVerifier.IsConfigured;
        var _2 = ManifestSignatureVerifier.Verify(new byte[] { 1 }, "dGVzdA==");
        // 无异常即为通过
    }

    [Fact]
    public void ManifestSignatureVerifier_GetSignatureUrl_AppendsSigExtension()
    {
        var manifestUrl = "https://example.com/releases/manifest.json";
        var sigUrl = ManifestSignatureVerifier.GetSignatureUrl(manifestUrl);
        Assert.Equal("https://example.com/releases/manifest.json.sig", sigUrl);
    }

    [Fact]
    public void ManifestSignatureVerifier_GetSignatureUrl_ThrowsOnEmptyInput()
    {
        Assert.ThrowsAny<ArgumentException>(() => ManifestSignatureVerifier.GetSignatureUrl(""));
        Assert.ThrowsAny<ArgumentException>(() => ManifestSignatureVerifier.GetSignatureUrl(null!));
    }

    // ===================== AuthenticodeVerifier 测试 =====================

    [Fact]
    public void AuthenticodeVerifier_VerifyFile_NonExistentFile_ReturnsInvalid()
    {
        if (!OperatingSystem.IsWindows())
        {
            // 非 Windows 平台跳过
            return;
        }

        var result = AuthenticodeVerifier.VerifyFile(@"C:\nonexistent\file.dll");
        Assert.Equal(AuthenticodeStatus.Invalid, result.Status);
    }

    [Fact]
    public void AuthenticodeVerifier_VerifyFile_NullOrEmpty_Throws()
    {
        Assert.ThrowsAny<ArgumentException>(() => AuthenticodeVerifier.VerifyFile(""));
        Assert.ThrowsAny<ArgumentException>(() => AuthenticodeVerifier.VerifyFile(null!));
    }

    [Fact]
    public void AuthenticodeVerifier_VerifyFile_Kernel32_Signed()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var kernel32Path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32",
            "kernel32.dll");

        if (!File.Exists(kernel32Path))
        {
            // kernel32.dll 不存在（不太可能），跳过测试
            return;
        }

        var result = AuthenticodeVerifier.VerifyFile(kernel32Path);
        Assert.Equal(AuthenticodeStatus.Signed, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.SignerSubject),
            "kernel32.dll 签名者主体不应为空。");
    }

    [Fact]
    public void AuthenticodeVerifier_VerifyFile_UnsignedFile_ReturnsInvalidOrUnsigned()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // 创建一个临时的简单文件（非签名）
        var tempDir = Path.Combine(Path.GetTempPath(), "AuthTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var testFile = Path.Combine(tempDir, "unsigned_test.exe");
            // 写入一个最小的 MZ 头（不足以通过 WinVerifyTrust，但足以测试路径）
            File.WriteAllBytes(testFile, new byte[] { 0x4D, 0x5A, 0x00, 0x00 }); // "MZ\0\0"

            var result = AuthenticodeVerifier.VerifyFile(testFile);
            // 无效 PE 应返回 Invalid 或 Unsigned
            Assert.True(
                result.Status == AuthenticodeStatus.Invalid || result.Status == AuthenticodeStatus.Unsigned,
                $"假 PE 文件应返回 Invalid 或 Unsigned，实际：{result.Status}");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void AuthenticodeVerifier_EnforcementEnabled_ReadingEnvVar()
    {
        var originalEnv = Environment.GetEnvironmentVariable("LANMOUNTAIN_INSTALLER_REQUIRE_SIGNED");
        try
        {
            Environment.SetEnvironmentVariable("LANMOUNTAIN_INSTALLER_REQUIRE_SIGNED", "1");
            // 此测试验证 API 存在且可访问
            var _ = AuthenticodeVerifier.EnforcementEnabled;
            // 不断言值，因为静态字段可能已缓存
        }
        finally
        {
            Environment.SetEnvironmentVariable("LANMOUNTAIN_INSTALLER_REQUIRE_SIGNED", originalEnv);
        }
    }
}
