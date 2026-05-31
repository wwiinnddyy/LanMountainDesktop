# 架构文档 / Architecture

## 中文

### 仓库结构

| 路径 | 角色 |
| --- | --- |
| `LanMountainDesktop/` | 主桌面宿主应用，包含 UI、服务、组件系统、插件运行时接入 |
| **`LanMountainDesktop.Launcher/`** | **启动器 - 负责 OOBE、Splash、版本目录选择与主程序启动** |
| **`LanMountainDesktop.AirAppRuntime/`** | **Air APP 独立运行容器 - 负责 Air APP IPC、实例表与 AirAppHost 进程生命周期** |
| `LanMountainDesktop.PluginSdk/` | 官方插件 SDK，定义插件可依赖的公开接口与打包行为 |
| `LanMountainDesktop.Shared.Contracts/` | 宿主与插件共享的稳定契约类型 |
| `LanMountainDesktop.Appearance/` | 主题、圆角、外观资源相关基础设施 |
| `LanMountainDesktop.Settings.Core/` | 设置域、持久化和设置基础抽象 |
| `LanMountainDesktop.DesktopHost/` | 桌面宿主流程与生命周期相关逻辑 |
| `LanMountainDesktop.DesktopComponents.Runtime/` | 组件运行时支撑能力 |
| `LanMountainDesktop.Host.Abstractions/` | 宿主侧抽象接口 |
| `LanMountainDesktop.PluginTemplate/` | `dotnet new lmd-plugin` 官方模板 |
| `LanMountainDesktop.Tests/` | 宿主与 SDK 的测试项目 |

### 宿主启动主线

**生产环境启动流程 (通过 Launcher):**

1. 用户启动 `LanMountainDesktop.Launcher.exe`
2. Launcher 扫描 `app-*` 目录,选择最佳版本 (优先 `.current` 标记,然后按版本号降序)
3. 首次启动显示 OOBE 引导 (`OobeWindow`)
4. 显示 Splash 启动动画 (`SplashWindow`)
5. 预启动包根下的 `LanMountainDesktop.AirAppRuntime`（框架依赖 JIT 进程）
6. 启动主程序 `app-{version}/LanMountainDesktop.exe`（更新检查、下载、应用、回滚和插件 pending 队列均由 Host 处理）
7. 主程序启动成功后将 Host PID 附加给 AirApp Runtime，并清理标记为 `.destroy` 的旧版本

**主程序启动流程 (LanMountainDesktop.exe):**

启动入口在 `LanMountainDesktop/Program.cs`：

1. 初始化日志、启动诊断和 Host 桌面生命周期
2. 初始化遥测身份、崩溃遥测与使用遥测
3. 构建 Avalonia `AppBuilder`
4. 进入 `LanMountainDesktop/App.axaml.cs`
5. 初始化主题、语言、设置窗口服务、天气定位刷新
6. 初始化桌面壳层、主窗口、托盘、插件运行时

### 运行时主数据流

- 设置流：`Settings.Core` 提供基础设置能力，宿主通过 facade 读取和监听设置变化
- 外观流：`Appearance` 提供主题和圆角资源，宿主在 `App.axaml.cs` 中应用到资源字典
- 组件流：`LanMountainDesktop/ComponentSystem/` 维护内置组件定义、注册和扩展接入
- 插件流：宿主侧 `plugins/` 负责 `.laapp` 的发现、安装、替换、激活与共享契约装配
- 设置页流：插件运行时可把自己的设置页注册进宿主设置窗口

### 关键目录落点

`LanMountainDesktop/` 内高频目录：

- `Views/`：窗口、页面、组件视图
- `ViewModels/`：视图模型
- `Services/`：业务服务、持久化、启动、遥测等
- `ComponentSystem/`：组件定义、注册、扩展加载
- `plugins/`：宿主侧插件运行时
- `Theme/` 与 `Styles/`：主题资源、样式、外观应用
- `DesktopEditing/`：桌面布局编辑相关逻辑
- `Localization/`：本地化资源

### 插件边界

- 插件 SDK 权威定义在 `LanMountainDesktop.PluginSdk/`
- 宿主与插件共享的稳定通信类型在 `LanMountainDesktop.Shared.Contracts/`
- 插件市场和开发者生态资料不在本仓库维护
- 本地 market 调试从兄弟仓库 `..\\LanAirApp` 读取数据

### 测试边界

`LanMountainDesktop.Tests/` 当前主要覆盖：
- 圆角与外观相关基础
- 组件放置与编辑数据
- 组件设置服务
- UI 异常防护
- 白板笔记持久化

涉及宿主行为、SDK 契约、布局计算或设置持久化的改动，应优先补对应测试。

### Launcher 架构详解

#### 职责范围

`LanMountainDesktop.Launcher/` 作为应用的唯一入口,负责:

1. **OOBE (首次体验)** - 首次启动引导和欢迎页面
2. **Splash Screen** - 启动动画和加载进度显示
3. **版本管理** - 多版本并存、版本选择、版本回退
4. **无更新职责** - 不检查、不下载、不应用、不回滚更新；更新系统完全由 Host 接管
5. **插件维护命令** - 保留 `plugin install` / `plugin update` 作为兼容 CLI；应用内插件市场由 Host 处理

#### 核心服务

| 服务 | 职责 |
|------|------|
| `DeploymentLocator` | 扫描和定位 `app-*` 版本目录,选择最佳版本 |
| `LauncherOrchestrator` / `LaunchPipeline` | 协调 OOBE → Splash → AirApp Runtime 预启动 → 启动主程序 |
| `OobeStateService` | 管理首次运行状态 |
| `PluginInstallerService` | CLI 维护：`plugin install` 直接安装 `.laapp` |
| `PluginUpgradeQueueService` | CLI 维护：`plugin update` 应用待处理队列（正常市场安装/升级由 Host 处理） |

#### 版本管理机制

**目录结构:**
```
安装根目录/
├── LanMountainDesktop.Launcher.exe  ← 唯一入口
├── app-1.0.0/                        ← 版本目录
│   ├── .current                      ← 当前版本标记
│   ├── LanMountainDesktop.exe
│   └── ...
├── app-1.0.1/                        ← 新版本
│   ├── .partial                      ← 下载中标记
│   └── ...
└── .Launcher/                        ← Launcher 数据
    ├── state/                        ← OOBE 状态
    ├── update/incoming/              ← 更新缓存
    └── snapshots/                    ← 更新快照
```

**版本选择算法:**
1. 扫描所有 `app-*` 目录
2. 过滤掉带 `.destroy` 或 `.partial` 标记的目录
3. 优先选择带 `.current` 标记的版本
4. 如果没有 `.current`,选择版本号最高的

**版本标记文件:**
- `.current` - 标记当前使用的版本
- `.partial` - 标记下载未完成的版本 (更新失败时自动清理)
- `.destroy` - 标记待删除的旧版本 (下次启动时清理)

#### 更新流程

**增量更新:**
1. Host 的 `UpdateOrchestrator` 检查更新、解析 manifest，并下载 PLONDS file map、签名和对象文件到 `.Launcher/update/incoming/`
2. Host 写入 `deployment.lock`，随后在 Host 进程内进入 `UpdateInstallGateway`
3. Host 负责签名校验、创建目标 `app-{new}/`、应用文件、验证 hash、切换 `.current`、写入快照和清理 incoming
4. 失败时 Host 使用快照尝试回滚；手动回滚通过 Host 设置页进入 `UpdateRollbackGateway`

**原子化保证:**
- 更新过程中保持 `.partial` 标记
- 任何失败都会触发回滚
- 旧版本保留直到新版本验证通过
- 快照记录允许手动回退

**版本回退:**
```bash
Host 设置页 → 更新 → 回滚
```
回退会:
1. 读取最新的更新快照
2. 移除当前版本的 `.current` 标记
3. 添加 `.current` 到上一个版本
4. 标记当前版本为 `.destroy`

#### CI/CD 集成

**发布产物结构:**
```
GitHub Release Assets:
├── LanMountainDesktop-Setup-1.0.1-x64.exe  (安装包)
├── app-1.0.1.zip                            (完整应用包)
├── delta-1.0.0-to-1.0.1.zip                (增量包)
├── files-1.0.1.json                         (文件清单)
└── files-1.0.1.json.sig                     (RSA 签名)
```

**增量包生成:**
- `scripts/Generate-DeltaPackage.ps1` - 对比两个版本生成增量包
- `scripts/Sign-FileMap.ps1` - 对 `files.json` 进行 RSA 签名
- `.github/workflows/release.yml` - 自动生成并上传增量包

**安装器集成:**
- Inno Setup 脚本修改为安装 Launcher 到根目录
- 主程序安装到 `app-{version}/` 子目录
- 快捷方式指向 `LanMountainDesktop.Launcher.exe`
- 安装后验证 Launcher 和 app 目录存在

## English

This repository is organized around a desktop host app plus a host-side plugin ecosystem. `LanMountainDesktop/` contains the application entry points, UI, services, component system, and plugin runtime integration. The surrounding projects provide the public SDK, shared contracts, appearance infrastructure, settings primitives, host abstractions, runtime support, and tests.

**Launcher Architecture**: `LanMountainDesktop.Launcher/` serves as the single entry point, managing OOBE, splash screen, version selection, and host startup. Update check/download/apply/rollback orchestration is fully Host-owned; the Launcher does not expose update CLI commands. In-app plugin market installation is also Host-owned. The Launcher still keeps plugin CLI commands as maintenance compatibility entry points. It uses a version directory structure (`app-{version}/`) with marker files (`.current`, `.partial`, `.destroy`) only to select the host version to start. See the Chinese section above for detailed architecture documentation.

The runtime flow starts with the Launcher selecting the best version, then proceeds into `Program.cs`, into `App.axaml.cs`, initializes settings/theme/localization services, then boots the desktop shell, tray, windows, and plugin runtime. The most important behavior boundaries are component registration, plugin activation, appearance resources, and settings persistence.

## VeloPack Integration Note

- Incremental package build/publish has moved to VeloPack native assets (
eleases.win.json + *.nupkg).
- Launcher runtime responsibilities are OOBE, startup orchestration, AirApp Runtime pre-start, version directory selection, and Host launch. Update check/download/apply/rollback stays in the Host.

## Plugin Isolation Modes

The current plugin runtime is still in-process. `PluginRuntimeService` and `PluginLoader` load plugin code inside the Host process, while `PluginLoadContext` only provides assembly isolation, not process isolation.

The repository now reserves three runtime modes:

- `in-proc`: current default and compatibility mode
- `isolated-background`: phase-1 mode, where background logic moves into a dedicated worker process and Host UI becomes a thin IPC-driven shell
- `isolated-window`: phase-2 mode, where plugin UI renders out of process and Host embeds a platform window handle

Two new supporting packages define the isolation boundary:

- `LanMountainDesktop.PluginIsolation.Contracts/`: transport-neutral DTOs, route constants, error codes, capabilities, and JSON context
- `LanMountainDesktop.PluginIsolation.Ipc/`: ClassIsland-inspired IPC facade that centralizes startup constants, routed notify IDs, and client/server wrappers over the future `dotnetCampus.Ipc` transport binding

For the detailed design, migration path, UI strategy, and residual risks, see `docs/PLUGIN_PROCESS_ISOLATION_ARCHITECTURE.md`.

## External IPC Public API

- The current IPC mainline is external integration, not plugin process isolation.
- `LanMountainDesktop.Shared.IPC` is the unified IPC base for Host public services, Launcher/OOBE startup notifications, and plugin-contributed public services.
- Strongly typed command/query access uses `[IpcPublic]` contracts plus `dotnetCampus.Ipc` generated proxy/joint support.
- One-way events use `JsonIpcDirectRoutedProvider.NotifyAsync` with fixed top-level notify IDs.
- Host remains the single external IPC entry point even when a capability is contributed by a plugin.

See `docs/EXTERNAL_IPC_ARCHITECTURE.md` for the detailed contract and migration model.

## Air APP Lifecycle

- `LanMountainDesktop.AirAppRuntime` is the lifecycle bridge between the desktop host and Air APP processes.
- The desktop host requests built-in Air APP operations through `IAirAppLifecycleService` on `LanMountainDesktop.AirAppRuntime.v1`.
- Launcher pre-starts `LanMountainDesktop.AirAppRuntime` during normal startup and attaches the launched Host PID through `IAirAppRuntimeControlService`.
- If that pipe is not available because the desktop host was started directly from IDE/dev tooling, the host starts `LanMountainDesktop.AirAppRuntime` and retries the request.
- AirApp Runtime owns Air APP process creation, activation, instance-key de-duplication, registration tracking, and exited-process cleanup.
- `LanMountainDesktop.AirAppHost` stays an independent rendering process and registers/unregisters itself with AirApp Runtime.
- Launcher waits for the desktop host startup path only; AirApp Runtime remains alive while Launcher/Host/requester or any AirAppHost process is alive, and exits when idle.
- Air APP windows are ordinary application windows: they do not use fused desktop bottom-most services and do not use global `Topmost` promotion.

## Fused Desktop Window Layer

- `TransparentOverlayWindow` and `DesktopWidgetWindow` are desktop-surface windows.
- On Windows, desktop-surface windows may attach to the desktop icon host through `IWindowBottomMostService`, or fall back to `HWND_BOTTOM`.
- Fused desktop windows refresh their bottom-most layer after being opened, shown, or reloaded so they do not cover ordinary apps.

## Main Window Desktop Layer

- The main desktop host window has a separate developer option, `EnableMainWindowDesktopLayer`.
- This mode is mutually exclusive with fused desktop because fused desktop manages component windows while main-window desktop layer manages the host window itself.
- The main-window service is `IMainWindowDesktopLayerService`; it attaches only the main window to the desktop icon host on Windows and falls back to `HWND_BOTTOM`.
- The main-window service does not use fused desktop click-through region logic, so the main desktop window remains interactive.
- Main-window restore paths refresh the desktop-layer attachment instead of using temporary `Topmost` foreground promotion while this mode is enabled.
- Air APP windows remain ordinary application windows and are not handled by either desktop-layer service.

## Air APP Window Chrome

- `LanMountainDesktop.AirAppHost` owns Air APP window chrome through `AirAppWindowDescriptor`.
- Supported chrome modes are `Standard`, `Borderless`, `FullScreen`, `Tool`, and reserved `BackgroundOnly`.
- Built-in `world-clock` uses `Standard` chrome with FluentAvalonia `FAAppWindow` title-bar controls.
- Built-in `whiteboard` uses `FullScreen` chrome and supplies its own in-app exit affordance.

## Launcher OOBE / Elevation Contract

- Launcher OOBE state is owned by a per-user JSON file under `%LOCALAPPDATA%\LanMountainDesktop\.launcher\state\oobe-state.json`.
- Same-user reinstall or upgrade should keep OOBE completed.
- `first_run_completed` is legacy migration-only data.
- The recognized launch sources are `normal`, `postinstall`, `plugin-install`, and `debug-preview`.
- Auto-OOBE is only allowed for normal user-mode startup.
- `postinstall` may show OOBE only when the launcher is not elevated.
- `plugin-install` and `debug-preview` must not auto-open OOBE.
- Elevation is allowed only for the installer, full installer update application, and user-confirmed legacy uninstall.
- Default plugin install targets the Host data root (`AppDataPathProvider.GetDataRoot()/Extensions/Plugins`) and should not ask for UAC when that directory is writable.
- In portable data mode, plugin packages follow the configured application data root. If that root is under an administrator-protected install path, Host downloads/verifies the package from a user-writable staging directory and invokes the restricted Launcher `plugin install` command with UAC to copy only into the configured data root.
- Marketplace plugin installs are queued under the Host data root when writable and take effect after restart; protected portable installs are applied immediately through the elevated maintenance command and still require restart before loading.
