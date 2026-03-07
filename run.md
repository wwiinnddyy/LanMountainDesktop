# LanMountainDesktop 运行指南

本文档只负责“怎么跑起来”。项目介绍请看 [README.md](./README.md)。

## 1. 环境准备
- 安装 .NET SDK 10（`net10.0`）。
- 建议使用 Windows 运行桌面端（当前桌面体验以 Windows 为主）。

## 2. 拉取依赖并构建
在仓库根目录执行：

```bash
dotnet restore
dotnet build LanMountainDesktop.sln -c Debug
```

## 3. 运行桌面端
```bash
dotnet run --project LanMountainDesktop/LanMountainDesktop.csproj
```

## 4. 推荐能力说明
桌面端已内置推荐数据服务（每日诗词 / 每日名画），默认无需额外启动本地推荐后端。

## 5. 常见问题
- 启动失败提示 SDK 版本不匹配：确认 `dotnet --info` 中已安装 .NET 10 SDK。
- 桌面端视频相关能力异常：优先在 Windows 环境下验证。
- 配置重置：删除 `%LOCALAPPDATA%\LanMountainDesktop\settings.json` 后重启应用。

## 6. Linux 音频功能依赖

如果在 Linux 上使用录音机组件或自习监测组件，需要安装以下音频库：

### Debian/Ubuntu
```bash
sudo apt install libportaudio2 libasound2
```

### Fedora/RHEL
```bash
sudo dnf install portaudio-libs alsa-lib
```

### Arch Linux
```bash
sudo pacman -S portaudio alsa-lib
```

### Alpine Linux
```bash
sudo apk add portaudio alsa-lib
```

> 注：如果未安装这些依赖，录音和自习监测功能将不可用，但应用其他功能可以正常运行。
