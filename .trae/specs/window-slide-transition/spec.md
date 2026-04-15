# 窗口过渡动画 Spec

## Why

当前全屏窗口在"回到 Windows"（最小化）和"恢复应用"时存在严重的视觉问题：
1. 恢复时经历 `Minimized → Normal → FullScreen` 两步跳变，用户会短暂看到无框小窗口
2. 状态切换无任何过渡动画，体验生硬
3. `OnWindowPropertyChanged` 使用 `Dispatcher.UIThread.Post` 延迟纠正，进一步延长了 Normal 中间态的可见时间

## What Changes

- 在 `MainWindow.axaml` 的 `DesktopPage` 上添加 `TranslateTransform` 和 `TranslateTransform.X` 过渡动画
- 修改 `MainWindow.axaml.cs` 的 `OnMinimizeClick`，实现退场动画（滑出/淡出 → 最小化）
- 修改 `App.axaml.cs` 的 `RestoreOrCreateMainWindow`，实现入场动画（全屏 → 滑入/淡入）
- 修改 `MainWindow.axaml.cs` 的 `OnWindowPropertyChanged`，在动画期间暂停强制全屏逻辑
- 在 `AppSettingsSnapshot` 中添加 `EnableSlideTransition` 设置项（默认关闭）
- 在 `GeneralSettingsPageViewModel` 中添加对应 ViewModel 属性
- 在 `GeneralSettingsPage.axaml` 中添加开关 UI（仅 Windows 平台显示）
- 添加平台检测逻辑：Windows 且开启设置时使用滑入滑出，其他情况使用 Opacity 淡入淡出

## Impact

- Affected specs: 窗口生命周期过渡动画
- Affected code:
  - `LanMountainDesktop/Views/MainWindow.axaml` - DesktopPage 添加 TranslateTransform
  - `LanMountainDesktop/Views/MainWindow.axaml.cs` - OnMinimizeClick、OnWindowPropertyChanged、新增动画方法
  - `LanMountainDesktop/App.axaml.cs` - RestoreOrCreateMainWindow、OnMainWindowPropertyChanged
  - `LanMountainDesktop/Models/AppSettingsSnapshot.cs` - 新增 EnableSlideTransition 字段
  - `LanMountainDesktop/ViewModels/SettingsViewModels.cs` - GeneralSettingsPageViewModel 新增属性
  - `LanMountainDesktop/Views/SettingsPages/GeneralSettingsPage.axaml` - 新增开关 UI

---

## ADDED Requirements

### Requirement: 窗口退场过渡动画

系统 SHALL 在主窗口最小化/隐藏时播放退场过渡动画，消除窗口状态跳变的视觉闪烁。

#### Scenario: Opacity 淡出退场（所有平台默认）
- **WHEN** 用户点击"回到 Windows"或触发最小化
- **THEN** 系统将 `DesktopPage.Opacity` 设为 0，触发淡出动画
- **AND THEN** 动画完成后执行 `WindowState = Minimized`
- **AND THEN** 最小化完成后重置 `DesktopPage.Opacity = 1`（窗口已不可见）

#### Scenario: 滑出退场（Windows + 开启设置）
- **WHEN** 用户点击"回到 Windows"且运行在 Windows 平台且已开启滑入滑出设置
- **THEN** 系统同时将 `DesktopPage.Opacity` 设为 0 且 `DesktopPageSlideTransform.X` 设为屏幕宽度
- **AND THEN** 动画完成后执行 `WindowState = Minimized`
- **AND THEN** 最小化完成后重置 `DesktopPageSlideTransform.X = 0` 和 `DesktopPage.Opacity = 1`

### Requirement: 窗口入场过渡动画

系统 SHALL 在主窗口恢复时播放入场过渡动画，消除 Normal 中间态的视觉闪烁。

#### Scenario: Opacity 淡入入场（所有平台默认）
- **WHEN** 主窗口从最小化/隐藏状态恢复
- **THEN** 系统先将 `DesktopPage.Opacity` 设为 0（遮住 Normal 中间态）
- **AND THEN** 完成 `Minimized → Normal → FullScreen` 状态切换
- **AND THEN** 等 FullScreen 状态生效后将 `DesktopPage.Opacity` 设为 1，触发淡入动画

#### Scenario: 滑入入场（Windows + 开启设置）
- **WHEN** 主窗口从最小化/隐藏状态恢复且运行在 Windows 平台且已开启滑入滑出设置
- **THEN** 系统先将 `DesktopPage.Opacity` 设为 0 且 `DesktopPageSlideTransform.X` 设为屏幕宽度
- **AND THEN** 完成 `Minimized → Normal → FullScreen` 状态切换
- **AND THEN** 等 FullScreen 状态生效后同时将 `DesktopPage.Opacity` 设为 1 且 `DesktopPageSlideTransform.X` 设为 0，触发滑入+淡入组合动画

### Requirement: 动画期间交互保护

系统 SHALL 在过渡动画播放期间防止用户交互和状态冲突。

#### Scenario: 动画期间禁止交互
- **WHEN** 退场或入场动画正在播放
- **THEN** `DesktopPage.IsHitTestVisible` 设为 `false`
- **AND THEN** 动画完成后恢复为 `true`

#### Scenario: 动画期间暂停强制全屏
- **WHEN** 入场动画正在播放且窗口临时处于 Normal 状态
- **THEN** `OnWindowPropertyChanged` 不执行强制全屏纠正
- **AND THEN** 入场动画完成后恢复正常强制全屏逻辑

#### Scenario: 防止快速连续操作
- **WHEN** 用户在动画播放期间再次触发最小化或恢复
- **THEN** 系统忽略重复操作，避免动画冲突

### Requirement: 滑入滑出设置项

系统 SHALL 在基本设置页面提供"滑入滑出过渡效果"开关，仅 Windows 平台可见。

#### Scenario: 设置项可见性
- **WHEN** 用户在 Windows 平台打开基本设置页面
- **THEN** 显示"滑入滑出过渡效果"开关
- **WHEN** 用户在非 Windows 平台打开基本设置页面
- **THEN** 不显示该开关

#### Scenario: 设置项默认值
- **WHEN** 用户首次安装应用
- **THEN** `EnableSlideTransition` 默认为 `false`

#### Scenario: 设置持久化
- **WHEN** 用户切换"滑入滑出过渡效果"开关
- **THEN** 设置值立即持久化到 `AppSettingsSnapshot.EnableSlideTransition`
- **AND THEN** 下次窗口过渡时立即生效，无需重启

### Requirement: DesktopPage TranslateTransform 声明

系统 SHALL 在 `DesktopPage` 上声明 `TranslateTransform` 和对应的过渡动画。

#### Scenario: XAML 声明
- **WHEN** MainWindow 初始化
- **THEN** `DesktopPage` 拥有名为 `DesktopPageSlideTransform` 的 `TranslateTransform`
- **AND THEN** `DesktopPage.Transitions` 包含 `Opacity` 和 `TranslateTransform.X` 两个过渡
- **AND THEN** 过渡时长使用 `FluttermotionToken.Duration.Page`（320ms）和 `FluttermotionToken.Duration.Intro`（400ms）
- **AND THEN** 缓动函数使用 `0.05,0.75,0.10,1.00`（DecelerateBezier）

## MODIFIED Requirements

### Requirement: OnMinimizeClick 行为

**当前**: 直接设置 `WindowState = WindowState.Minimized`，无动画

**修改后**: 先播放退场动画，动画完成后再设置 `WindowState = WindowState.Minimized`

### Requirement: RestoreOrCreateMainWindow 行为

**当前**: `Show() → Normal → FullScreen`，无过渡动画，用户可见 Normal 中间态

**修改后**: 先将 `DesktopPage` 设为不可见（Opacity=0 + 可选滑出位），再执行状态切换，最后播放入场动画

### Requirement: OnWindowPropertyChanged 强制全屏逻辑

**当前**: 任何非 Minimized/FullScreen 状态立即纠正为 FullScreen

**修改后**: 动画期间允许临时 Normal 状态存在，动画完成后恢复强制全屏逻辑

## REMOVED Requirements

无移除的需求。
