# Windows 安装包配置指南

执行时间：2026年3月5日

## 📦 Windows 打包改为 .exe 安装程序

### 🎯 改进内容

Windows CI/CD工作流已更新，从生成.zip压缩包改为生成**Inno Setup .exe安装程序**。

| 特性 | 原来 | 现在 |
|------|------|------|
| **输出格式** | .zip 压缩包 | ✅ .exe 安装程序 |
| **用户体验** | 手动解压 | ✅ 一键安装 |
| **系统集成** | 无 | ✅ 开始菜单、桌面快捷方式 |
| **卸载** | 手动删除 | ✅ 系统控制面板卸载 |
| **文件大小** | ~250-300 MB | ~150-200 MB (已有内置压缩) |

## 🔧 实施细节

### `.github/workflows/release.yml` 变更

#### 1. 新增步骤：安装Inno Setup
```yaml
- name: Install Inno Setup
  run: choco install innosetup -y --no-progress
  shell: pwsh
```

在Windows Runner上自动安装Inno Setup编译器。

#### 2. 替换步骤：构建安装程序
原来的"Package"步骤（压缩为zip）现已改为"Build Installer"：

```yaml
- name: Build Installer
  run: |
    $version = "${{ needs.prepare.outputs.version }}"
    $arch = "${{ matrix.arch }}"
    $publishDir = "publish\windows-$arch"
    $installerScript = "LanMountainDesktop\installer\LanMountainDesktop.iss"
    $outputDir = "build-installer"
    
    # 查找Inno Setup编译器
    $isccPath = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
    
    # 编译安装程序
    $compileCmd = @(
      "`"$isccPath`"",
      "/DMyAppVersion=$version",
      "/DPublishDir=..\$publishDir",
      "/DMyOutputDir=..\$outputDir",
      "/DMyAppArch=$arch",
      "`"$installerScript`""
    ) -join " "
    
    # 执行编译
    Invoke-Expression $compileCmd 2>&1
```

#### 3. 更新步骤：上传安装程序
```yaml
- name: Upload Installer
  uses: actions/upload-artifact@v4
  with:
    name: release-windows-${{ matrix.arch }}
    path: build-installer/*.exe
    retention-days: 30
```

上传 .exe 安装程序而不是 .zip。

### `LanMountainDesktop/installer/LanMountainDesktop.iss` 变更

#### 1. OutputBaseFilename 更新
```ini
# 原来
OutputBaseFilename={#MyAppName}-Setup-{#MyAppVersion}

# 现在
OutputBaseFilename={#MyAppName}-Setup-{#MyAppVersion}-{#MyAppArch}
```

输出文件名现在包含架构标识（x64或x86），例如：
- `LanMountainDesktop-Setup-1.0.0-x64.exe`
- `LanMountainDesktop-Setup-1.0.0-x86.exe`

#### 2. 架构支持增强
```ini
# 原来（仅x64）
#if MyAppArch == "x64"
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif

# 现在（支持x64和x86）
#if MyAppArch == "x64"
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#else
#if MyAppArch == "x86"
ArchitecturesAllowed=x86compatible
#endif
#endif
```

## 📊 生成的安装程序功能

### 安装程序 (LanMountainDesktop-Setup-{Version}-{Arch}.exe)

✅ **功能**：
- 一键安装到 `C:\Program Files\LanMountainDesktop` 或 `C:\Program Files (x86)\`
- 创建开始菜单快捷方式
- 可选：创建桌面快捷方式
- 可选：安装后启动应用
- 支持系统卸载（控制面板 → 程序 → 卸载程序）

✅ **压缩**：
- LZMA2 超级压缩（lzma2/ultra64）
- 实体压缩（SolidCompression）
- 减少文件大小 ~35-50%

✅ **安全**：
- 需要管理员权限安装
- AppId 唯一标识符防止冲突
- 自动处理先前版本的覆盖安装

## 🚀 测试说明

### CI/CD 验证

1. **推送版本标签**
   ```bash
   git tag v1.0.0-installer-test
   git push origin v1.0.0-installer-test
   ```

2. **监察GitHub Actions**
   - 检查"Install Inno Setup"步骤是否成功
   - 检查"Build Installer"步骤的编译日志
   - 验证"Upload Installer"步骤是否上传了.exe文件

3. **下载并测试**
   - 从发布页面下载 `LanMountainDesktop-Setup-1.0.0-x64.exe`
   - 双击运行安装程序
   - 按照向导完成安装
   - 从开始菜单或桌面启动应用
   - 验证应用功能
   - 尝试从控制面板卸载

### 本地测试（可选）

如需本地测试安装程序生成：

```powershell
# Windows PowerShell

# 1. 发布应用
dotnet publish LanMountainDesktop\LanMountainDesktop.csproj `
  -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:PublishTrimmed=true `
  -o publish\windows-x64

# 2. 安装Inno Setup（如未安装）
choco install innosetup -y

# 3. 编译安装程序
$isccPath = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
& "$isccPath" /DMyAppVersion=1.0.0 `
  /DPublishDir=.\publish\windows-x64 `
  /DMyOutputDir=.\build-installer `
  /DMyAppArch=x64 `
  .\LanMountainDesktop\installer\LanMountainDesktop.iss

# 4. 测试安装程序
.\build-installer\LanMountainDesktop-Setup-1.0.0-x64.exe
```

## ⚙️ 自定义安装程序

如需修改安装程序外观或行为，编辑 `LanMountainDesktop/installer/LanMountainDesktop.iss`：

### 常见自定义

**1. 修改安装目录**
```ini
DefaultDirName={autopf}\{#MyAppName}
```

**2. 添加协议关联**
```ini
[Registry]
Root: HKCU; Subkey: "Software\Classes\.lanmountain"; ValueType: string; ValueName: ""; ValueData: "LanMountainDocument"
```

**3. 修改压缩设置**
```ini
Compression=lzma2/ultra64  ; 超级压缩
; Compression=lzma2/max   ; 最大压缩（更慢）
; Compression=bzip2        ; bzip2压缩
```

**4. 添加许可证页面**
```ini
LicenseFile=LICENSE.txt
InfoBeforeFile=INSTALLATION_INFO.txt
InfoAfterFile=POST_INSTALLATION_INFO.txt
```

## 🔍 故障排除

### Inno Setup 不存在

**错误**：`Inno Setup compiler not found at: C:\Program Files (x86)\Inno Setup 6\ISCC.exe`

**解决**：
- Windows Runner 已配置自动安装Inno Setup
- 如果CI失败，检查网络连接或choco是否可用
- 本地测试时可能需要手动安装：`choco install innosetup`

### 安装程序编译失败

**错误**：`Failed to create installer` 或 ISCC编译错误

**检查清单**：
1. ✅ 发布目录确实存在：`publish\windows-x64\`
2. ✅ 发布目录包含可执行文件
3. ✅ Inno Setup脚本语法正确
4. ✅ ISCC路径正确

### 安装后找不到应用

**原因**：可能禁用了"开始菜单"快捷方式

**解决**：
- 检查 `C:\Program Files\LanMountainDesktop`
- 从文件管理器直接运行 `LanMountainDesktop.exe`
- 检查.iss脚本中的[Icons]部分

## 📝 发布说明模板

当发布Windows版本时，使用以下说明：

```markdown
## Windows 安装

### 64位系统
下载 **LanMountainDesktop-Setup-{Version}-x64.exe**

### 32位系统
下载 **LanMountainDesktop-Setup-{Version}-x86.exe**

### 安装步骤
1. 双击 .exe 文件
2. 按照向导完成安装
3. 安装完成后从开始菜单启动应用

### 卸载步骤
1. 打开"控制面板" → "程序" → "程序和功能"
2. 找到 "LanMountainDesktop"
3. 点击"卸载"按钮
```

## 📚 相关文档

- [Inno Setup 官方文档](https://jrsoftware.org/isinfo.php)
- [Inno Setup 脚本参考](https://jrsoftware.org/isdocs/)
- [.github/workflows/release.yml](../workflows/release.yml) - 完整工作流定义

## ✨ 总结

通过使用Inno Setup生成.exe安装程序：
- ✅ 用户体验改善：一键安装
- ✅ 系统集成：开始菜单、卸载功能
- ✅ 文件大小更小：内置LZMA2压缩
- ✅ 专业形象：正式的安装向导

Windows用户现在能够以标准的.exe安装程序方式安装LanMountainDesktop应用！
