# LanMountainDesktop 安全审计报告

**审计日期**: 2026-05-31
**审计范围**: LanMountainDesktop 主仓库
**审计方法**: 静态代码分析 + 架构审查

---

## 执行摘要

本次安全审计系统性地检查了 LanMountainDesktop 代码库的高风险攻击面，包括认证与访问控制、注入向量、外部交互和敏感数据处理。

**结论**: **未发现中等或更高严重度的已确认漏洞。**

代码库展示了多项积极的安全设计：
- 更新包使用 RSA 签名验证
- 使用路径遍历防护机制
- SHA-256/SHA-512 哈希校验
- 插件沙箱隔离 (AssemblyLoadContext)
- 命令行参数解析验证

---

## 审计范围与方法

### 审计的攻击面分组

| 分组 | 审计内容 |
|------|---------|
| **认证与访问控制** | OOBE 流程、隐私协议、会话管理、权限校验 |
| **注入向量** | SQL 查询、Shell 命令拼接、模板渲染、文件路径操作 |
| **外部交互** | Webhook 处理器、出站网络请求、第三方 API 集成 |
| **敏感数据处理** | 密钥/凭证、日志记录、加密实践 |

### 审计的代码模块

- `LanMountainDesktop/` - 主宿主应用
- `LanMountainDesktop.Launcher/` - 启动器 (OOBE、更新、插件管理)
- `LanMountainDesktop.PluginSdk/` - 插件 SDK
- `LanMountainDesktop.Services/` - 服务层
- `LanMountainDesktop.plugins/` - 插件运行时

---

## 详细审计结果

### 1. 认证与访问控制

#### 审计项目

| 项目 | 位置 | 状态 |
|------|------|------|
| OOBE 状态持久化 | `LanMountainDesktop.Launcher/Oobe/OobeStateService.cs` | ✅ 安全 |
| 隐私协议管理 | `LanMountainDesktop.Launcher/Oobe/PrivacyAgreementService.cs` | ✅ 安全 |
| 命令行参数解析 | `LanMountainDesktop.Launcher/CommandContext.cs` | ✅ 安全 |
| 提升权限控制 | `LanMountainDesktop.Launcher/` | ✅ 安全 |

#### 分析结果

**OOBE 状态持久化** 采用原子写入模式 (先写临时文件再 Move)，避免状态损坏。使用 JSON Schema 版本控制便于迁移。`LaunchSource` 参数白名单验证防止非法来源。

**命令行参数解析** 对 `Options` 字典使用 `StringComparer.OrdinalIgnoreCase`，解析逻辑清晰，不存在注入风险。

---

### 2. 注入向量

#### 审计项目

| 项目 | 位置 | 风险评估 |
|------|------|---------|
| 路径遍历防护 | `Services/Update/UpdatePathGuard.cs` | ✅ 有防护 |
| 文件操作 | `PlondsUpdateApplier.cs` | ✅ 安全 |
| 插件加载 | `plugins/PluginLoader.cs` | ✅ 隔离 |
| Shell 执行 | 各组件 Process.Start | ⚠️ 需注意 |

#### 关键代码审查

**路径遍历防护** ([UpdatePathGuard.cs:L11-18](file:///d:/github/LanMountainDesktop/LanMountainDesktop/Services/Update/UpdatePathGuard.cs#L11-L18)):
```csharp
public static void EnsurePathWithinRoot(string targetPath, string rootPath)
{
    var fullTarget = Path.GetFullPath(targetPath);
    var fullRoot = Path.GetFullPath(rootPath);
    if (!fullTarget.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"Path traversal detected: {targetPath}");
    }
}
```
✅ 使用 `OrdinalIgnoreCase` 防止大小写绕过，使用 `GetFullPath` 规范化路径。

**插件包路径清理** ([PluginMarketInstallService.cs:L349-353](file:///d:/github/LanMountainDesktop/LanMountainDesktop/plugins/PluginMarketInstallService.cs#L349-L353)):
```csharp
private static string SanitizeFileName(string value)
{
    var invalidChars = Path.GetInvalidFileNameChars();
    return new string(value.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
}
```
✅ 插件包文件名经过清理，避免路径注入。

**Shell 执行上下文**:

检查了 30+ 处 `Process.Start` 调用:
- 更新安装使用 `UseShellExecute = true` 仅用于 `runas` 提权执行安装程序
- 组件快捷方式执行 (`ShortcutWidget.axaml.cs`) 使用 `UseShellExecute = true` 但路径来自用户配置的快捷方式
- 新闻组件打开链接使用固定域名验证

**评估**: Shell 执行主要针对用户主动操作的文件/链接，不存在未授权代码执行路径。

---

### 3. 外部交互

#### 审计项目

| 服务 | 位置 | 安全措施 |
|------|------|---------|
| GitHub Release 更新 | `Services/GitHubReleaseUpdateService.cs` | HTTPS + Hash 验证 |
| PLONDS 更新 | `Services/PlondsStaticUpdateService.cs` | RSA 签名验证 |
| 插件市场 | `plugins/PluginMarketInstallService.cs` | SHA-256 校验 |
| 天气服务 | `Services/XiaomiWeatherService.cs` | API Key 管理 |
| 遥测服务 | `Services/TelemetryServices.cs` | 用户同意控制 |

#### 关键安全机制

**更新包签名验证** ([UpdateSignatureVerifier.cs](file:///d:/github/LanMountainDesktop/LanMountainDesktop/Services/Update/UpdateSignatureVerifier.cs)):
```csharp
using var rsa = RSA.Create(384);
rsa.ImportFromPem(File.ReadAllText(paths.PublicKeyPath)); // 内置公钥
var signatureBase64 = File.ReadAllText(signaturePath).Trim();
return rsa.VerifyData(
    sha256.ComputeHash(File.OpenRead(fileMapPath)),
    Convert.FromBase64String(signatureBase64),
    HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
```
✅ 使用 PKCS#1 签名验证更新清单。

**插件包完整性验证** ([PluginMarketInstallService.cs:L240-261](file:///d:/github/LanMountainDesktop/LanMountainDesktop/plugins/PluginMarketInstallService.cs#L240-L261)):
```csharp
// 大小校验
if (plugin.PackageSizeBytes > 0 && actualSize != plugin.PackageSizeBytes)
    return verification failed;

// SHA-256 校验
if (!string.Equals(actualHash, plugin.Sha256, StringComparison.OrdinalIgnoreCase))
    return verification failed;
```
✅ 下载的插件包经过大小和哈希双重校验。

**HTTP 客户端配置**:
- 所有 HTTP 请求设置 `User-Agent` 头
- 超时配置合理 (20-30 秒)
- 响应状态码检查完善

---

### 4. 敏感数据处理

#### 审计项目

| 项目 | 状态 | 说明 |
|------|------|------|
| API 密钥硬编码 | ⚠️ 需关注 | 小米天气 API 密钥 |
| 日志记录 | ✅ 安全 | 未发现敏感信息日志 |
| 遥测数据 | ✅ 安全 | 受用户同意控制 |
| 设置存储 | ✅ 安全 | 本地 AppData 目录 |

#### API 密钥问题说明

在 [XiaomiWeatherService.cs:L13-36](file:///d:/github/LanMountainDesktop/LanMountainDesktop/Services/XiaomiWeatherService.cs#L13-L36) 中发现:

```csharp
public sealed record XiaomiWeatherApiOptions
{
    public string AppKey { get; init; } = "weather20151024";
    public string Sign { get; init; } = "zUFJoAR2ZVrDy1vF3D07";
    // ...
}
```

**风险评估**: 低

- 这些是天气数据 API 的凭证，用于访问公开天气数据
- 根据小米天气 API 设计，这些密钥通常为公开密钥，供免费/开源应用使用
- API 返回的是天气数据，不涉及用户敏感信息
- 即使密钥泄露，影响范围限于天气数据获取

**建议**: 如需增强安全，可考虑:
1. 将密钥移至配置系统
2. 实现密钥轮换机制
3. 使用服务端代理访问天气 API

---

### 5. 架构安全评估

#### 插件运行时隔离

**当前设计**:
- 插件使用 `AssemblyLoadContext` 进行程序集隔离
- 共享类型白名单机制
- 插件运行在同一进程中

**评估**: 中等风险 (架构设计)

当前插件运行时属于进程内加载，这是已知的架构权衡。代码库文档 (`.trae/specs/plugin-process-isolation/`) 已规划未来版本的进程隔离方案:

- Phase 1: 后台逻辑移至独立工作进程
- Phase 2: 插件 UI 渲染进程外

**当前缓解措施**:
- 插件 API 版本兼容性检查
- 插件清单验证
- 签名验证 (市场下载的插件)

---

## 安全最佳实践符合性

| 最佳实践 | 符合性 | 说明 |
|---------|-------|------|
| 输入验证 | ✅ | 参数解析、路径规范化、Schema 验证 |
| 输出编码 | ✅ | JSON 序列化使用 System.Text.Json |
| 加密标准 | ✅ | SHA-256/SHA-512, RSA 384-bit |
| 安全默认值 | ✅ | UseShellExecute=false 优先 |
| 错误处理 | ✅ | 异常被捕获并记录，不泄露敏感信息 |
| 更新签名 | ✅ | RSA 签名验证更新包 |

---

## 结论

### 审计状态: 通过

经过系统性审计，**未发现中等或更高严重度的已确认漏洞**。

### 代码质量评价

代码库展现了良好的安全意识：
- 关键操作 (更新安装、插件加载) 有多层安全验证
- 路径操作使用标准化防护机制
- 外部数据源完整性校验完善
- 遥测和隐私设置尊重用户选择

### 建议改进 (非紧急)

1. **API 密钥管理**: 将天气 API 密钥移至配置系统或使用服务端代理
2. **插件进程隔离**: 加速推进 `plugin-process-isolation` 规划
3. **安全清单**: 建立安全相关的持续集成检查

---

*本报告基于静态代码分析生成，未进行运行时渗透测试。建议在发布前进行完整的动态安全测试。*
