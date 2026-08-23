# LanMountainDesktop AI Guide

本文件是 AI 助手进入本仓库时的第一入口。面向 Codex、Cursor、Trae 等工具，目标是减少重复探索，快速定位权威文档、关键目录和执行约束。

## 1. 项目目标与仓库边界

- 本仓库是阑山桌面桌面宿主、宿主侧 AirApp 运行时、AirApp SDK、共享契约与基础外观/设置能力的权威来源。
- 不要把轻应用市场元数据、开发者门户或官方示例轻应用实现当作本仓库内容维护。
- 市场和生态材料属于兄弟仓库 `LanAirApp`。
- 官方示例轻应用属于独立仓库 `LanMountainDesktop.SamplePlugin`。

边界详情看：

- `docs/archive/ECOSYSTEM_BOUNDARIES.md`
- `docs/archive/ARCHITECTURE.md`（新结构见 `docs/04-架构与实现/01-整体架构.md`）

## 2. 关键目录地图

- `desktop/LanMountainDesktop/`: 主宿主应用，包含 UI、服务、组件系统、主题与 AirApp 运行时接入
- `desktop/LanMountainDesktop/ComponentSystem/`: 内置组件定义、注册、扩展加载
- `desktop/LanMountainDesktop/plugins/`: 宿主侧 AirApp 运行时、安装与 market 集成
- `desktop/LanMountainDesktop/Views/` and `ViewModels/`: UI 页面、窗口与视图模型
- `desktop/LanMountainDesktop/Services/`: 设置、遥测、启动、持久化、业务服务
- `airapp/LanMountainDesktop.AirAppSdk/`: AirApp SDK 公共接口和默认打包行为（含隔离层与宿主桥接契约）
- `platform/LanMountainDesktop.Platform/`: 平台差异层（接口 + Windows/macOS 实现）
- `core/LanMountainDesktop.Core/`: 宿主/AirApp 共享契约、IPC 基础设施与打包
- `mobile/LanMountainDesktop.Mobile/`: 共享移动 UI 壳（组件面板）
- `mobile/LanMountainDesktop.Mobile.Android/`: Android head（入口）
- `tests/LanMountainDesktop.Tests/`: 宿主与 SDK 测试
- `.trae/specs/`: feature 级规格、任务拆解和验收清单

更详细映射看 `docs/ai/CODEBASE_MAP.md`。

## 3. 常用命令

```bash
dotnet restore
dotnet build LanMountainDesktop.slnx -c Debug
dotnet run --project desktop/LanMountainDesktop/LanMountainDesktop.csproj
dotnet test LanMountainDesktop.slnx -c Debug
```

AirApp 本地包生成：

```powershell
./scripts/Pack-AirAppPackages.ps1
```

## 4. 改动前后必做检查

改动前：

- 先确认需求是否已经在 `.trae/specs/` 中存在
- 先确认产品、架构、专题规范分别以哪份文档为准
- 避免沿用旧根目录产品文档中的过时事实

改动后：

- 至少检查构建和与改动相关的测试
- 如果行为、流程、边界或命令变化，更新对应文档
- 如果是新功能或行为调整，补齐或更新 `.trae/specs/<feature>/`

## 5. 高频区域注意事项

### UI

- 主题、资源和视觉语义优先遵守 `docs/03-组件设计规范/02-视觉规范.md`（归档见 `docs/archive/VISUAL_SPEC.md`）与 `docs/archive/CORNER_RADIUS_SPEC.md`
- **圆角规范 (AI 强制建议)**：
    - **桌面组件根容器**：必须且仅能使用 `{DynamicResource DesignCornerRadiusComponent}`。
    - **内部元素**：必须根据嵌套层级使用 `DesignCornerRadiusSm/Md/Lg` 等 Token，严禁硬编码像素值。
    - **禁止修改系数**：严禁在圆角资源上乘以任何 `scale` 变量，圆角现在由全局样式固定控制。
- 设置页相关改动通常同时落在 `Views/`、`ViewModels/`、`Services/` 和 `.trae/specs/`
- UI 启动与窗口生命周期主线在 `Program.cs` 和 `App.axaml.cs`

### AirApp

- SDK 公共 API 以 `airapp/LanMountainDesktop.AirAppSdk/` 为准
- 共享契约以 `core/LanMountainDesktop.Core/` 为准
- market 数据来源默认是兄弟仓库 `..\\LanAirApp`
- 迁移或 breaking change 优先同步 `docs/AIRAPP_SDK_V1_MIGRATION.md`

### 设置与主题

- 设置持久化和 scope 变化优先检查 `airapp/LanMountainDesktop.AirAppSdk/`（合并自 `LanMountainDesktop.Settings.Core`）
- 外观、圆角、主题资源优先检查 `airapp/LanMountainDesktop.AirAppSdk/`（合并自 `LanMountainDesktop.Appearance`）与专题规范
- **圆角统一**：桌面组件（Widget）必须统一使用动态资源 `DesignCornerRadiusComponent`。严禁在组件根容器使用硬编码数值或非组件级令牌（如 `Xs`, `Md` 等），以确保全局圆角缩放设置能正确应用到所有组件。

## 6. 权威来源

- 产品定位：`docs/00-快速开始/01-项目介绍.md`（归档见 `docs/archive/PRODUCT.md`）
- 架构与模块职责：`docs/04-架构与实现/01-整体架构.md`（归档见 `docs/archive/ARCHITECTURE.md`）
- 运行、构建、测试、打包：`docs/archive/DEVELOPMENT.md`
- feature 规格：`.trae/specs/`
- 视觉规范：`docs/03-组件设计规范/02-视觉规范.md`（归档见 `docs/archive/VISUAL_SPEC.md`）
- 圆角规范：`docs/archive/CORNER_RADIUS_SPEC.md`
- 生态边界：`docs/archive/ECOSYSTEM_BOUNDARIES.md`
- 跨平台架构：`docs/CROSS_PLATFORM.md`
- AirApp SDK 迁移：`docs/AIRAPP_SDK_V1_MIGRATION.md`

如果多个文档都提到同一件事，以 `docs/ai/DOC_SOURCES.md` 列出的权威来源为准。
