# 跨平台架构说明

> 本文档描述阑山桌面的跨平台（桌面 + 移动）架构方案与分层约束。
> 当前目标平台：Windows（主力）、Linux/macOS（桌面构建）、Android（移动端）。iOS 暂不在范围内。

## 1. 总体分层

```
┌─────────────────────────────────────────────────────┐
│  Heads（平台入口）                                    │
│  LanMountainDesktop（WinExe 桌面宿主）                │
│  LanMountainDesktop.Mobile.Android（Android head）    │
├─────────────────────────────────────────────────────┤
│  共享 UI 层                                          │
│  LanMountainDesktop.Mobile（移动壳/组件面板）          │
│  ComponentSystem / Controls / Theme（随桌面宿主）      │
├─────────────────────────────────────────────────────┤
│  平台差异层（Platform）                               │
│  LanMountainDesktop.Platform.Abstractions（接口）     │
│  LanMountainDesktop.Platform.Windows（Windows 实现）  │
│  （后续按需增加 Platform.Android / Platform.MacOS）    │
├─────────────────────────────────────────────────────┤
│  共享核心层（必须无平台依赖）                          │
│  Shared.Contracts / Shared.IPC / Settings.Core        │
│  Appearance / DesktopComponents.Runtime / PluginSdk   │
│  Host.Abstractions / PluginIsolation.Contracts        │
└─────────────────────────────────────────────────────┘
```

## 1.1 当前落地状态

- ✅ `Platform.Abstractions`：接口 + NoOp 实现 + `PlatformLog` 日志桥
- ✅ `Platform.Windows`：电源管理、原生对话框、桌面层嵌入、窗口置底/区域穿透、
  DWM 互操作、图标服务（WindowsIconService/UwpManifestIconResolver）、包标识查询
- ✅ `Platform.MacOS`：MacIconService
- ✅ `Platform.Android`：骨架（后续按需填充）
- ✅ `Mobile` + `Mobile.Android`：单视图组件面板壳，APK 可构建
- ✅ 主工程 `LanMountainDesktop` 已无任何 DllImport/LibraryImport
- ✅ PluginIsolation.Ipc 新增 `InProcPluginIpcTransport`（进程内直通，含测试）

主工程保留的平台相关内容（有意为之）：
- `WindowsNotificationListener` / `WindowsSmtcMusicControlService` / `LocationService`
  的 WinRT **反射**调用路径（无编译期平台依赖，运行时 `OperatingSystem.IsWindows()` 保护），
  P/Invoke 部分已抽到 `Platform.Windows.WindowsPackageIdentity`。
- `LinuxPowerManagementService`（纯命令行调用，无平台 API）。
- 静态工厂门面（`*ServiceFactory`）留在 `LanMountainDesktop.Services` 命名空间，
  保持既有调用点不变，内部委托平台实现。

## 2. Platform 层规则

- 所有 P/Invoke、`DllImport`/`LibraryImport`、Windows 注册表、COM/Office 互操作等平台专属代码，
  一律放入 `LanMountainDesktop.Platform.<平台>` 项目，禁止出现在主工程与共享层。
- `Platform.Abstractions` 只包含接口、跨平台 DTO 与 Null/NoOp 兜底实现。
- Head 项目在启动时负责注册对应平台实现（桌面注册 Windows 实现，Android 注册移动实现或 NoOp）。

## 3. 插件体系的平台策略

| 能力 | 桌面 | Android |
|---|---|---|
| 插件契约（PluginSdk / PluginIsolation.Contracts） | 保留 | 保留（不变） |
| 进程内加载（AssemblyLoadContext） | 保留 | 保留 |
| 进程外隔离（命名管道 + AirAppHost 子进程） | 保留 | 不可用，降级为进程内 |
| IPC 传输 | 命名管道（dotnetCampus.Ipc） | 进程内直通（InProc transport，同一契约） |
| AirApp 运行时（独立进程） | 保留 | 第一版不提供 |

插件代码不感知传输差异：同一套 `PluginIsolation` 契约在桌面走管道，在移动端走进程内直通。

## 4. 移动端形态

- 移动端不存在"桌面层自由摆放"的交互，组件以**组件面板**（可滚动卡片流/网格）呈现。
- 复用同一批组件控件与 Appearance 主题资源，仅替换容器布局。
- 单窗口生命周期（`ISingleViewApplicationLifetime`）。

## 5. 明确舍弃（移动端不提供）

- 电源管理（PowerManagementService）
- 桌面层嵌入 / 点击穿透（MainWindowDesktopLayerService、WindowPassthroughService）
- Office 互操作（MudTools.OfficeInterop.*）
- Harmony 运行时补丁（Platform/Windows/Patches，仅随 Windows head 编译）
- AirApp 独立进程运行时

## 6. 构建与 CI

- 桌面：`dotnet build LanMountainDesktop.slnx`（Windows/Linux/macOS 三平台 CI）。
- Android：`dotnet build LanMountainDesktop.Mobile.Android/...csproj`（CI `build-android` job，
  需要 `dotnet workload install android`）。
- 共享层新增依赖时，必须确认该包支持 `net10.0-android`，否则放入 Platform 层。

## 7. 相关文档

- 共享库平台依赖审计报告：`docs/cross-platform-shared-audit.md`
- 架构总览：`docs/ARCHITECTURE.md`
