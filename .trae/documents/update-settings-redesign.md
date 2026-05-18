# 更新设置界面重设计实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将更新设置页面从丑陋的卡片堆叠布局重设计为遵循 Fluent Design 的 FASettingsExpander 列表布局，与项目其他设置页面保持视觉一致性。

**Architecture:** 移除所有 `Border.settings-section-card` 包裹，改用 `FASettingsExpander` + `IconText` 分节标题 + `Separator` 分隔线的统一模式。操作按钮改为仅显示当前可用操作。版本信息改为 `FASettingsExpanderItem` 行项目展示。ViewModel 层新增 `ActiveActions` 计算属性来驱动按钮可见性。

**Tech Stack:** Avalonia UI 11, FluentAvalonia 2.x, CommunityToolkit.Mvvm

---

## 当前状态分析

### 现有文件

| 文件 | 职责 |
|------|------|
| `LanMountainDesktop/Views/SettingsPages/UpdateSettingsPage.axaml` | 更新页面 AXAML 布局 |
| `LanMountainDesktop/Views/SettingsPages/UpdateSettingsPage.axaml.cs` | 代码隐藏 |
| `LanMountainDesktop/ViewModels/UpdateSettingsViewModel.cs` | 视图模型 |
| `LanMountainDesktop/Styles/SettingsCardStyles.axaml` | 通用设置样式 |
| `LanMountainDesktop/Controls/IconText.axaml(.cs)` | 分节标题控件 |
| `LanMountainDesktop.Shared.Contracts/Update/UpdateState.cs` | UpdatePhase 枚举和扩展方法 |

### 核心问题

1. **4 个 `Border.settings-section-card` 卡片**：状态卡、版本信息卡、进度卡、操作卡，每个都带边框+阴影+圆角，视觉零碎
2. **FAInfoBar 嵌套在卡片内**：冗余的容器层级
3. **7 个按钮 3×3 网格**：大量按钮在当前阶段不可用但仍然占据空间
4. **与其他设置页面风格不一致**：GeneralSettingsPage、AppearanceSettingsPage 等全部使用 `FASettingsExpander` 列表

### 参考基准

- [GeneralSettingsPage.axaml](file:///d:/github/LanMountainDesktop/LanMountainDesktop/Views/SettingsPages/GeneralSettingsPage.axaml)：`IconText` 分节标题 → `FASettingsExpander` 列表 → `Separator` 分隔
- [AppearanceSettingsPage.axaml](file:///d:/github/LanMountainDesktop/LanMountainDesktop/Views/SettingsPages/AppearanceSettingsPage.axaml)：同上模式
- [AboutSettingsPage.axaml](file:///d:/github/LanMountainDesktop/LanMountainDesktop/Views/SettingsPages/AboutSettingsPage.axaml)：`FAInfoBar` 用于静态信息展示
- Windows 11 设置 > Windows Update：顶部状态区 + 进度条 + 操作按钮，下方展开区展示详情

---

## 设计决策

| 决策项 | 选择 | 理由 |
|--------|------|------|
| 布局模式 | FASettingsExpander 列表 | 与其他设置页面统一，Fluent Design 原生控件 |
| 按钮策略 | 仅显示可用操作 | 简洁、不混乱，Windows 11 更新页面也是此模式 |
| 版本信息 | FASettingsExpanderItem 行项目 | 每行一个信息，干净可扫描 |
| 进度展示 | 内嵌在状态 Expander 内 | 进度是状态的一部分，不应独立成卡 |
| 偏好设置 | 保留 FASettingsExpander | 已经是正确模式，微调即可 |

---

## 新布局结构

```
ScrollViewer
└── StackPanel (settings-page-container settings-page-animated)
    ├── TextBlock (settings-section-title: "更新")
    ├── TextBlock (settings-section-description: 描述文字)
    │
    ├── IconText (Icon="ArrowSync", Text="更新状态")
    │
    ├── FASettingsExpander "检查更新" (IsClickEnabled=True, Command=CheckCommand)
    │   ├── IconSource: ArrowSync 图标
    │   └── Footer: Button "检查更新" (仅 CanCheck 时可见)
    │
    ├── FASettingsExpander "更新进度" (IsVisible=IsBusy||IsProgressVisible||IsPaused)
    │   ├── IconSource: FAProgressRing / 对应阶段图标
    │   ├── Footer: PhaseText + ProgressFraction
    │   └── FASettingsExpanderItem
    │       ├── ProgressBar (ProgressFraction)
    │       ├── ProgressDetail 文字
    │       └── 操作按钮行 (仅可用操作)
    │           ├── Button "下载" (CanDownload)
    │           ├── Button "安装" (CanInstall)
    │           ├── Button "暂停" (CanPause)
    │           ├── Button "继续" (CanResume)
    │           ├── Button "回滚" (CanRollback)
    │           └── Button "取消" (CanCancel)
    │
    ├── FASettingsExpander "暂停" (IsVisible=IsPaused)
    │   └── FAInfoBar (PausedBadgeText + PausedHintText)
    │
    ├── Separator (settings-separator)
    │
    ├── IconText (Icon="Info", Text="版本信息")
    │
    ├── FASettingsExpander "当前版本" (IsClickEnabled=False)
    │   ├── IconSource: 版本图标
    │   └── Footer: CurrentVersionText
    │
    ├── FASettingsExpander "最新版本" (IsClickEnabled=False)
    │   ├── IconSource: 下载图标
    │   └── Footer: LatestVersionText (或 "已是最新")
    │
    ├── FASettingsExpander "发布时间" (IsClickEnabled=False)
    │   ├── IconSource: 日历图标
    │   └── Footer: PublishedAtText
    │
    ├── FASettingsExpander "上次检查" (IsClickEnabled=False)
    │   ├── IconSource: 时钟图标
    │   └── Footer: LastCheckedText
    │
    ├── FASettingsExpander "更新类型" (IsClickEnabled=False)
    │   ├── IconSource: 标签图标
    │   └── Footer: UpdateTypeText
    │
    ├── Separator (settings-separator)
    │
    ├── IconText (Icon="Settings", Text="更新偏好")
    │
    └── FASettingsExpander "更新偏好" (IsExpanded=True)
        ├── IconSource: 设置齿轮图标
        ├── FASettingsExpanderItem "更新频道"
        │   └── Footer: ComboBox (stable/preview)
        ├── FASettingsExpanderItem "下载源"
        │   └── Footer: ComboBox (plonds/github/proxy)
        ├── FASettingsExpanderItem "更新模式"
        │   └── Footer: ComboBox (manual/confirm/silent)
        └── FASettingsExpanderItem "下载线程数"
            └── Footer: Slider + TextBlock
```

---

## Proposed Changes

### Task 1: 重写 UpdateSettingsPage.axaml 布局

**Files:**
- Modify: `LanMountainDesktop/Views/SettingsPages/UpdateSettingsPage.axaml`

**What:** 完全重写 AXAML，将 4 个 `Border.settings-section-card` 替换为 `FASettingsExpander` 列表布局。

**Key changes:**
1. 移除所有 `Border.settings-section-card` 包裹
2. 使用 `controls:IconText` 做分节标题（与 GeneralSettingsPage 一致）
3. 状态区域：`FASettingsExpander` + `IsClickEnabled=True` + `Command=CheckCommand`，Footer 放检查按钮
4. 进度区域：`FASettingsExpander` 内嵌 ProgressBar + 操作按钮，仅 `IsBusy||IsProgressVisible||IsPaused` 时可见
5. 版本信息：每个字段一个 `FASettingsExpander`，Footer 直接显示值（参考 Windows 11 更新页面的行项目模式）
6. 偏好设置：保留 `FASettingsExpander` + `FASettingsExpanderItem` 模式，但将 TextBox 改为 ComboBox（更符合 Fluent 规范）
7. 使用 `Separator classes="settings-separator"` 分隔三大区域

**Why:** 与项目其他设置页面统一风格，遵循 Fluent Design，消除卡片堆叠的视觉噪音。

**How:**
- 参照 GeneralSettingsPage.axaml 的布局模式
- 参照 AppearanceSettingsPage.axaml 的 FASettingsExpander 使用方式
- 参照 AboutSettingsPage.axaml 的 FAInfoBar 使用方式

### Task 2: 更新 ViewModel — 添加 ComboBox 数据源和按钮可见性属性

**Files:**
- Modify: `LanMountainDesktop/ViewModels/UpdateSettingsViewModel.cs`

**What:**
1. 将更新频道、下载源、更新模式从 `TextBox` 绑定改为 `ComboBox` 绑定，添加 `ObservableCollection<SelectionOption>` 类型的数据源属性
2. 添加 `IsProgressSectionVisible` 计算属性（`IsBusy || IsProgressVisible || IsPaused`）
3. 添加 `IsUpdateAvailableSectionVisible` 计算属性（`IsUpdateAvailable`）
4. 添加 `IsStatusInfoVisible` 计算属性（有 StatusMessage 且非空闲时）
5. 移除不再需要的独立按钮文本属性（CheckButtonText 保留，其他按钮文本属性保留但仅在可见时使用）

**Why:** ComboBox 比 TextBox 更适合有限选项的输入，且与 GeneralSettingsPage 的模式一致。按钮可见性属性让 AXAML 可以用 `IsVisible` 绑定控制按钮显示。

**How:**
- 参考 GeneralSettingsPageViewModel 中 SelectionOption 的使用方式
- 在 `OnCurrentPhaseChanged` 中触发新属性的 OnPropertyChanged

### Task 3: 将偏好设置 TextBox 替换为 ComboBox

**Files:**
- Modify: `LanMountainDesktop/Views/SettingsPages/UpdateSettingsPage.axaml` (在 Task 1 中一并完成)
- Modify: `LanMountainDesktop/ViewModels/UpdateSettingsViewModel.cs` (在 Task 2 中一并完成)

**What:** 将更新频道、下载源、更新模式三个 `TextBox` 替换为 `ComboBox`，使用 `SelectionOption` 数据模板。

**Why:** 有限选项应使用 ComboBox 而非自由文本输入，这是 Fluent Design 的基本规范，也与 GeneralSettingsPage 中的语言/时区选择一致。

### Task 4: 构建验证

**Files:**
- 无新文件

**What:** 运行 `dotnet build` 确保编译通过，检查 AXAML 绑定是否正确。

---

## Assumptions & Decisions

1. **不修改 UpdateOrchestrator 和 UpdateState** — 只改 UI 层和 ViewModel 的展示逻辑，不改底层更新引擎
2. **不修改 SettingsCardStyles.axaml** — 通用样式保持不变，移除的是 UpdateSettingsPage 对它的使用
3. **保留所有 ViewModel 属性** — 即使某些属性在新布局中不再直接使用（如独立的 ActionsTitle），也保留以避免破坏本地化系统
4. **ComboBox 选项硬编码在 ViewModel** — 参考 GeneralSettingsPageViewModel 的 SelectionOption 模式
5. **进度区域在空闲时隐藏** — 不显示空的进度条，只在有活动时展示
6. **FAInfoBar 仅用于暂停/错误提示** — 不再嵌套在卡片内，直接放在 FASettingsExpanderItem 内

---

## Verification Steps

1. `dotnet build LanMountainDesktop.slnx -c Debug` 编译通过
2. 运行应用，导航到设置 > 更新页面，验证：
   - 页面布局与 GeneralSettingsPage 风格一致
   - 无圆角矩形卡片包裹
   - 检查更新按钮可用
   - 进度区域在空闲时隐藏
   - 版本信息以行项目形式展示
   - 偏好设置使用 ComboBox
   - 操作按钮仅显示当前可用的
3. 点击「检查更新」，验证状态变化和进度展示
4. 验证偏好设置的 ComboBox 选择能正确保存和加载
