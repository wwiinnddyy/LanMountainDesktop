# 组件系统模块（Component System Module）

本目录提供组件系统的模块化基础，用于支持内置组件管理与第三方扩展接入。  
This directory provides the modular foundation for built-in component management and third-party extension integration.

## 核心文件职责（Core Files）
- `BuiltInComponentIds.cs`：内置组件 ID 常量（例如 `Clock`）。  
  Built-in component ID constants (for example `Clock`).
- `DesktopComponentDefinition.cs`：组件元数据定义（名称、类别、最小尺寸、可放置区域等）。  
  Component metadata model (name, category, minimum size, placement permissions).
- `ComponentPlacementRules.cs`：组件放置规则（最小尺寸、状态栏高度限制、网格边界约束）。  
  Placement rules (minimum size, status-bar height rule, grid clamping).
- `ComponentRegistry.cs`：组件注册中心，负责内置组件与扩展组件合并。  
  Registry that merges built-in and extension components.
- `Extensions/IComponentExtensionProvider.cs`：扩展提供者接口契约。  
  Extension provider interface contract.
- `Extensions/JsonComponentExtensionProvider.cs`：基于 JSON 的扩展加载器。  
  JSON-based extension loader.

## 第三方扩展契约（Extension Contract）
- 第三方可通过实现 `IComponentExtensionProvider` 提供组件定义。  
  Third parties can provide component definitions via `IComponentExtensionProvider`.
- 当前内置了 JSON 提供者，运行时扫描目录：  
  Built-in JSON provider scans at runtime:
  - `Extensions/Components/*.json`（相对应用输出目录）  
    `Extensions/Components/*.json` (relative to app output directory)

## 加载流程（Load Flow）
1. `ComponentRegistry.CreateDefault()` 先注册内置组件。  
   Register built-in components first via `ComponentRegistry.CreateDefault()`.
2. 调用 `.RegisterExtensions(...)` 合并扩展组件。  
   Merge extension components via `.RegisterExtensions(...)`.
3. 主窗口通过注册中心校验组件合法性与放置权限。  
   Main window validates component identity and placement permission through the registry.

## JSON 清单格式（Manifest Schema）
JSON 文件为数组，每一项代表一个组件定义。  
The JSON file is an array, where each item represents one component definition.

```json
[
  {
    "id": "Weather",
    "displayName": "Weather",
    "iconKey": "WeatherSunny",
    "category": "Status",
    "minWidthCells": 1,
    "minHeightCells": 1,
    "allowStatusBarPlacement": true,
    "allowDesktopPlacement": true
  }
]
```

字段说明（Field notes）：
- `id`：组件唯一 ID（建议英文、稳定不变）。  
  Unique component ID (prefer stable English key).
- `displayName`：显示名。  
  Display name.
- `iconKey`：图标键（由上层 UI 解释）。  
  Icon key resolved by UI layer.
- `category`：组件分类。  
  Component category.
- `minWidthCells` / `minHeightCells`：最小占格，必须满足 `>= 1`。  
  Minimum cell size, must satisfy `>= 1`.
- `allowStatusBarPlacement`：是否允许放到顶部状态栏。  
  Whether placing in top status bar is allowed.
- `allowDesktopPlacement`：是否允许放到桌面区域。  
  Whether placing in desktop area is allowed.

## 放置规则摘要（Placement Rules Summary）
- 最小尺寸约束：`minWidthCells >= 1` 且 `minHeightCells >= 1`。  
  Minimum size constraint: `minWidthCells >= 1` and `minHeightCells >= 1`.
- 状态栏约束：状态栏组件高度必须为 `1` 格。  
  Status bar constraint: component height must be exactly `1` cell.
- 越界约束：所有组件坐标会被网格边界钳制（clamp）。  
  Out-of-bounds constraint: component coordinates are clamped to grid bounds.
