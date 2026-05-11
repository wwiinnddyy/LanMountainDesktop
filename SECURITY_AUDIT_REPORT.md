# 安全审计报告

**项目**: LanMountainDesktop
**审计日期**: 2026-05-11
**审计范围**: 整体代码库安全性评估
**审计方法**: 自动化静态代码分析 + 架构审查

---

## 执行摘要

本次审计对 LanMountainDesktop 代码库进行了系统性安全评估，重点关注认证与访问控制、注入向量、外部交互以及敏感数据处理等高风险攻击面。

**审计结论**: 发现 **4 个已确认的中等及以上严重度漏洞**，建议立即修复。

---

## 已确认漏洞

### 漏洞 #1 - PostHog API Key 硬编码（高严重度）

| 属性 | 详情 |
|------|------|
| **严重度** | 高 |
| **CWE** | CWE-798 - 使用硬编码凭证 |
| **位置** | `LanMountainDesktop/Services/PostHogUsageTelemetryService.cs:14` |
| **攻击者画像** | 源代码仓库的任何访问者（包括外部攻击者通过代码泄露或供应链攻击） |
| **可控输入** | 无（静态硬编码密钥） |

**代码路径**:
```csharp
// PostHogUsageTelemetryService.cs:14
private const string PostHogApiKey = "phc_bhQZvKDDfsEdLT6kkRFvrWMT8Pc5aCGGsnxoc5ijSf9";
```

**影响**:
- 攻击者可能滥用此 API Key 向 PostHog 项目发送伪造遥测数据
- 可能导致遥测数据污染或服务滥用
- API Key 暴露在公开仓库中，任何人都能获取

**修复建议**:
```csharp
private const string PostHogApiKey = Environment.GetEnvironmentVariable("POSTHOG_API_KEY")
    ?? throw new InvalidOperationException("PostHog API key not configured.");
```

---

### 漏洞 #2 - Sentry DSN 硬编码（高严重度）

| 属性 | 详情 |
|------|------|
| **严重度** | 高 |
| **CWE** | CWE-798 - 使用硬编码凭证 |
| **位置** | `LanMountainDesktop/Services/SentryCrashTelemetryService.cs:15` |
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
- 可能导致崩溃数据污染或敏感信息收集

**修复建议**:
```csharp
private const string SentryDsn = Environment.GetEnvironmentVariable("SENTRY_DSN")
    ?? throw new InvalidOperationException("Sentry DSN not configured.");
```

---

### 漏洞 #3 - 小米天气 API 签名密钥硬编码（高严重度）

| 属性 | 详情 |
|------|------|
| **严重度** | 高 |
| **CWE** | CWE-798 - 使用硬编码凭证 |
| **位置** | `LanMountainDesktop/Services/XiaomiWeatherService.cs:25` |
| **攻击者画像** | 源代码仓库的任何访问者 |
| **可控输入** | 无（静态硬编码密钥） |

**代码路径**:
```csharp
// XiaomiWeatherService.cs:25
public string Sign { get; init; } = "zUFJoAR2ZVrDy1vF3D07";
```

**影响**:
- 第三方 API 凭证暴露在公开仓库
- 可能导致天气服务被滥用
- 如密钥有权限限制，攻击者可能突破限制

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
| **位置** | `LanMountainDesktop/Services/SentryCrashTelemetryService.cs:212` |
| **攻击者画像** | Sentry 后端管理员、内部威胁或数据泄露事件 |
| **可控输入** | 用户环境的机器名、用户名等系统信息 |
| **利用路径** | `程序启动 → TelemetryIdentityService.Initialize()` → 遥测数据上报 |

**代码路径**:
```csharp
// SentryCrashTelemetryService.cs:212
options.SendDefaultPii = true;
```

**影响**:
- `SendDefaultPii = true` 配置会收集和上报用户 IP 地址
- 可能违反隐私法规（如 GDPR）要求
- 在崩溃报告中可能暴露用户敏感信息

**修复建议**:
```csharp
options.SendDefaultPii = false;  // 默认收集 PII
options.SendDefaultPii = TelemetryEnvironmentInfo.IsTelemetryPiiAllowed();  // 或根据用户同意状态动态设置
```

---

## 未发现漏洞的区域

经过系统性审计，以下区域未发现中等及以上严重度的已确认漏洞：

### 认证与访问控制
- 单实例服务实现正确（使用互斥体）
- IPC 通信使用命名管道，无明显认证绕过风险
- 插件隔离使用独立进程边界

### 注入向量
- SQLite 使用参数化查询，无 SQL 注入风险
- JSON 反序列化使用强类型上下文，无反序列化漏洞
- 文件路径操作使用 `Path.Combine`，有基本的路径遍历防护
- 未发现命令执行注入

### 外部交互
- HTTP 请求正确使用 `HttpClient` 和超时配置
- Webhook/回调 URL 使用 `Uri.EscapeDataString` 编码
- 下载服务验证目标路径，无路径遍历风险

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

---

## 修复优先级

| 优先级 | 漏洞 | 预计工作量 |
|--------|------|------------|
| P0 - 紧急 | #1 PostHog API Key | 低 |
| P0 - 紧急 | #2 Sentry DSN | 低 |
| P0 - 紧急 | #3 Xiaomi Weather Sign | 低 |
| P1 - 高 | #4 SendDefaultPii | 低 |

---

## 建议的安全改进

1. **实施密钥管理**: 使用环境变量或密钥管理服务（如 Azure Key Vault、AWS Secrets Manager）存储所有 API 凭证
2. **添加密钥扫描**: 在 CI/CD 流程中集成 secrets scanning（如 GitGuardian、trufflehog）
3. **隐私合规审查**: 确认遥测数据收集符合当地隐私法规要求
4. **代码审计**: 建议进行定期安全审计

---

*报告生成工具: 自动安全审计系统*
*审计方法: 静态代码分析 + 架构审查*
