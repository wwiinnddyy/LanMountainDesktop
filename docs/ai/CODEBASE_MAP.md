# Codebase Map

## 目标

本文件帮助 AI 在最短时间内定位“需求应该落到哪一层”，减少把改动打到错误项目或错误目录的概率。

## 顶层项目地图

| 路径 | 主要职责 | 典型改动 |
| --- | --- | --- |
| `desktop/LanMountainDesktop/` | 桌面宿主应用 | UI、服务、主流程、组件系统、AirApp 运行时接入 |
| `desktop/LanMountainDesktop.Launcher/` | 启动器 | OOBE、Splash、版本目录选择、AirApp Runtime 预启动、Host 启动 |
| `airapp/LanMountainDesktop.AirAppSdk/` | AirApp SDK | 公共接口、隔离层、宿主桥接契约 |
| `airapp/LanMountainDesktop.AirAppRuntime/` | 轻应用生命周期容器 | AirApp IPC、实例表、AirAppHost 进程管理 |
| `airapp/LanMountainDesktop.AirAppHost/` | 轻应用窗口宿主进程 | 窗口渲染、AirApp 窗口生命周期 |
| `airapp/LanMountainDesktop.AirAppTemplate/` | 轻应用模板 | `dotnet new lmd-airapp` 模板内容 |
| `core/LanMountainDesktop.Core/` | 共享契约与 IPC | 宿主/AirApp 共享契约、IPC 基础设施、打包 |
| `platform/LanMountainDesktop.Platform/` | 平台差异层 | 接口 + Windows/macOS 实现、P/Invoke 隔离 |
| `mobile/LanMountainDesktop.Mobile/` | 共享移动 UI 壳 | 组件面板 |
| `mobile/LanMountainDesktop.Mobile.Android/` | Android head | 移动端入口 |
| `install/LanDesktopPLONDS.installer/` | 在线安装器 | 安装/更新分发 |
| `tests/LanMountainDesktop.Tests/` | 测试 | 行为回归、契约验证、基础能力校验 |

## 主宿主工程内的高频落点

| 路径 | 用途 | 常见需求 |
| --- | --- | --- |
| `LanMountainDesktop/Program.cs` | 进程启动主线 | 启动诊断、启动配置 |
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

- 设置页改造：优先看 `desktop/LanMountainDesktop/Views/`, `ViewModels/`, `Services/`, `.trae/specs/`
- 组件注册或元数据变化：优先看 `desktop/LanMountainDesktop/ComponentSystem/`
- AirApp 安装、market、加载：优先看 `airapp/` 与 `desktop/LanMountainDesktop/Services/`
- 主题、颜色、圆角：优先看 `airapp/LanMountainDesktop.AirAppSdk/`（外观/设置能力）与 `desktop/LanMountainDesktop/Theme/`
- 设置持久化：优先看 `airapp/LanMountainDesktop.AirAppSdk/` 与宿主设置 facade
- SDK 接口调整：优先看 `airapp/LanMountainDesktop.AirAppSdk/` 和 `core/LanMountainDesktop.Core/`
- 平台差异：优先看 `platform/LanMountainDesktop.Platform/`
- 桌面壳层或生命周期：优先看 `desktop/LanMountainDesktop/Program.cs`, `App.axaml.cs`

## 测试对照

当前测试工程 `LanMountainDesktop.Tests/` 内的典型覆盖包括：

- `CornerRadiusScaleTests.cs`: 圆角和外观缩放相关
- `DesktopPlacementMathTests.cs`: 桌面布局数学
- `DesktopEditCommitMathTests.cs`: 桌面编辑提交计算
- `ComponentSettingsServiceTests.cs`: 组件设置服务
- `UiExceptionGuardTests.cs`: UI 异常保护
- `WhiteboardNotePersistenceServiceTests.cs`: 白板笔记持久化

如果改动落在这些行为附近，优先扩展已有测试而不是新建无关测试入口。
