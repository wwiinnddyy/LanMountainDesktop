using System.Security.Cryptography;
using System.Text;

namespace LanDesktopPLONDS.Installer.Services;

/// <summary>
/// 清单文件 RSA-PSS-SHA256 离线签名验证器。
/// 使用 .NET 10 BCL 内置 RSA + PSS padding + SHA-256，零外部依赖，AOT 安全。
/// 签名约定：清单 URL 附加 ".sig" 后缀即为对应签名文件。
///
/// 注意：.NET 10 BCL 中 Ed25519 不作为独立类型暴露（仅存在于 MLDsa 复合签名方案中），
/// 因此使用 RSA-PSS-SHA256 作为替代方案。
/// </summary>
internal static class ManifestSignatureVerifier
{
    private const string PublicKeyEnvVar = "LANMOUNTAIN_PLONDS_MANIFEST_PUBKEY";

    // 正式发布时在此处内置产品公钥（PEM）。为空表示密钥尚未配置，
    // 此时 IsConfigured = false，验证跳过并记录警告。
    private const string EmbeddedPublicKeyPem = "";

    private static readonly Lazy<RSA?> LazyRsa = new(InitializeRsaCore);

    /// <summary>
    /// 当前是否已配置有效公钥。
    /// false 时 <see cref="Verify"/> 将跳过验证并返回 true（宽松模式）。
    /// </summary>
    public static bool IsConfigured => LazyRsa.Value is not null;

    /// <summary>
    /// 验证清单字节数组的 RSA-PSS-SHA256 签名。
    /// </summary>
    /// <param name="manifestBytes">原始清单内容。</param>
    /// <param name="signatureBase64">Base64 编码的 RSA-PSS-SHA256 签名。</param>
    /// <returns>签名有效返回 true；未配置公钥时跳过验证返回 true；签名无效返回 false。</returns>
    public static bool Verify(byte[] manifestBytes, string signatureBase64)
    {
        if (manifestBytes is null || manifestBytes.Length == 0)
        {
            InstallerStartupDiagnostics.Log("[签名验证] 清单内容为空，验证失败。");
            return false;
        }

        if (string.IsNullOrWhiteSpace(signatureBase64))
        {
            InstallerStartupDiagnostics.Log("[签名验证] 签名数据为空，验证失败。");
            return false;
        }

        if (!IsConfigured)
        {
            // 公钥未配置，跳过验证——流水线可在密钥配置前正常运行。
            InstallerStartupDiagnostics.Log(
                "[签名验证] 警告：RSA-PSS-SHA256 公钥未配置（使用占位符），签名验证已跳过。" +
                $"请设置环境变量 {PublicKeyEnvVar} 以启用验证。");
            return true;
        }

        try
        {
            var signatureBytes = Convert.FromBase64String(signatureBase64);
            var rsa = LazyRsa.Value!;

            return rsa.VerifyData(
                manifestBytes,
                signatureBytes,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pss);
        }
        catch (FormatException)
        {
            InstallerStartupDiagnostics.Log("[签名验证] 签名 Base64 解码失败。");
            return false;
        }
        catch (CryptographicException ex)
        {
            InstallerStartupDiagnostics.Log($"[签名验证] 密码学异常：{ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 根据清单 URL 计算对应的签名文件 URL。
    /// 约定：清单 URL 附加 ".sig" 后缀。
    /// </summary>
    /// <param name="manifestUrl">清单文件的远程 URL。</param>
    /// <returns>签名文件 URL。</returns>
    public static string GetSignatureUrl(string manifestUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestUrl);
        return manifestUrl + ".sig";
    }

    private static RSA? InitializeRsaCore()
    {
        // 优先从环境变量读取（测试和 CI 场景）
        var envKey = Environment.GetEnvironmentVariable(PublicKeyEnvVar);
        if (!string.IsNullOrWhiteSpace(envKey))
        {
            try
            {
                var rsa = RSA.Create();
                // 支持 DER 编码的公钥或 PEM 格式
                if (envKey.Contains("BEGIN PUBLIC KEY", StringComparison.Ordinal))
                {
                    rsa.ImportFromPem(envKey);
                }
                else
                {
                    var keyBytes = Convert.FromBase64String(envKey);
                    rsa.ImportSubjectPublicKeyInfo(keyBytes, out _);
                }

                InstallerStartupDiagnostics.Log(
                    $"[签名验证] 已从环境变量 {PublicKeyEnvVar} 加载 RSA-PSS-SHA256 公钥。");
                return rsa;
            }
            catch (FormatException)
            {
                InstallerStartupDiagnostics.Log(
                    $"[签名验证] 环境变量 {PublicKeyEnvVar} 不是有效的 Base64，使用占位符。");
            }
            catch (CryptographicException ex)
            {
                InstallerStartupDiagnostics.Log(
                    $"[签名验证] 环境变量 {PublicKeyEnvVar} 中的密钥无效：{ex.Message}，使用占位符。");
            }
        }

        // 回退到内置公钥；为空则视为未配置（返回 null，验证跳过）
        if (!string.IsNullOrWhiteSpace(EmbeddedPublicKeyPem))
        {
            try
            {
                var embeddedRsa = RSA.Create();
                embeddedRsa.ImportFromPem(EmbeddedPublicKeyPem);
                return embeddedRsa;
            }
            catch (CryptographicException ex)
            {
                InstallerStartupDiagnostics.Log($"[签名验证] 内置公钥无效：{ex.Message}");
            }
            catch (ArgumentException ex)
            {
                InstallerStartupDiagnostics.Log($"[签名验证] 内置公钥格式错误：{ex.Message}");
            }
        }

        InstallerStartupDiagnostics.Log(
            $"[签名验证] 未找到公钥配置（环境变量 {PublicKeyEnvVar}），签名验证将跳过。");
        return null;
    }
}
