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

## 4. 可选：运行推荐后端
如果你需要每日诗词/名画等推荐能力，可单独启动后端：

```bash
dotnet run --project LanMountainDesktop.RecommendationBackend/LanMountainDesktop.RecommendationBackend.csproj
```

后端默认会输出监听地址（通常是 `http://localhost:5xxx` 或 `https://localhost:7xxx`）。

可用健康检查：

```bash
curl http://localhost:5000/health
```

说明：端口以你本机启动日志为准，`5000` 仅为示例。

## 5. 常见问题
- 启动失败提示 SDK 版本不匹配：确认 `dotnet --info` 中已安装 .NET 10 SDK。
- 桌面端视频相关能力异常：优先在 Windows 环境下验证。
- 配置重置：删除 `%LOCALAPPDATA%\LanMountainDesktop\settings.json` 后重启应用。
