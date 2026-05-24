# LanMountainDesktop 安全审计报告

**项目**: LanMountainDesktop
**审计日期**: 2026-05-24
**审计范围**: 代码库安全性系统性评估
**审计方法**: 静态代码分析 + 架构审查 + 攻击面映射

---

## 执行摘要

本次审计对 LanMountainDesktop 代码库进行了全面的安全评估，系统性地检查了认证与访问控制、注入向量、外部交互以及敏感数据处理等高风险攻击面。

**审计结论**: 发现 **5 个已确认的中等及以上严重度漏洞**，均具有可论证的利用路径。

---

## 已确认漏洞

### 漏洞 #1 - PostHog API Key 硬编码（高严重度）

| 属性 | 详情 |
|------|------|
| **严重度** | 高 |
| **CWE** | CWE-798 - 使用硬编码凭证 |
| **位置** | [PostHogUsageTelemetryService.cs:14](file:///d:/github/LanMountainDesktop/LanMountainDesktop/Services/PostHogUsageTelemetryService.cs#L14) |
| **攻击者画像** | 源代码仓库的任何访问者（通过代码泄露、供应链攻击或Git历史） |
| **可控输入** | 无（静态硬编码密钥） |

**代码路径**:
```csharp
// PostHogUsageTelemetryService.cs:14
private const string PostHogApiKey = "phc_bhQZvKDDfsEdLT6kkRFvrWMT8Pc5aCGGsnxoc5ijSf9";
```

**影响**:
- 攻击者可滥用此 API Key 向 PostHog 项目发送伪造遥测数据
- 可能导致遥测数据污染，干扰产品分析决策
- API Key 暴露在公开仓库中，任何人都能获取并滥用

**修复建议**:
```csharp
private static string GetPostHogApiKey()
{
    var key = Environment.GetEnvironmentVariable("POSTHOG_API_KEY");
    if (string.IsNullOrEmpty(key))
        throw new InvalidOperationException("PostHog API key not configured.");
    return key;
}
```

---

### 漏洞 #2 - Sentry DSN 硬编码（高严重度）

| 属性 | 详情 |
|------|------|
| **严重度** | 高 |
| **CWE** | CWE-798 - 使用硬编码凭证 |
| **位置** | [SentryCrashTelemetryService.cs:15](file:///d:/github/LanMountainDesktop/LanMountainDesktop/Services/SentryCrashTelemetryService.cs#L15) |
| **攻击者画像** | 源代码仓库的任何访问者 |
| **可控输入** | 无（静态硬编码密钥） |

**代码路径**:
```csharp
// SentryCrashTelemetryService.cs:15
private const string SentryDsn = "https://f2aad3a1c63b5f2213ad82683ce93c06@o4511049423257600.ingest.us.sentry.io/4511049425813504";
```

**影响**:
- Sentry DSN 等同于项目的访问凭证
- 攻击者可利用此 DSN 向项目发送伪造崩溃报告
- 可能导致崩溃数据污染，干扰错误追踪
- 如 DSN 配置不当，可导致敏感崩溃信息被发送至攻击者控制的端点

**修复建议**:
```csharp
private static string GetSentryDsn()
{
    var dsn = Environment.GetEnvironmentVariable("SENTRY_DSN");
    if (string.IsNullOrEmpty(dsn))
        throw new InvalidOperationException("Sentry DSN not configured.");
    return dsn;
}
```

---

### 漏洞 #3 - 小米天气 API 签名密钥硬编码（高严重度）

| 属性 | 详情 |
|------|------|
| **严重度** | 高 |
| **CWE** | CWE-798 - 使用硬编码凭证 |
| **位置** | [XiaomiWeatherService.cs:25](file:///d:/github/LanMountainDesktop/LanMountainDesktop/Services/XiaomiWeatherService.cs#L25) |
| **攻击者画像** | 源代码仓库的任何访问者 |
| **可控输入** | 无（静态硬编码密钥） |

**代码路径**:
```csharp
// XiaomiWeatherService.cs:25
public string Sign { get; init; } = "zUFJoAR2ZVrDy1vF3D07";
```

**影响**:
- API 签名凭证暴露在公开仓库
- 攻击者可能利用此凭证访问天气服务 API
- 可能导致 API 配额滥用或服务成本增加
- 如密钥具有更高权限，可能导致数据泄露

**修复建议**:
```csharp
public string Sign { get; init; } = Environment.GetEnvironmentVariable("XIAOMI_WEATHER_SIGN") ?? "";
```

---

### 漏洞 #4 - Sentry PII 收集配置（中等严重度）

| 属性 | 详情 |
|------|------|
| **严重度** | 中等 |
| **CWE** | CWE-359 - 个人身份信息（PII）意外暴露 |
| **位置** | [SentryCrashTelemetryService.cs:212](file:///d:/github/LanMountainDesktop/LanMountainDesktop/Services/SentryCrashTelemetryService.cs#L212) |
| **攻击者画像** | Sentry 后端管理员、内部威胁或数据泄露事件 |
| **可控输入** | 用户环境的机器名、用户名、IP地址等系统信息 |

**代码路径**:
```csharp
// SentryCrashTelemetryService.cs:212
options.SendDefaultPii = true;
```

**影响**:
- `SendDefaultPii = true` 配置会收集和上报用户 IP 地址
- 可能违反隐私法规（如 GDPR、中国个人信息保护法）要求
- 在崩溃报告中可能暴露用户敏感信息
- 用户未明确同意即被收集 PII

**修复建议**:
```csharp
// 根据用户同意状态动态设置
options.SendDefaultPii = TelemetryEnvironmentInfo.IsTelemetryPiiAllowed();
```

---

### 漏洞 #5 - SSL 证书验证被禁用（中等严重度）

| 属性 | 详情 |
|------|------|
| **严重度** | 中等 |
| **CWE** | CWE-295 - 证书验证不正确 |
| **位置** | [RecommendationDataService.cs:105](file:///d:/github/LanMountainDesktop/LanMountainDesktop/Services/RecommendationDataService.cs#L105) |
| **攻击者画像** | 网络中间人攻击者（在同一网络环境的攻击者） |
| **可控输入** | 用户网络流量 |
| **利用路径** | 用户发起API请求 → 攻击者拦截流量 → 伪造响应 |

**代码路径**:
```csharp
// RecommendationDataService.cs:100-106
var handler = new HttpClientHandler
{
    SslProtocols = System.Security.Authentication.SslProtocols.Tls12 |
                   System.Security.Authentication.SslProtocols.Tls13,
    ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
};
```

**影响**:
- 禁用了服务器证书验证，使应用程序容易受到中间人（MITM）攻击
- 攻击者可以拦截和篡改 API 响应数据
- 可能导致注入恶意内容或数据操纵
- 即使使用 TLS 1.2/1.3，证书验证被禁用仍然不安全

**修复建议**:
```csharp
var handler = new HttpClientHandler
{
    SslProtocols = System.Security.Authentication.SslProtocols.Tls12 |
                   System.Security.Authentication.SslProtocols.Tls13,
    // 删除 ServerCertificateCustomValidationCallback 或实现正确的验证
};
```

---

## 未发现漏洞的区域

经过系统性审计，以下区域未发现中等及以上严重度的已确认漏洞：

### 认证与访问控制
- 单实例服务实现正确（使用互斥体）
- IPC 通信使用命名管道，无明显认证绕过风险
- 插件隔离使用独立进程边界
- 插件加载使用 AppDomain/AssemblyLoadContext 隔离

### 注入向量
- SQLite 使用参数化查询，无 SQL 注入风险 ([ComponentDomainStorage.cs](file:///d:/github/LanMountainDesktop/LanMountainDesktop/Services/Settings/ComponentDomainStorage.cs))
- JSON 反序列化使用强类型上下文 (`JsonSerializerContext`)，无反序列化漏洞
- 文件路径操作使用 `Path.Combine` 和 `Path.GetInvalidFileNameChars()` 过滤
- 未发现命令执行注入（Process.Start 使用固定参数）

### 外部交互
- HTTP 请求使用 `HttpClient` 和超时配置
- Webhook/回调 URL 使用 `Uri.EscapeDataString` 编码
- 下载服务验证目标路径，无路径遍历风险
- URL 参数正确使用编码函数

### 敏感数据处理
- 数据库本地存储，使用 WAL 模式
- 设置数据通过 JSON 序列化存储在用户目录
- 日志文件路径正确隔离在应用数据目录

---

## 架构安全评估

| 组件 | 安全评级 | 说明 |
|------|----------|------|
| 插件系统 | 良好 | 使用独立进程隔离 |
| IPC 通信 | 良好 | 命名管道通信，进程边界隔离 |
| 更新系统 | 良好 | 支持签名验证 |
| 遥测系统 | **需改进** | 存在硬编码凭证和 PII 配置问题 |
| 数据存储 | 良好 | 使用标准加密实践 |
| 网络通信 | **需改进** | 存在证书验证绕过问题 |

---

## 修复优先级

| 优先级 | 漏洞 | 严重度 | 预计工作量 |
|--------|------|--------|------------|
| P0 - 紧急 | #1 PostHog API Key | 高 | 低 |
| P0 - 紧急 | #2 Sentry DSN | 高 | 低 |
| P0 - 紧急 | #3 Xiaomi Weather Sign | 高 | 低 |
| P1 - 高 | #4 SendDefaultPii | 中 | 低 |
| P1 - 高 | #5 SSL 证书验证禁用 | 中 | 中 |

---

## 建议的安全改进

1. **实施密钥管理**: 使用环境变量或密钥管理服务存储所有 API 凭证
2. **添加密钥扫描**: 在 CI/CD 流程中集成 secrets scanning（如 GitGuardian、trufflehog）
3. **隐私合规审查**: 确认遥测数据收集符合当地隐私法规要求
4. **证书验证修复**: 移除禁用的证书验证，确保 HTTPS 通信安全
5. **代码审计**: 建议进行定期安全审计

---

*报告生成工具: 自动安全审计系统*
*审计方法: 静态代码分析 + 架构审查 + 攻击面映射*
