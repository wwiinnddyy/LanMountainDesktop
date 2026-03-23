# LanMountainDesktop AI Guide

本文件是 AI 助手进入本仓库时的第一入口。面向 Codex、Cursor、Trae 等工具，目标是减少重复探索，快速定位权威文档、关键目录和执行约束。

## 1. 项目目标与仓库边界

- 本仓库是阑山桌面桌面宿主、宿主侧插件运行时、Plugin SDK、共享契约与基础外观/设置能力的权威来源。
- 不要把插件市场元数据、开发者门户或官方示例插件实现当作本仓库内容维护。
- 市场和生态材料属于兄弟仓库 `LanAirApp`。
- 官方示例插件属于独立仓库 `LanMountainDesktop.SamplePlugin`。

边界详情看：

- `docs/ECOSYSTEM_BOUNDARIES.md`
- `docs/ARCHITECTURE.md`

## 2. 关键目录地图

- `LanMountainDesktop/`: 主宿主应用，包含 UI、服务、组件系统、主题与插件运行时接入
- `LanMountainDesktop/ComponentSystem/`: 内置组件定义、注册、扩展加载
- `LanMountainDesktop/plugins/`: 宿主侧插件运行时、安装与 market 集成
- `LanMountainDesktop/Views/` and `ViewModels/`: UI 页面、窗口与视图模型
- `LanMountainDesktop/Services/`: 设置、遥测、启动、持久化、业务服务
- `LanMountainDesktop.PluginSdk/`: 插件 SDK 公共接口和默认打包行为
- `LanMountainDesktop.Shared.Contracts/`: 宿主/插件共享契约
- `LanMountainDesktop.Tests/`: 宿主与 SDK 测试
- `.trae/specs/`: feature 级规格、任务拆解和验收清单

更详细映射看 `docs/ai/CODEBASE_MAP.md`。

## 3. 常用命令

```bash
dotnet restore
dotnet build LanMountainDesktop.slnx -c Debug
dotnet run --project LanMountainDesktop/LanMountainDesktop.csproj
dotnet test LanMountainDesktop.slnx -c Debug
```

插件本地包生成：

```powershell
./scripts/Pack-PluginPackages.ps1
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

- 主题、资源和视觉语义优先遵守 `docs/VISUAL_SPEC.md` 与 `docs/CORNER_RADIUS_SPEC.md`
- 设置页相关改动通常同时落在 `Views/`、`ViewModels/`、`Services/` 和 `.trae/specs/`
- UI 启动与窗口生命周期主线在 `Program.cs` 和 `App.axaml.cs`

### 插件

- SDK 公共 API 以 `LanMountainDesktop.PluginSdk/` 为准
- 共享契约以 `LanMountainDesktop.Shared.Contracts/` 为准
- market 数据来源默认是兄弟仓库 `..\\LanAirApp`
- 迁移或 breaking change 优先同步 `docs/PLUGIN_SDK_V4_MIGRATION.md`

### 设置与主题

- 设置持久化和 scope 变化优先检查 `LanMountainDesktop.Settings.Core/`
- 外观、圆角、主题资源优先检查 `LanMountainDesktop.Appearance/` 与专题规范

## 6. 权威来源

- 产品定位：`docs/PRODUCT.md`
- 架构与模块职责：`docs/ARCHITECTURE.md`
- 运行、构建、测试、打包：`docs/DEVELOPMENT.md`
- feature 规格：`.trae/specs/`
- 视觉规范：`docs/VISUAL_SPEC.md`
- 圆角规范：`docs/CORNER_RADIUS_SPEC.md`
- 生态边界：`docs/ECOSYSTEM_BOUNDARIES.md`
- SDK v4 迁移：`docs/PLUGIN_SDK_V4_MIGRATION.md`

如果多个文档都提到同一件事，以 `docs/ai/DOC_SOURCES.md` 列出的权威来源为准。
