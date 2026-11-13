# 安装器安全模型

本文描述安装器与更新链的安全措施，以及**哪些措施实际生效、哪些还没有接上生产路径**。

最后核对：2026-11-14。

> **本次重要更正**：本文旧版把清单签名算法写成了 Ed25519，并给出 `[System.Security.Cryptography.Ed25519]` 的生成/签名示例。**那是错的**：.NET 10 不把 Ed25519 作为独立 BCL 类型暴露，代码里的实际选择是 **RSA-PSS-SHA256**（`ManifestSignatureVerifier` 的注释原文说明了这一点）。旧版示例脚本在 .NET 10 上无法运行。

---

## 1. 生效状态总览（先看这张表）

| 机制 | 实现 | 生产路径是否生效 |
|---|---|---|
| PLONDS 清单校验和 + 逐文件哈希 | `PlondsVerifier` / `VerifyFiles` | **是**（主路径） |
| delta 应用路径 RSA-PKCS1-SHA256 验签 | `UpdateSignatureVerifier` | 仅在非主流程的 delta 应用分支 |
| 安装器清单 RSA-PSS-SHA256 验签 | `ManifestSignatureVerifier` | **否**——生产路径没有调用点，仅测试引用 |
| Authenticode（WinVerifyTrust） | `AuthenticodeVerifier` | 默认**只报告**，不阻止执行 |
| .NET 运行时安装器签名强制 | `RuntimePrerequisite` | **是**——非 Microsoft 签名即拒绝 |
| 路径遍历防护 | `PathContainment` | **是**——宿主编排、安装器、包解压都调用 |
| CI 代码签名 | — | **不存在** |

"实现存在"与"生效"是两件事。上表右列就是全部区别。

---

## 2. 威胁模型与缓解

### 2.1 清单篡改

**威胁**：攻击者篡改远程清单，注入恶意版本或下载地址。

**缓解**：

- 安装器侧 `ManifestSignatureVerifier` 做 **RSA-PSS-SHA256** 离线签名验证，签名约定是"清单 URL 附加 `.sig`"（分离签名）。
- 公钥来源优先级：环境变量 `LANMOUNTAIN_PLONDS_MANIFEST_PUBKEY`（支持 PEM 或 Base64 的 SubjectPublicKeyInfo）→ 编译期内置 `EmbeddedPublicKeyPem`。
- **密钥未配置时验证被跳过并返回 `true`（宽松模式）**，只记一条警告。这是一种"先让流水线跑起来"的取舍，但它的直接后果是：**当前 `EmbeddedPublicKeyPem` 为空，且没有生产调用点，所以这条防护目前不保护任何东西**。
- 系统级缓解落在 PLONDS 主路径上：清单 `checksums` + 落地逐文件哈希（见 2.3）。

### 2.2 源注入（AddManifestSources）

**威胁**：通过清单源机制注入恶意源。

**缓解**：源列表由应用配置控制，不接受运行时外部输入。但请注意——**能"接受外部输入"的是清单源列表，不是清单内容**；源一旦被控制，内容级的防护就只剩签名，而签名目前未生效。

### 2.3 校验和自循环

**威胁**：攻击者同时改文件内容与对应校验和，使自校验失效。

**缓解（实际生效的那条）**：

- PLONDS 清单的 `checksums` 覆盖下载包（`md5:` / `sha256:` 前缀）。
- 文件落地后由 `VerifyFiles` / `VerifyFileEntry` 逐文件比对哈希。

这条防护的作用域是"清单本身可信"——它挡不住篡改清单的人。**清单可信依赖签名，而签名未接上生产路径**，这是当前最需要补的一环。

### 2.4 DLL 植入

**威胁**：攻击者在 `%LOCALAPPDATA%` 放置恶意 DLL，借 `SetDllDirectory` / PATH 操纵加载优先级。

**缓解**：已移除 `NativeDependencyBootstrapper`（原实现是 gzip 解压到 `%LOCALAPPDATA%\LanDesktopPLONDS\Installer\native\{arch}\{version}\`、`SetDllDirectory` 前置、`NativeLibrary.Load` 显式加载）。现在改为 `PublishSingleFile` + `IncludeNativeLibrariesForSelfExtract`（`EnableCompressionInSingleFile` 压缩存储），由 .NET 运行时解压到它管理的临时目录，**不做 DLL 搜索路径操纵**。

风险消解点很具体：不再有"可预测路径 + 搜索顺序前置"这个组合。

### 2.5 PE 文件完整性（Authenticode）

**威胁**：安装器捆绑或下载的可执行文件被替换。

**缓解**：

- `AuthenticodeVerifier` 用 Win32 `WinVerifyTrust`（`WINTRUST_ACTION_GENERIC_VERIFY_V2`）验证 PE 签名，再用 `X509Certificate.CreateFromSignedFile` 提取签名者主体用于审计。
- 三态结果：`Signed` / `Unsigned` / `Invalid`（`TRUST_E_NOSIGNATURE` = `0x800B0100` 判为未签名）。
- **默认为报告模式**：`EnforcementEnabled` 只在 `LANMOUNTAIN_INSTALLER_REQUIRE_SIGNED=1` 时为真。
- 非 Windows 平台直接返回 `Unsigned` 并跳过。

因为 CI 里没有签名步骤，当前所有产物在用户机器上都会落到"未签名"或"无效"，而报告模式不会阻止它们执行——这两件事必须一起看。

### 2.6 运行时前置

下载的 .NET 运行时安装器**必须带 Microsoft 签名**，否则拒绝（`RuntimePrerequisite`）。这是少数"默认就拒绝"的检查。

---

## 3. 组件接口

### ManifestSignatureVerifier

```
ManifestSignatureVerifier
├── Verify(byte[] manifestBytes, string signatureBase64) → bool
│     · 空清单 / 空签名 → false
│     · 未配置公钥 → true 并记警告（宽松模式）
│     · 否则 RSA-PSS-SHA256 验签
├── GetSignatureUrl(string manifestUrl) → string        // 追加 ".sig"
└── IsConfigured → bool
```

技术选型：.NET 10 BCL 的 `RSA` + `RSASignaturePadding.Pss` + `SHA256`；零外部 NuGet 依赖；AOT 安全（无反射、无动态代码生成）。

**为什么不是 Ed25519**：.NET 10 的 BCL 不把 Ed25519 作为独立类型暴露（仅存在于 MLDsa 复合方案中）。这是代码注释里的原话，也是选型理由。

### AuthenticodeVerifier

```
AuthenticodeVerifier
├── VerifyFile(string path) → AuthenticodeResult { Status, SignerSubject }
└── EnforcementEnabled → bool
```

技术选型：Win32 `WinVerifyTrust` P/Invoke + `X509Certificate.CreateFromSignedFile`；AOT 安全的 `DllImport` 声明。

---

## 4. 环境变量

| 变量 | 值 | 说明 |
|---|---|---|
| `LANMOUNTAIN_PLONDS_MANIFEST_PUBKEY` | PEM（含 `BEGIN PUBLIC KEY`）或 Base64 的 SubjectPublicKeyInfo | 清单验签公钥；未设置时验证被跳过 |
| `LANMOUNTAIN_INSTALLER_REQUIRE_SIGNED` | `1` | 启用 Authenticode 强制验证（默认关闭） |

---

## 5. 密钥配置与签名（RSA，非 Ed25519）

### 5.1 生成密钥对

```powershell
$ErrorActionPreference = 'Stop'

$keysDir = ".\keys"
New-Item -ItemType Directory -Path $keysDir -Force | Out-Null

# 生成 RSA 密钥（建议 3072 位或以上，与验证端 RSA 实现兼容）
$rsa = [System.Security.Cryptography.RSA]::Create(3072)

# 私钥：PEM，务必排除出版本控制
[System.IO.File]::WriteAllText("$keysDir\manifest_signing.key", $rsa.ExportRSAPrivateKeyPem())

# 公钥：PEM（验证端优先识别 PEM）
[System.IO.File]::WriteAllText("$keysDir\manifest_signing.pub", $rsa.ExportSubjectPublicKeyInfoPem())
```

### 5.2 部署公钥

验证端接受两种形态：

- **PEM**：直接把 `.pub` 文件内容设为 `LANMOUNTAIN_PLONDS_MANIFEST_PUBKEY`（含 `BEGIN PUBLIC KEY` 即可被识别）。
- **Base64**：`[Convert]::ToBase64String($rsa.ExportSubjectPublicKeyInfo())` 的结果。

长期方案是把它填进 `ManifestSignatureVerifier.EmbeddedPublicKeyPem`（编译期内置），目前该常量为空。

### 5.3 签名清单

```powershell
param(
    [Parameter(Mandatory = $true)][string] $ManifestPath,
    [Parameter(Mandatory = $true)][string] $PrivateKeyPath
)

$ErrorActionPreference = 'Stop'

$rsa = [System.Security.Cryptography.RSA]::Create()
$rsa.ImportFromPem((Get-Content -Path $PrivateKeyPath -Raw))

$manifestBytes = [System.IO.File]::ReadAllBytes($ManifestPath)
$signature = $rsa.SignData(
    $manifestBytes,
    [System.Security.Cryptography.HashAlgorithmName]::SHA256,
    [System.Security.Cryptography.RSASignaturePadding]::Pss)

[System.IO.File]::WriteAllText("$ManifestPath.sig", [System.Convert]::ToBase64String($signature))
```

**算法必须与验证端一致**：`SHA256` + `RSASignaturePadding.Pss`。写成 `Pkcs1` 会验证失败（注意：宿主 delta 应用路径用的确实是 `Pkcs1`，那是另一套，不要混）。

### 5.4 CI 集成现状

`plonds-uploader.yml` 目前**不做签名**。要让 2.1 的防护真正生效，需要：

1. 在 CI 里用私钥对清单签名并上传 `.sig`；
2. 把公钥内置进 `EmbeddedPublicKeyPem`（或注入环境变量）；
3. 给 `ManifestSignatureVerifier.Verify` 接上生产调用点。

三步缺一不可——只做前两步，验证器仍不会被调用。

---

## 6. 测试

```bash
dotnet test LanMountainDesktop.Tests --filter "FullyQualifiedName~InstallerSecurity"
```

覆盖：签名验证（有效 / 篡改 / 空输入）、签名 URL 计算、Authenticode 三态、强制验证模式读取。

另有 `ZeroUseMemberRatchetTests` 记录了一个事实：`.sig` URL 的拼法**只被测试使用**——这正是"验证器未接生产路径"的机器证据。

---

## 7. 已移除的风险：NativeDependencyBootstrapper

**原行为**：从嵌入资源 gzip 解压 `libHarfBuzzSharp.dll` / `libSkiaSharp.dll` 到 `%LOCALAPPDATA%\LanDesktopPLONDS\Installer\native\{arch}\{version}\`，`SetDllDirectory` 前置该路径，再 `NativeLibrary.Load`。

**风险**：

- DLL 植入：攻击者可在该可预测路径预置恶意 DLL
- 路径操纵：`SetDllDirectory` 改变进程级搜索顺序
- 版本目录可预测：可预先创建目标目录

**替代**：`PublishSingleFile` + `IncludeNativeLibrariesForSelfExtract` + `EnableCompressionInSingleFile`，运行时解压到 .NET 管理的临时目录。该类在全仓已不存在。

---

## 相关文档

- [更新系统架构](05-更新与发布/01-更新系统架构.md) —— 主路径实际用的是校验和而非签名
- [打包与构建](05-更新与发布/03-打包与构建.md) —— 签名步骤缺失的现状
