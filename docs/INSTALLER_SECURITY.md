# 安装器安全模型

## 威胁模型

### 1. 清单篡改（Manifest Tampering）

**威胁**：攻击者篡改远程清单文件，注入恶意插件版本或下载地址。

**缓解措施**：
- `ManifestSignatureVerifier` 使用 Ed25519 离线签名验证清单完整性
- 签名约定：清单 URL 附加 `.sig` 后缀即为对应签名文件（分离签名）
- 公钥通过环境变量 `LANMOUNTAIN_PLONDS_MANIFEST_PUBKEY` 或编译时常量配置
- 密钥未配置时（占位符模式），验证自动跳过并记录警告——流水线可正常运行

### 2. 源注入（Source Injection via AddManifestSources）

**威胁**：攻击者通过 `AddManifestSources` 机制注入恶意清单源。

**缓解措施**：
- 清单源列表由应用配置控制，不接受运行时外部输入
- 每个源的清单均经过 `ManifestSignatureVerifier` 验证
- 恶意源的篡改清单无法通过签名验证

### 3. 校验和自循环（Checksum Self-Consistency Loop）

**威胁**：攻击者同时修改文件内容和对应校验和，使自校验失效。

**缓解措施**：
- 清单签名覆盖完整清单内容（包括校验和字段）
- 即使攻击者修改了校验和，签名验证仍会失败
- 签名使用 Ed25519 公钥密码学，无法伪造

### 4. DLL 植入（DLL Planting / DLL Hijacking）

**威胁**：攻击者在 `%LOCALAPPDATA%` 路径放置恶意 DLL，利用 `SetDllDirectory` 和 PATH 操纵加载优先级。

**缓解措施**：
- 已移除 `NativeDependencyBootstrapper`（原始 gzip 提取 + PATH 前置 + SetDllDirectory 机制）
- 原生库现通过 `PublishSingleFile` + `IncludeNativeLibrariesForSelfExtract` 正常打包
- `EnableCompressionInSingleFile` 确保原生库压缩存储在单文件中
- 运行时自动解压到安全的临时目录，无 DLL 搜索路径操纵

### 5. PE 文件完整性（Authenticode Verification）

**威胁**：安装器捆绑或下载的可执行文件被替换或篡改。

**缓解措施**：
- `AuthenticodeVerifier` 使用 WinVerifyTrust API 验证 PE 文件签名
- 提取签名者主体信息用于审计记录
- 默认为**报告模式**（不阻止执行）
- 环境变量 `LANMOUNTAIN_INSTALLER_REQUIRE_SIGNED=1` 启用强制验证模式

## 组件架构

### ManifestSignatureVerifier

```
ManifestSignatureVerifier
├── Verify(byte[] manifestBytes, string signatureBase64) → bool
├── GetSignatureUrl(string manifestUrl) → string
└── IsConfigured → bool
```

**技术选型**：
- **Ed25519**（.NET 10 BCL 内置，`System.Security.Cryptography.Ed25519`）
- 零外部 NuGet 包依赖
- AOT 安全（无反射，无动态代码生成）
- 签名大小：64 字节；公钥大小：32 字节

### AuthenticodeVerifier

```
AuthenticodeVerifier
├── VerifyFile(string path) → AuthenticodeResult
├── EnforcementEnabled → bool
└── AuthenticodeResult { Status, SignerSubject }
```

**技术选型**：
- Win32 `WinVerifyTrust` P/Invoke（`WINTRUST_ACTION_GENERIC_VERIFY_V2`）
- `X509Certificate.CreateFromSignedFile` 提取签名者信息
- AOT 安全的 DllImport 声明

## 密钥配置运行手册

### 1. 密钥对生成

```powershell
# 使用 .NET 10 内置 Ed25519 工具生成密钥对
# 此脚本生成私钥和公钥文件

$ErrorActionPreference = 'Stop'

# 生成 Ed25519 密钥对
$privateKeyBytes = [byte[]]::new(64)  # Ed25519 种子 + 公钥
$publicKeyBytes = [byte[]]::new(32)

# 使用 .NET 的 Ed25519 类生成密钥
Add-Type -AssemblyName System.Security.Cryptography
$key = [System.Security.Cryptography.Ed25519]::GenerateKeyPair()
$privateKeyBytes = $key.PrivateKey
$publicKeyBytes = $key.PublicKey

# 保存密钥对（私钥需妥善保管，不纳入版本控制）
$keysDir = ".\keys"
New-Item -ItemType Directory -Path $keysDir -Force | Out-Null

[System.IO.File]::WriteAllBytes("$keysDir\manifest_signing.key", $privateKeyBytes)
[System.IO.File]::WriteAllBytes("$keysDir\manifest_signing.pub", $publicKeyBytes)

Write-Host "密钥对已生成："
Write-Host "  私钥: $keysDir\manifest_signing.key"
Write-Host "  公钥: $keysDir\manifest_signing.pub"
Write-Host ""
Write-Host "⚠️  请务必将私钥文件从版本控制中排除！"
```

### 2. 公钥部署

将公钥（Base64 编码）设置为编译时常量或 CI 环境变量：

```powershell
# 读取公钥并转换为 Base64
$publicKey = [System.IO.File]::ReadAllBytes("$keysDir\manifest_signing.pub")
$publicKeyBase64 = [System.Convert]::ToBase64String($publicKey)

Write-Host "公钥 Base64: $publicKeyBase64"
```

**部署位置**：
- 开发环境：设置环境变量 `LANMOUNTAIN_PLONDS_MANIFEST_PUBKEY`
- CI/CD：在构建管道中注入环境变量
- 生产环境：编译时嵌入或安全存储在配置中心

### 3. 清单签名（CI/CD 集成）

```powershell
# 清单签名 PowerShell 片段（用于 CI/CD 管道）
param(
    [Parameter(Mandatory = $true)]
    [string] $ManifestPath,

    [Parameter(Mandatory = $true)]
    [string] $PrivateKeyPath
)

$ErrorActionPreference = 'Stop'

# 读取清单内容
$manifestBytes = [System.IO.File]::ReadAllBytes($ManifestPath)

# 读取私钥
$privateKeyBytes = [System.IO.File]::ReadAllBytes($PrivateKeyPath)

# 使用 Ed25519 签名
$signature = [System.Security.Cryptography.Ed25519]::SignData($manifestBytes, $privateKeyBytes)
$signatureBase64 = [System.Convert]::ToBase64String($signature)

# 写入签名文件（manifest.json.sig）
$sigPath = "$ManifestPath.sig"
[System.IO.File]::WriteAllText($sigPath, $signatureBase64)

Write-Host "清单签名完成："
Write-Host "  清单: $ManifestPath"
Write-Host "  签名: $sigPath"
Write-Host "  签名者: CI/CD Pipeline"
```

### 4. CI/CD 管道集成示例

```yaml
# GitHub Actions 示例
- name: Sign manifest
  run: |
    $manifest = Get-Content "releases/manifest.json" -Raw
    $manifestBytes = [System.Text.Encoding]::UTF8.GetBytes($manifest)
    $key = [System.Convert]::FromBase64String("${{ secrets.MANIFEST_SIGNING_KEY }}")
    $sig = [System.Security.Cryptography.Ed25519]::SignData($manifestBytes, $key)
    [System.IO.File]::WriteAllText("releases/manifest.json.sig", [System.Convert]::ToBase64String($sig))
```

## 环境变量参考

| 变量名 | 值 | 说明 |
|--------|-----|------|
| `LANMOUNTAIN_PLONDS_MANIFEST_PUBKEY` | Base64 编码的 Ed25519 公钥 | 清单签名验证公钥 |
| `LANMOUNTAIN_INSTALLER_REQUIRE_SIGNED` | `1` | 启用 Authenticode 强制验证（默认关闭） |

## 测试

运行安装器安全模块测试：

```bash
dotnet test LanMountainDesktop.Tests.csproj --filter "FullyQualifiedName~InstallerSecurity"
```

测试覆盖：
- Ed25519 签名验证（有效/篡改/空输入）
- 签名 URL 计算
- Authenticode 验证（签名文件/未签名文件/不存在文件）
- 强制验证模式读取

## 已移除的安全风险

### NativeDependencyBootstrapper（已删除）

**原始行为**：
1. 从嵌入资源中 gzip 解压 `libHarfBuzzSharp.dll` 和 `libSkiaSharp.dll` 到 `%LOCALAPPDATA%\LanDesktopPLONDS\Installer\native\{arch}\{version}\`
2. 使用 `SetDllDirectory` 将该路径添加到进程 DLL 搜索路径首位
3. 使用 `NativeLibrary.Load` 显式加载 DLL

**安全风险**：
- DLL 植入：攻击者可在 `%LOCALAPPDATA%` 路径放置恶意 DLL
- 路径操纵：`SetDllDirectory` 改变进程级 DLL 搜索顺序
- 版本目录可预测：攻击者可预先创建目标版本目录

**替代方案**：
- `PublishSingleFile` + `IncludeNativeLibrariesForSelfExtract` 自动打包原生库
- `EnableCompressionInSingleFile` 压缩存储
- 运行时解压到安全的临时目录（由 .NET 运行时管理）
