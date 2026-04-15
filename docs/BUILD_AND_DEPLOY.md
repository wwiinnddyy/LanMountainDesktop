# 构建和部署指南

> LanMountainDesktop 完整构建、打包和发布流程

## 目录

- [本地构建](#本地构建)
- [发布构建](#发布构建)
- [生成安装包](#生成安装包)
- [CI/CD 流程](#cicd-流程)
- [手动发布](#手动发布)

## 本地构建

### 环境要求

- .NET SDK 10.0 或更高版本
- Windows 10/11 (推荐)
- Inno Setup 6 (仅生成安装包时需要)

### 快速构建

```bash
# 1. 还原依赖
dotnet restore LanMountainDesktop.slnx

# 2. 构建 Debug 版本
dotnet build LanMountainDesktop.slnx -c Debug

# 3. 运行主程序
dotnet run --project LanMountainDesktop/LanMountainDesktop.csproj
```

### 构建 Release 版本

```bash
dotnet build LanMountainDesktop.slnx -c Release
```

## 发布构建

### Windows (x64, 自包含)

```bash
dotnet publish LanMountainDesktop/LanMountainDesktop.csproj `
  -c Release `
  -o ./publish/windows-x64 `
  --self-contained `
  -r win-x64 `
  -p:PublishSingleFile=false `
  -p:DebugType=none `
  -p:DebugSymbols=false
```

**发布后的目录结构:**
```
publish/windows-x64/
├── LanMountainDesktop.Launcher.exe  ← 入口
├── app-{version}/                    ← 主程序
│   ├── .current
│   ├── LanMountainDesktop.exe
│   └── ...
```

### Linux (x64)

```bash
dotnet publish LanMountainDesktop/LanMountainDesktop.csproj `
  -c Release `
  -o ./publish/linux-x64 `
  --self-contained `
  -r linux-x64
```

### macOS (arm64)

```bash
dotnet publish LanMountainDesktop/LanMountainDesktop.csproj `
  -c Release `
  -o ./publish/osx-arm64 `
  --self-contained `
  -r osx-arm64
```

## 生成安装包

### Windows 安装包 (Inno Setup)

**前提条件:**
```powershell
# 安装 Inno Setup
choco install innosetup -y
```

**生成安装包:**
```powershell
# 1. 发布应用
dotnet publish LanMountainDesktop/LanMountainDesktop.csproj `
  -c Release `
  -o ./publish/windows-x64 `
  --self-contained `
  -r win-x64

# 2. 运行 Inno Setup 编译器
$version = "1.0.0"
$arch = "x64"

iscc.exe `
  /DMyAppVersion=$version `
  /DMyAppArch=$arch `
  /DPublishDir="publish\windows-x64" `
  /DMyOutputDir="build-installer" `
  LanMountainDesktop\installer\LanMountainDesktop.iss
```

**输出:**
```
build-installer/
└── LanMountainDesktop-Setup-1.0.0-x64.exe
```

### Linux 包 (.deb)

```bash
# TODO: 添加 .deb 打包脚本
```

### macOS 包 (.dmg)

```bash
# TODO: 添加 .dmg 打包脚本
```

## CI/CD 流程

### GitHub Actions 工作流

项目使用 GitHub Actions 自动化构建和发布。

**触发条件:**
- 推送 `v*` 标签 (例如: `v1.0.0`)
- 手动触发 (workflow_dispatch)

**工作流文件:** `.github/workflows/release.yml`

### 发布流程

```
1. prepare job
   ├─ 解析版本号
   └─ 设置构建变量

2. build-windows job
   ├─ 构建 x64 和 x86 版本
   ├─ 重组为 app-{version} 结构
   ├─ 生成增量包
   ├─ 生成 Inno Setup 安装包
   └─ 上传 artifacts

3. build-linux job
   ├─ 构建 x64 版本
   ├─ 生成 .deb 包
   └─ 上传 artifacts

4. build-macos job
   ├─ 构建 arm64 和 x64 版本
   ├─ 生成 .dmg 包
   └─ 上传 artifacts

5. release job
   ├─ 下载所有 artifacts
   ├─ 创建 GitHub Release
   └─ 上传所有安装包和增量包
```

### 发布产物

**GitHub Release Assets:**
```
LanMountainDesktop-v1.0.0/
├── LanMountainDesktop-Setup-1.0.0-x64.exe      # Windows 安装包
├── LanMountainDesktop-Setup-1.0.0-x86.exe
├── LanMountainDesktop-1.0.0-linux-x64.deb      # Linux 包
├── LanMountainDesktop-1.0.0-macos-arm64.dmg    # macOS 包
├── app-1.0.0.zip                                # 完整应用包
├── delta-0.9.9-to-1.0.0.zip                    # 增量包
├── files-1.0.0.json                             # 文件清单
└── files-1.0.0.json.sig                         # RSA 签名
```

## 手动发布

### 1. 准备发布

```bash
# 1. 更新版本号
# 编辑 Directory.Build.props 中的 <Version>

# 2. 更新 CHANGELOG.md
# 记录本次发布的变更

# 3. 提交变更
git add .
git commit -m "chore: prepare release v1.0.0"
git push
```

### 2. 创建 Release 标签

```bash
# 创建标签
git tag v1.0.0

# 推送标签 (触发 CI)
git push origin v1.0.0
```

### 3. 等待 CI 完成

访问 GitHub Actions 页面,等待构建完成:
```
https://github.com/YourOrg/LanMountainDesktop/actions
```

### 4. 验证 Release

1. 访问 Releases 页面
2. 检查所有安装包是否上传成功
3. 下载并测试安装包
4. 验证增量更新功能

### 5. 发布公告

- 在 GitHub Release 中编辑发布说明
- 发布到社区/论坛
- 更新官网下载链接

## 增量包生成

### 手动生成增量包

```powershell
# 1. 准备两个版本的发布目录
dotnet publish ... -o ./publish/app-1.0.0
dotnet publish ... -o ./publish/app-1.0.1

# 2. 生成增量包
./scripts/Generate-DeltaPackage.ps1 `
  -PreviousVersion "1.0.0" `
  -CurrentVersion "1.0.1" `
  -PreviousDir "./publish/app-1.0.0" `
  -CurrentDir "./publish/app-1.0.1" `
  -OutputDir "./delta-output"

# 3. 签名文件清单
./scripts/Sign-FileMap.ps1 `
  -FilesJsonPath "./delta-output/files-1.0.1.json" `
  -PrivateKeyPath "./private-key.pem"
```

**输出:**
```
delta-output/
├── delta-1.0.0-to-1.0.1.zip
├── files-1.0.1.json
└── files-1.0.1.json.sig
```

### 生成 RSA 密钥对

```powershell
# 生成私钥
openssl genrsa -out private-key.pem 2048

# 提取公钥
openssl rsa -in private-key.pem -pubout -out public-key.pem
```

**重要:**
- 私钥保存在安全位置 (GitHub Secrets)
- 公钥打包到 Launcher 中 (`.launcher/update/public-key.pem`)

## 版本号规范

遵循 [Semantic Versioning 2.0.0](https://semver.org/):

```
MAJOR.MINOR.PATCH[-PRERELEASE][+BUILD]

例如:
- 1.0.0          (正式版)
- 1.0.1          (补丁版本)
- 1.1.0          (新功能)
- 2.0.0          (破坏性变更)
- 1.0.0-beta.1   (预览版)
- 1.0.0-rc.1     (候选版本)
```

### 版本号更新规则

- **MAJOR**: 破坏性 API 变更
- **MINOR**: 新功能,向后兼容
- **PATCH**: Bug 修复,向后兼容
- **PRERELEASE**: 预览版标识 (alpha, beta, rc)

## 故障排除

### 构建失败

**问题**: `error NU1102: Unable to find package`

**解决**:
```bash
dotnet restore --force
dotnet nuget locals all --clear
```

### 发布失败

**问题**: Launcher 目录不存在

**解决**: 检查 `LanMountainDesktop.csproj` 中的 `CopyLauncherToPublish` 目标是否正确执行。

### 安装包生成失败

**问题**: Inno Setup 找不到文件

**解决**: 确保 `PublishDir` 路径正确,且包含 `app-{version}/` 目录结构。

## 相关文档

- [开发文档](DEVELOPMENT.md)
- [Launcher 架构](LAUNCHER.md)
- [更新系统](UPDATE_SYSTEM.md)
- [故障排除](TROUBLESHOOTING.md)
