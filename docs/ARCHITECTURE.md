# 架构文档 / Architecture

## 中文

### 仓库结构

| 路径 | 角色 |
| --- | --- |
| `LanMountainDesktop/` | 主桌面宿主应用，包含 UI、服务、组件系统、插件运行时接入 |
| **`LanMountainDesktop.Launcher/`** | **启动器 - 负责 OOBE、Splash、版本管理、增量更新、插件安装** |
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
5. 检查并应用待处理的更新 (`UpdateEngineService.ApplyPendingUpdate`)
6. 处理插件升级队列 (`PluginUpgradeQueueService`)
7. 启动主程序 `app-{version}/LanMountainDesktop.exe`
8. 清理标记为 `.destroy` 的旧版本

**主程序启动流程 (LanMountainDesktop.exe):**

启动入口在 `LanMountainDesktop/Program.cs`：

1. 初始化日志、单实例锁和启动诊断
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
4. **应用更新** - 增量更新、静默更新、原子化更新
5. **插件管理** - 插件安装、插件更新队列处理

#### 核心服务

| 服务 | 职责 |
|------|------|
| `DeploymentLocator` | 扫描和定位 `app-*` 版本目录,选择最佳版本 |
| `UpdateCheckService` | 调用 GitHub Release API 检查更新,支持 Stable/Preview 频道 |
| `UpdateEngineService` | 下载、验证、应用增量更新,支持原子化更新和回滚 |
| `LauncherFlowCoordinator` | 协调 OOBE → Splash → 更新 → 插件 → 启动主程序的完整流程 |
| `OobeStateService` | 管理首次运行状态 |
| `PluginInstallerService` | 处理 `.laapp` 插件包安装 |
| `PluginUpgradeQueueService` | 批量处理插件升级队列 |

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
└── .launcher/                        ← Launcher 数据
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
1. `UpdateCheckService` 调用 GitHub Release API
2. 根据更新频道 (Stable/Preview) 过滤版本
3. 下载 `delta-{old}-to-{new}.zip` 和 `files-{new}.json`
4. 创建 `app-{new}/` 目录并标记 `.partial`
5. 解压增量包,从旧版本复用未变更文件
6. 验证所有文件 SHA256
7. 删除 `.partial`,添加 `.current` 到新版本
8. 标记旧版本 `.destroy`
9. 保存更新快照到 `.launcher/snapshots/`

**原子化保证:**
- 更新过程中保持 `.partial` 标记
- 任何失败都会触发回滚
- 旧版本保留直到新版本验证通过
- 快照记录允许手动回退

**版本回退:**
```bash
LanMountainDesktop.Launcher.exe update rollback
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

**Launcher Architecture**: `LanMountainDesktop.Launcher/` serves as the single entry point, managing OOBE, splash screen, multi-version deployment, incremental updates, and plugin installation. It uses a version directory structure (`app-{version}/`) with marker files (`.current`, `.partial`, `.destroy`) to enable atomic updates and rollback capabilities. See the Chinese section above for detailed architecture documentation.

The runtime flow starts with the Launcher selecting the best version, then proceeds into `Program.cs`, into `App.axaml.cs`, initializes settings/theme/localization services, then boots the desktop shell, tray, windows, and plugin runtime. The most important behavior boundaries are component registration, plugin activation, appearance resources, and settings persistence.

## VeloPack Integration Note

- Incremental package build/publish has moved to VeloPack native assets (eleases.win.json + *.nupkg).
- Launcher runtime responsibilities are unchanged: OOBE, startup orchestration, update apply, and rollback.


## Launcher OOBE / Elevation Contract

- Launcher OOBE state is owned by a per-user JSON file under `%LOCALAPPDATA%\LanMountainDesktop\.launcher\state\oobe-state.json`.
- Same-user reinstall or upgrade should keep OOBE completed.
- `first_run_completed` is legacy migration-only data.
- The recognized launch sources are `normal`, `postinstall`, `apply-update`, `plugin-install`, and `debug-preview`.
- Auto-OOBE is only allowed for normal user-mode startup.
- `postinstall` may show OOBE only when the launcher is not elevated.
- `apply-update`, `plugin-install`, and `debug-preview` must not auto-open OOBE.
- Elevation is allowed only for the installer, full installer update application, and user-confirmed legacy uninstall.
- Default plugin install should stay inside the user's LocalAppData scope and should not ask for UAC.
