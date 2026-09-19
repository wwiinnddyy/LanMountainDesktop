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

## 对外的 IPC 契约：只写异步方法

宿主用 `[IpcPublic]` 接口向外部进程暴露服务（`IPublicAppInfoService`、`IPublicAirAppCatalogService`、
`IPublicShellControlService`、`IAirAppLifecycleService`、`IAirAppRuntimeControlService`）。
**新增方法必须返回 `Task`/`Task<T>`（或 `ValueTask`），不要写同步方法。**

原因是死锁而不是风格：`dotnetCampus.Ipc` 为同步方法生成的代理内部用 `Task.Wait()` 等回包，
而库在自己的读循环线程上投递通知、也在那条线程上续接 `ConnectAsync` 的延续。
调用方一旦在这类线程上同步等，回包就只能由当前线程投递 → 进程静默挂死，不报错、不超时。
`ExternalIpcPublicApiTests` 曾因为直接同步调 `GetAppInfo()` 挂死整个测试子进程。

例外只有已随 `LanMountainDesktop.Core` 1.0.0 发出的两个历史同步方法
（`IPublicAppInfoService.GetAppInfo`、`IPublicAirAppCatalogService.GetCatalog`）：它们是公开 API，
只能"加异步版本 + 标废弃"两步撤，不能直接改签名。宿主自己要目录/会话信息时走
`LanMountainDesktopIpcClient.GetCatalogAsync()` / `GetSessionInfoAsync()`，不要改用那两个同步代理。

约束由 `IpcPublicContractArchitectureTests` 机器检查（新增同步方法会测试失败，例外名单只减不增）。

## 相关文档

- [AirApp 开发指南](../01-AirApp开发/)
- [AirApp SDK V1 迁移指南](../AIRAPP_SDK_V1_MIGRATION.md)
