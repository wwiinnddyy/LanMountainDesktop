# LanMountainDesktop Code Wiki

> 本文档是 LanMountainDesktop（阑山桌面）项目的结构化 Code Wiki，涵盖项目整体架构、主要模块职责、关键类与函数说明、依赖关系以及项目运行方式等关键信息。
>
> 生成日期：2026-05-07
> 产品版本：1.0.0
> Plugin SDK API 基线：5.0.0

---

## 目录

1. [项目概述](#1-项目概述)
2. [整体架构](#2-整体架构)
3. [项目结构与模块职责](#3-项目结构与模块职责)
4. [关键类与函数说明](#4-关键类与函数说明)
5. [依赖关系](#5-依赖关系)
6. [项目运行方式](#6-项目运行方式)
7. [启动流程详解](#7-启动流程详解)
8. [插件系统架构](#8-插件系统架构)
9. [数据流与交互模型](#9-数据流与交互模型)
10. [测试体系](#10-测试体系)

---

## 1. 项目概述

### 1.1 产品定位

**阑山桌面（LanMountainDesktop）** 是一款跨平台桌面环境增强工具，基于 Avalonia UI 和 .NET 10 构建。

- **产品口号**：你的桌面，不止一面
- **技术基线**：Avalonia UI + .NET 10
- **支持平台**：Windows、Linux、macOS
- **仓库角色**：桌面宿主、插件运行时、Plugin SDK 与共享契约的权威来源

### 1.2 目标用户

- **学生用户**：课程表、自习监测、计时、天气和日常信息聚合
- **办公用户**：日历、资讯、最近文档、常用工具入口
- **效率和美化爱好者**：自由布局、主题切换、插件扩展
- **中文用户**：本地化界面、农历和节假日等本地语境支持

### 1.3 核心能力

- **桌面组件系统**：内置组件与扩展组件统一注册、统一放置约束
- **插件系统**：宿主加载插件、整合设置页、组件与市场安装流
- **外观系统**：主题、玻璃层级、圆角与颜色资源统一管理
- **设置系统**：独立设置窗口、设置页注册与分域持久化
- **跨平台运行**：基于 Avalonia 的桌面宿主运行在 Windows、Linux、macOS

### 1.4 生态边界

| 仓库 | 职责 |
|------|------|
| `LanMountainDesktop`（本仓库） | 宿主代码、插件运行时、SDK、共享契约、主题与设置基础设施 |
| `LanAirApp`（兄弟仓库） | 插件市场元数据、开发者生态材料 |
| `LanMountainDesktop.SamplePlugin` | 官方示例插件实现 |

---

## 2. 整体架构

### 2.1 架构分层

```
┌─────────────────────────────────────────────────────────────┐
│                      用户界面层 (UI Layer)                    │
│  Views/  │  ViewModels/  │  Theme/  │  Styles/  │  Localization/
├─────────────────────────────────────────────────────────────┤
│                     业务服务层 (Service Layer)                │
│  Services/  │  ComponentSystem/  │  DesktopEditing/  │  plugins/
├─────────────────────────────────────────────────────────────┤
│                     基础设施层 (Infrastructure)               │
│  DesktopHost/  │  Appearance/  │  Settings.Core/  │  Shared.IPC/
├─────────────────────────────────────────────────────────────┤
│                     抽象与契约层 (Abstractions)               │
│  Host.Abstractions/  │  Shared.Contracts/  │  PluginSdk/
├─────────────────────────────────────────────────────────────┤
│                     启动与更新层 (Launcher)                   │
│  LanMountainDesktop.Launcher/                                │
└─────────────────────────────────────────────────────────────┘
```

### 2.2 核心设计原则

1. **插件优先**：核心功能通过插件扩展，宿主提供运行时和基础设施
2. **组件化桌面**：所有桌面元素都是组件，统一注册、统一放置
3. **设置分域**：App / Launcher / ComponentInstance / Plugin 四级设置作用域
4. **主题动态化**：支持 Material Design 3 动态配色、系统主题跟随
5. **进程隔离预留**：当前为进程内加载，预留了隔离进程架构

---

## 3. 项目结构与模块职责

### 3.1 解决方案项目列表

| 项目路径 | 输出类型 | 主要职责 |
|---------|---------|---------|
| `LanMountainDesktop/` | WinExe | 主桌面宿主应用，包含 UI、服务、组件系统、插件运行时接入 |
| `LanMountainDesktop.Launcher/` | WinExe | 启动器 - 负责 OOBE、Splash、版本管理、增量更新、插件安装 |
| `LanMountainDesktop.PluginSdk/` | Library | 官方插件 SDK，定义插件可依赖的公开接口与打包行为 |
| `LanMountainDesktop.Shared.Contracts/` | Library | 宿主与插件共享的稳定契约类型 |
| `LanMountainDesktop.Shared.IPC/` | Library | 统一 IPC 基础，用于 Host 公共服务、Launcher/OOBE 启动通知、插件贡献的公共服务 |
| `LanMountainDesktop.Appearance/` | Library | 主题、圆角、外观资源相关基础设施 |
| `LanMountainDesktop.Settings.Core/` | Library | 设置域、持久化和设置基础抽象 |
| `LanMountainDesktop.DesktopHost/` | Library | 桌面宿主流程与生命周期相关逻辑 |
| `LanMountainDesktop.DesktopComponents.Runtime/` | Library | 组件运行时支撑能力 |
| `LanMountainDesktop.Host.Abstractions/` | Library | 宿主侧抽象接口 |
| `LanMountainDesktop.PluginIsolation.Contracts/` | Library | 插件隔离机制的传输无关 DTO、路由常量、错误码 |
| `LanMountainDesktop.PluginIsolation.Ipc/` | Library | 插件隔离 IPC 外观，基于 dotnetCampus.Ipc |
| `LanMountainDesktop.PluginTemplate/` | Library | `dotnet new lmd-plugin` 官方模板 |
| `LanMountainDesktop.PluginUpgradeHelper/` | Library | 插件升级帮助程序 |
| `LanMountainDesktop.Tests/` | Test | 宿主与 SDK 的测试项目 |

### 3.2 主宿主工程内部结构

```
LanMountainDesktop/
├── Program.cs                    # 进程启动主线
├── App.axaml.cs                  # 应用初始化、主题、语言、托盘、插件运行时
├── Views/                        # 界面视图
│   ├── MainWindow.axaml          # 主窗口
│   ├── SettingsWindow.axaml      # 设置窗口
│   ├── ComponentLibraryWindow.axaml  # 组件库窗口
│   ├── FusedDesktopComponentLibraryWindow.axaml  # 融合桌面组件库
│   ├── NotificationWindow.axaml  # 通知窗口
│   ├── TransparentOverlayWindow.axaml  # 透明覆盖层窗口
│   ├── SettingsPages/            # 设置页面
│   ├── Components/               # 桌面组件视图
│   └── ComponentEditors/         # 组件编辑器视图
├── ViewModels/                   # 视图模型
│   ├── MainWindowViewModel.cs
│   ├── ViewModelBase.cs
│   └── ...
├── Services/                     # 业务服务层
│   ├── AppearanceThemeService.cs # 外观主题服务
│   ├── Settings/                 # 设置相关服务
│   ├── MaterialColorService.cs   # Material 颜色服务
│   ├── DesktopTrayService.cs     # 桌面托盘服务
│   ├── FusedDesktopManagerService.cs  # 融合桌面管理
│   └── ...
├── ComponentSystem/              # 组件系统
│   ├── ComponentRegistry.cs      # 组件注册表
│   ├── DesktopComponentDefinition.cs  # 组件定义
│   └── ...
├── plugins/                      # 插件运行时
│   ├── PluginRuntimeService.cs   # 插件运行时服务
│   ├── PluginLoader.cs           # 插件加载器
│   └── ...
├── Theme/                        # 主题资源
├── Styles/                       # 样式规则
├── DesktopEditing/               # 桌面布局编辑
├── Localization/                 # 本地化资源
└── Models/                       # 数据模型
```

### 3.3 Launcher 工程结构

```
LanMountainDesktop.Launcher/
├── Program.cs                    # 启动器入口
├── App.axaml.cs                  # 启动器应用初始化
├── Views/                        # 启动器视图
│   ├── OobeWindow.axaml          # 首次体验窗口
│   └── SplashWindow.axaml        # 启动动画窗口
└── Services/                     # 启动器服务
    ├── DeploymentLocator.cs      # 版本目录定位
    ├── UpdateCheckService.cs     # 更新检查
    ├── UpdateEngineService.cs    # 更新引擎
    ├── LauncherFlowCoordinator.cs # 流程协调器
    ├── OobeStateService.cs       # OOBE 状态管理
    ├── PluginInstallerService.cs # 插件安装
    └── PluginUpgradeQueueService.cs # 插件升级队列
```

---

## 4. 关键类与函数说明

### 4.1 应用程序入口与生命周期

#### `Program`（LanMountainDesktop/Program.cs）

**职责**：应用程序入口点，负责启动初始化、单实例控制、资源加载、渲染模式配置、日志初始化。

**关键属性**：

```csharp
internal static string StartupRenderMode { get; private set; } = AppRenderingModeHelper.Default;
```

**关键方法**：

| 方法 | 签名 | 说明 |
|------|------|------|
| `Main` | `public static void Main(string[] args)` | 应用入口，初始化日志、单实例、遥测，构建 Avalonia AppBuilder |
| `BuildAvaloniaApp` | `public static AppBuilder BuildAvaloniaApp(string renderMode)` | 构建 Avalonia 应用，配置 Win32 渲染模式 |
| `AcquireSingleInstance` | `private static SingleInstanceService AcquireSingleInstance(int? restartParentProcessId)` | 获取单实例锁，支持重启场景 |
| `LoadConfiguredRenderMode` | `private static string LoadConfiguredRenderMode()` | 从设置加载配置的渲染模式 |
| `RegisterGlobalExceptionLogging` | `private static void RegisterGlobalExceptionLogging()` | 注册全局未处理异常日志和遥测 |

#### `App`（LanMountainDesktop/App.axaml.cs）

**职责**：应用启动和生命周期管理，包含应用初始化、主窗口管理、插件运行时初始化、主题设置、设置系统初始化。

**关键属性**：

```csharp
internal static SingleInstanceService? CurrentSingleInstanceService { get; set; }
internal static IHostApplicationLifecycle? CurrentHostApplicationLifecycle { get; }
internal static INotificationService? CurrentNotificationService { get; }
public PluginRuntimeService? PluginRuntimeService => _pluginRuntimeService;
public ISettingsFacadeService SettingsFacade => _settingsFacade;
```

**关键方法**：

| 方法 | 签名 | 说明 |
|------|------|------|
| `Initialize` | `public override void Initialize()` | 初始化应用资源、主题、语言、设置服务 |
| `OnFrameworkInitializationCompleted` | `public override void OnFrameworkInitializationCompleted()` | 框架初始化完成后调用，初始化 IPC、桌面壳层 |
| `InitializeDesktopShell` | `private void InitializeDesktopShell()` | 初始化桌面壳层，包括插件运行时、托盘、主窗口 |
| `OpenIndependentSettingsModule` | `internal void OpenIndependentSettingsModule(string source, string? pageTag)` | 打开独立设置窗口 |
| `ActivateMainWindow` | `internal void ActivateMainWindow()` | 激活主窗口 |

### 4.2 插件系统

#### `PluginRuntimeService`（LanMountainDesktop/plugins/PluginRuntimeService.cs）

**职责**：插件系统的核心运行时类，负责插件的加载、卸载、管理、依赖注入、插件贡献点注册。

**关键属性**：

```csharp
public string PluginsDirectory { get; }                    // 插件目录路径
public IReadOnlyList<LoadedPlugin> LoadedPlugins { get; }  // 已加载插件列表
public IReadOnlyList<PluginLoadResult> LoadResults { get; } // 加载结果列表
public IReadOnlyList<PluginCatalogEntry> Catalog { get; }   // 插件目录
public IReadOnlyList<PluginSettingsSectionContribution> SettingsSections { get; } // 设置页贡献
public IReadOnlyList<PluginDesktopComponentContribution> DesktopComponents { get; } // 组件贡献
public IReadOnlyList<PluginDesktopComponentEditorContribution> DesktopComponentEditors { get; } // 编辑器贡献
```

**关键方法**：

| 方法 | 签名 | 说明 |
|------|------|------|
| `LoadInstalledPlugins` | `public void LoadInstalledPlugins()` | 加载所有已安装插件 |
| `SetPluginEnabled` | `public bool SetPluginEnabled(string pluginId, bool isEnabled)` | 启用/禁用插件 |
| `InstallPluginPackage` | `public PluginManifest InstallPluginPackage(string packagePath)` | 安装插件包（.laapp） |
| `DeleteInstalledPlugin` | `public bool DeleteInstalledPlugin(string pluginId)` | 删除已安装插件 |

#### `IPlugin`（LanMountainDesktop.PluginSdk/IPlugin.cs）

**职责**：插件接口，定义了插件的基本生命周期和能力。插件必须实现此接口以被宿主识别和加载。

```csharp
public interface IPlugin
{
    void Initialize(HostBuilderContext context, IServiceCollection services);
}
```

#### `PluginBase`（LanMountainDesktop.PluginSdk/PluginBase.cs）

**职责**：插件基类，提供了插件开发的基础实现。

```csharp
public abstract class PluginBase : IPlugin
{
    public virtual void Initialize(HostBuilderContext context, IServiceCollection services) { }
}
```

#### `PluginManifest`（LanMountainDesktop.PluginSdk/PluginManifest.cs）

**职责**：插件清单信息类，包含插件的元数据。

```csharp
public sealed record PluginManifest(
    string Id,                    // 插件唯一标识
    string Name,                  // 插件名称
    string EntranceAssembly,      // 入口程序集
    string? Description = null,   // 描述
    string? Author = null,        // 作者
    string? Version = null,       // 版本
    string? ApiVersion = null,    // API 版本
    IReadOnlyList<PluginSharedContractReference>? SharedContracts = null,
    PluginRuntimeConfiguration? Runtime = null)
```

**关键方法**：

| 方法 | 签名 | 说明 |
|------|------|------|
| `Load` | `public static PluginManifest Load(string manifestPath)` | 从文件加载插件清单 |
| `ResolveEntranceAssemblyPath` | `public string ResolveEntranceAssemblyPath(string manifestPath)` | 解析入口程序集路径 |

### 4.3 设置系统

#### `SettingsService`（LanMountainDesktop/Services/Settings/SettingsService.cs）

**职责**：设置系统的核心服务，管理应用和插件的设置数据持久化、读取和保存、设置变更监听。

**关键属性**：

```csharp
public event EventHandler<SettingsChangedEvent>? Changed;  // 设置变更事件
```

**关键方法**：

| 方法 | 签名 | 说明 |
|------|------|------|
| `LoadSnapshot` | `public T LoadSnapshot<T>(SettingsScope scope, string? subjectId = null, string? placementId = null)` | 加载设置快照 |
| `SaveSnapshot` | `public void SaveSnapshot<T>(SettingsScope scope, T snapshot, ...)` | 保存设置快照 |
| `LoadSection` | `public T LoadSection<T>(SettingsScope scope, string subjectId, string sectionId, ...)` | 加载设置节 |
| `SaveSection` | `public void SaveSection<T>(SettingsScope scope, string subjectId, string sectionId, T section, ...)` | 保存设置节 |
| `GetValue` | `public T? GetValue<T>(SettingsScope scope, string key, ...)` | 获取单个值 |
| `SetValue` | `public void SetValue<T>(SettingsScope scope, string key, T value, ...)` | 设置单个值 |
| `GetComponentAccessor` | `public IComponentSettingsAccessor GetComponentAccessor(string componentId, string? placementId)` | 获取组件设置访问器 |

**设置作用域（SettingsScope）**：

| 作用域 | 说明 |
|--------|------|
| `App` | 应用级设置 |
| `Launcher` | 启动器设置 |
| `ComponentInstance` | 组件实例设置 |
| `Plugin` | 插件设置 |

### 4.4 外观主题系统

#### `IAppearanceThemeService`（LanMountainDesktop/Services/AppearanceThemeService.cs）

**职责**：外观主题服务接口，定义了主题获取、预览构建、资源应用等方法。

```csharp
public interface IAppearanceThemeService
{
    AppearanceThemeSnapshot GetCurrent();
    AppearanceThemeSnapshot BuildPreview(ThemeAppearanceSettingsState pendingState);
    event EventHandler<AppearanceThemeSnapshot>? Changed;
    void ApplyThemeResources(IResourceDictionary resources);
    AppearanceMaterialSurface GetMaterialSurface(MaterialSurfaceRole role);
    void ApplyWindowMaterial(Window window, MaterialSurfaceRole role);
}
```

#### `AppearanceThemeService`

**职责**：外观主题服务的实现，委托给 `MaterialColorService` 处理具体逻辑。

**关键方法**：

| 方法 | 签名 | 说明 |
|------|------|------|
| `GetCurrent` | `public AppearanceThemeSnapshot GetCurrent()` | 获取当前主题快照 |
| `BuildPreview` | `public AppearanceThemeSnapshot BuildPreview(ThemeAppearanceSettingsState pendingState)` | 构建主题预览 |
| `ApplyThemeResources` | `public void ApplyThemeResources(IResourceDictionary resources)` | 应用主题资源到资源字典 |
| `GetMaterialSurface` | `public AppearanceMaterialSurface GetMaterialSurface(MaterialSurfaceRole role)` | 获取材质表面配置 |
| `ApplyWindowMaterial` | `public void ApplyWindowMaterial(Window window, MaterialSurfaceRole role)` | 应用窗口材质效果 |

**材质表面角色（MaterialSurfaceRole）**：

| 角色 | 说明 |
|------|------|
| `WindowBackground` | 窗口背景 |
| `SettingsWindowBackground` | 设置窗口背景 |
| `DockBackground` | 停靠栏背景 |
| `StatusBarBackground` | 状态栏背景 |
| `DesktopComponentHost` | 桌面组件宿主 |
| `StatusBarComponentHost` | 状态栏组件宿主 |
| `OverlayPanel` | 覆盖层面板 |

### 4.5 桌面宿主

#### `DesktopBootstrap`（LanMountainDesktop.DesktopHost/DesktopBootstrap.cs）

**职责**：桌面启动引导，协调启动服务初始化和应用初始化。

```csharp
public static class DesktopBootstrap
{
    public static void InitializeStartupServices(
        Action initializeTelemetryIdentity,
        Action initializeCrashTelemetry,
        Action initializeUsageTelemetry,
        Action scheduleStartupCleanup);

    public static void InitializeApplication(Application application, Action initializeShell);
}
```

### 4.6 Launcher 核心服务

#### `DeploymentLocator`（LanMountainDesktop.Launcher/Services/DeploymentLocator.cs）

**职责**：扫描和定位 `app-*` 版本目录，选择最佳版本。

**版本选择算法**：
1. 扫描所有 `app-*` 目录
2. 过滤掉带 `.destroy` 或 `.partial` 标记的目录
3. 优先选择带 `.current` 标记的版本
4. 如果没有 `.current`，选择版本号最高的

#### `UpdateEngineService`

**职责**：下载、验证、应用增量更新，支持原子化更新和回滚。

#### `LauncherFlowCoordinator`

**职责**：协调 OOBE → Splash → 更新 → 插件 → 启动主程序的完整流程。

---

## 5. 依赖关系

### 5.1 项目间依赖图

```
LanMountainDesktop (主程序)
├── LanMountainDesktop.Host.Abstractions
├── LanMountainDesktop.Shared.Contracts
├── LanMountainDesktop.Shared.IPC
├── LanMountainDesktop.Settings.Core
├── LanMountainDesktop.Appearance
├── LanMountainDesktop.DesktopComponents.Runtime
├── LanMountainDesktop.DesktopHost
├── LanMountainDesktop.PluginSdk
└── ThirdParty/DotNetCampus.InkCanvas

LanMountainDesktop.Launcher (启动器)
├── LanMountainDesktop.Shared.Contracts
├── LanMountainDesktop.Shared.IPC
└── LanMountainDesktop.Settings.Core

LanMountainDesktop.PluginSdk (插件SDK)
└── (无项目引用，纯公共接口)

LanMountainDesktop.DesktopHost
├── LanMountainDesktop.Host.Abstractions
└── LanMountainDesktop.Shared.Contracts

LanMountainDesktop.Appearance
├── LanMountainDesktop.Settings.Core
└── LanMountainDesktop.Shared.Contracts

LanMountainDesktop.DesktopComponents.Runtime
├── LanMountainDesktop.Host.Abstractions
└── LanMountainDesktop.Shared.Contracts

LanMountainDesktop.PluginIsolation.Ipc
├── LanMountainDesktop.PluginIsolation.Contracts
└── LanMountainDesktop.Shared.IPC
```

### 5.2 主要 NuGet 依赖

| 包名 | 版本 | 用途 |
|------|------|------|
| Avalonia | 12.0.2 | 跨平台 UI 框架 |
| Avalonia.Controls.WebView | 12.0.0 | WebView 控件 |
| Avalonia.Desktop | 12.0.2 | 桌面平台支持 |
| Avalonia.Themes.Fluent | 12.0.2 | Fluent 主题 |
| FluentAvaloniaUI | 3.0.0-preview2 | Fluent UI 控件库 |
| Material.Avalonia | 3.16.1 | Material Design 控件 |
| MaterialColorUtilities | 0.3.0 | Material Design 3 动态配色 |
| CommunityToolkit.Mvvm | 8.4.2 | MVVM 工具包 |
| Microsoft.Extensions.DependencyInjection | 11.0.0-preview | 依赖注入 |
| Microsoft.Extensions.Hosting.Abstractions | 11.0.0-preview | 宿主抽象 |
| Microsoft.Data.Sqlite | 11.0.0-preview | SQLite 数据库 |
| PostHog | 2.6.0 | 使用遥测 |
| Sentry | 6.4.1 | 崩溃遥测 |
| Downloader | 5.4.0 | 文件下载 |
| Lib.Harmony.Thin | 2.4.2 | 运行时方法拦截 |
| log4net | 3.3.1 | 日志记录 |

---

## 6. 项目运行方式

### 6.1 环境准备

- 安装 **.NET SDK 10**（由 `global.json` 锁定版本 `10.0.103`）
- 桌面端建议优先在 Windows 上开发和验证
- 仓库主入口解决方案文件为 `LanMountainDesktop.slnx`

### 6.2 常用命令

#### 还原与构建

```bash
dotnet restore
dotnet build LanMountainDesktop.slnx -c Debug
```

#### 运行桌面宿主（开发模式）

```bash
# 直接运行主程序，跳过 Launcher
dotnet run --project LanMountainDesktop/LanMountainDesktop.csproj
```

#### 运行桌面宿主（生产模式）

```bash
# 先构建 Launcher
dotnet build LanMountainDesktop.Launcher/LanMountainDesktop.Launcher.csproj -c Debug

# 通过 Launcher 启动主程序
dotnet run --project LanMountainDesktop.Launcher/LanMountainDesktop.Launcher.csproj -- launch
```

#### Launcher 其他命令

```bash
# 检查更新
dotnet run --project LanMountainDesktop.Launcher/LanMountainDesktop.Launcher.csproj -- update check

# 安装插件
dotnet run --project LanMountainDesktop.Launcher/LanMountainDesktop.Launcher.csproj -- plugin install <path-to-plugin.laapp>

# 版本回退
dotnet run --project LanMountainDesktop.Launcher/LanMountainDesktop.Launcher.csproj -- update rollback
```

#### 运行测试

```bash
dotnet test LanMountainDesktop.slnx -c Debug
```

#### 插件本地包生成

```powershell
./scripts/Pack-PluginPackages.ps1
```

### 6.3 Linux 录音依赖

如果在 Linux 上使用录音机或自习监测相关能力，需要安装音频库：

```bash
# Debian/Ubuntu
sudo apt install libportaudio2 libasound2

# Fedora/RHEL
sudo dnf install portaudio-libs alsa-lib

# Arch Linux
sudo pacman -S portaudio alsa-lib

# Alpine Linux
sudo apk add portaudio alsa-lib
```

---

## 7. 启动流程详解

### 7.1 生产环境启动流程（通过 Launcher）

```
用户启动 LanMountainDesktop.Launcher.exe
    │
    ▼
Launcher 扫描 app-* 目录，选择最佳版本
（优先 .current 标记，然后按版本号降序）
    │
    ▼
首次启动？→ 显示 OOBE 引导（OobeWindow）
    │
    ▼
显示 Splash 启动动画（SplashWindow）
    │
    ▼
检查并应用待处理的更新（UpdateEngineService.ApplyPendingUpdate）
    │
    ▼
处理插件升级队列（PluginUpgradeQueueService）
    │
    ▼
启动主程序 app-{version}/LanMountainDesktop.exe
    │
    ▼
清理标记为 .destroy 的旧版本
```

### 7.2 主程序启动流程（LanMountainDesktop.exe）

```
Program.cs Main()
    │
    ├── 初始化日志（AppLogger.Initialize）
    ├── 初始化应用数据路径（AppDataPathProvider.Initialize）
    ├── 解析开发插件选项（DevPluginOptions.Parse）
    ├── 注册全局异常日志
    └── 获取重启父进程 ID
    │
    ▼
获取单实例锁（SingleInstanceService）
    │
    ├── 非主实例？→ 通知主实例并退出
    └── 是主实例？→ 继续
    │
    ▼
初始化启动服务（DesktopBootstrap.InitializeStartupServices）
    │
    ├── 初始化遥测身份（TelemetryIdentityService）
    ├── 初始化崩溃遥测（SentryCrashTelemetryService）
    ├── 初始化使用遥测（PostHogUsageTelemetryService）
    └── 调度白板笔记启动清理
    │
    ▼
运行启动诊断（StartupDiagnosticsService.Run）
    │
    ▼
加载配置的渲染模式（LoadConfiguredRenderMode）
    │
    ▼
构建 Avalonia AppBuilder（BuildAvaloniaApp）
    │
    ▼
进入 App.axaml.cs
    │
    ├── 初始化主题（ApplyThemeFromSettings）
    ├── 初始化语言（ApplyCurrentCultureFromSettings）
    ├── 初始化设置窗口服务（EnsureSettingsWindowService）
    ├── 初始化天气定位刷新（EnsureWeatherLocationRefreshService）
    └── 初始化通知服务（EnsureNotificationService）
    │
    ▼
框架初始化完成（OnFrameworkInitializationCompleted）
    │
    ├── 初始化公共 IPC（InitializePublicIpc）
    ├── 启动单实例激活监听
    ├── 初始化 Launcher IPC（InitializeLauncherIpcAsync）
    └── 初始化桌面壳层（InitializeDesktopShell）
    │
    ▼
桌面壳层初始化
    │
    ├── 初始化插件运行时（InitializePluginRuntime）
    ├── 初始化托盘图标（InitializeTrayIcon）
    ├── 创建主窗口（CreateAndAssignMainWindow）
    └── 启动天气定位刷新
```

### 7.3 版本目录结构

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

**版本标记文件**：
- `.current` - 标记当前使用的版本
- `.partial` - 标记下载未完成的版本（更新失败时自动清理）
- `.destroy` - 标记待删除的旧版本（下次启动时清理）

---

## 8. 插件系统架构

### 8.1 插件生命周期

```
插件包（.laapp）
    │
    ▼
发现阶段（DiscoverCandidates）
    │
    ├── 扫描 PluginsDirectory
    ├── 解析 plugin.json 清单
    └── 验证 API 版本兼容性
    │
    ▼
加载阶段（PluginLoader.LoadFromPackage / LoadFromManifest）
    │
    ├── 注册共享契约
    ├── 加载入口程序集
    ├── 调用 IPlugin.Initialize
    └── 收集贡献点（设置页、组件、编辑器）
    │
    ▼
激活阶段
    │
    ├── 注册设置页到设置窗口
    ├── 注册组件到组件系统
    └── 注册编辑器到编辑器系统
    │
    ▼
运行阶段
    │
    ├── 插件服务通过 DI 容器解析
    ├── 插件通过 IPluginContext 访问宿主功能
    └── 插件通过 IPC 与宿主通信
    │
    ▼
卸载阶段
    │
    ├── 卸载插件程序集
    ├── 清理贡献点
    └── 释放资源
```

### 8.2 插件运行时模式

| 模式 | 状态 | 说明 |
|------|------|------|
| `in-proc` | 当前默认 | 进程内加载，PluginLoadContext 提供程序集隔离 |
| `isolated-background` | 预留 | 后台逻辑移至独立工作进程，Host UI 变为薄 IPC 驱动壳 |
| `isolated-window` | 预留 | 插件 UI 离屏渲染，Host 嵌入平台窗口句柄 |

### 8.3 插件贡献点

插件可以向宿主贡献以下内容：

1. **设置页（Settings Sections）**：通过 `IPluginSettingsService` 注册自定义设置页
2. **桌面组件（Desktop Components）**：通过组件贡献点注册可放置的桌面组件
3. **组件编辑器（Component Editors）**：为组件提供自定义编辑器界面
4. **公共服务（Public Services）**：通过 IPC 向外部提供公共服务

### 8.4 插件目录结构

```
PluginsDirectory/
├── PluginA/
│   ├── plugin.json           # 插件清单
│   ├── PluginA.dll           # 入口程序集
│   └── ...                   # 其他资源
├── PluginB.laapp             # 打包的插件包
└── ...
```

---

## 9. 数据流与交互模型

### 9.1 设置流

```
Settings.Core（基础设置能力）
    │
    ├── 宿主通过 SettingsFacade 读取和监听设置变化
    ├── 插件通过 IPluginSettingsService 访问设置
    └── 组件通过 IComponentSettingsAccessor 访问设置
```

### 9.2 外观流

```
Appearance（主题和圆角资源）
    │
    ├── 宿主在 App.axaml.cs 中应用到资源字典
    ├── MaterialColorService 处理动态配色
    └── 主题变更通过事件通知所有订阅者
```

### 9.3 组件流

```
ComponentSystem（组件定义、注册、扩展接入）
    │
    ├── 内置组件在 ComponentSystem/ 中定义
    ├── 插件通过贡献点注册扩展组件
    └── DesktopEditing/ 处理组件放置和布局
```

### 9.4 插件流

```
plugins/（宿主侧插件运行时）
    │
    ├── .laapp 插件包的发现、安装、替换
    ├── 插件激活与共享契约装配
    └── 插件设置页注册到宿主设置窗口
```

### 9.5 IPC 流

```
Shared.IPC（统一 IPC 基础）
    │
    ├── Host 公共服务
    ├── Launcher/OOBE 启动通知
    ├── 插件贡献的公共服务
    └── 外部集成（External IPC Public API）
```

---

## 10. 测试体系

### 10.1 测试项目

测试项目 `LanMountainDesktop.Tests/` 覆盖以下方面：

| 测试类 | 覆盖内容 |
|--------|---------|
| `CornerRadiusScaleTests.cs` | 圆角和外观缩放 |
| `DesktopPlacementMathTests.cs` | 桌面布局数学计算 |
| `DesktopEditCommitMathTests.cs` | 桌面编辑提交计算 |
| `ComponentSettingsServiceTests.cs` | 组件设置服务 |
| `UiExceptionGuardTests.cs` | UI 异常保护 |
| `WhiteboardNotePersistenceServiceTests.cs` | 白板笔记持久化 |
| `MaterialColorIntegrationTests.cs` | 材质颜色集成 |
| `OobeStateServiceTests.cs` | OOBE 状态服务 |
| `PluginInstallerServiceTests.cs` | 插件安装服务 |
| `PluginUpgradeQueueServiceTests.cs` | 插件升级队列 |
| `LauncherFlowCoordinatorTests.cs` | 启动器流程协调 |
| `LauncherBackgroundServiceTests.cs` | 启动器后台服务 |
| `PluginIpcServerTests.cs` | 插件 IPC 服务端 |
| `PluginIpcClientTests.cs` | 插件 IPC 客户端 |
| `HostShutdownGateTests.cs` | 主机关闭门 |
| `SingleInstanceServiceTests.cs` | 单实例服务 |

### 10.2 测试原则

- 涉及宿主行为、SDK 契约、布局计算或设置持久化的改动，应优先补对应测试
- 优先扩展已有测试而不是新建无关测试入口

---

## 附录 A：快速参考

### A.1 关键文件速查

| 需求 | 优先查看文件 |
|------|-------------|
| 启动问题 | `LanMountainDesktop/Program.cs`, `LanMountainDesktop/App.axaml.cs` |
| Launcher 启动问题 | `LanMountainDesktop.Launcher/Program.cs`, `Services/LauncherFlowCoordinator.cs` |
| 版本管理问题 | `LanMountainDesktop.Launcher/Services/DeploymentLocator.cs` |
| 更新系统问题 | `LanMountainDesktop.Launcher/Services/UpdateEngineService.cs`, `UpdateCheckService.cs` |
| 设置窗口和设置页 | `LanMountainDesktop/Views/`, `ViewModels/`, `Services/Settings/` |
| 插件加载与安装 | `LanMountainDesktop/plugins/PluginRuntimeService.cs` |
| 组件元数据或放置规则 | `LanMountainDesktop/ComponentSystem/` |
| 主题、颜色、圆角 | `LanMountainDesktop/Theme/`, `Styles/`, `LanMountainDesktop.Appearance/` |
| 设置持久化 | `LanMountainDesktop.Settings.Core/`, `LanMountainDesktop/Services/Settings/SettingsService.cs` |
| SDK 接口调整 | `LanMountainDesktop.PluginSdk/`, `LanMountainDesktop.Shared.Contracts/` |
| 桌面壳层或生命周期 | `Program.cs`, `App.axaml.cs`, `LanMountainDesktop.DesktopHost/` |

### A.2 文档权威来源

| 主题 | 权威文档 |
|------|---------|
| 产品定位 | `docs/PRODUCT.md` |
| 架构与模块职责 | `docs/ARCHITECTURE.md` |
| 运行、构建、测试、打包 | `docs/DEVELOPMENT.md` |
| 视觉规范 | `docs/VISUAL_SPEC.md` |
| 圆角规范 | `docs/CORNER_RADIUS_SPEC.md` |
| 生态边界 | `docs/ECOSYSTEM_BOUNDARIES.md` |
| SDK v5 迁移 | `docs/PLUGIN_SDK_V5_MIGRATION.md` |
| 代码地图 | `docs/ai/CODEBASE_MAP.md` |
| AI 协作入口 | `AGENTS.md` |
| Feature 规格 | `.trae/specs/` |

---

*本文档基于 LanMountainDesktop 仓库代码和文档自动生成，如有更新请以仓库最新代码为准。*
