# 更新系统文档

> LanMountainDesktop 增量更新和版本管理系统

## 目录

- [概述](#概述)
- [更新流程](#更新流程)
- [增量更新](#增量更新)
- [原子化更新](#原子化更新)
- [版本回退](#版本回退)
- [CI/CD 集成](#cicd-集成)
- [安全机制](#安全机制)

## 概述

LanMountainDesktop 使用基于 GitHub Release 的增量更新系统,支持:
- ✅ 增量更新 (只下载变更文件)
- ✅ 原子化更新 (保证完整性)
- ✅ 签名验证 (RSA)
- ✅ 版本回退
- ✅ 更新频道 (Stable/Preview)
- ✅ 静默更新 (后台下载)

## 更新流程

### 完整更新流程图

```
Launcher 启动
    ↓
UpdateCheckService.CheckForUpdateAsync()
    ├─ 调用 GitHub Release API
    ├─ 根据更新频道过滤版本
    └─ 对比当前版本和最新版本
    ↓
有新版本? ──No→ 继续启动
    ↓ Yes
UpdateEngineService.DownloadAsync()
    ├─ 下载 files-{version}.json
    ├─ 下载 files-{version}.json.sig
    └─ 下载 delta-{old}-to-{new}.zip (或完整包)
    ↓
保存到 .launcher/update/incoming/
    ↓
下次启动时
    ↓
UpdateEngineService.ApplyPendingUpdate()
    ├─ 验证签名
    ├─ 创建 app-{new}/ 目录
    ├─ 标记 .partial
    ├─ 解压增量包
    ├─ 从旧版本复用未变更文件
    ├─ 验证所有文件 SHA256
    ├─ 删除 .partial
    ├─ 添加 .current 到新版本
    ├─ 标记旧版本 .destroy
    └─ 保存更新快照
    ↓
启动新版本
    ↓
清理旧版本 (.destroy)
```

### 更新频道

| 频道 | 说明 | GitHub Release 过滤 |
|------|------|---------------------|
| **Stable** | 正式版 | `prerelease=false` |
| **Preview** | 预览版 | 所有版本 (包括 `prerelease=true`) |

用户可以在设置中切换更新频道。

## 增量更新

### 增量包结构

**GitHub Release Assets:**
```
LanMountainDesktop-v1.0.1/
├── LanMountainDesktop-Setup-1.0.1-x64.exe  # 完整安装包
├── app-1.0.1.zip                            # 完整应用包
├── delta-1.0.0-to-1.0.1.zip                # 增量包
├── files-1.0.1.json                         # 文件清单
└── files-1.0.1.json.sig                     # RSA 签名
```

### files.json 格式

```json
{
  "FromVersion": "1.0.0",
  "ToVersion": "1.0.1",
  "GeneratedAt": "2025-01-01T00:00:00Z",
  "Files": [
    {
      "Path": "LanMountainDesktop.exe",
      "Action": "replace",
      "Sha256": "abc123...",
      "Size": 1024000,
      "ArchivePath": "LanMountainDesktop.exe"
    },
    {
      "Path": "LanMountainDesktop.dll",
      "Action": "reuse",
      "Sha256": "def456...",
      "Size": 512000
    },
    {
      "Path": "OldFile.dll",
      "Action": "delete"
    }
  ]
}
```

### 文件操作类型

| Action | 说明 | 处理方式 |
|--------|------|----------|
| `add` | 新增文件 | 从增量包解压 |
| `replace` | 替换文件 | 从增量包解压 |
| `reuse` | 复用文件 | 从旧版本复制 |
| `delete` | 删除文件 | 不操作 (新版本中不存在) |

### 增量包生成

使用 `Generate-DeltaPackage.ps1` 脚本:

```powershell
./scripts/Generate-DeltaPackage.ps1 `
  -PreviousVersion "1.0.0" `
  -CurrentVersion "1.0.1" `
  -PreviousDir "./publish/app-1.0.0" `
  -CurrentDir "./publish/app-1.0.1" `
  -OutputDir "./delta-output"
```

**生成过程:**
1. 扫描两个版本的所有文件
2. 计算每个文件的 SHA256
3. 对比哈希值,识别变更
4. 只打包变更的文件到 `delta.zip`
5. 生成 `files.json` 清单

**优势:**
- 大幅减少下载大小 (通常只有 10-30% 的完整包大小)
- 加快更新速度
- 节省带宽

## 原子化更新

### 原子化保证

更新过程中的任何失败都会触发自动回滚,确保应用始终处于可用状态。

**关键机制:**
1. **`.partial` 标记** - 更新过程中保持此标记
2. **旧版本保留** - 直到新版本验证通过
3. **SHA256 验证** - 确保所有文件完整性
4. **快照记录** - 记录更新前后状态
5. **自动回滚** - 失败时恢复到旧版本

### 更新步骤详解

```csharp
public LauncherResult ApplyPendingUpdate()
{
    // 1. 验证签名
    var verifyResult = VerifySignature(fileMapPath, signaturePath);
    if (!verifyResult.Success)
        return Failed("signature_failed");
    
    // 2. 创建新版本目录
    var targetDeployment = _deploymentLocator.BuildNextDeploymentDirectory(targetVersion);
    Directory.CreateDirectory(targetDeployment);
    
    // 3. 标记为未完成
    File.WriteAllText(Path.Combine(targetDeployment, ".partial"), string.Empty);
    
    // 4. 保存快照
    var snapshot = new SnapshotMetadata { ... };
    SaveSnapshot(snapshotPath, snapshot);
    
    try
    {
        // 5. 解压增量包
        ZipFile.ExtractToDirectory(archivePath, extractRoot);
        
        // 6. 应用文件操作
        foreach (var file in fileMap.Files)
        {
            ApplyFileEntry(file, currentDeployment, targetDeployment, extractRoot);
        }
        
        // 7. 验证所有文件
        foreach (var file in fileMap.Files)
        {
            var actualHash = ComputeSha256Hex(fullPath);
            if (actualHash != file.Sha256)
                throw new InvalidOperationException("Hash mismatch");
        }
        
        // 8. 激活新版本
        ActivateDeployment(currentDeployment, targetDeployment);
        
        // 9. 更新快照状态
        snapshot.Status = "applied";
        SaveSnapshot(snapshotPath, snapshot);
        
        // 10. 清理
        CleanupIncomingArtifacts();
        
        return Success();
    }
    catch (Exception ex)
    {
        // 自动回滚
        TryRollbackOnFailure(snapshot);
        snapshot.Status = "rolled_back";
        SaveSnapshot(snapshotPath, snapshot);
        return Failed("apply_failed", ex.Message);
    }
}
```

### 失败回滚

```csharp
private void TryRollbackOnFailure(SnapshotMetadata snapshot)
{
    try
    {
        // 1. 删除未完成的新版本目录
        if (Directory.Exists(snapshot.TargetDirectory))
            Directory.Delete(snapshot.TargetDirectory, true);
        
        // 2. 移除旧版本的 .destroy 标记
        var destroyMarker = Path.Combine(snapshot.SourceDirectory, ".destroy");
        if (File.Exists(destroyMarker))
            File.Delete(destroyMarker);
        
        // 3. 确保旧版本有 .current 标记
        var currentMarker = Path.Combine(snapshot.SourceDirectory, ".current");
        if (!File.Exists(currentMarker))
            File.WriteAllText(currentMarker, string.Empty);
    }
    catch
    {
        // 记录错误但不抛出
    }
}
```

## 版本回退

### 手动回退

```bash
LanMountainDesktop.Launcher.exe update rollback
```

### 回退流程

```csharp
public LauncherResult RollbackLatest()
{
    // 1. 读取最新快照
    var snapshotPath = Directory
        .EnumerateFiles(_snapshotsRoot, "*.json")
        .OrderByDescending(File.GetCreationTimeUtc)
        .FirstOrDefault();
    
    var snapshot = JsonSerializer.Deserialize<SnapshotMetadata>(
        File.ReadAllText(snapshotPath));
    
    // 2. 获取当前部署
    var currentDeployment = _deploymentLocator.FindCurrentDeploymentDirectory();
    
    // 3. 激活旧版本
    ActivateDeployment(currentDeployment, snapshot.SourceDirectory);
    
    // 4. 更新快照状态
    snapshot.Status = "manual_rollback";
    SaveSnapshot(snapshotPath, snapshot);
    
    return Success($"Rolled back to {snapshot.SourceVersion}");
}
```

### 快照格式

```json
{
  "SnapshotId": "abc123...",
  "SourceVersion": "1.0.0",
  "TargetVersion": "1.0.1",
  "CreatedAt": "2025-01-01T00:00:00Z",
  "SourceDirectory": "C:\\...\\app-1.0.0",
  "TargetDirectory": "C:\\...\\app-1.0.1",
  "Status": "applied"
}
```

## CI/CD 集成

### GitHub Actions 工作流

**release.yml 关键步骤:**

```yaml
- name: Restructure for Launcher
  run: |
    # 重组为 app-{version} 结构
    $appDir = "app-${{ needs.prepare.outputs.version }}"
    New-Item -ItemType Directory -Path "publish-launcher/windows-x64"
    Move-Item -Path "publish/windows-x64" -Destination "publish-launcher/windows-x64/$appDir"
    
    # 移动 Launcher 到根目录
    Move-Item -Path "publish-launcher/windows-x64/$appDir/Launcher/*" -Destination "publish-launcher/windows-x64/"
    
    # 创建 .current 标记
    New-Item -ItemType File -Path "publish-launcher/windows-x64/$appDir/.current"

- name: Generate Delta Package
  run: |
    # 生成 files.json
    $files = Get-ChildItem -Path $currentAppPath -Recurse -File
    # ... 计算 SHA256 ...
    
    # 创建完整应用包
    Compress-Archive -Path "$currentAppPath\*" -DestinationPath "app-$version.zip"

- name: Upload Delta Package
  uses: actions/upload-artifact@v4
  with:
    name: delta-package-windows-x64
    path: delta-output/*
```

### 增量包生成脚本

**scripts/Generate-DeltaPackage.ps1:**
- 对比两个版本目录
- 识别新增、修改、删除的文件
- 只打包变更文件
- 生成 `files.json` 清单

**scripts/Sign-FileMap.ps1:**
- 使用 RSA 私钥签名 `files.json`
- 生成 `files.json.sig`

## 安全机制

### RSA 签名验证

**签名生成 (CI):**
```powershell
# 读取私钥
$privateKeyPem = Get-Content -Path $PrivateKeyPath -Raw
$rsa = [System.Security.Cryptography.RSA]::Create()
$rsa.ImportFromPem($privateKeyPem)

# 签名
$jsonBytes = [System.IO.File]::ReadAllBytes($FilesJsonPath)
$signature = $rsa.SignData(
    $jsonBytes,
    [System.Security.Cryptography.HashAlgorithmName]::SHA256,
    [System.Security.Cryptography.RSASignaturePadding]::Pkcs1
)

# 保存为 Base64
$signatureBase64 = [Convert]::ToBase64String($signature)
Set-Content -Path "$FilesJsonPath.sig" -Value $signatureBase64
```

**签名验证 (Launcher):**
```csharp
private (bool Success, string Message) VerifySignature(
    string fileMapPath,
    string signaturePath)
{
    // 1. 读取公钥
    var publicKeyPath = Path.Combine(_launcherRoot, "update", "public-key.pem");
    using var rsa = RSA.Create();
    rsa.ImportFromPem(File.ReadAllText(publicKeyPath));
    
    // 2. 读取签名
    var signatureBase64 = File.ReadAllText(signaturePath).Trim();
    var signature = Convert.FromBase64String(signatureBase64);
    
    // 3. 验证
    var jsonBytes = File.ReadAllBytes(fileMapPath);
    var isValid = rsa.VerifyData(
        jsonBytes,
        signature,
        HashAlgorithmName.SHA256,
        RSASignaturePadding.Pkcs1);
    
    return isValid
        ? (true, "ok")
        : (false, "Signature verification failed");
}
```

### 文件完整性验证

```csharp
// 验证所有文件的 SHA256
foreach (var file in fileMap.Files)
{
    if (!NeedsVerification(file))
        continue;
    
    var fullPath = Path.Combine(targetDeployment, file.Path);
    var actualHash = ComputeSha256Hex(fullPath);
    
    if (!string.Equals(actualHash, file.Sha256, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"File hash mismatch for '{file.Path}'.");
    }
}
```

### 路径遍历防护

```csharp
private static void EnsurePathWithinRoot(string targetPath, string rootPath)
{
    var fullTarget = Path.GetFullPath(targetPath);
    var fullRoot = Path.GetFullPath(rootPath);
    
    if (!fullTarget.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"Path traversal detected: {targetPath}");
    }
}
```

## 相关文档

- [Launcher 架构文档](LAUNCHER.md)
- [构建和部署指南](BUILD_AND_DEPLOY.md)
- [故障排除指南](TROUBLESHOOTING.md)

## VeloPack Packaging (Current)

- Release pipeline now produces VeloPack native assets (eleases.win.json, *.nupkg, RELEASES).
- Launcher remains the installer and rollback authority; only package generation moved to VeloPack.
- Legacy iles.json + update.zip generation remains available only as a disabled fallback path in CI.

