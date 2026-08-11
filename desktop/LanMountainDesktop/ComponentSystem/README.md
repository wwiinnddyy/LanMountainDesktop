# 组件系统说明

## 中文

`ComponentSystem/` 提供阑山桌面组件定义、注册和扩展的基础能力。

### 主要职责

- 管理内置组件 ID 和元数据
- 约束组件最小尺寸与可放置区域
- 合并内置组件与扩展组件
- 通过 JSON 或扩展提供者接入第三方组件

### 关键文件

- `BuiltInComponentIds.cs`：内置组件 ID 常量
- `DesktopComponentDefinition.cs`：组件元数据模型
- `ComponentPlacementRules.cs`：放置规则
- `ComponentRegistry.cs`：组件注册中心
- `Extensions/IComponentExtensionProvider.cs`：扩展提供者接口
- `Extensions/JsonComponentExtensionProvider.cs`：JSON 扩展加载器

### 扩展方式

- 当前默认扫描 `Extensions/Components/*.json`
- 组件清单定义显示名、分类、最小尺寸和可放置区域
- 主程序通过注册中心统一验证组件是否合法

## English

`ComponentSystem/` contains the foundation for component definition, registration, and extension in LanMountainDesktop.

### Responsibilities

- manage built-in component IDs and metadata
- enforce placement rules
- merge built-in and extension components
- support third-party registration through JSON or provider contracts
