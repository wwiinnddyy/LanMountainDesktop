# 独立设置窗口 Spec

## Why

- 当前设置窗口仍然带有桌面壳的 owner / anchor 语义，点击“回到 Windows”或触发桌面动画时，容易被一起隐藏或重新定位。
- 产品新增了“在任务栏显示图标”和“启用滑入滑出动画”设置，需要明确边界：它们只影响桌面主窗口，不影响设置窗口。
- 桌面底栏、托盘菜单、IPC、组件库等入口应当始终打开同一个独立设置窗口，而不是切换成附属浮窗或开关行为。

## What Changes

- 将设置窗口改为独立顶层窗口，始终使用自己的任务栏按钮和图标。
- `SettingsWindowService.Open` 改为幂等的 open-or-focus；重复打开只聚焦已有窗口，并在提供目标页时切换到对应页面。
- 移除 `Owner`、锚点定位和 `Toggle` 语义；首次打开按参考屏幕居中，关闭为真实关闭。
- 桌面壳的“回到 Windows”、最小化到托盘/任务栏、滑入滑出动画，只影响 `MainWindow`，不会影响设置窗口。
- 统一桌面、托盘、IPC、组件库等设置入口，全部走 `OpenIndependentSettingsModule`。
- 设置页文案明确“在任务栏显示图标”只控制桌面主窗口；设置窗口始终保留独立任务栏图标。

## Impact

- Affected code:
  - `LanMountainDesktop/Services/Settings/SettingsWindowService.cs`
  - `LanMountainDesktop/App.axaml.cs`
  - `LanMountainDesktop/Views/MainWindow.axaml.cs`
  - `LanMountainDesktop/Views/MainWindow.ComponentSystem.cs`
  - `LanMountainDesktop/Views/FusedDesktopComponentLibraryControl.axaml.cs`
  - `LanMountainDesktop/Views/SettingsPages/GeneralSettingsPage.axaml`
- Affected behavior:
  - 设置窗口生命周期
  - 设置入口一致性
  - 任务栏图标与桌面壳显示边界

---

## ADDED Requirements

### Requirement: 设置窗口为独立顶层窗口

系统 SHALL 将设置窗口作为独立顶层窗口显示，而不是作为桌面主窗口的附属子窗。

#### Scenario: 设置窗口拥有独立任务栏图标
- **WHEN** 用户打开设置窗口
- **THEN** 设置窗口使用独立顶层窗口方式显示
- **AND THEN** 设置窗口在任务栏中保留自己的独立按钮和图标
- **AND THEN** “在任务栏显示图标”开关不会影响设置窗口的任务栏按钮

### Requirement: 设置入口统一为 open-or-focus

系统 SHALL 让所有设置入口打开或聚焦同一个设置窗口实例。

#### Scenario: 已打开时重复触发设置入口
- **WHEN** 设置窗口已经打开，用户再次从桌面、托盘或 IPC 触发打开设置
- **THEN** 系统只聚焦现有设置窗口
- **AND THEN** 如果请求包含目标页，则导航到目标页
- **AND THEN** 不会把已打开的设置窗口当作开关关闭

### Requirement: 设置窗口不参与桌面壳可见性切换

系统 SHALL 让桌面壳的隐藏、最小化和进出场动画只作用于主窗口。

#### Scenario: 回到 Windows 时设置窗口保持可见
- **WHEN** 主窗口执行“回到 Windows”并隐藏到托盘或最小化到任务栏
- **THEN** 设置窗口保持当前可见状态
- **AND THEN** 设置窗口不会跟随主窗口一起隐藏、最小化或重定位

#### Scenario: 桌面滑入滑出动画不作用于设置窗口
- **WHEN** 启用了滑入滑出动画并触发主窗口退场或入场
- **THEN** 只有主窗口参与动画
- **AND THEN** 设置窗口不会消失，也不会跟随主窗口做进出场动画

### Requirement: 关闭设置窗口时真实销毁实例

系统 SHALL 在用户关闭设置窗口时真实关闭该窗口实例。

#### Scenario: 关闭后再次打开
- **WHEN** 用户点击设置窗口右上角关闭按钮
- **THEN** 当前设置窗口实例被关闭并销毁
- **AND THEN** 下次再次打开设置时创建新的设置窗口实例
- **AND THEN** 新窗口按参考屏幕居中显示
