# 运行指南

## 中文

本文档只说明如何在本地运行阑山桌面。

### 环境准备

- 安装 .NET SDK 10。
- 桌面端建议在 Windows 上运行。

### 构建

```bash
dotnet restore
dotnet build LanMountainDesktop.sln -c Debug
```

### 运行桌面端

```bash
dotnet run --project LanMountainDesktop/LanMountainDesktop.csproj
```

### 常见问题

- 如果提示 SDK 版本不匹配，先检查 `dotnet --info`。
- 如果视频能力异常，优先在 Windows 环境验证。
- 如果要重置配置，可删除 `%LOCALAPPDATA%\LanMountainDesktop\settings.json` 后重启。

### Linux 录音依赖

如果在 Linux 上使用录音机或自习监测相关能力，需要安装音频库：

- Debian/Ubuntu：`sudo apt install libportaudio2 libasound2`
- Fedora/RHEL：`sudo dnf install portaudio-libs alsa-lib`
- Arch Linux：`sudo pacman -S portaudio alsa-lib`
- Alpine Linux：`sudo apk add portaudio alsa-lib`

## English

This guide explains how to run LanMountainDesktop locally.

### Build

```bash
dotnet restore
dotnet build LanMountainDesktop.sln -c Debug
```

### Run

```bash
dotnet run --project LanMountainDesktop/LanMountainDesktop.csproj
```
