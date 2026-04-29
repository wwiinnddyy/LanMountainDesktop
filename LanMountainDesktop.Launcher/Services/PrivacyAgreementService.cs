using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LanMountainDesktop.Launcher.Models;

namespace LanMountainDesktop.Launcher.Services;

/// <summary>
/// 隐私协议同意状态管理服务（带防篡改保护）
/// </summary>
internal sealed class PrivacyAgreementService
{
    private readonly string _storagePath;
    private readonly string _secretKey;
    private const string ConfigFileName = "privacy-agreement.state.json";
    private const string CurrentAgreementVersion = "1.0";

    public PrivacyAgreementService(string launcherDataPath)
    {
        _storagePath = Path.Combine(launcherDataPath, ConfigFileName);
        // 使用机器特定信息生成密钥，增加篡改难度
        _secretKey = GenerateMachineSpecificKey();
    }

    /// <summary>
    /// 检查用户是否已同意隐私协议
    /// </summary>
    public bool HasUserAgreed()
    {
        try
        {
            if (!File.Exists(_storagePath))
            {
                Logger.Info("[PrivacyAgreementService] 未找到隐私协议同意状态文件");
                return false;
            }

            var json = File.ReadAllText(_storagePath);
            var state = JsonSerializer.Deserialize(json, AppJsonContext.Default.PrivacyAgreementState);

            if (state == null)
            {
                Logger.Warn("[PrivacyAgreementService] 无法解析隐私协议状态文件");
                return false;
            }

            // 验证数据完整性
            if (!VerifyIntegrity(state))
            {
                Logger.Warn("[PrivacyAgreementService] 隐私协议状态文件已被篡改！");
                // 删除被篡改的文件
                try
                {
                    File.Delete(_storagePath);
                }
                catch (Exception ex)
                {
                    Logger.Warn($"[PrivacyAgreementService] 删除被篡改文件失败: {ex.Message}");
                }
                return false;
            }

            // 检查协议版本是否匹配
            if (state.AgreementVersion != CurrentAgreementVersion)
            {
                Logger.Info($"[PrivacyAgreementService] 隐私协议版本已更新: {state.AgreementVersion} -> {CurrentAgreementVersion}");
                return false;
            }

            Logger.Info($"[PrivacyAgreementService] 用户已于 {state.AgreedAtUtc:yyyy-MM-dd HH:mm:ss} UTC 同意隐私协议");
            return state.IsAgreed;
        }
        catch (Exception ex)
        {
            Logger.Warn($"[PrivacyAgreementService] 检查同意状态时出错: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 保存用户同意隐私协议的状态
    /// </summary>
    public bool SaveAgreement(bool isAgreed, string userId, string deviceId)
    {
        try
        {
            // 确保目录存在
            var directory = Path.GetDirectoryName(_storagePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // 生成随机盐值
            var salt = GenerateRandomSalt();

            var state = new PrivacyAgreementState
            {
                IsAgreed = isAgreed,
                AgreedAtUtc = DateTime.UtcNow,
                AgreementVersion = CurrentAgreementVersion,
                UserId = userId,
                DeviceId = deviceId,
                Salt = salt
            };

            // 计算完整性哈希
            state.IntegrityHash = CalculateIntegrityHash(state);

            // 保存到文件
            var json = JsonSerializer.Serialize(state, AppJsonContext.Default.PrivacyAgreementState);
            File.WriteAllText(_storagePath, json);

            Logger.Info($"[PrivacyAgreementService] 隐私协议同意状态已保存: IsAgreed={isAgreed}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn($"[PrivacyAgreementService] 保存同意状态失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 获取当前的协议版本
    /// </summary>
    public string GetCurrentAgreementVersion() => CurrentAgreementVersion;

    /// <summary>
    /// 清除同意状态（用于测试或重置）
    /// </summary>
    public bool ClearAgreement()
    {
        try
        {
            if (File.Exists(_storagePath))
            {
                File.Delete(_storagePath);
                Logger.Info("[PrivacyAgreementService] 隐私协议同意状态已清除");
            }
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn($"[PrivacyAgreementService] 清除同意状态失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 生成机器特定的密钥
    /// </summary>
    private string GenerateMachineSpecificKey()
    {
        try
        {
            // 组合多个机器特定信息生成密钥
            var machineName = Environment.MachineName;
            var userName = Environment.UserName;
            var osVersion = Environment.OSVersion.Version.ToString();
            var processorCount = Environment.ProcessorCount.ToString();

            // 使用硬件信息（如果可用）
            var hardwareId = GetHardwareIdentifier();

            var keyData = $"{machineName}:{userName}:{osVersion}:{processorCount}:{hardwareId}:LanMountainDesktop";

            // 使用 SHA-256 生成固定长度的密钥
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(keyData));
            return Convert.ToHexString(hash);
        }
        catch
        {
            // 如果无法获取机器信息，使用备用密钥
            return "LanMountainDesktop-Privacy-Agreement-Fallback-Key-2026";
        }
    }

    /// <summary>
    /// 获取硬件标识符
    /// </summary>
    private string GetHardwareIdentifier()
    {
        try
        {
            // 尝试使用系统目录创建时间作为硬件标识的一部分
            var systemDir = Environment.SystemDirectory;
            var dirInfo = new DirectoryInfo(systemDir);
            return dirInfo.CreationTimeUtc.ToString("yyyyMMddHHmmss");
        }
        catch
        {
            return "Unknown";
        }
    }

    /// <summary>
    /// 生成随机盐值
    /// </summary>
    private string GenerateRandomSalt()
    {
        var saltBytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(saltBytes);
        return Convert.ToHexString(saltBytes);
    }

    /// <summary>
    /// 计算完整性哈希（HMAC-SHA256）
    /// </summary>
    private string CalculateIntegrityHash(PrivacyAgreementState state)
    {
        // 构建需要哈希的数据字符串
        var dataToHash = $"{state.IsAgreed}:{state.AgreedAtUtc:o}:{state.AgreementVersion}:{state.UserId}:{state.DeviceId}:{state.Salt}";

        // 使用 HMAC-SHA256 计算哈希
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_secretKey));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(dataToHash));
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// 验证数据完整性
    /// </summary>
    private bool VerifyIntegrity(PrivacyAgreementState state)
    {
        try
        {
            if (string.IsNullOrEmpty(state.IntegrityHash) || string.IsNullOrEmpty(state.Salt))
            {
                return false;
            }

            var expectedHash = CalculateIntegrityHash(state);
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(state.IntegrityHash),
                Encoding.UTF8.GetBytes(expectedHash));
        }
        catch
        {
            return false;
        }
    }
}
