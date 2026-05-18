# UI Design System Guide (design.md)

> Settings window shell-specific rules live in `docs/ai/SETTINGS_WINDOW_DESIGN.md`.

> **目标**: 让 AI 正确使用 Fluent Avalonia / Fluent Icons / Material Avalonia，避免窗口套窗口、容器套容器
>
> **最后更新**: 2026-04-11

---

## 一句话总结

**主界面用 Fluent + FluentIcon，编辑器用 Material + MaterialIcon，永远不要混用，保持扁平结构。**

---

## 1. 技术栈与职责

### 1.1 库清单

| 库 | 包名 | 什么时候用 |
|---|------|----------|
| **FluentAvaloniaUI** | `FluentAvaloniaUI` | 主界面、设置页、导航 |
| **FluentIcons.Avalonia.Fluent** | `FluentIcons.Avalonia.Fluent` | 主界面图标（**首选**）|
| **FluentIcons.Avalonia** | `FluentIcons.Avalonia` | 旧图标兼容（SymbolIcon）|
| **Material.Icons.Avalonia** | `Material.Icons.Avalonia` | 编辑器图标（**仅限 ComponentEditorWindow**）|
| **Material.Avalonia** | `Material.Avalonia` | MD3 主题（**仅限 ComponentEditorWindow**）|

### 1.2 初始化顺序（App.axaml）

```xml
<Application.Styles>
    <sty:FluentAvaloniaTheme />           <!-- 第 1 位：基础 Win11 风格 -->
    <mi:MaterialIconStyles />             <!-- 第 2 位：全局注册 Material Icons 样式 -->
    
    <!-- 项目自定义样式 -->
    <StyleInclude Source="avares://LanMountainDesktop/Styles/FlutermotionToken.axaml" />
    <StyleInclude Source="avares://LanMountainDesktop/Styles/GlassModule.axaml" />
    <StyleInclude Source="avares://LanMountainDesktop/Styles/SettingsAnimations.axaml" />
    <StyleInclude Source="avares://LanMountainDesktop/Styles/SettingsCardStyles.axaml" />
    <StyleInclude Source="avares://LanMountainDesktop/Styles/NavigationStyles.axaml" />
</Application.Styles>
```

---

## 2. 命名空间速查表

**复制粘贴用：**

```xml
<!-- 必需 -->
xmlns="https://github.com/avaloniaui"
xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"

<!-- FluentAvaloniaUI 控件（主界面/设置页必需）-->
xmlns:ui="using:FluentAvalonia.UI.Controls"

<!-- Fluent Icons - 新版推荐（主界面首选）-->
xmlns:fi="using:FluentIcons.Avalonia.Fluent"

<!-- Fluent Icons - 旧版兼容（已有代码维护用）-->
<!-- xmlns:fi-legacy="using:FluentIcons.Avalonia" -->

<!-- Material Icons（仅 ComponentEditorWindow 使用）-->
<!-- xmlns:mi="clr-namespace:Material.Icons.Avalonia;assembly=Material.Icons.Avalonia" -->

<!-- Material Theme（仅 ComponentEditorWindow 使用）-->
<!-- xmlns:themes="clr-namespace:Material.Styles.Themes;assembly=Material.Styles" -->

<!-- 项目控件 -->
xmlns:local="using:LanMountainDesktop.Controls"
xmlns:comp="using:LanMountainDesktop.Views.Components"
```

---

## 3. 图标系统

### 3.1 选择决策树

```
你在写什么？
├─ 设置页面 / 主界面 / 桌面组件？
│  └─ 用 FluentIcon（fi:FluentIcon）
│     ├─ 需要 Filled/Regular 切换？→ IconVariant="Filled" 或 "Regular"
│     └─ 简单静态图标？→ 也用 FluentIcon，不用 SymbolIcon
│
├─ ComponentEditorWindow 及其子页面？
│  └─ 用 MaterialIcon（mi:MaterialIcon）
│
└─ 其他情况？
   └─ 默认 FluentIcon
```

### 3.2 FluentIcon 使用方法

```xml
<fi:FluentIcon Icon="Settings"              <!-- 图标名称 -->
               IconVariant="Filled"         <!-- Filled | Regular -->
               Classes="icon-m" />          <!-- icon-s(12px) | icon-m(16px) | icon-l(20px) -->
```

**常用图标名称：**
- 导航类：`Home`, `Settings`, `Navigation`, `ArrowLeft`, `ChevronRight`, `Dismiss`
- 操作类：`Add`, `Delete`, `Edit`, `Save`, `Refresh`, `Sync`, `ArrowSync`
- 状态类：`Info`, `Warning`, `ErrorBadge`, `CheckmarkCircle`
- 外观类：`ThemeLightDark`, `ColorBackground`, `Appearance`

### 3.3 MaterialIcon 使用方法（仅限编辑器）

```xml
<mi:MaterialIcon Kind="Close"                <!-- 图标名称 -->
                 Width="24"
                 Height="24"
                 Foreground="{DynamicResource EditorPrimaryBrush}" />
```

**常用 Kind 值：**
- 操作：`Close`, `Check`, `Pencil`, `Delete`, `Settings`, `Plus`
- 导航：`ArrowLeft`, `ArrowRight`, `Home`, `Menu`
- 系统：`Power`, `Lock`, `ExitToApp`, `Refresh`, `WeatherNight`

### 3.4 ❌ 禁止事项

```xml
<!-- ❌ 错误：同一区域混用两种图标库 -->
<StackPanel>
    <fi:FluentIcon Icon="Home" />           <!-- Fluent -->
    <mi:MaterialIcon Kind="Settings" />     <!-- Material -->
</StackPanel>

<!-- ❌ 错误：硬编码尺寸 -->
<fi:FluentIcon Icon="Settings" FontSize="18" />

<!-- ✅ 正确：使用预定义 class -->
<fi:FluentIcon Icon="Settings" Classes="icon-m" />
```

---

## 4. 容器嵌套规范（核心！）

### 4.1 最大深度限制

| 场景 | 最大层数 | 从哪里开始数 |
|-----|---------|------------|
| 普通页面 | **≤ 4 层** | Window/UserControl → ... → 叶子节点 |
| Popup/Dialog | **≤ 3 层** | Border → Content |
| 列表项/DataTemplate | **≤ 3 层** | Root → ... → 元素 |
| MainWindow 桌面布局 | **≤ 6 层** | 特殊允许（多层叠加需求）|

### 4.2 如何数层级？

从根元素到目标元素经过的容器标签数：

```xml
<UserControl>                          <!-- 层级 0（根）-->
    <Border>                           <!-- 层级 1 -->
        <Grid>                         <!-- 层级 2 -->
            <StackPanel>               <!-- 层级 3 -->
                <Button>               <!-- 层级 4（叶子节点）✅ OK -->
                </Button>
            </StackPanel>
        </Grid>
    </Border>
</UserControl>
```

### 4.3 推荐的标准结构

#### 结构 A：标准设置页面（3-4 层）

```xml
<UserControl>
    <ScrollViewer>                                     <!-- 层 1 -->
        <StackPanel Spacing="24">                      <!-- 层 2 -->
            <Border Classes="settings-section-card">  <!-- 层 3 -->
                <StackPanel Spacing="16">              <!-- 层 4 ✅ -->
                    <!-- 内容 -->
                </StackPanel>
            </Border>
        </StackPanel>
    </ScrollViewer>
</UserControl>
```

#### 结构 B：卡片/面板组件（3 层）

```xml
<Border CornerRadius="..." Padding="...">            <!-- 层 1（根）-->
    <Grid RowDefinitions="Auto,*">                   <!-- 层 2 -->
        <Grid ColumnDefinitions="Auto,*,Auto">        <!-- 层 3a: Header -->
            <fi:FluentIcon Classes="icon-l" />
            <TextBlock Grid.Column="1" />
            <ContentPresenter Grid.Column="2" />
        </Grid>
        <ContentControl Grid.Row="1" />              <!-- 层 3b: Body ✅ -->
    </Grid>
</Border>
```

#### 结构 C：列表项（2-3 层）

```xml
<Border Classes="list-item">                         <!-- 层 1 -->
    <Grid ColumnDefinitions="Auto,*,Auto">           <!-- 层 2 -->
        <Border Classes="icon-host">                 <!-- 层 3a -->
            <fi:FluentIcon Classes="icon-m" />
        </Border>
        <StackPanel Grid.Column="1" Spacing="4">     <!-- 层 3b -->
            <TextBlock Classes="title" />
            <TextBlock Classes="subtitle" />
        </StackPanel>
        <Button Grid.Column="2">...</Button>         <!-- 层 3c ✅ -->
    </Grid>
</Border>
```

### 4.4 ❌ 反模式：过度嵌套

```xml
<!-- ❌ 错误：7 层嵌套（实际项目中出现的反面教材）-->
<Grid>                              <!-- 1 -->
    <Grid>                          <!-- 2 -->
        <Border>                    <!-- 3 -->
            <Grid>                  <!-- 4 -->
                <Border>            <!-- 5 -->
                    <Grid>          <!-- 6 -->
                        <Border>    <!-- 7 ❌ 太深了！-->
                            <Button Content="Click" />
                        </Border>
                    </Grid>
                </Border>
            </Grid>
        </Border>
    </Grid>
</Grid>

<!-- ✅ 重构后：2 层 -->
<Grid HorizontalAlignment="Center"
      VerticalAlignment="Center">
    <Button Content="Click"
            Padding="16,8" />
</Grid>
```

### 4.5 何时可以超过限制？

只有这 3 种情况：

1. **MainWindow 桌面布局**（壁纸层 + 组件层 + 拖拽层 + 任务栏）
2. **需要独立动画层**（Transform/Opacity 动画需要单独容器）
3. **复杂 Popup 内部**

**必须加注释说明原因：**
```xml
<!--
  允许深层嵌套原因：桌面渲染需要支持以下视觉层级
  - DesktopWallpaperLayer（壁纸背景）
  - DesktopPagesContainer（桌面分页）
  - LauncherPagePanel（启动器面板）
  - Canvas 拖拽层
  - BottomTaskbarContainer（任务栏）
-->
<Grid x:Name="DesktopHost">
    ...
</Grid>
```

---

## 5. 窗口 vs UserControl vs Border

### 5.1 什么时候用什么？

| 需求 | 用什么 | 示例 |
|-----|-------|------|
| 独立窗口（有标题栏、可拖动、任务栏可见）| **Window** | SettingsWindow, ComponentEditorWindow |
| 可复用的 UI 组件块 | **UserControl** | SettingsOptionCard, ClockWidget |
| 视觉上的卡片/面板/容器 | **Border** | 设置分区卡片、弹出面板 |

### 5.2 ❌ 禁止：窗口套窗口

```csharp
// ❌ 错误：在 XAML 中实例化另一个 Window
// <local:SettingsWindow Visibility="Visible" />

// ✅ 正确：通过代码显示独立窗口
var settings = new SettingsWindow { Owner = this };
settings.Show();
```

### 5.3 ❌ 禁止：把 Window 当 UserControl 用

```xml
<!-- ❌ 错误：MainWindow 内部嵌入了一个本应是独立窗口的东西 -->
<Window x:Class="MainWindow">
    <Grid>
        <Border x:Name="ComponentLibraryWindow"  <!-- 这看起来像窗口，但不应该是 Window -->
               Width="620"
               Height="320"
               CornerRadius="36">
            <!-- 组件库内容 -->
        </Border>
    </Grid>
</Window>
```

**如果它不是真正的操作系统窗口，就用 Border 或 UserControl。**

---

## 6. 颜色与资源使用规范

### 6.1 必须使用 DynamicResource

```xml
<!-- ❌ 错误：硬编码颜色 -->
<TextBlock Foreground="#FF1D1B20" />
<Border Background="#FFF3EDF7" />
<Button Background="#FF6750A4" />

<!-- ✅ 正确：使用动态资源 -->
<TextBlock Foreground="{DynamicResource AdaptiveTextPrimaryBrush}" />
<Border Background="{DynamicResource AdaptiveSurfaceRaisedBrush}" />
<Button Background="{DynamicResource AccentBrush}" />
```

### 6.2 常用资源键速查

| 资源键 | 用途 | 示例场景 |
|--------|------|---------|
| `AdaptiveTextPrimaryBrush` | 主要文本 | 标题、正文 |
| `AdaptiveTextSecondaryBrush` | 次要文本 | 描述文字、提示 |
| `AdaptiveSurfaceRaisedBrush` | 抬高表面 | 卡片背景、面板 |
| `AdaptiveSurfaceOverlayBrush` | 覆盖层 | 遮罩、弹窗背景 |
| `AccentBrush` | 强调色 | 主按钮、选中态 |
| `AppFontFamily` | 应用字体 | 全局字体设置 |

### 6.3 圆角 Token（强制！）

```xml
<!-- ❌ 错误：硬编码圆角 -->
<Border CornerRadius="8" />
<Button CornerRadius="12" />

<!-- ❌ 错误：桌面组件根容器用了非组件级 Token -->
<Border CornerRadius="{DynamicResource DesignCornerRadiusMd}" />

<!-- ✅ 正确 -->
<!-- 桌面组件（Widget）根容器必须且只能用这个 -->
<Border CornerRadius="{DynamicResource DesignCornerRadiusComponent}" />

<!-- 内部元素按层级选择 -->
<Border CornerRadius="{DynamicResource DesignCornerRadiusSm}" />   <!-- 小 -->
<Border CornerRadius="{DynamicResource DesignCornerRadiusMd}" />   <!-- 中 -->
<Border CornerRadius="{DynamicResource DesignCornerRadiusLg}" />   <!-- 大 -->
```

---

## 7. FluentAvaloniaUI 控件用法

### 7.1 NavigationView（设置页导航）

```xml
<ui:NavigationView x:Name="RootNav"
                   PaneDisplayMode="Auto"           <!-- 自动折叠 -->
                   OpenPaneLength="283"             <!-- Win11 标准宽度 -->
                   IsSettingsVisible="False"        <!-- 隐藏默认设置按钮 -->
                   IsBackButtonVisible="False"
                   SelectionChanged="OnNavChanged">

    <ui:NavigationView.Resources>
        <!-- 移除默认背景色 -->
        <SolidColorBrush x:Key="NavigationViewContentBackground" Color="Transparent" />
        <SolidColorBrush x:Key="NavigationViewContentGridBorderBrush" Color="Transparent" />
    </ui:NavigationView.Resources>

    <Grid Margin="12,0,16,16">
        <ui:Frame x:Name="ContentFrame" />
    </Grid>
</ui:NavigationView>
```

### 7.2 Frame（页面导航容器）

```xml
<ui:Frame x:Name="ContentFrame" />
```

```csharp
// C# 代码中导航
ContentFrame.Navigate(typeof(SettingsHomePage));
// 或绑定
ContentFrame.SourcePageType = typeof(SettingsHomePage);
```

### 7.3 InfoBar（内联通知条）

```xml
<ui:InfoBar Title="发现新版本"
             Message="v2.0.0 可用"
             Severity="Informational"       <!-- Informational | Warning | Error | Success -->
             IsOpen="True"
             ActionButtonText="立即更新"
             ActionButtonClick="OnUpdateClick"
             CloseButtonClick="OnDismissClick" />
```

### 7.4 ContentDialog（模态对话框）

```csharp
var dialog = new ContentDialog
{
    Title = "确认删除",
    Content = "确定要删除吗？此操作不可撤销。",
    PrimaryButtonText = "删除",
    CloseButtonText = "取消",
    DefaultButton = ContentDialogButton.Primary
};

var result = await dialog.ShowAsync(this);
if (result == ContentDialogResult.Primary)
{
}
```

---

## 8. Material.Avalonia 使用规范（严格限制！）

### 8.1 ⚠️ 只能在这里用

**✅ 允许：**
- `ComponentEditorWindow.axaml`
- ComponentEditorWindow 的子编辑页面

**❌ 禁止：**
- MainWindow
- SettingsWindow
- NotificationWindow
- 任何桌面组件（Widget）
- 任何其他地方

### 8.2 如何在 ComponentEditorWindow 中启用

```xml
<Window ...>
    <Window.Resources>
        <!-- MD3 色板（仅此窗口有效）-->
        <SolidColorBrush x:Key="EditorPrimaryBrush" Color="#FF6750A4" />
        <SolidColorBrush x:Key="EditorSurfaceContainerBrush" Color="#FFF3EDF7" />
        <SolidColorBrush x:Key="EditorOnPrimaryBrush" Color="#FFFFFFFF" />
    </Window.Resources>

    <Window.Styles>
        <!-- 加载 Material 主题（只影响此窗口）-->
        <themes:CustomMaterialTheme BaseTheme="Light"
                                     PrimaryColor="#6750A4"
                                     SecondaryColor="#625B71" />

        <!-- 项目自定义覆盖 -->
        <StyleInclude Source="avares://LanMountainDesktop/Styles/ComponentEditorThemeResources.axaml" />
    </Window.Styles>
</Window>
```

### 8.3 MD3 组件示例

#### FAB 按钮（浮动操作按钮）

```xml
<Button x:Name="SaveFAB"
        HorizontalAlignment="Right"
        VerticalAlignment="Bottom"
        Margin="28"
        Width="64" Height="64"
        Background="{DynamicResource EditorPrimaryBrush}"
        Foreground="{DynamicResource EditorOnPrimaryBrush}"
        CornerRadius="{DynamicResource DesignCornerRadiusComponent}"
        Click="OnSaveClick">
    <mi:MaterialIcon Kind="Check" Width="32" Height="32" />
</Button>
```

#### Top App Bar（顶栏）

```xml
<Border Background="{DynamicResource EditorTopAppBarBackgroundBrush}"
        Padding="24,16">
    <Grid ColumnDefinitions="Auto,*,Auto">
        <mi:MaterialIcon Kind="Widgets"
                         Width="28" Height="28"
                         Foreground="{DynamicResource EditorPrimaryBrush}" />
        <TextBlock Grid.Column="1"
                   FontSize="20" FontWeight="SemiBold"
                   Text="组件编辑器"
                   Margin="16,0,0,0" />
        <Button Grid.Column="2" Click="OnCloseClick">
            <mi:MaterialIcon Kind="Close" Width="24" Height="24" />
        </Button>
    </Grid>
</Border>
```

---

## 9. 实战代码模板

### 模板 1：新建设置页面

文件位置：`Views/Settings/YourPageName.axaml`

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:fi="using:FluentIcons.Avalonia.Fluent"
             xmlns:local="using:LanMountainDesktop.Controls"
             x:Class="LanMountainDesktop.Views.Settings.YourPageName">

    <ScrollViewer Padding="24,0,24,24">
        <StackPanel Spacing="24">

            <!-- 分区卡片 -->
            <Border Classes="settings-section-card">
                <StackPanel Spacing="16">

                    <!-- 分区标题 -->
                    <Grid ColumnDefinitions="Auto,*" ColumnSpacing="12">
                        <fi:FluentIcon Icon="YourSectionIcon"
                                       IconVariant="Filled"
                                       Classes="icon-l" />
                        <TextBlock Grid.Column="1"
                                   Classes="settings-section-title"
                                   Text="分区标题" />
                    </Grid>

                    <!-- 选项卡 1 -->
                    <local:SettingsOptionCard Icon="OptionIcon1"
                                              Title="选项标题"
                                              Title="选项描述">
                        <local:SettingsOptionCard.ActionContent>
                            <ToggleSwitch IsChecked="{Binding YourProperty, Mode=TwoWay}" />
                        </local:SettingsOptionCard.ActionContent>
                    </local:SettingsOptionCard>

                    <!-- 选项卡 2 -->
                    <local:SettingsOptionCard Icon="OptionIcon2"
                                              Title="另一个选项"
                                              Title="描述信息" />

                </StackPanel>
            </Border>

        </StackPanel>
    </ScrollViewer>
</UserControl>
```

**嵌套检查：** ScrollViewer(1) > StackPanel(2) > Border(3) > StackPanel(4) > Items(5) ✅ （因为有 ScrollViewer 容器，5 层可接受）

### 模板 2：新建桌面小组件

文件位置：`Views/Components/YourWidget.axaml`

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="LanMountainDesktop.Views.Components.YourWidget">

    <Border Classes="desktop-widget-root"
            CornerRadius="{DynamicResource DesignCornerRadiusComponent}"
            Padding="16,12">
        <Grid RowDefinitions="Auto,*" RowSpacing="8">

            <!-- 标题区 -->
            <TextBlock x:Name="TitleTextBlock"
                       FontSize="14"
                       FontWeight="SemiBold" />

            <!-- 内容区 -->
            <ContentControl Grid.Row="1" />

        </Grid>
    </Border>
</UserControl>
```

**嵌套检查：** Border(1) > Grid(2) > Elements(3) ✅ 完美！

### 模板 3：新建独立窗口

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:ui="using:FluentAvalonia.UI.Controls"
        xmlns:fi="using:FluentIcons.Avalonia.Fluent"
        x:Class="LanMountainDesktop.Views.YourWindow"
        Width="800"
        Height="600"
        MinWidth="400"
        MinHeight="300"
        CanResize="True"
        SystemDecorations="BorderOnly"
        Background="Transparent"
        Title="窗口标题">

    <Grid RowDefinitions="Auto,*">

        <!-- 自定义标题栏 -->
        <Border Height="48"
                Padding="12,0"
                PointerPressed="OnTitleBarPressed">
            <Grid ColumnDefinitions="Auto,*,Auto">
                <fi:FluentIcon Icon="WindowIcon"
                               IconVariant="Filled"
                               Classes="icon-m" />
                <TextBlock Grid.Column="1"
                           Text="{Binding Title}"
                           VerticalAlignment="Center" />
                <Button Grid.Column="2"
                        Click="OnCloseClick">
                    <fi:FluentIcon Icon="Dismiss" IconVariant="Regular" />
                </Button>
            </Grid>
        </Border>

        <!-- 主内容区 -->
        <ContentControl Grid.Row="1"
                        Content="{Binding Content}" />

    </Grid>
</Window>
```

**嵌套检查：** Grid(1) > Border(2) > Grid(3) > Elements(4) ✅

---

## 10. AI 编码检查清单

### 写代码前问自己

- [ ] 这个文件是设置页/主界面？→ 用 Fluent + FluentIcon
- [ ] 这个文件是 ComponentEditorWindow？→ 用 Material + MaterialIcon
- [ ] 我用了正确的命名空间吗？（见第 2 节速查表）
- [ ] 图标用了 Classes="icon-s/m/l" 而非硬编码 FontSize 吗？

### 写完代码后检查

- [ ] 数一下最大嵌套深度（见第 4.2 节）
- [ ] 有没有硬编码颜色值？（应该都用 DynamicResource）
- [ ] 有没有硬编码 CornerRadius？（应该用 DesignCornerRadiusXxx）
- [ ] 有没有在同一区域混用 FluentIcon 和 MaterialIcon？
- [ ] 是不是不小心写了 `<local:SomeWindow>` 在另一个 Window 里？
- [ ] 是不是连续写了 Border > Grid > Border > Grid 可以合并？

### 如果审查别人代码

- [ ] 发现窗口套窗口了吗？
- [ ] 发现超过 4 层的无意义嵌套了吗？（没有注释说明原因的话）
- [ ] 发现 Fluent 和 Material 控件混在同一区域了吗？
- [ ] 发现应该用 DynamicResource 的地方硬编码了吗？

---

## 附录：常见错误快速修复

| 错误现象 | 问题原因 | 修复方法 |
|---------|---------|---------|
| 图标不显示或大小不对 | 用了错误的命名空间或硬编码尺寸 | 改用 `fi:FluentIcon` + `Classes="icon-m"` |
| 圆角在设置里改了但没生效 | 硬编码了 CornerRadius | 改用 `{DynamicResource DesignCornerRadiusComponent}` |
| 深色模式下颜色刺眼 | 硬编码了颜色值 | 改用 `{DynamicResource AdaptiveTextPrimaryBrush}` 等 |
| 设置页风格和其他窗口不一致 | 混用了 Material 控件 | 统一用 FluentAvaloniaUI 控件 |
| 性能差/渲染慢 | 嵌套太深（>6 层）| 扁平化结构，合并多余容器 |
| 弹窗显示位置/大小异常 | 把 Window 当成 UserControl 嵌套了 | 改为代码中 `.Show()` 显示 |

---

**相关文档：**
- [VISUAL_SPEC.md](./VISUAL_SPEC.md) - 视觉规范总纲
- [CORNER_RADIUS_SPEC.md](./CORNER_RADIUS_SPEC.md) - 圆角详细规范
- AGENTS.md - AI 强制规则
