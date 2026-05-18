# 融合桌面组件库分类图标统一规格

## Why

融合桌面组件库窗口（FusedDesktopComponentLibraryControl）的分类图标使用了手动硬编码的 `ResolveCategoryIcon` 方法映射分类 ID 到 `Symbol` 枚举，与阑山桌面主窗口（MainWindow）中的映射存在不一致（例如 `Info` 分类在主窗口映射到 `Symbol.Apps`，在融合桌面映射到 `Symbol.Info`）。同时，`DesktopComponentDefinition.IconKey` 字段已经存储了正确的 FluentIcon 枚举名称字符串，但未被利用。需要统一三处图标映射逻辑，确保所有组件库入口的分类图标一致且正确。

## What Changes

- **统一分类图标映射**：将三处分散的 `ResolveCategoryIcon`/`ResolveComponentLibraryCategoryIcon` 方法合并为共享的统一映射
- **使用 `IconKey` 驱动图标**：分类图标应基于该分类下组件的 `IconKey` 字段推导，而非硬编码的分类 ID 映射
- **使用 `FluentIcons.Common.Icon` 枚举**：`fi:FluentIcon` 控件使用 `Icon` 枚举（非 `Symbol` 枚举），分类图标应使用 `Icon` 枚举以与 `fi:FluentIcon` 兼容
- **修改 ViewModel**：`ComponentLibraryCategoryViewModel.Icon` 属性类型从 `Symbol` 改为 `Icon`

## Impact

- 受影响文件：
  - `LanMountainDesktop/ViewModels/ComponentLibraryWindowViewModel.cs`（Icon 属性类型从 Symbol 改为 Icon）
  - `LanMountainDesktop/Views/FusedDesktopComponentLibraryControl.axaml`（绑定路径不变，但 Icon 类型变化）
  - `LanMountainDesktop/Views/FusedDesktopComponentLibraryControl.axaml.cs`（移除硬编码映射，使用统一方法）
  - `LanMountainDesktop/Views/ComponentLibraryWindow.axaml.cs`（移除硬编码映射，使用统一方法）
  - `LanMountainDesktop/Views/MainWindow.ComponentSystem.cs`（移除硬编码映射，使用统一方法）
  - 新增共享映射工具类（或在现有服务中添加）

## ADDED Requirements

### Requirement: 统一分类图标映射

系统 SHALL 提供一个共享的分类图标映射方法，所有组件库入口（阑山桌面主窗口、融合桌面组件库、独立组件库窗口）均使用此方法。

#### Scenario: 图标映射来源
- **GIVEN** 一个组件分类 ID
- **WHEN** 需要获取该分类的图标
- **THEN** 系统应基于该分类下组件的 `IconKey` 字段推导分类图标
- **AND** 推导规则为：取该分类下第一个组件的 `IconKey`，解析为 `FluentIcons.Common.Icon` 枚举值
- **AND** 若 `IconKey` 无法解析为有效的 `Icon` 枚举值，则回退到 `Icon.Apps`

#### Scenario: 特殊分类处理
- **GIVEN** 分类 ID 为 "all"
- **WHEN** 需要获取该分类的图标
- **THEN** 系统应返回 `Icon.Apps`

#### Scenario: 三处映射一致性
- **GIVEN** 任意一个组件分类
- **WHEN** 在阑山桌面主窗口、融合桌面组件库、独立组件库窗口中显示该分类
- **THEN** 三处应显示完全相同的图标

### Requirement: ViewModel 使用 Icon 枚举

`ComponentLibraryCategoryViewModel.Icon` 属性 SHALL 使用 `FluentIcons.Common.Icon` 枚举类型（而非 `FluentIcons.Common.Symbol`），以与 `fi:FluentIcon` 控件的 `Icon` 属性兼容。

#### Scenario: XAML 绑定兼容
- **GIVEN** `ComponentLibraryCategoryViewModel.Icon` 属性类型为 `Icon`
- **WHEN** 在 XAML 中通过 `{Binding Icon}` 绑定到 `fi:FluentIcon` 控件
- **THEN** 图标应正确渲染，无需额外转换

## MODIFIED Requirements

### Requirement: 分类图标解析

原实现使用硬编码的 `if/switch` 语句将分类 ID 映射到 `Symbol` 枚举，新实现改为：

- 使用 `DesktopComponentDefinition.IconKey` 字段作为图标来源
- 通过 `Enum.TryParse<Icon>(iconKey, ignoreCase: true, out var icon)` 解析
- 解析失败时回退到 `Icon.Apps`
- 移除所有三处硬编码映射方法

### Requirement: ComponentLibraryCategoryViewModel.Icon 类型

原类型为 `Symbol`，修改为 `Icon`，与 `fi:FluentIcon` 控件的 `Icon` 依赖属性类型一致。

## REMOVED Requirements

无移除的需求。
