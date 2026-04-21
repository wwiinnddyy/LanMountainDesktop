# Codebase Map

## 目标

本文件帮助 AI 在最短时间内定位“需求应该落到哪一层”，减少把改动打到错误项目或错误目录的概率。

## 顶层项目地图

| 路径 | 主要职责 | 典型改动 |
| --- | --- | --- |
| `LanMountainDesktop/` | 桌面宿主应用 | UI、服务、主流程、组件系统、插件接入 |
| `LanMountainDesktop.PluginSdk/` | 插件 SDK | 公共接口、扩展方法、默认打包行为 |
| `LanMountainDesktop.Shared.Contracts/` | 共享契约 | 宿主与插件共享记录、模型、边界类型 |
| `LanMountainDesktop.Appearance/` | 外观基础设施 | 主题、圆角、外观资源相关逻辑 |
| `LanMountainDesktop.Settings.Core/` | 设置基础设施 | 设置 scope、存储抽象、设置 facade 支撑 |
| `LanMountainDesktop.DesktopHost/` | 桌面宿主流程 | 生命周期、宿主流程支撑 |
| `LanMountainDesktop.DesktopComponents.Runtime/` | 组件运行时 | 组件宿主运行时支撑 |
| `LanMountainDesktop.Host.Abstractions/` | 宿主抽象 | 宿主接口与抽象层 |
| `LanMountainDesktop.Launcher/` | 启动器 | 发布输出、OOBE、启动页、更新与插件安装/更新 |
| `LanMountainDesktop.PluginTemplate/` | 插件模板 | `dotnet new lmd-plugin` 模板内容 |
| `LanMountainDesktop.Tests/` | 测试 | 行为回归、契约验证、基础能力校验 |

## 主宿主工程内的高频落点

| 路径 | 用途 | 常见需求 |
| --- | --- | --- |
| `LanMountainDesktop/Program.cs` | 进程启动主线 | 启动诊断、单实例、启动配置 |
| `LanMountainDesktop/App.axaml.cs` | 应用初始化 | 主题、语言、托盘、插件运行时、主窗口 |
| `LanMountainDesktop/Views/` | 界面视图 | 设置页、主窗口、组件 UI |
| `LanMountainDesktop/ViewModels/` | 视图模型 | 页面状态、命令、交互行为 |
| `LanMountainDesktop/Services/` | 服务层 | 设置、存储、遥测、业务能力 |
| `LanMountainDesktop/ComponentSystem/` | 组件系统 | 组件定义、注册、放置规则、扩展清单 |
| `LanMountainDesktop/plugins/` | 插件运行时 | 插件发现、安装、替换、market 集成 |
| `LanMountainDesktop/Theme/` and `Styles/` | 主题和样式 | 视觉资源、主题行为、样式规则 |
| `LanMountainDesktop/Localization/` | 本地化 | 语言资源、语言切换 |
| `LanMountainDesktop/DesktopEditing/` | 布局编辑 | 组件摆放、数学计算、编辑状态 |

## 需求到目录的快速映射

- 设置页改造：优先看 `Views/`, `ViewModels/`, `Services/`, `.trae/specs/`
- 组件注册或元数据变化：优先看 `ComponentSystem/`
- 插件安装、market、插件加载：优先看 `plugins/`
- 主题、颜色、圆角：优先看 `Theme/`, `Styles/`, `LanMountainDesktop.Appearance/`
- 设置持久化：优先看 `LanMountainDesktop.Settings.Core/` 与宿主设置 facade
- SDK 接口调整：优先看 `LanMountainDesktop.PluginSdk/` 和 `LanMountainDesktop.Shared.Contracts/`
- 桌面壳层或生命周期：优先看 `Program.cs`, `App.axaml.cs`, `LanMountainDesktop.DesktopHost/`

## 测试对照

当前测试工程 `LanMountainDesktop.Tests/` 内的典型覆盖包括：

- `CornerRadiusScaleTests.cs`: 圆角和外观缩放相关
- `DesktopPlacementMathTests.cs`: 桌面布局数学
- `DesktopEditCommitMathTests.cs`: 桌面编辑提交计算
- `ComponentSettingsServiceTests.cs`: 组件设置服务
- `UiExceptionGuardTests.cs`: UI 异常保护
- `WhiteboardNotePersistenceServiceTests.cs`: 白板笔记持久化

如果改动落在这些行为附近，优先扩展已有测试而不是新建无关测试入口。
