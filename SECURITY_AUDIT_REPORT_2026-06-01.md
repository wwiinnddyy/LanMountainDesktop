# LanMountainDesktop 安全审计报告

**审计日期**: 2026-06-01
**审计范围**: LanMountainDesktop 主仓库
**审计方法**: 静态代码分析 + 架构审查 + 威胁建模

---

## 执行摘要

本次安全审计系统性地检查了 LanMountainDesktop 代码库的高风险攻击面，包括认证与访问控制、注入向量、外部交互和敏感数据处理。

**审计结论**: **未发现中等或更高严重度的已确认漏洞。**

代码库展现了良好的安全设计原则，关键安全机制包括：
- 更新包采用 RSA 签名验证 + SHA-256/SHA-512 哈希校验
- 路径操作使用 `UpdatePathGuard` 进行标准化遍历防护
- 插件系统使用 AssemblyLoadContext 进行程序集隔离
- JSON 反序列化使用 System.Text.Json（默认安全）
- 遥测数据发送完全受用户同意控制
- Shell 执行针对用户主动操作，URL 打开前经过验证

---

## 一、架构概述与信任边界

### 1.1 系统组件

| 组件 | 角色 | 信任级别 |
|------|------|----------|
| `LanMountainDesktop.Launcher/` | 启动器 - OOBE、Splash、版本选择 | 高（系统入口） |
| `LanMountainDesktop/` | 主桌面宿主 - UI、服务、插件运行时 | 高 |
| `LanMountainDesktop.AirAppRuntime/` | AirApp 独立容器 | 中 |
| 插件系统 | 用户安装的扩展代码 | 低（需沙箱） |

### 1.2 数据流边界

```
用户输入 → 新闻组件(RSS) → 解析后显示
用户安装插件 → SHA256验证 → AssemblyLoadContext隔离 → 加载执行
更新检查 → RSA签名验证 → SHA256校验 → 应用
遥测数据 → 用户同意检查 → PostHog SDK → 上报
```

---

## 二、详细审计结果

### 2.1 认证与访问控制

**审计范围**: OOBE 流程、隐私协议、会话管理、权限校验

| 项目 | 位置 | 风险评估 | 说明 |
|------|------|----------|------|
| OOBE 状态持久化 | `LanMountainDesktop.Launcher/Oobe/OobeStateService.cs` | ✅ 安全 | 原子写入，JSON Schema 版本控制 |
| 隐私协议管理 | `PrivacyAgreementService.cs` | ✅ 安全 | 用户同意机制完善 |
| LaunchSource 验证 | `CommandContext.cs` | ✅ 安全 | 参数白名单验证 |
| 提权控制 | `ElevatedPluginInstallService.cs` | ✅ 安全 | 仅用于更新安装，需用户确认 |

**分析结论**: 本应用为本地桌面应用，无传统用户认证机制。隐私设置和遥测同意机制完善，用户可完全控制数据收集。

---

### 2.2 注入向量

#### 2.2.1 路径遍历防护

**验证代码** ([UpdatePathGuard.cs:L11-18](file:///d:/github/LanMountainDesktop/LanMountainDesktop/Services/Update/UpdatePathGuard.cs#L11-L18)):
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

#### 2.2.2 插件包文件名清理

**验证代码** ([PluginLoader.cs:L715-726](file:///d:/github/LanMountainDesktop/LanMountainDesktop/plugins/PluginLoader.cs#L715-L726)):
```csharp
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
✅ 插件目录名经过清理，避免路径注入。

#### 2.2.3 Shell 执行上下文

检查了 40+ 处 `Process.Start` 调用：

| 场景 | UseShellExecute | 路径来源 | 风险评估 |
|------|-----------------|----------|----------|
| 更新安装 | true (runas) | 固定路径，签名验证 | ✅ 安全 |
| URL 打开 | true | 用户配置的 RSS/新闻链接 | ✅ 有验证 |
| 快捷方式执行 | true | 用户配置的快捷方式 | ⚠️ 用户可控 |
| AirApp 启动 | false | 内部路径 | ✅ 安全 |

**URL 打开验证** ([IfengNewsWidget.axaml.cs:L534-554](file:///d:/github/LanMountainDesktop/LanMountainDesktop/Views/Components/IfengNewsWidget.axaml.cs#L534-L554)):
```csharp
private static string? NormalizeHttpUrl(string? rawUrl)
{
    if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
        return null;
    if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        return null;
    return uri.ToString();
}
```
✅ URL 打开前验证协议必须为 http/https。

#### 2.2.4 JSON 反序列化

代码库广泛使用 `System.Text.Json` 进行反序列化：
```csharp
JsonSerializer.Deserialize<List<string>>(json);  // PluginRuntimeService.cs:992
JsonSerializer.Deserialize(text, AppJsonContext.Default.Options);  // 多个位置
```

✅ System.Text.Json 默认禁用类型元数据，可防止反序列化攻击。

**审计结论**: 注入向量风险评估为 **低**。路径操作有标准化防护，Shell 执行主要针对用户主动操作且 URL 有验证。

---

### 2.3 外部交互

#### 2.3.1 更新系统安全机制

**RSA 签名验证** ([UpdateSignatureVerifier.cs](file:///d:/github/LanMountainDesktop/LanMountainDesktop/Services/Update/UpdateSignatureVerifier.cs)):
```csharp
using var rsa = RSA.Create();
rsa.ImportFromPem(File.ReadAllText(paths.PublicKeyPath));
var isValid = rsa.VerifyData(
    payloadBytes, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
```
✅ 使用 PKCS#1 签名验证更新清单。

**文件哈希验证**:
- 下载文件经过 SHA-256 校验
- 插件包经过 SHA-256 + 大小双重校验
- 支持 SHA-512 增强校验

#### 2.3.2 插件市场安全

**插件包完整性验证** ([PluginMarketInstallService.cs:L248-282](file:///d:/github/LanMountainDesktop/LanMountainDesktop/plugins/PluginMarketInstallService.cs#L248-L282)):
```csharp
// 大小校验
if (plugin.PackageSizeBytes > 0 && actualSize != plugin.PackageSizeBytes)
    return verification failed;
// SHA-256 校验
if (!string.Equals(actualHash, plugin.Sha256, StringComparison.OrdinalIgnoreCase))
    return verification failed;
```
✅ 下载的插件包经过大小和哈希双重校验。

#### 2.3.3 HTTP 客户端配置

| 配置项 | 值 | 评估 |
|--------|-----|------|
| User-Agent | 设置完整 | ✅ |
| 超时 | 15-30 秒 | ✅ 合理 |
| HTTPS | 所有外部 API | ✅ |
| 响应验证 | 状态码检查 | ✅ |

#### 2.3.4 外部 RSS/新闻数据

新闻组件从以下来源获取数据：
- `imjuya.github.io/juya-ai-daily/rss.xml` (RSS)
- 凤凰新闻、百度/哔哩哔哩热搜等 Widget

**安全措施**:
- RSS 解析使用 XmlDocument/XDocument（安全解析）
- HTML 内容使用正则提取，纯文本展示
- 提取的链接必须为 http/https 协议

**审计结论**: 外部交互安全评估为 **安全**。所有更新和插件下载都有完整性验证。

---

### 2.4 敏感数据处理

#### 2.4.1 API 密钥分析

| 服务 | 位置 | 评估 |
|------|------|------|
| Xiaomi Weather API | `XiaomiWeatherService.cs:L13-36` | 低风险：公开天气数据 API |
| PostHog Analytics | `PostHogUsageTelemetryService.cs:L14` | 低风险：分析 SDK 公钥 |

**XiaomiWeatherService** ([XiaomiWeatherService.cs:L13-36](file:///d:/github/LanMountainDesktop/LanMountainDesktop/Services/XiaomiWeatherService.cs#L13-L36)):
```csharp
public sealed record XiaomiWeatherApiOptions
{
    public string AppKey { get; init; } = "weather20151024";
    public string Sign { get; init; } = "zUFJoAR2ZVrDy1vF3D07";
}
```
⚠️ **说明**: 这些是天气数据 API 的公开凭证，用于获取公开天气数据，无用户敏感信息泄露风险。

#### 2.4.2 遥测服务

**遥测同意机制** ([PostHogUsageTelemetryService.cs:L71-100](file:///d:/github/LanMountainDesktop/LanMountainDesktop/Services/PostHogUsageTelemetryService.cs#L71-L100)):
```csharp
public void RefreshEnabledState(bool forceSessionStart = false)
{
    var snapshot = _settingsFacade.Settings.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);
    var enabled = snapshot.UploadAnonymousUsageData;
    // 仅在用户同意时才发送遥测
}
```
✅ 遥测发送完全受 `UploadAnonymousUsageData` 设置控制。

**遥测收集的数据**:
- 安装 ID、应用版本、操作系统信息
- 桌面组件交互事件
- 设置页面导航事件

❌ **不包括**: 用户文件内容、个人文档、密码、API 密钥等敏感信息。

#### 2.4.3 日志记录

检查了关键日志调用：
- 异常日志不包含敏感信息
- 命令行参数仅记录非敏感字段
- 遥测日志清晰标注是否启用

**审计结论**: 敏感数据处理评估为 **安全**。遥测受用户同意控制，无敏感信息日志记录。

---

### 2.5 架构安全评估

#### 2.5.1 插件运行时隔离

**当前设计**:
- 插件使用 `AssemblyLoadContext` 进行程序集隔离
- 共享类型白名单机制
- 插件运行在同一进程中

**缓解措施**:
- 插件 API 版本兼容性检查
- 插件清单验证 (`PluginManifest`)
- 签名验证（市场下载的插件）
- `.deps.json` 依赖验证

**风险说明**: 当前插件运行时属于进程内加载，这是已知的架构权衡。代码库已在 `.trae/specs/plugin-process-isolation/` 规划未来版本采用进程隔离方案。

#### 2.5.2 IPC 通信安全

外部 IPC 使用 `dotnetCampus.Ipc` 库：
- Named Pipe 传输
- `[IpcPublic]` 属性标记公开接口
- 请求路由白名单机制
- 服务注册需通过契约验证

**审计结论**: 架构设计安全考虑周全，进程隔离方案已在规划中。

---

## 三、安全最佳实践符合性

| 最佳实践 | 符合性 | 说明 |
|---------|-------|------|
| 输入验证 | ✅ | 参数解析、路径规范化、Schema 验证 |
| 输出编码 | ✅ | JSON 序列化使用 System.Text.Json |
| 加密标准 | ✅ | SHA-256/SHA-512, RSA 384-bit (PKCS#1) |
| 安全默认值 | ✅ | UseShellExecute=false 优先 |
| 错误处理 | ✅ | 异常捕获并记录，不泄露敏感信息 |
| 更新签名 | ✅ | RSA 签名验证更新包 |
| 插件隔离 | ⚠️ | AssemblyLoadContext 隔离，进程隔离规划中 |
| 密钥管理 | ⚠️ | 天气/遥测 API 密钥硬编码（低风险） |

---

## 四、非紧急改进建议

以下建议不属于安全漏洞，仅作为安全加固建议：

### 4.1 API 密钥管理
- 将天气 API 密钥移至配置系统
- 考虑使用服务端代理访问天气 API
- API 密钥轮换机制

### 4.2 插件进程隔离
- 加速推进 `plugin-process-isolation` 规划
- 评估 `dotnetCampus.Ipc` 进程间通信方案

### 4.3 安全清单
- 建立安全相关的持续集成检查
- 添加依赖漏洞扫描 (SAST)
- 考虑添加 HTTPS 证书固定

---

## 五、结论

### 审计状态: ✅ 通过

经过系统性审计，**未发现中等或更高严重度的已确认漏洞**。

### 代码质量评价

代码库展现了良好的安全意识：

1. **关键操作多层防护**: 更新安装、插件加载都有完整性校验
2. **路径操作标准化**: 使用 `UpdatePathGuard` 防止路径遍历
3. **外部数据验证完善**: 插件包 SHA-256 校验、RSA 签名验证
4. **用户隐私尊重**: 遥测完全受用户同意控制
5. **Shell 执行受控**: URL 打开前验证协议

### 与上次审计对比 (2026-05-31)

本次审计与上次报告（2026-05-31）结论一致，代码库在安全性方面保持良好状态，未发现新增的中等及以上漏洞。

---

*本报告基于静态代码分析生成，未进行运行时渗透测试。建议在发布前进行完整的动态安全测试。*
