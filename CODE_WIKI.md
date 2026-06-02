# LanMountainDesktop Code Wiki

> 本文档是 LanMountainDesktop（阑山桌面）项目的结构化 Code Wiki，涵盖项目整体架构、主要模块职责、关键类与函数说明、依赖关系以及项目运行方式等关键信息。
>
> 生成日期：2026-06-02
> 技术基线：Avalonia 12.0.3 + .NET 10
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
9. [AirApp 系统架构](#9-airapp-系统架构)
10. [更新与分发系统（Plonds）](#10-更新与分发系统plonds)
11. [数据流与交互模型](#11-数据流与交互模型)
12. [测试体系](#12-测试体系)
13. [附录](#附录)

---

## 1. 项目概述

### 1.1 产品定位

**阑山桌面（LanMountainDesktop）** 是一款跨平台桌面环境增强工具，基于 Avalonia UI 和 .NET 10 构建。

- **产品口号**：你的桌面，不止一面
- **技术基线**：Avalonia UI 12.0.3 + .NET 10 (net10.0)
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
- **AirApp 系统**：独立进程运行的轻量应用（时钟、白板等），通过 IPC 与宿主通信
- **更新与分发**：Plonds 分发系统，支持增量更新、签名验证与回滚
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
┌─────────────────────────────────────────────────────────────────────┐
│                        用户界面层 (UI Layer)                         │
│  Views/  │  ViewModels/  │  Theme/  │  Styles/  │  Localization/   │
├─────────────────────────────────────────────────────────────────────┤
│                       业务服务层 (Service Layer)                     │
│  Services/  │  ComponentSystem/  │  DesktopEditing/  │  plugins/    │
├─────────────────────────────────────────────────────────────────────┤
│                       基础设施层 (Infrastructure)                    │
│  DesktopHost/  │  Appearance/  │  Settings.Core/  │  Shared.IPC/    │
├─────────────────────────────────────────────────────────────────────┤
│                       抽象与契约层 (Abstractions)                    │
│  Host.Abstractions/  │  Shared.Contracts/  │  PluginSdk/            │
├─────────────────────────────────────────────────────────────────────┤
│                       启动与更新层 (Launcher & Update)               │
│  LanMountainDesktop.Launcher/  │  Plonds (分发系统)                  │
├─────────────────────────────────────────────────────────────────────┤
│                       AirApp 层 (独立进程应用)                       │
│  AirAppHost/  │  AirAppRuntime/  │  PluginIsolation/                │
└─────────────────────────────────────────────────────────────────────┘
```

### 2.2 核心设计原则

1. **插件优先**：核心功能通过插件扩展，宿主提供运行时和基础设施
2. **组件化桌面**：所有桌面元素都是组件，统一注册、统一放置
3. **设置分域**：App / Launcher / ComponentInstance / Plugin 四级设置作用域
4. **主题动态化**：支持 Material Design 3 动态配色、系统主题跟随
5. **进程隔离预留**：当前为进程内加载，预留了隔离进程架构（PluginIsolation）
6. **AirApp 独立进程**：轻量应用在独立进程中运行，通过 IPC 与宿主通信
7. **圆角统一**：桌面组件根容器必须使用 `DesignCornerRadiusComponent` 动态资源

### 2.3 进程模型

```
┌──────────────────────────────────────────────────────┐
│                   Launcher 进程                       │
│  OOBE → Splash → 更新检查 → 启动主程序               │
└──────────────────┬───────────────────────────────────┘
                   │ IPC
┌──────────────────▼───────────────────────────────────┐
│                 主宿主进程 (Desktop)                   │
│  ┌────────────────────────────────────────────────┐  │
│  │  UI 线程: MainWindow / Settings / Components   │  │
│  ├────────────────────────────────────────────────┤  │
│  │  插件运行时: PluginLoadContext (进程内)         │  │
│  ├────────────────────────────────────────────────┤  │
│  │  IPC 服务端: PublicIpcHostService              │  │
│  └────────────────────────────────────────────────┘  │
└──────────────────┬───────────────────────────────────┘
                   │ IPC
┌──────────────────▼───────────────────────────────────┐
│              AirAppRuntime 进程                       │
│  管理多个 AirApp 实例的生命周期                        │
│  ┌──────────────┐  ┌──────────────┐                  │
│  │  ClockAirApp │  │ WhiteboardApp│  ...              │
│  └──────────────┘  └──────────────┘                  │
└──────────────────────────────────────────────────────┘
```

---

## 3. 项目结构与模块职责

### 3.1 解决方案项目列表

| 项目路径 | 输出类型 | 主要职责 |
|---------|---------|---------|
| `LanMountainDesktop/` | WinExe | 主桌面宿主应用，包含 UI、服务、组件系统、插件运行时接入 |
| `LanMountainDesktop.Launcher/` | WinExe | 启动器 — 负责 OOBE、Splash、版本管理、增量更新、插件安装 |
| `LanMountainDesktop.PluginSdk/` | Library (NuGet) | 官方插件 SDK v5，定义插件可依赖的公开接口与打包行为 |
| `LanMountainDesktop.Shared.Contracts/` | Library | 宿主与插件共享的稳定契约类型（版本信息、更新协议、IPC 常量等） |
| `LanMountainDesktop.Shared.IPC/` | Library | 统一 IPC 基础，用于 Host 公共服务、Launcher/OOBE 启动通知、插件贡献的公共服务 |
| `LanMountainDesktop.Appearance/` | Library | 主题、圆角、外观资源相关基础设施 |
| `LanMountainDesktop.Settings.Core/` | Library | 设置域、持久化和设置基础抽象 |
| `LanMountainDesktop.DesktopHost/` | Library | 桌面宿主流程与生命周期相关逻辑（引导、壳层、关机协调） |
| `LanMountainDesktop.DesktopComponents.Runtime/` | Library | 组件运行时支撑能力 |
| `LanMountainDesktop.Host.Abstractions/` | Library | 宿主侧抽象接口 |
| `LanMountainDesktop.PluginIsolation.Contracts/` | Library | 插件隔离机制的传输无关 DTO、路由常量、错误码 |
| `LanMountainDesktop.PluginIsolation.Ipc/` | Library | 插件隔离 IPC 外观，基于 dotnetCampus.Ipc |
| `LanMountainDesktop.PluginPackaging/` | Library | 插件打包与安装工具 |
| `LanMountainDesktop.PluginTemplate/` | Template | `dotnet new lmd-plugin` 官方模板 |
| `LanMountainDesktop.AirAppHost/` | WinExe | AirApp 独立宿主进程，承载 AirApp 窗口 |
| `LanMountainDesktop.AirAppRuntime/` | Library | AirApp 运行时 IPC 宿主，管理 AirApp 实例生命周期 |
| `ThirdParty/DotNetCampus.InkCanvas/` | Library | 第三方墨迹画布控件（白板功能） |
| `LanMountainDesktop.Tests/` | Test | 宿主与 SDK 的测试项目 |
| `PenguinLogisticsOnlineNetworkDistributionSystem/` | Tool | Plonds 分发系统（更新源管理、包验证） |

### 3.2 主宿主工程内部结构

```
LanMountainDesktop/
├── Program.cs                    # 进程启动主线
├── App.axaml.cs                  # 应用初始化、主题、语言、托盘、插件运行时
├── ViewLocator.cs                # 视图定位器
├── Views/                        # 界面视图
│   ├── MainWindow.axaml(.cs)     # 主窗口（含多个 partial class）
│   ├── MainWindow.ComponentSystem.cs   # 主窗口-组件系统集成
│   ├── MainWindow.DesktopEditing.cs    # 主窗口-桌面编辑
│   ├── MainWindow.DesktopPaging.cs     # 主窗口-桌面分页
│   ├── MainWindow.RenderBackend.cs     # 主窗口-渲染后端
│   ├── SettingsWindow.axaml(.cs)       # 设置窗口
│   ├── ComponentEditorWindow.axaml(.cs) # 组件编辑器窗口
│   ├── ComponentLibraryWindow.axaml    # 组件库窗口
│   ├── DesktopWidgetWindow.axaml(.cs)  # 桌面小部件窗口
│   ├── NotificationWindow.axaml(.cs)   # 通知窗口
│   ├── NotificationDialogWindow.axaml  # 对话框通知窗口
│   ├── UpdateProgressDialog.axaml(.cs) # 更新进度对话框
│   ├── StudySessionReportWindow.axaml  # 自习报告窗口
│   └── Components/               # 桌面组件视图
│       ├── ClockWidget.axaml(.cs)     # 时钟组件
│       ├── DateWidget.axaml(.cs)      # 日期组件
│       ├── TimerWidget.axaml(.cs)     # 计时器组件
│       ├── WeatherWidget.axaml        # 天气组件
│       ├── ShortcutWidget.axaml       # 快捷方式组件
│       ├── DailyNewsView.axaml        # 每日新闻组件
│       ├── JuyaNewsWidget.axaml       # 聚雅新闻组件
│       ├── BrowserWidget.axaml        # 浏览器组件
│       └── WeatherIconView.cs         # 天气图标视图
├── ViewModels/                   # 视图模型
│   ├── ViewModelBase.cs          # MVVM 基类
│   ├── MainWindowViewModel.cs    # 主窗口 VM
│   ├── SettingsViewModels.cs     # 设置 VM
│   ├── MusicControlViewModel.cs  # 音乐控制 VM
│   ├── NotificationViewModel.cs  # 通知 VM
│   ├── PrivacyPolicyViewModel.cs # 隐私策略 VM
│   ├── ShortcutEditorViewModel.cs # 快捷键编辑 VM
│   ├── UpdateProgressViewModel.cs # 更新进度 VM
│   └── UpdateSettingsViewModel.cs # 更新设置 VM
├── Services/                     # 业务服务层
│   ├── Settings/                 # 设置相关服务
│   │   └── SettingsService.cs    # 设置核心服务
│   ├── Plonds/                   # Plonds 分发服务
│   │   ├── IPlondsService.cs     # Plonds 服务接口
│   │   ├── PlondsService.cs      # Plonds 服务实现
│   │   ├── PlondsPackageStore.cs # 包存储
│   │   ├── PlondsSourceStore.cs  # 源存储
│   │   └── PlondsVerifier.cs     # 签名验证
│   ├── Update/                   # 更新服务
│   │   ├── UpdateOrchestrator.cs # 更新编排器
│   │   ├── UpdateStateStore.cs   # 更新状态存储
│   │   ├── UpdatePathGuard.cs    # 更新路径守卫
│   │   ├── RollbackStrategy.cs   # 回滚策略
│   │   └── ResumableDownloadService.cs # 断点续传下载
│   ├── AirAppLauncherService.cs  # AirApp 启动服务
│   ├── AppDataPathProvider.cs    # 应用数据路径
│   ├── AppDatabaseService.cs     # 数据库服务
│   ├── AppLogger.cs              # 日志服务
│   ├── AppRestartService.cs      # 应用重启服务
│   ├── AppSettingsService.cs     # 应用设置服务
│   ├── AppearanceThemeService.cs # 外观主题服务
│   ├── AttendanceDataStore.cs    # 考勤数据存储
│   ├── CalculatorDataService.cs  # 计算器服务
│   ├── ComponentLibraryServices.cs # 组件库服务
│   ├── ComponentSettingsService.cs # 组件设置服务
│   ├── CurrentUserProfileService.cs # 用户档案服务
│   ├── DataStorageService.cs     # 数据存储服务
│   ├── DesktopGridLayoutService.cs # 桌面网格布局服务
│   ├── DesktopTrayService.cs     # 桌面托盘服务
│   ├── FontFamilyService.cs      # 字体服务
│   ├── FusedDesktopLayoutService.cs # 融合桌面布局服务
│   ├── GlassEffectService.cs     # 毛玻璃效果服务
│   ├── HolidayCalendarService.cs # 节假日日历服务
│   ├── HostShutdownGate.cs       # 宿主关闭门
│   ├── LauncherSettingsService.cs # 启动器设置服务
│   ├── LocalizationService.cs    # 本地化服务
│   ├── LocationService.cs        # 定位服务
│   ├── LunarCalendarService.cs   # 农历服务
│   ├── MaterialColorService.cs   # Material 颜色服务
│   ├── MaterialSurfaceService.cs # Material 表面服务
│   ├── MonetColorService.cs      # Monet 配色服务
│   ├── NotificationService.cs    # 通知服务
│   ├── PowerManagementService.cs # 电源管理服务
│   ├── RecommendationDataService.cs # 推荐数据服务
│   ├── SettingsSearchService.cs  # 设置搜索服务
│   ├── ShortcutHelper.cs         # 快捷方式辅助
│   ├── StudyAnalyticsService.cs  # 学习分析服务
│   ├── SystemWallpaperProvider.cs # 系统壁纸提供者
│   ├── TelemetryServices.cs      # 遥测服务
│   ├── ThemeColorSystemService.cs # 主题颜色系统服务
│   ├── TimeZoneService.cs        # 时区服务
│   ├── UiExceptionGuard.cs       # UI 异常保护
│   ├── WallpaperColorPipeline.cs # 壁纸颜色管线
│   ├── WeatherIconAssetResolver.cs # 天气图标资源解析
│   ├── WebView2RuntimeProbe.cs   # WebView2 运行时探测
│   ├── WindowMaterialService.cs  # 窗口材质服务
│   ├── WindowPassthroughService.cs # 窗口穿透服务
│   ├── WindowsStartMenuService.cs # Windows 开始菜单服务
│   ├── WindowsStartupService.cs  # Windows 开机启动服务
│   └── XiaomiWeatherService.cs   # 小米天气服务
├── ComponentSystem/              # 组件系统
│   └── ComponentRegistry.cs      # 组件注册表
├── DesktopEditing/               # 桌面布局编辑
│   └── DesktopEditSession.cs     # 桌面编辑会话
├── plugins/                      # 插件运行时
│   ├── LoadedPlugin.cs           # 已加载插件
│   ├── PluginLoadContext.cs      # 插件程序集加载上下文
│   ├── PluginExportRegistry.cs   # 插件导出注册表
│   ├── PluginContributions.cs    # 插件贡献点定义
│   ├── PluginCatalogEntry.cs     # 插件目录条目
│   └── DevPluginOptions.cs       # 开发插件选项
├── Controls/                     # 自定义控件
│   ├── GridPreviewControl.cs     # 网格预览控件
│   ├── IconText.axaml(.cs)       # 图标文本控件
│   ├── SettingsOptionCard.axaml(.cs) # 设置选项卡片
│   ├── SettingsSectionCard.axaml # 设置节卡片
│   └── SmoothBorder.cs           # 平滑边框
├── Converters/                   # 值转换器
│   ├── HexToBrushConverter.cs    # 十六进制转画刷
│   └── HexToColorConverter.cs    # 十六进制转颜色
├── Models/                       # 数据模型
│   ├── AppSettingsSnapshot.cs    # 应用设置快照
│   ├── LauncherSettingsSnapshot.cs # 启动器设置快照
│   ├── ComponentSettingsSnapshot.cs # 组件设置快照
│   ├── FusedDesktopLayoutSnapshot.cs # 融合桌面布局快照
│   ├── NotificationItem.cs       # 通知项
│   ├── TaskbarActionItem.cs      # 任务栏操作项
│   ├── WeatherDataModels.cs      # 天气数据模型
│   ├── MaterialColorModels.cs    # Material 颜色模型
│   ├── MonetPalette.cs           # Monet 调色板
│   ├── StudyAnalyticsModels.cs   # 学习分析模型
│   ├── AttendanceModels.cs       # 考勤模型
│   ├── WhiteboardNoteSnapshot.cs # 白板笔记快照
│   └── ...                       # 其他模型
├── Platform/                     # 平台特定代码
│   └── Windows/
│       ├── ChromePatchState.cs   # 窗口边框补丁状态
│       └── PatcherEntrance.cs    # 补丁入口
├── Theme/                        # 主题资源
│   ├── AppThemePalette.cs        # 应用主题调色板
│   ├── ColorMath.cs              # 颜色数学工具
│   ├── FluttermotionToken.cs     # Flutter Motion Token
│   └── ThemeColorContext.cs      # 主题颜色上下文
├── Styles/                       # 样式规则
│   ├── FluttermotionToken.axaml  # Flutter Motion Token 样式
│   ├── GlassModule.axaml         # 毛玻璃模块样式
│   ├── NavigationStyles.axaml    # 导航样式
│   ├── SettingsAnimations.axaml  # 设置动画
│   └── SettingsCardStyles.axaml  # 设置卡片样式
├── Localization/                 # 本地化资源
│   ├── zh-CN.json               # 简体中文
│   ├── en-US.json               # 英文
│   ├── ja-JP.json               # 日文
│   └── ko-KR.json               # 韩文
└── Assets/                       # 静态资源
    ├── Documents/                # 文档资源
    ├── Fonts/                    # 字体文件 (MiSans-VF)
    ├── MaterialWeatherIcons/     # Material 天气图标
    └── endfiled/                 # 表情图片资源
```

### 3.3 Launcher 工程结构

```
LanMountainDesktop.Launcher/
├── Program.cs                    # 启动器入口（CLI 命令解析 + GUI 启动）
├── App.axaml.cs                  # 启动器应用初始化
├── CommandContext.cs             # 命令上下文解析
├── LauncherRuntimeContext.cs     # 启动器运行时上下文
├── AppJsonContext.cs             # JSON 序列化上下文
├── GlobalUsings.cs              # 全局 using
├── Deployment/                   # 部署相关
│   └── HostLaunchPlan.cs        # 宿主启动计划
├── Infrastructure/              # 基础设施
│   ├── Commands.cs              # 命令处理
│   └── Logger.cs                # 日志
├── Models/                      # 数据模型
│   ├── DataLocationModels.cs    # 数据位置模型
│   ├── LauncherResult.cs        # 启动结果
│   ├── OobeStateModels.cs       # OOBE 状态模型
│   ├── PrivacyConfig.cs         # 隐私配置
│   ├── ReleaseInfo.cs           # 发布信息
│   └── UpdateModels.cs          # 更新模型
├── Oobe/                        # 首次体验引导
│   ├── IOobeStep.cs             # OOBE 步骤接口
│   ├── OobeStateService.cs      # OOBE 状态服务
│   ├── WelcomeOobeStep.cs       # 欢迎步骤
│   └── DataLocationOobeStep.cs  # 数据位置步骤
├── Shell/                       # 壳层服务
│   ├── AirAppRuntimeBridge.cs   # AirApp 运行时桥接
│   ├── LaunchUiPresenter.cs     # 启动 UI 展示
│   └── ThemeService.cs          # 主题服务
├── Startup/                     # 启动流程
│   ├── ExistingHostProbe.cs     # 已有宿主探测
│   ├── HostLaunchModels.cs      # 宿主启动模型
│   ├── HostLaunchService.cs     # 宿主启动服务
│   └── LaunchPipeline.cs        # 启动管线
├── ViewModels/
│   └── RelayCommand.cs          # 命令绑定
├── Views/                       # 视图
│   ├── OobeWindow.axaml(.cs)    # OOBE 窗口
│   ├── SplashWindow.axaml(.cs)  # 启动动画窗口
│   ├── UpdateWindow.axaml(.cs)  # 更新窗口
│   ├── ErrorWindow.axaml(.cs)   # 错误窗口
│   ├── ErrorDebugWindow.axaml   # 错误调试窗口
│   └── DevDebugWindow.axaml     # 开发调试窗口
└── Resources/                   # 资源
    ├── Strings.cs               # 本地化字符串
    ├── Strings.resx             # 默认语言
    ├── Strings.en-US.resx       # 英文
    ├── Strings.ja-JP.resx       # 日文
    └── Strings.ko-KR.resx       # 韩文
```

### 3.4 AirApp 工程结构

```
LanMountainDesktop.AirAppHost/
├── Program.cs                    # AirApp 宿主进程入口
├── AirApp.axaml(.cs)            # AirApp 应用定义
├── AirAppWindow.axaml(.cs)      # AirApp 窗口（IPC 通信 + 窗口管理）
├── AirAppLaunchOptions.cs       # 启动选项
├── AirAppWindowChromeMode.cs    # 窗口边框模式
├── AirAppWindowDescriptor.cs    # 窗口描述符
├── ClockAirAppView.axaml(.cs)   # 时钟 AirApp 视图
└── WorldClockAirAppView.axaml   # 世界时钟 AirApp 视图

LanMountainDesktop.AirAppRuntime/
├── Program.cs                    # AirApp 运行时入口
├── AirAppRuntimeIpcHost.cs      # IPC 宿主服务
├── AirAppHostLocator.cs         # AirApp 宿主定位
├── AirAppInstanceKey.cs         # 实例标识
├── AirAppRuntimeLogger.cs       # 运行时日志
└── AirAppRuntimeOptions.cs      # 运行时选项
```

---

## 4. 关键类与函数说明

### 4.1 应用程序入口与生命周期

#### `Program`（LanMountainDesktop/Program.cs）

**职责**：应用程序入口点，负责启动初始化、渲染模式配置、全局异常日志、遥测初始化。

**关键属性**：

```csharp
internal static string StartupRenderMode { get; private set; } = AppRenderingModeHelper.Default;
```

**关键方法**：

| 方法 | 签名 | 说明 |
|------|------|------|
| `Main` | `public static void Main(string[] args)` | 应用入口，初始化日志、数据路径、开发插件选项、遥测，构建 Avalonia AppBuilder |
| `BuildAvaloniaApp` | `public static AppBuilder BuildAvaloniaApp(string renderMode)` | 构建 Avalonia 应用，配置 Win32 渲染模式 |
| `LoadConfiguredRenderMode` | `private static string LoadConfiguredRenderMode()` | 从设置加载配置的渲染模式 |
| `LoadChromePatchState` | `private static void LoadChromePatchState()` | 加载窗口边框补丁状态 |
| `InstallChromePatchersIfNeeded` | `private static void InstallChromePatchersIfNeeded()` | 安装窗口边框补丁（仅 Windows x64/x86） |
| `RegisterGlobalExceptionLogging` | `private static void RegisterGlobalExceptionLogging()` | 注册全局未处理异常日志和遥测 |
| `InitializeTelemetryIdentity` | `private static void InitializeTelemetryIdentity()` | 初始化遥测身份 |
| `InitializeCrashTelemetry` | `private static void InitializeCrashTelemetry()` | 初始化 Sentry 崩溃遥测 |
| `InitializeUsageTelemetry` | `private static void InitializeUsageTelemetry()` | 初始化 PostHog 使用遥测 |

#### `App`（LanMountainDesktop/App.axaml.cs）

**职责**：应用启动和生命周期管理，包含应用初始化、主窗口管理、插件运行时初始化、主题设置、设置系统初始化。

**关键方法**：

| 方法 | 签名 | 说明 |
|------|------|------|
| `Initialize` | `public override void Initialize()` | 初始化应用资源、主题、语言、设置服务 |
| `OnFrameworkInitializationCompleted` | `public override void OnFrameworkInitializationCompleted()` | 框架初始化完成后调用，初始化 IPC、桌面壳层 |
| `InitializeDesktopShell` | `private void InitializeDesktopShell()` | 初始化桌面壳层，包括插件运行时、托盘、主窗口 |
| `OpenIndependentSettingsModule` | `internal void OpenIndependentSettingsModule(string source, string? pageTag)` | 打开独立设置窗口 |
| `ActivateMainWindow` | `internal void ActivateMainWindow()` | 激活主窗口 |

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

#### `DesktopShellHost`（LanMountainDesktop.DesktopHost/DesktopShellHost.cs）

**职责**：桌面壳层宿主，管理桌面壳层的初始化和生命周期。

#### `ShutdownCoordinator`（LanMountainDesktop.DesktopHost/ShutdownCoordinator.cs）

**职责**：关机协调器，协调各模块的关闭顺序。

### 4.2 插件系统

#### `LoadedPlugin`（LanMountainDesktop/plugins/LoadedPlugin.cs）

**职责**：表示一个已加载的插件，包含其元数据、程序集和托管生命周期（含释放逻辑）。

#### `PluginLoadContext`（LanMountainDesktop/plugins/PluginLoadContext.cs）

**职责**：自定义程序集加载上下文，负责解析和加载插件程序集及其依赖项，提供程序集隔离。

#### `PluginExportRegistry`（LanMountainDesktop/plugins/PluginExportRegistry.cs）

**职责**：维护插件服务导出注册表，提供查询和管理插件间服务集成的方法。

#### `PluginContributions`（LanMountainDesktop/plugins/PluginContributions.cs）

**职责**：定义插件向系统贡献的内容记录，包括设置页、组件和编辑器贡献。

#### `PluginCatalogEntry`（LanMountainDesktop/plugins/PluginCatalogEntry.cs）

**职责**：表示插件目录中的条目，包含清单数据、加载状态和插件能力信息。

#### `DevPluginOptions`（LanMountainDesktop/plugins/DevPluginOptions.cs）

**职责**：解析和管理开发模式下的插件设置和命令行参数。

### 4.3 Plugin SDK 接口

#### `IPlugin`（LanMountainDesktop.PluginSdk/IPlugin.cs）

**职责**：插件核心接口，定义插件的初始化方法。

```csharp
public interface IPlugin
{
    void Initialize(HostBuilderContext context, IServiceCollection services);
}
```

#### `PluginBase`（LanMountainDesktop.PluginSdk/PluginBase.cs）

**职责**：插件抽象基类，提供默认实现。

```csharp
public abstract class PluginBase : IPlugin
{
    public virtual void Initialize(HostBuilderContext context, IServiceCollection services) { }
}
```

#### `IPluginWorker` / `PluginWorkerBase`

**职责**：定义插件后台工作线程的生命周期。

```csharp
public interface IPluginWorker
{
    void ConfigureServices(IServiceCollection services);
    Task StartAsync(IPluginWorkerContext context, CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}
```

#### `IPluginRuntimeContext`（LanMountainDesktop.PluginSdk/IPluginRuntimeContext.cs）

**职责**：插件运行时上下文，提供访问插件清单、目录、服务提供者和外观上下文。

```csharp
public interface IPluginRuntimeContext
{
    PluginManifest Manifest { get; }
    string PluginDirectory { get; }
    IServiceProvider Services { get; }
    IPluginAppearanceContext Appearance { get; }
}
```

#### `IPluginExportRegistry`（LanMountainDesktop.PluginSdk/IPluginExportRegistry.cs）

**职责**：插件服务导出注册表接口。

```csharp
public interface IPluginExportRegistry
{
    T? GetExport<T>();
    IEnumerable<T> GetExports<T>();
}
```

#### `IPluginMessageBus`（LanMountainDesktop.PluginSdk/IPluginMessageBus.cs）

**职责**：插件间通信的消息总线机制。

```csharp
public interface IPluginMessageBus
{
    void Publish<T>(T message) where T : class;
    IObservable<T> Subscribe<T>() where T : class;
}
```

#### `IPluginPackageManager`（LanMountainDesktop.PluginSdk/IPluginPackageManager.cs）

**职责**：插件包管理器，处理插件的安装、卸载和更新。

#### `IPluginPublicIpcBuilder`（LanMountainDesktop.PluginSdk/IPluginPublicIpcBuilder.cs）

**职责**：插件公共 IPC 构建器，用于构建插件间的 IPC 通信。

#### `IPluginSettingsService`（LanMountainDesktop.PluginSdk/IPluginSettingsService.cs）

**职责**：插件设置服务，提供设置项的获取和更新。

#### `IPluginAppearanceContext`（LanMountainDesktop.PluginSdk/IPluginAppearanceContext.cs）

**职责**：插件外观上下文，用于插件与应用程序主题和外观相关的操作。

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

#### `PluginRuntimeMode`（LanMountainDesktop.PluginSdk/PluginRuntimeMode.cs）

**职责**：定义插件的运行时模式。

| 模式 | 说明 |
|------|------|
| `InProc` | 进程内加载（当前默认） |
| `IsolatedBackground` | 后台逻辑移至独立工作进程（预留） |
| `IsolatedWindow` | 插件 UI 离屏渲染（预留） |

#### `PluginSdkInfo`（LanMountainDesktop.PluginSdk/PluginSdkInfo.cs）

**职责**：提供插件 SDK 的版本信息和 API 版本信息。

#### `ISettingsService` / `ISettingsCatalog` / `SettingsPageBase`

**职责**：插件设置系统接口，提供设置页注册、设置项读写和分类管理。

### 4.4 设置系统

#### `SettingsService`（LanMountainDesktop/Services/Settings/SettingsService.cs）

**职责**：设置系统的核心服务，管理应用和插件的设置数据持久化、读取和保存、设置变更监听。

**关键事件**：

```csharp
public event EventHandler<SettingsChangedEvent>? Changed;
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

#### `AppSettingsService`（LanMountainDesktop/Services/AppSettingsService.cs）

**职责**：应用全局设置的加载和保存，带缓存机制。

#### `LauncherSettingsService`（LanMountainDesktop/Services/LauncherSettingsService.cs）

**职责**：启动器设置的加载和保存，处理旧设置文件的迁移。

### 4.5 外观主题系统

#### `IMaterialColorService`（LanMountainDesktop/Services/IMaterialColorService.cs）

**职责**：材料颜色服务的核心公开接口。

```csharp
public interface IMaterialColorService
{
    MaterialColorSnapshot GetColorSnapshot();
    void ApplyThemeResources(IResourceDictionary resources);
    AppearanceMaterialSurface GetMaterialSurface(MaterialSurfaceRole role);
    void ApplyWindowMaterial(Window window, MaterialSurfaceRole role);
}
```

#### `MaterialColorService`（LanMountainDesktop/Services/MaterialColorService.cs）

**职责**：Material Design 3 颜色和材料主题的构建与应用，包括颜色获取、表面材质构建、窗体材质应用。

#### `AppearanceThemeService`（LanMountainDesktop/Services/AppearanceThemeService.cs）

**职责**：外观主题服务的主要实现，委托给 `MaterialColorService` 处理具体逻辑。

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

#### `ThemeColorSystemService`（LanMountainDesktop/Services/ThemeColorSystemService.cs）

**职责**：主题颜色的构建和应用，定义资源键并构建不同颜色上下文下的应用主题调色板。

#### `GlassEffectService`（LanMountainDesktop/Services/GlassEffectService.cs）

**职责**：毛玻璃效果的资源应用，将毛玻璃材质应用于 UI 资源。

#### `WindowMaterialService`（LanMountainDesktop/Services/WindowMaterialService.cs）

**职责**：窗口材料的应用，定义可用的系统材料模式并实现对指定窗口应用特定材料类型。

### 4.6 桌面组件系统

#### `ComponentRegistry`（LanMountainDesktop/ComponentSystem/ComponentRegistry.cs）

**职责**：组件注册中心，负责组件的注册和管理，包括加载组件插件、注册插件组件以及搜索插件组件。

#### `DesktopEditSession`（LanMountainDesktop/DesktopEditing/DesktopEditSession.cs）

**职责**：桌面编辑会话，支持编辑桌面组件、管理组件状态和完成编辑操作。

#### `DesktopGridLayoutService`（LanMountainDesktop/Services/DesktopGridLayoutService.cs）

**职责**：桌面网格布局的设置与应用，支持获取和设置桌面网格的行数、列数以及自动网格等参数。

#### `FusedDesktopLayoutService`（LanMountainDesktop/Services/FusedDesktopLayoutService.cs）

**职责**：融合桌面布局，通过初始化和应用桌面布局策略，融合不同的桌面网格布局。

### 4.7 通知系统

#### `NotificationService`（LanMountainDesktop/Services/NotificationService.cs）

**职责**：通知显示服务，支持普通通知和对话框通知，可根据通知内容展示信息、成功、警告或错误类型的通知，支持自定义图标和按钮。

### 4.8 AirApp 系统

#### `AirAppLauncherService`（LanMountainDesktop/Services/AirAppLauncherService.cs）

**职责**：启动 Air 应用的服务，通过 IPC 通信与 AirApp Runtime 交互，支持启动世界时钟和白板等应用。

#### `AirAppRuntimeIpcHost`（LanMountainDesktop.AirAppRuntime/AirAppRuntimeIpcHost.cs）

**职责**：管理 AirApp 的 IPC 通信，处理注册、生命周期和 IPC 服务。

#### `AirAppWindow`（LanMountainDesktop.AirAppHost/AirAppWindow.axaml.cs）

**职责**：AirApp 窗口的创建和行为管理，支持不同窗口类型和进程间通信。

### 4.9 更新系统

#### `UpdateOrchestrator`（LanMountainDesktop/Services/Update/UpdateOrchestrator.cs）

**职责**：更新编排器，管理软件更新流程，包括检查更新、下载、安装、回滚等阶段。

**关键事件**：

```csharp
public event EventHandler<UpdateStateChangedEventArgs>? StateChanged;
public event EventHandler<UpdateProgressChangedEventArgs>? ProgressChanged;
```

**关键方法**：

| 方法 | 说明 |
|------|------|
| `CheckForUpdateAsync` | 检查更新 |
| `DownloadUpdateAsync` | 下载更新 |
| `ApplyUpdateAsync` | 应用更新 |
| `RollbackAsync` | 回滚更新 |

### 4.10 Plonds 分发系统

#### `IPlondsService` / `PlondsService`（LanMountainDesktop/Services/Plonds/）

**职责**：Plonds 分发服务接口和实现，管理更新源和包的分发。

#### `PlondsPackageStore` / `PlondsSourceStore`

**职责**：包存储和源存储，管理 Plonds 包和源的持久化。

#### `PlondsVerifier`

**职责**：签名验证，确保分发包的完整性和来源可信。

### 4.11 遥测系统

#### `TelemetryServices`（LanMountainDesktop/Services/TelemetryServices.cs）

**职责**：遥测数据收集，负责将日志信息发送到远程服务器并支持客户端标识和上报频率设置。

**子服务**：
- `SentryCrashTelemetryService` — 崩溃遥测（基于 Sentry）
- `PostHogUsageTelemetryService` — 使用遥测（基于 PostHog）
- `TelemetryIdentityService` — 遥测身份管理

### 4.12 其他关键服务

| 服务 | 职责 |
|------|------|
| `LocalizationService` | 本地化和语言设置管理 |
| `IWeatherDataService` / `XiaomiWeatherService` | 天气数据获取（小米天气 API） |
| `IStudyAnalyticsService` / `StudyAnalyticsService` | 学习分析和行为数据收集 |
| `IMusicControlService` | 音乐播放控制 |
| `ICalculatorDataService` / `CalculatorDataService` | 计算器服务 |
| `HolidayCalendarService` | 节假日日历 |
| `LunarCalendarService` | 农历计算 |
| `LocationService` | 地理定位 |
| `PowerManagementService` | 电源管理 |
| `WindowsStartMenuService` | Windows 开始菜单应用枚举 |
| `WindowsStartupService` | Windows 开机启动项管理 |
| `HostShutdownGate` | 宿主关闭门，协调关闭流程 |
| `UiExceptionGuard` | UI 异常保护 |
| `WebView2RuntimeProbe` | WebView2 运行时可用性探测 |
| `AppDataPathProvider` | 应用数据路径提供 |
| `AppDatabaseService` | SQLite 数据库服务 |
| `ResumableDownloadService` | 断点续传下载服务 |
| `WallpaperColorPipeline` | 壁纸颜色提取管线 |
| `MonetColorService` | Monet 动态配色服务 |

### 4.13 共享契约（Shared.Contracts）

#### `AppVersionProvider`（LanMountainDesktop.Shared.Contracts/Launcher/AppVersionProvider.cs）

**职责**：提供多种方法来解析和获取应用程序版本信息，包括从文件、可执行文件、部署目录等。

#### `AppVersionInfo`

```csharp
public record AppVersionInfo(Version Version, string Codename, string FullVersionText);
```

#### `LauncherRuntimeMetadata`

**职责**：从命令行参数、环境变量等中提取运行时元数据，包括包根路径、版本、代号等。

#### `HostExitCodes`

**职责**：定义标准的主机进程退出码。

#### `LauncherIpc`

**职责**：定义启动器 IPC 相关的常量（环境变量、选项名称等）。

#### `LoadingState`

**职责**：定义加载项类型、加载状态以及相关数据结构，用于跟踪启动过程中的加载进度。

#### `UpdateManifest` / `UpdateState` / `UpdatePaths` / `UpdateMessages`

**职责**：更新协议相关类型，定义更新清单、状态、路径和消息格式。

#### `AppearanceCornerRadiusTokens`

**职责**：外观圆角样式标记，提供统一的 UI 圆角设计 Token。

### 4.14 共享 IPC（Shared.IPC）

#### `IAirAppLifecycleService`（LanMountainDesktop.Shared.IPC/Abstractions/Services/）

**职责**：AirApp 生命周期管理服务契约。

```csharp
public interface IAirAppLifecycleService
{
    Task OpenAsync(AirAppInstanceKey key, AirAppWindowDescriptor descriptor);
    Task ActivateAsync(AirAppInstanceKey key);
    Task RegisterAsync(AirAppInstanceKey key);
    Task UnregisterAsync(AirAppInstanceKey key);
    Task CloseAsync(AirAppInstanceKey key);
}
```

#### `PublicIpcHostService`

**职责**：公共 IPC 宿主服务，管理 IPC 会话和路由通知。

#### `IpcConstants` / `IpcRoutedNotifyIds`

**职责**：IPC 常量和路由通知 ID 定义。

#### `PublicAppInfoSnapshot` / `PublicPluginDescriptor`

**职责**：公共应用信息快照和插件描述符。

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
├── LanMountainDesktop.PluginPackaging
├── LanMountainDesktop.PluginSdk
└── ThirdParty/DotNetCampus.InkCanvas

LanMountainDesktop.Launcher (启动器)
├── LanMountainDesktop.Shared.Contracts
├── LanMountainDesktop.Shared.IPC
└── LanMountainDesktop.PluginPackaging

LanMountainDesktop.PluginSdk (插件 SDK)
├── LanMountainDesktop.PluginIsolation.Contracts
├── LanMountainDesktop.Shared.Contracts
├── LanMountainDesktop.Shared.IPC
└── NuGet: Avalonia, FluentAvaloniaUI, FluentIcons, DI Abstractions, dotnetCampus.Ipc

LanMountainDesktop.DesktopHost
├── LanMountainDesktop.Host.Abstractions
└── LanMountainDesktop.Shared.Contracts

LanMountainDesktop.Appearance
├── LanMountainDesktop.Settings.Core
└── LanMountainDesktop.Shared.Contracts

LanMountainDesktop.DesktopComponents.Runtime
├── LanMountainDesktop.Host.Abstractions
└── LanMountainDesktop.Shared.Contracts

LanMountainDesktop.Settings.Core
└── LanMountainDesktop.Shared.Contracts

LanMountainDesktop.Host.Abstractions
└── LanMountainDesktop.Shared.Contracts

LanMountainDesktop.Shared.IPC
└── LanMountainDesktop.Shared.Contracts

LanMountainDesktop.PluginIsolation.Ipc
├── LanMountainDesktop.PluginIsolation.Contracts
└── LanMountainDesktop.Shared.IPC

LanMountainDesktop.PluginIsolation.Contracts
└── (无项目引用)

LanMountainDesktop.AirAppHost
└── LanMountainDesktop.Shared.Contracts

LanMountainDesktop.AirAppRuntime
└── LanMountainDesktop.Shared.Contracts
```

### 5.2 主要 NuGet 依赖

| 包名 | 版本 | 用途 |
|------|------|------|
| Avalonia | 12.0.3 | 跨平台 UI 框架 |
| Avalonia.Controls.WebView | 12.0.1 | WebView 控件 |
| Avalonia.Desktop | 12.0.3 | 桌面平台支持 |
| Avalonia.Themes.Fluent | 12.0.3 | Fluent 主题 |
| Avalonia.Fonts.Inter | 12.0.3 | Inter 字体 |
| FluentAvaloniaUI | 3.0.0-preview4 | Fluent UI 控件库 |
| FluentIcons.Avalonia | 2.1.325 | Fluent 图标 |
| Material.Avalonia | 3.17.0 | Material Design 控件 |
| Material.Icons.Avalonia | 3.0.3-nightly.0.2 | Material 图标 |
| MaterialColorUtilities | 0.3.0 | Material Design 3 动态配色 |
| CommunityToolkit.Mvvm | 8.4.2 | MVVM 工具包 |
| Microsoft.Extensions.DependencyInjection | 11.0.0-preview | 依赖注入 |
| Microsoft.Extensions.Hosting.Abstractions | 11.0.0-preview | 宿主抽象 |
| Microsoft.Data.Sqlite | 11.0.0-preview | SQLite 数据库 |
| PostHog | 2.7.1 | 使用遥测 |
| Sentry | 6.5.0 | 崩溃遥测 |
| Downloader | 5.4.0 | 文件下载 |
| Lib.Harmony.Thin | 2.4.2 | 运行时方法拦截 |
| dotnetCampus.Ipc | 2.0.0-alpha436 | 进程间通信 |
| DotNetCampus.AvaloniaInkCanvas | 1.0.1 | 墨迹画布（白板） |
| ClassIsland.Markdown.Avalonia | 12.0.0 | Markdown 渲染 |
| PortAudioSharp2 | 1.0.6 | 音频录制 |
| System.Drawing.Common | 11.0.0-preview | 图像处理 |
| System.Runtime.WindowsRuntime | 5.0.0-preview | WinRT 互操作 |
| Tmds.DBus.Protocol | 0.92.0 | Linux DBus 通信 |
| MudTools.OfficeInterop | 2.0.9 | Office 互操作 |
| YamlDotNet | 17.1.0 | YAML 解析 |
| log4net | 3.3.1 | 日志记录 |

### 5.3 全局构建配置

**Directory.Build.props**：

```xml
<Version>0.0.0-dev</Version>
<TargetFramework>net10.0</TargetFramework>
<Nullable>enable</Nullable>
<ImplicitUsings>enable</ImplicitUsings>
<ServerGarbageCollection>true</ServerGarbageCollection>
```

**Directory.Packages.props**：使用中央包版本管理（`ManagePackageVersionsCentrally`），所有 NuGet 包版本在此统一声明。

---

## 6. 项目运行方式

### 6.1 环境准备

- 安装 **.NET SDK 10**
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
Launcher Program.Main() 解析命令上下文
    │
    ├── 旧版插件安装？→ PluginInstallerService → 退出
    ├── 非 GUI 命令？→ RunCliCommandAsync → 退出
    └── GUI 命令？→ 继续
    │
    ▼
LauncherRuntimeContext 初始化
LauncherServiceRegistration 注册服务
    │
    ▼
LaunchPipeline 启动管线
    │
    ├── ExistingHostProbe 探测已有宿主实例
    ├── HostLaunchService 准备宿主启动
    │
    ▼
首次启动？→ 显示 OOBE 引导（OobeWindow）
    │
    ▼
显示 Splash 启动动画（SplashWindow）
    │
    ▼
检查并应用待处理的更新
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
    └── 注册全局异常日志
    │
    ▼
DesktopBootstrap.InitializeStartupServices
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
加载窗口边框补丁状态（LoadChromePatchState）
    │
    ▼
安装窗口边框补丁（InstallChromePatchersIfNeeded，仅 Windows x64/x86）
    │
    ▼
构建 Avalonia AppBuilder（BuildAvaloniaApp）
    │
    ▼
进入 App.axaml.cs
    │
    ├── 初始化主题（ApplyThemeFromSettings）
    ├── 初始化语言（ApplyCurrentCultureFromSettings）
    ├── 初始化设置窗口服务
    ├── 初始化天气定位刷新
    └── 初始化通知服务
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
│   ├── AirAppHost/                   ← AirApp 宿主
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
- `.current` — 标记当前使用的版本
- `.partial` — 标记下载未完成的版本（更新失败时自动清理）
- `.destroy` — 标记待删除的旧版本（下次启动时清理）

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
加载阶段（PluginLoadContext 加载程序集）
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
    ├── 插件通过 IPluginRuntimeContext 访问宿主功能
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
| `InProc` | 当前默认 | 进程内加载，`PluginLoadContext` 提供程序集隔离 |
| `IsolatedBackground` | 预留 | 后台逻辑移至独立工作进程，Host UI 变为薄 IPC 驱动壳 |
| `IsolatedWindow` | 预留 | 插件 UI 离屏渲染，Host 嵌入平台窗口句柄 |

### 8.3 插件贡献点

插件可以向宿主贡献以下内容：

1. **设置页（Settings Sections）**：通过 `IPluginSettingsService` 注册自定义设置页
2. **桌面组件（Desktop Components）**：通过组件贡献点注册可放置的桌面组件
3. **组件编辑器（Component Editors）**：为组件提供自定义编辑器界面
4. **公共服务（Public Services）**：通过 `IPluginPublicIpcBuilder` 向外部提供 IPC 服务
5. **消息订阅（Message Bus）**：通过 `IPluginMessageBus` 进行插件间通信

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

### 8.5 插件清单格式（plugin.json）

```json
{
  "id": "com.example.my-plugin",
  "name": "My Plugin",
  "entranceAssembly": "MyPlugin.dll",
  "description": "A sample plugin",
  "author": "Author Name",
  "version": "1.0.0",
  "apiVersion": "5.0.0",
  "sharedContracts": [],
  "runtime": {
    "mode": "in-proc"
  }
}
```

---

## 9. AirApp 系统架构

### 9.1 概述

AirApp 是阑山桌面的轻量独立应用机制。每个 AirApp 在独立进程中运行，通过 IPC 与主宿主通信。

### 9.2 架构组件

| 组件 | 职责 |
|------|------|
| `AirAppLauncherService` | 宿主侧服务，负责启动 AirApp Runtime 进程 |
| `AirAppRuntimeIpcHost` | Runtime 侧 IPC 宿主，管理 AirApp 实例的注册和生命周期 |
| `AirAppHost` | 独立进程，承载 AirApp 窗口 |
| `AirAppWindow` | AirApp 窗口，支持不同窗口类型和 IPC 通信 |
| `IAirAppLifecycleService` | AirApp 生命周期管理服务契约 |

### 9.3 AirApp 启动流程

```
宿主 AirAppLauncherService.LaunchAsync()
    │
    ▼
启动 AirAppRuntime 进程
    │
    ▼
AirAppRuntimeIpcHost 初始化 IPC
    │
    ▼
通过 IPC 请求打开 AirApp 实例
    │
    ▼
AirAppHost 进程启动，创建 AirAppWindow
    │
    ▼
AirAppWindow 通过 IPC 与宿主通信
```

### 9.4 内置 AirApp

| AirApp | 视图 | 说明 |
|--------|------|------|
| 时钟 | `ClockAirAppView` | 桌面时钟 |
| 世界时钟 | `WorldClockAirAppView` | 多时区时钟 |
| 白板 | （使用 InkCanvas） | 桌面白板笔记 |

---

## 10. 更新与分发系统（Plonds）

### 10.1 概述

Plonds（Penguin Logistics Online Network Distribution System）是阑山桌面的分发系统，负责更新源管理、包签名验证和增量更新。

### 10.2 架构组件

| 组件 | 职责 |
|------|------|
| `IPlondsService` / `PlondsService` | 分发服务接口和实现 |
| `PlondsPackageStore` | 包存储管理 |
| `PlondsSourceStore` | 更新源存储 |
| `PlondsVerifier` | 包签名验证 |
| `UpdateOrchestrator` | 更新编排器（检查、下载、安装、回滚） |
| `ResumableDownloadService` | 断点续传下载 |
| `RollbackStrategy` | 回滚策略 |
| `UpdatePathGuard` | 更新路径守卫 |

### 10.3 更新流程

```
UpdateOrchestrator.CheckForUpdateAsync()
    │
    ▼
PlondsService 获取更新清单
    │
    ▼
PlondsVerifier 验证签名
    │
    ▼
UpdateOrchestrator.DownloadUpdateAsync()
    │
    ├── ResumableDownloadService 断点续传下载
    └── 进度通知
    │
    ▼
UpdateOrchestrator.ApplyUpdateAsync()
    │
    ├── UpdatePathGuard 保护路径
    ├── 创建版本目录
    ├── 标记 .partial
    └── 标记 .current / .destroy
    │
    ▼
如失败 → RollbackStrategy 回滚
```

---

## 11. 数据流与交互模型

### 11.1 设置流

```
Settings.Core（基础设置能力）
    │
    ├── 宿主通过 SettingsFacade 读取和监听设置变化
    ├── 插件通过 IPluginSettingsService 访问设置
    └── 组件通过 IComponentSettingsAccessor 访问设置
```

### 11.2 外观流

```
Appearance（主题和圆角资源）
    │
    ├── 宿主在 App.axaml.cs 中应用到资源字典
    ├── MaterialColorService 处理动态配色
    ├── MonetColorService 处理壁纸取色
    ├── WallpaperColorPipeline 从系统壁纸提取颜色
    └── 主题变更通过事件通知所有订阅者
```

### 11.3 组件流

```
ComponentSystem（组件定义、注册、扩展接入）
    │
    ├── 内置组件在 Views/Components/ 中定义
    ├── 插件通过贡献点注册扩展组件
    ├── ComponentRegistry 统一管理组件注册
    ├── DesktopEditSession 处理组件放置和布局编辑
    └── DesktopGridLayoutService / FusedDesktopLayoutService 管理网格布局
```

### 11.4 插件流

```
plugins/（宿主侧插件运行时）
    │
    ├── .laapp 插件包的发现、安装、替换
    ├── PluginLoadContext 提供程序集隔离
    ├── PluginExportRegistry 管理服务导出
    ├── PluginContributions 收集贡献点
    └── 插件设置页注册到宿主设置窗口
```

### 11.5 IPC 流

```
Shared.IPC（统一 IPC 基础）
    │
    ├── Host 公共服务（PublicIpcHostService）
    ├── Launcher/OOBE 启动通知
    ├── AirApp 生命周期管理（IAirAppLifecycleService）
    ├── 插件贡献的公共服务
    └── 外部集成（External IPC Public API）
```

### 11.6 更新流

```
Plonds（分发系统）
    │
    ├── PlondsService 管理更新源
    ├── PlondsVerifier 验证包签名
    ├── UpdateOrchestrator 编排更新流程
    └── ResumableDownloadService 断点续传下载
```

---

## 12. 测试体系

### 12.1 测试项目

测试项目 `LanMountainDesktop.Tests/` 覆盖以下方面：

| 测试类 | 覆盖内容 |
|--------|---------|
| `CornerRadiusStyleTests` | 圆角和外观缩放 |
| `DesktopPlacementMathTests` | 桌面布局数学计算 |
| `DesktopEditCommitMathTests` | 桌面编辑提交计算 |
| `ComponentSettingsServiceTests` | 组件设置服务 |
| `SettingsCatalogServiceTests` | 设置目录服务 |
| `SettingsSearchServiceTests` | 设置搜索服务 |
| `UiExceptionGuardTests` | UI 异常保护 |
| `OobeStateServiceTests` | OOBE 状态服务 |
| `PluginInstallerServiceTests` | 插件安装服务 |
| `PluginManifestRuntimeTests` | 插件清单运行时验证 |
| `PluginRuntimeDataPathTests` | 插件运行时数据路径 |
| `HostShutdownGateTests` | 主机关闭门 |
| `HostStartupMonitorTests` | 宿主启动监控 |
| `HostActivationPolicyTests` | 宿主激活策略 |
| `HostLaunchPlanBuilderTests` | 宿主启动计划构建 |
| `LauncherArchitectureTests` | 启动器架构测试 |
| `LauncherUpdateCommandTests` | 启动器更新命令 |
| `AirAppLauncherServiceTests` | AirApp 启动服务 |
| `ClockAirAppMvpTests` | 时钟 AirApp MVP |
| `MusicControlServiceTests` | 音乐控制服务 |
| `MusicControlViewModelTests` | 音乐控制 VM |
| `StudyAnalyticsServiceTests` | 学习分析服务 |
| `WeatherPreviewDataTests` | 天气预览数据 |
| `ThemeAppearanceValuesTests` | 主题外观值 |
| `SystemChromeModeTests` | 系统边框模式 |
| `PlondsClientServiceTests` | Plonds 客户端服务 |
| `PackagingRuntimePolicyTests` | 打包运行时策略 |
| `ExternalIpcPublicApiTests` | 外部 IPC 公共 API |
| `WindowLayerIsolationTests` | 窗口层隔离 |
| `DotNetRuntimeProbeTests` | .NET 运行时探测 |
| `DeploymentLocatorTests` | 部署定位器 |
| `DataLocationResolverTests` | 数据位置解析器 |
| `AppVersionProviderTests` | 应用版本提供者 |
| `CommandContextTests` | 命令上下文 |
| `StartupSuccessTrackerTests` | 启动成功追踪器 |

### 12.2 测试原则

- 涉及宿主行为、SDK 契约、布局计算或设置持久化的改动，应优先补对应测试
- 优先扩展已有测试而不是新建无关测试入口

---

## 附录 A：快速参考

### A.1 关键文件速查

| 需求 | 优先查看文件 |
|------|-------------|
| 启动问题 | `LanMountainDesktop/Program.cs`, `LanMountainDesktop/App.axaml.cs` |
| Launcher 启动问题 | `LanMountainDesktop.Launcher/Program.cs`, `Startup/LaunchPipeline.cs` |
| 版本管理问题 | `LanMountainDesktop.Shared.Contracts/Launcher/AppVersionProvider.cs` |
| 更新系统问题 | `LanMountainDesktop/Services/Update/UpdateOrchestrator.cs`, `Services/Plonds/PlondsService.cs` |
| 设置窗口和设置页 | `LanMountainDesktop/Views/`, `ViewModels/`, `Services/Settings/` |
| 插件加载与安装 | `LanMountainDesktop/plugins/`, `LanMountainDesktop.PluginSdk/` |
| 组件元数据或放置规则 | `LanMountainDesktop/ComponentSystem/`, `DesktopEditing/` |
| 主题、颜色、圆角 | `LanMountainDesktop/Theme/`, `Styles/`, `LanMountainDesktop.Appearance/` |
| 设置持久化 | `LanMountainDesktop.Settings.Core/`, `LanMountainDesktop/Services/Settings/SettingsService.cs` |
| SDK 接口调整 | `LanMountainDesktop.PluginSdk/`, `LanMountainDesktop.Shared.Contracts/` |
| 桌面壳层或生命周期 | `Program.cs`, `App.axaml.cs`, `LanMountainDesktop.DesktopHost/` |
| AirApp 相关 | `LanMountainDesktop.AirAppHost/`, `LanMountainDesktop.AirAppRuntime/`, `Services/AirAppLauncherService.cs` |
| IPC 通信 | `LanMountainDesktop.Shared.IPC/`, `LanMountainDesktop.PluginIsolation.Ipc/` |
| 圆角规范 | `docs/CORNER_RADIUS_SPEC.md`, `LanMountainDesktop.Appearance/` |

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
| 文档权威来源 | `docs/ai/DOC_SOURCES.md` |
| AI 协作入口 | `AGENTS.md` |
| Feature 规格 | `.trae/specs/` |

### A.3 圆角开发准则（AI 强制建议）

- **桌面组件根容器**：必须且仅能使用 `{DynamicResource DesignCornerRadiusComponent}`
- **内部元素**：必须根据嵌套层级使用 `DesignCornerRadiusSm/Md/Lg` 等 Token
- **禁止硬编码**：严禁硬编码像素值
- **禁止缩放**：严禁在圆角资源上乘以任何 `scale` 变量

---

*本文档基于 LanMountainDesktop 仓库代码和文档自动生成，如有更新请以仓库最新代码为准。*
