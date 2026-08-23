# AirApp 运行时与窗口轻应用

## AirApp 是统一扩展系统

AirApp（轻应用）是阑山桌面的统一扩展系统。开发者通过 `LanMountainDesktop.AirAppSdk` 可以开发两种形态的轻应用：

- **桌面组件**：放置到桌面网格，主进程内渲染（`AddAirAppComponent<TControl>`）。
- **窗口轻应用**：独立窗口应用，由 `AirAppHost` 进程隔离承载（`AddAirAppWindow<TWindow>`）。

## 运行时拓扑

```
主 Host 内置桌面组件 / 第三方 AirApp 桌面组件
         ↓ 组件内调用 OpenWindowAsync(windowId)
Host 内 AirAppLauncherService
         ↓ AirAppOpenRequest（含 --app-package 包目录，IPC）
独立 LanMountainDesktop.AirAppRuntime
         ↓ 启动或激活
独立 LanMountainDesktop.AirAppHost 进程（--app-package --target-entry-id）
         ↓ 按 appId 加载第三方 AirApp 程序集，解析 [AirAppEntrance]
AirAppWindowLoader → 创建 IAirAppWindow → 内容嵌入 FAAppWindow 外壳
```

## 各进程职责

| 进程/模块 | 职责 |
|----------|------|
| `LanMountainDesktop` Host | 扫描/加载 AirApp（`airapp.json` + `[AirAppEntrance]`，ALC 隔离）；渲染桌面组件；组装 `AirAppOpenRequest` 并调用 Runtime IPC |
| `LanMountainDesktop.Launcher` | OOBE、Splash、版本选择、预启动 Runtime、启动 Host |
| `LanMountainDesktop.AirAppRuntime` | 生命周期与控制 IPC、实例去重、启动/激活/关闭 AirAppHost，转发 `--app-package` |
| `LanMountainDesktop.AirAppHost` | 内置 3 个窗口（world-clock/whiteboard/rss-reader）+ 按 `--app-package` 加载第三方 AirApp 窗口 |
| `LanMountainDesktop.AirAppSdk` | 统一 SDK（`IAirApp`/`AirAppBase`/`IAirAppWidget`/`IAirAppWindow`/`airapp.json`） |
| `LanMountainDesktop.AirAppTemplate` | `dotnet new lmd-airapp` 模板（组件 + 窗口 + 设置示例） |
| `LanMountainDesktop.AirAppDevServer` | 开发者工具：监视项目 → `dotnet build` 出 `.laapp` → 打包/预览 |

## 窗口轻应用开发

1. 用 `dotnet new lmd-airapp` 创建 AirApp 工程。
2. 在 `AirApp.Initialize` 中 `AddAirAppWindow<MyWindow>("my-window", "My Window")`。
3. 组件内通过 `AirAppComponentContext.OpenWindowAsync("my-window")` 打开窗口。
4. `dotnet build` 生成 `.laapp`，安装到 `Extensions/AirApps`。

第三方 AirApp 窗口由 `AirAppHost` 进程承载：`AirAppWindowLoader` 读取包目录的 `airapp.json`，用 `AirAppLoader` 加载入口程序集，按 `--target-entry-id` 解析 `AirAppWindowRegistration`，把 `IAirAppWindow.Content` 嵌入 FAAppWindow 外壳。

## 内置窗口链路（回归保障）

`world-clock`、`whiteboard`、`rss-reader` 三个内置窗口保持原有路径（不携带 `--app-package`），行为不变。

## 相关文档

- [AirApp 开发指南](../01-AirApp开发/)
- [AirApp SDK V1 迁移指南](../AIRAPP_SDK_V1_MIGRATION.md)
