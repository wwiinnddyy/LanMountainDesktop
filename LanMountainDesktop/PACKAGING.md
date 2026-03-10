# 桌面端打包指南

## 中文

本指南说明阑山桌面的本地打包和 CI 打包流程。

### 前置条件

- 安装 .NET SDK 10
- Windows 安装包需要 Inno Setup 6（`ISCC.exe`）

### 本地打包命令

#### Windows 安装包

```powershell
.\scripts\package.ps1 -RuntimeIdentifier win-x64 -Version 1.0.1
```

#### Linux 包

```powershell
pwsh ./scripts/package.ps1 -RuntimeIdentifier linux-x64 -Version 1.0.1
```

#### macOS 包

```powershell
pwsh ./scripts/package.ps1 -RuntimeIdentifier osx-x64 -Version 1.0.1
```

### 产物位置

- 发布目录：`artifacts/publish/<rid>`
- 安装包或压缩包：`artifacts/installer` 或 `artifacts/packages`

### CI 流程

- 工作流文件：`.github/workflows/windows-ci.yml`
- 日常构建会验证桌面端可编译
- 手动触发或 `v*` 标签可生成正式包并上传到 Release

## English

This guide covers local packaging and CI packaging for LanMountainDesktop.

### Key points

- use `scripts/package.ps1` with the target runtime identifier
- Windows installer requires Inno Setup
- CI can publish artifacts and attach them to GitHub Releases
