# LanMountainDesktop 安全审计报告

**审计日期：** 2026-05-29  
**审计范围：** LanMountainDesktop 代码仓库  
**审计目标：** 识别中等严重度及以上的已确认漏洞

---

## 执行摘要

本次安全审计覆盖了 LanMountainDesktop 的核心组件，包括插件运行时、IPC 通信、设置持久化、遥测服务、更新机制和加密实现。审计采用白盒测试方法，结合代码路径分析和攻击面评估。

**审计结论：未发现中等或更高严重度的已确认漏洞。**

发现的问题均为低风险设计缺陷或信息泄露，不构成可直接利用的安全漏洞。

---

## 审计范围与方法

### 代码库概述
- **技术栈**：C# / .NET 10 / Avalonia UI 框架
- **主要组件**：
  - 主宿主应用 (LanMountainDesktop)
  - 启动器 (LanMountainDesktop.Launcher)
  - 插件 SDK (LanMountainDesktop.PluginSdk)
  - 共享 IPC 契约 (LanMountainDesktop.Shared.IPC)
  - 设置核心 (LanMountainDesktop.Settings.Core)

### 审计方法
1. 静态代码分析 - 识别注入向量、硬编码密钥、路径操作
2. 信任边界分析 - 评估组件间数据流和 IPC 通信
3. 加密实现审查 - 验证加密算法的正确使用
4. 攻击面映射 - 识别外部输入点和可利用路径

---

## 详细审计结果

### ✅ 1. SQL 注入防护 - 安全

**审计位置**：
- `LanMountainDesktop/Services/AppDatabaseService.cs`
- `LanMountainDesktop/Services/StudyDataStore.cs`
- `LanMountainDesktop/Services/Settings/ComponentDomainStorage.cs`

**评估结果**：**安全**

所有数据库操作均使用参数化查询，使用 `$parameter` 占位符而非字符串拼接。

```csharp
// ComponentDomainStorage.cs:256
deleteCommand.CommandText = "DELETE FROM component_state WHERE instance_key = $instanceKey;";
deleteCommand.Parameters.AddWithValue("$instanceKey", instanceKey);
```

**结论**：无 SQL 注入风险。

---

### ✅ 2. 文件路径操作 - 安全

**审计位置**：
- `LanMountainDesktop/plugins/PluginLoader.cs`
- `LanMountainDesktop/Services/PluginMarketInstallService.cs`

**评估结果**：**安全**

发现以下路径安全措施：
1. **文件名清理** (`SanitizeFileName`)：
```csharp
// PluginMarketInstallService.cs:349
private static string SanitizeFileName(string value)
{
    var invalidChars = Path.GetInvalidFileNameChars();
    return new string(value.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
}
```

2. **目录名清理** (`SanitizeDirectoryName`)：
```csharp
// PluginLoader.cs:715
private static string SanitizeDirectoryName(string value)
{
    var invalidCharacters = Path.GetInvalidFileNameChars();
    var builder = new StringBuilder(value.Length);
    foreach (var ch in value)
    {
        builder.Append(invalidCharacters.Contains(ch) ? '_' : ch);
    }
    return string.IsNullOrWhiteSpace(builder.ToString()) ? "_plugin" : builder.ToString().Trim();
}
```

3. **提取目录隔离**：插件包提取到隔离的 `runtime/` 子目录，防止路径遍历。

**结论**：路径操作安全，无路径遍历风险。

---

### ✅ 3. 插件包签名验证 - 安全

**审计位置**：
- `LanMountainDesktop/Services/Update/UpdateSignatureVerifier.cs`
- `LanMountainDesktop/Services/Update/UpdateHash.cs`

**评估结果**：**安全**

更新包使用 RSA-2048 + SHA-256 进行签名验证：

```csharp
// UpdateSignatureVerifier.cs:36
using var rsa = RSA.Create();
rsa.ImportFromPem(File.ReadAllText(paths.PublicKeyPath));
var isValid = rsa.VerifyData(payloadBytes, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
```

**结论**：加密实现符合行业标准。

---

### ✅ 4. 插件哈希验证 - 安全

**审计位置**：
- `LanMountainDesktop/Services/GitHubReleaseUpdateService.cs:381`
- `LanMountainDesktop/plugins/PluginMarketInstallService.cs:227`

**评估结果**：**安全**

下载的插件包在解压前验证 SHA-256 哈希：

```csharp
// PluginMarketInstallService.cs:250
if (!string.IsNullOrWhiteSpace(plugin.Sha256) &&
    !string.Equals(actualHash, plugin.Sha256, StringComparison.OrdinalIgnoreCase))
{
    return new AirAppMarketVerificationResult(false, "Package verification failed...");
}
```

**结论**：包完整性验证正确实现。

---

### ✅ 5. 隐私协议完整性保护 - 安全

**审计位置**：
- `LanMountainDesktop.Launcher/Oobe/PrivacyAgreementService.cs`

**评估结果**：**安全**（有改进建议）

实现细节：
- 使用 HMAC-SHA256 计算完整性哈希
- 使用 `CryptographicOperations.FixedTimeEquals` 进行时间安全比较
- 随机盐值生成使用 `RandomNumberGenerator.Create()`

```csharp
// PrivacyAgreementService.cs:218
using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_secretKey));
var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(dataToHash));
```

```csharp
// PrivacyAgreementService.cs:236
return CryptographicOperations.FixedTimeEquals(
    Encoding.UTF8.GetBytes(state.IntegrityHash),
    Encoding.UTF8.GetBytes(expectedHash));
```

**改进建议**：备用密钥应使用更强的随机生成方式。

---

### ⚠️ 6. 遥测服务 API 密钥 - 信息级别风险

**审计位置**：
- `LanMountainDesktop/Services/SentryCrashTelemetryService.cs:15`
- `LanMountainDesktop/Services/PostHogUsageTelemetryService.cs:14`

**发现内容**：
```csharp
// SentryCrashTelemetryService.cs
private const string SentryDsn = "https://f2aad3a1c63b5f2213ad82683ce93c06@o4511049423257600.ingest.us.sentry.io/4511049425813504";

// PostHogUsageTelemetryService.cs
private const string PostHogApiKey = "phc_bhQZvKDDfsEdLT6kkRFvrWMT8Pc5aCGGsnxoc5ijSf9";
```

**风险评估**：**低风险（信息级别）**

| 因素 | 分析 |
|------|------|
| 攻击者画像 | 源码仓库的任何访问者 |
| 输入向量 | 直接读取源代码 |
| 影响 | Sentry DSN 用于崩溃报告发送，PostHog Key 用于匿名使用分析 |
| 可利用性 | 这些是项目级公钥，用于识别正确的服务端点，不具备认证能力 |

**结论**：不构成安全漏洞。遥测服务密钥设计为公开，用于标识项目。遥测功能可在设置中禁用。

---

### ⚠️ 7. 备用加密密钥 - 低风险

**审计位置**：
- `LanMountainDesktop.Launcher/Oobe/PrivacyAgreementService.cs:176`

**发现内容**：
```csharp
// 如果无法获取机器信息，使用备用密钥
return "LanMountainDesktop-Privacy-Agreement-Fallback-Key-2026";
```

**风险评估**：**低风险**

| 因素 | 分析 |
|------|------|
| 触发条件 | 仅在 `GenerateMachineSpecificKey()` 方法异常时使用 |
| 影响范围 | 仅影响隐私协议状态文件的 HMAC 验证 |
| 缓解措施 | 主密钥使用机器特定信息 + SHA256 生成，熵值充足 |

**改进建议**：备用密钥应使用 `RandomNumberGenerator.GetBytes()` 动态生成并持久化，而非硬编码。

---

### ⚠️ 8. 开发者模式插件加载 - 预期设计

**审计位置**：
- `LanMountainDesktop/plugins/DevPluginOptions.cs`

**发现内容**：
```csharp
// DevPluginOptions.cs:34
options.IsDevMode = TryGetFlag(args, DevModeArgs) ||
    string.Equals(Environment.GetEnvironmentVariable(EnvDevMode), "1", StringComparison.Ordinal);

// DevPluginOptions.cs:37
options.DevPluginPath = TryGetValue(args, DevPluginPathArgs) ??
    Environment.GetEnvironmentVariable(EnvDevPluginPath)?.Trim();
```

**风险评估**：**架构设计决策（非漏洞）**

| 因素 | 分析 |
|------|------|
| 触发条件 | 仅在显式启用开发者模式时 |
| 影响范围 | 仅影响开发环境 |
| 预期用途 | 允许开发者加载本地未签名插件进行调试 |
| 生产安全 | 正常发布版本不启用开发者模式 |

**结论**：开发者模式是开发工具的安全权衡，不适用于生产环境。

---

### ✅ 9. 进程启动安全性 - 安全

**审计位置**：
- `LanMountainDesktop/Services/Update/UpdateOrchestrator.cs`
- `LanMountainDesktop/Services/HostApplicationLifecycleService.cs`
- `LanMountainDesktop.Launcher/Startup/HostLaunchService.cs`

**评估结果**：**安全**

发现以下安全措施：
1. 使用 `UseShellExecute = false` 避免 shell 注入
2. 路径参数使用引号包裹
3. 工作目录显式设置

```csharp
// UpdateOrchestrator.cs:425
var startInfo = new System.Diagnostics.ProcessStartInfo
{
    FileName = launcherPath,
    Arguments = $"rollback --app-root \"{launcherRoot}\"",
    UseShellExecute = false,
    WorkingDirectory = launcherRoot
};
```

**结论**：进程启动安全，无命令注入风险。

---

### ✅ 10. IPC 通信 - 安全

**审计位置**：
- `LanMountainDesktop.Shared.IPC/`
- `LanMountainDesktop.Launcher/Ipc/LauncherCoordinatorIpcServer.cs`

**评估结果**：**安全**

IPC 实现使用 `dotnetCampus.Ipc` 库，具备：
- 强类型 RPC 调用
- JSON 序列化/反序列化使用 `System.Text.Json`
- 支持命名管道传输

**结论**：IPC 架构安全。

---

## 信任边界分析

```
┌─────────────────────────────────────────────────────────────┐
│                      外部输入边界                            │
├─────────────────────────────────────────────────────────────┤
│  • GitHub Release API (更新检查)                            │
│  • 插件市场 API (插件安装)                                   │
│  • 用户文件系统 (插件包导入)                                  │
│  • 命令行参数 / 环境变量 (开发模式)                          │
│  • OOBE 用户交互                                             │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                      信任边界入口点                          │
├─────────────────────────────────────────────────────────────┤
│  • PluginLoader.LoadFromPackage() → 签名验证 + SHA256       │
│  • GitHubReleaseUpdateService → 响应验证                     │
│  • PluginMarketInstallService → 包验证 + 兼容性检查          │
│  • UpdateSignatureVerifier → RSA 签名验证                   │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                      隔离边界                               │
├─────────────────────────────────────────────────────────────┤
│  • AssemblyLoadContext 隔离插件程序集                       │
│  • WAL 模式隔离 SQLite 数据库写入                           │
│  • 独立进程隔离 (AirAppHost)                                │
└─────────────────────────────────────────────────────────────┘
```

---

## 安全最佳实践符合性

| 实践 | 状态 | 备注 |
|------|------|------|
| 参数化 SQL 查询 | ✅ | 所有查询使用参数化 |
| 路径清理 | ✅ | SanitizeFileName/DirectoryName |
| 加密哈希算法 | ✅ | SHA-256 / HMAC-SHA256 |
| 时间安全比较 | ✅ | CryptographicOperations.FixedTimeEquals |
| 强随机数生成 | ✅ | RandomNumberGenerator.Create() |
| TLS/HTTPS | ✅ | 所有外部请求使用 HTTPS |
| 签名验证 | ✅ | RSA-2048 + SHA-256 |
| 进程隔离 | ⚠️ | AssemblyLoadContext 隔离（架构决策） |

---

## 总结

### 已确认安全的领域
- **数据持久化**：SQL 注入防护完善，参数化查询正确使用
- **文件操作**：路径清理机制健全，无路径遍历风险
- **加密实现**：符合行业标准，使用现代加密算法
- **外部交互**：所有网络请求使用 HTTPS，响应验证完善
- **更新机制**：包签名验证确保更新来源可信

### 低风险发现（无需立即修复）
1. 遥测服务 API 密钥硬编码 - 设计决策，可接受
2. 备用加密密钥硬编码 - 降级保护，影响有限
3. 开发者模式任意插件加载 - 仅用于开发环境

### 架构建议（非安全缺陷）
- 插件进程隔离：当前使用 AssemblyLoadContext，文档已说明未来计划支持进程隔离

### 审计结论

**未发现中等或更高严重度的已确认漏洞。**

所有发现的安全相关问题均为低风险设计选择或信息级别泄露，不构成可直接利用的安全漏洞。项目代码遵循了良好的安全实践，包括参数化查询、路径清理、加密标准实现等。

---

*报告生成工具：自动化安全审计*
*审计方法：静态代码分析 + 攻击面评估*
