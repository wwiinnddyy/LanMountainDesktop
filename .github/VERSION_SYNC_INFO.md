# 版本号自动同步说明

## 📋 概述

从本次更新开始，Release 工作流已配置为**自动同步版本号**，确保应用的每个版本号来源都保持一致。

## 🔄 版本号流转链路

```
Git Tag (v1.0.1)
    ↓
[Release 工作流 prepare 任务]
    ↓
提取版本号: 1.0.1
    ↓
[Update version in .csproj] ✨ 新增步骤
    ↓
自动更新 .csproj 文件版本号
    ↓
dotnet restore/build
    ↓
构建时读取更新后的版本号
    ↓
应用内显示版本号 (MainWindow.Localization.cs 动态读取)
```

## 🎯 工作原理

### 1. 版本号提取
当推送 Git Tag 时（如 `git tag v1.0.1`），Release 工作流的 `prepare` 任务自动提取版本号：
- TAG: `v1.0.1` → VERSION: `1.0.1`

### 2. 自动更新 .csproj
在三个平台的构建任务中，新增了 **"Update version in .csproj"** 步骤：

**Windows (PowerShell)**:
```powershell
$VERSION = "1.0.1"
(Get-Content file.csproj) -replace '<Version>.*?</Version>', "<Version>$VERSION</Version>" | Set-Content file.csproj
```

**Linux/macOS (Bash)**:
```bash
VERSION="1.0.1"
sed -i "s/<Version>.*<\/Version>/<Version>$VERSION<\/Version>/" file.csproj
```

### 3. 构建和发布
更新后的版本号被用于：
- 程序集版本 (`AssemblyVersion`)
- 包文件名 (`LanMontainDesktop-1.0.1-win-x64.zip`)
- 应用内显示 (About 页面)
- GitHub Release 标题

## 📍 涉及的文件

自动更新的文件：
1. `LanMontainDesktop/LanMontainDesktop.csproj`
2. `LanMontainDesktop.RecommendationBackend/LanMontainDesktop.RecommendationBackend.csproj`

## ✅ 使用流程

### 发布新版本

```bash
# 1. 更新代码（可选：代码中的版本号现在会自动更新）
git add .
git commit -m "feat: Add new features"

# 2. 创建版本标签
git tag v1.0.1
# 或带注释的标签
git tag -a v1.0.1 -m "Release v1.0.1"

# 3. 推送标签到 GitHub
git push origin v1.0.1

# 4. Release 工作流自动运行：
#    - 自动更新 .csproj 文件
#    - 构建所有平台
#    - 创建 GitHub Release
#    - 附带所有平台的发布包
```

## 🔒 版本号一致性保证

现在应用的三个版本号来源完全同步：

| 来源 | 说明 | 自动更新 |
|------|------|--------|
| `.csproj` <Version> | 项目文件版本 | ✅ 是 |
| 程序集版本 | 编译时读取 | ✅ 是 |
| 应用内显示 | About 页面 | ✅ 是 |
| 发布包文件名 | Release 工作流 | ✅ 是 |
| GitHub Release | Release 工作流 | ✅ 是 |

## ⚠️ 注意事项

### 不需要手动更新
- ❌ 不需要在 `.csproj` 中手动修改 Version
- ❌ 不需要修改多个地方的版本号

### 只需执行
- ✅ 创建 Git Tag: `git tag v1.0.1`
- ✅ 推送 Tag: `git push origin v1.0.1`
- ✅ 其他由工作流自动处理

## 📊 版本号格式

支持的格式：
- ✅ `v1.0.0` (builds -> 1.0.0)
- ✅ `v1.2.3` (builds -> 1.2.3)
- ✅ `v2.0.0-rc1` (builds -> 2.0.0-rc1, 如果需要)

## 🛠️ 工作流文件

更新的工作流文件：
- `.github/workflows/release.yml` - Release 工作流

## 📝 相关文件

- [MULTIPLATFORM_RELEASE_GUIDE.md](./MULTIPLATFORM_RELEASE_GUIDE.md) - 多平台发布指南
- [WORKFLOWS_GUIDE.md](./WORKFLOWS_GUIDE.md) - 工作流使用指南

---

**最后更新**: 2026-03-04  
**工作流版本**: 2.0 (自动版本同步)
