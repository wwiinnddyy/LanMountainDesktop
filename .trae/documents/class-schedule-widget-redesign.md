# 课程表组件视觉重构 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 彻底重构阑山桌面的课程表（ClassScheduleWidget）组件视觉设计，参考小爱课程表的桌面小部件风格，实现时间轴+色块卡片布局、科目自动配色、当前课程进度高亮等现代化视觉效果。

**Architecture:** 保留现有数据层（ClassIslandScheduleDataService、Models）和组件注册机制不变，仅重构 Widget 的 UI 渲染层（XAML + code-behind 中的渲染逻辑）。新增科目配色服务，为每门课程分配稳定的区分色。先创建 HTML Mock 验证视觉效果，再移植到 Avalonia XAML。

**Tech Stack:** Avalonia UI (XAML + C# code-behind)、HTML/CSS (Mock 预览)

---

## 当前状态分析

### 现有组件结构
- **XAML**: `ClassScheduleWidget.axaml` — 仅定义了 RootBorder、HeaderGrid（日期+星期+课数）、ScrollViewer+CourseListPanel、StatusTextBlock
- **Code-behind**: `ClassScheduleWidget.axaml.cs` — 所有课程项 UI 在 `CreateSingleItemControl()` 中手动构建：圆点(Bullet) + 文字栈（课程名/时间/详情）
- **数据层**: `ClassIslandScheduleDataService` + `ClassIslandScheduleModels` — 不变
- **编辑器**: `ClassScheduleComponentEditor.axaml(.cs)` — 不变

### 现有设计问题
1. **视觉单调**: 仅用小圆点区分课程，所有课程外观一致，缺乏层次感
2. **信息密度低**: 课程名、时间、教师名挤在一行，可读性差
3. **当前课不突出**: 仅通过圆点颜色变化标识当前课程，几乎无法一眼识别
4. **色彩硬编码**: 颜色值直接写在 C# 中，不使用语义资源键，不遵循 VISUAL_SPEC
5. **无时间轴感**: 列表式排列无法体现课程的时间先后和持续长度

### 小爱课程表参考设计特征
1. **时间轴布局**: 左侧显示时间刻度，右侧是课程色块卡片
2. **科目配色**: 每门课程自动分配一种柔和区分色，卡片使用对应色块背景
3. **当前课高亮**: 正在进行的课程有明显的视觉强调（放大/进度条/发光）
4. **进度指示**: 当前课程显示上课进度（已过时间/总时长）
5. **紧凑信息**: 课程名+教室/教师信息在色块内清晰排列
6. **课间分隔**: 课间休息区域有视觉分隔（虚线/淡色区域）

---

## 设计方案

### 视觉论文 (Visual Thesis)
时间轴驱动的色块卡片布局，柔和科目配色，当前课程进度高亮——在桌面小组件有限空间内实现信息密度与美感的平衡。

### 布局结构
```
┌─────────────────────────────────────┐
│  7/24  周一          今天3节课       │  ← 头部：日期 + 星期 + 课数
├─────────────────────────────────────┤
│  08:00 ┌──────────────────────┐     │
│        │  语文                │     │  ← 科目色块卡片
│        │  王老师 · 教室301     │     │
│  08:45 └──────────────────────┘     │
│        ┌──────────────────────┐     │
│        │  数学 ████████░░ 75% │     │  ← 当前课：进度条 + 高亮
│        │  李老师 · 教室205     │     │
│  09:30 └──────────────────────┘     │
│  ...                                │
└─────────────────────────────────────┘
```

### 科目配色方案
使用一组预定义的柔和色彩，按科目名哈希值稳定分配：
- 语文: #5B8FF9 (蓝)
- 数学: #F6903D (橙)
- 英语: #5AD8A6 (绿)
- 物理: #E8684A (红)
- 化学: #9270CA (紫)
- 生物: #FF9845 (琥珀)
- 历史: #1E9493 (青)
- 地理: #FF99C3 (粉)
- 政治: #7262FD (靛)
- 体育: #78D3F8 (天蓝)
- 默认: #8B95A5 (灰)

### 当前课程高亮
- 卡片左侧显示 3px 宽的强调色竖条
- 卡片底部显示细进度条（已过时间/总时长）
- 卡片背景使用科目色的 15% 透明度版本
- 非当前课程使用科目色的 8% 透明度版本

---

## 文件变更清单

| 文件 | 操作 | 说明 |
|------|------|------|
| `LanMountainDesktop/Views/Components/ClassScheduleWidget.axaml` | 修改 | 重构 XAML 布局：时间轴+卡片区域 |
| `LanMountainDesktop/Views/Components/ClassScheduleWidget.axaml.cs` | 修改 | 重构渲染逻辑：色块卡片、科目配色、进度条 |
| `LanMountainDesktop/Views/Components/SubjectColorService.cs` | 新建 | 科目配色服务：稳定哈希分配颜色 |
| `mocks/class-schedule-mock.html` | 新建 | HTML Mock 预览（亮色+暗色） |

---

## Task 分解

### Task 1: 创建 HTML Mock 预览

**Files:**
- Create: `mocks/class-schedule-mock.html`

- [ ] **Step 1: 创建 HTML Mock 文件**

创建完整的 HTML Mock，包含：
- 亮色/暗色主题切换
- 时间轴+色块卡片布局
- 科目自动配色
- 当前课程进度条高亮
- 课间分隔区域
- 响应式尺寸（模拟桌面组件 2x4 / 4x4 等尺寸）

Mock 中应包含示例数据：
```
08:00-08:45  语文  王老师
08:55-09:40  数学  李老师 (当前课，进度 60%)
09:50-10:35  英语  张老师
10:45-11:30  物理  赵老师
14:00-14:45  化学  陈老师
14:55-15:40  生物  刘老师
```

- [ ] **Step 2: 在浏览器中打开 Mock 验证效果**

Run: `start mocks/class-schedule-mock.html`

- [ ] **Step 3: 根据视觉效果调整 Mock 细节**

调整间距、色值、字体大小、进度条样式等直到满意。

---

### Task 2: 创建科目配色服务

**Files:**
- Create: `LanMountainDesktop/Views/Components/SubjectColorService.cs`

- [ ] **Step 1: 实现 SubjectColorService**

```csharp
using System;
using Avalonia.Media;

namespace LanMountainDesktop.Views.Components;

internal static class SubjectColorService
{
    private static readonly (string Name, string Hex)[] Palette = [
        ("语文", "#5B8FF9"),
        ("数学", "#F6903D"),
        ("英语", "#5AD8A6"),
        ("物理", "#E8684A"),
        ("化学", "#9270CA"),
        ("生物", "#FF9845"),
        ("历史", "#1E9493"),
        ("地理", "#FF99C3"),
        ("政治", "#7262FD"),
        ("体育", "#78D3F8"),
        ("音乐", "#F25E7E"),
        ("美术", "#C2A1FD"),
    ];

    private static readonly string DefaultHex = "#8B95A5";

    public static Color ResolveColor(string subjectName)
    {
        foreach (var (name, hex) in Palette)
        {
            if (subjectName.Contains(name, StringComparison.OrdinalIgnoreCase))
            {
                return Color.Parse(hex);
            }
        }

        var hash = StableHash(subjectName);
        var index = (int)(hash % (uint)Palette.Length);
        return Color.Parse(Palette[index].Hex);
    }

    public static Color ResolveBackgroundColor(string subjectName, bool isCurrent, bool isNight)
    {
        var baseColor = ResolveColor(subjectName);
        var alpha = isCurrent ? 0.18 : 0.08;
        return new Color(
            (byte)(alpha * 255),
            baseColor.R,
            baseColor.G,
            baseColor.B);
    }

    public static Color ResolveForegroundColor(string subjectName, bool isNight)
    {
        var baseColor = ResolveColor(subjectName);
        return isNight
            ? new Color(0xFF, (byte)Math.Min(255, baseColor.R + 60), (byte)Math.Min(255, baseColor.G + 60), (byte)Math.Min(255, baseColor.B + 60))
            : baseColor;
    }

    private static uint StableHash(string input)
    {
        uint hash = 5381;
        foreach (var c in input)
        {
            hash = ((hash << 5) + hash) ^ (uint)c;
        }
        return hash;
    }
}
```

- [ ] **Step 2: 验证编译通过**

Run: `dotnet build LanMountainDesktop/LanMountainDesktop.csproj -c Debug --no-restore`

---

### Task 3: 重构 ClassScheduleWidget XAML 布局

**Files:**
- Modify: `LanMountainDesktop/Views/Components/ClassScheduleWidget.axaml`

- [ ] **Step 1: 重写 XAML 布局**

新的 XAML 结构：
- RootBorder 保持 `DesignCornerRadiusComponent`
- 头部区域：日期（大号）+ 星期 + 课数 + 进度摘要
- 课程列表区域：ScrollViewer 包裹 StackPanel
- 每个 CourseItem 将在 code-behind 中构建为：Grid(时间列 + 卡片列)
  - 时间列：StartTime / EndTime 垂直排列
  - 卡片列：Border(科目色背景) > StackPanel(课程名 + 教师信息 + 进度条)

XAML 只定义骨架，课程项仍由 code-behind 动态构建（因为需要科目配色和进度计算）。

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="LanMountainDesktop.Views.Components.ClassScheduleWidget">
    <Border x:Name="RootBorder"
            Background="{DynamicResource AdaptiveSurfaceRaisedBrush}"
            BorderBrush="{DynamicResource AdaptiveButtonBorderBrush}"
            BorderThickness="1"
            CornerRadius="{DynamicResource DesignCornerRadiusComponent}"
            Padding="0">
        <Grid x:Name="LayoutGrid"
              RowDefinitions="Auto,*">
            <Grid x:Name="HeaderGrid"
                  ColumnDefinitions="Auto,*,Auto"
                  Padding="16,12,16,8">
                <StackPanel x:Name="DateGroup"
                            Orientation="Horizontal"
                            VerticalAlignment="Center">
                    <TextBlock x:Name="MonthTextBlock"
                               FontWeight="Bold"
                               TextTrimming="CharacterEllipsis" />
                    <TextBlock x:Name="SlashTextBlock"
                               Text="/"
                               FontWeight="Bold" />
                    <TextBlock x:Name="DayTextBlock"
                               FontWeight="Bold"
                               TextTrimming="CharacterEllipsis" />
                </StackPanel>
                <TextBlock x:Name="WeekdayTextBlock"
                           Grid.Column="1"
                           HorizontalAlignment="Center"
                           VerticalAlignment="Center"
                           FontWeight="SemiBold"
                           TextTrimming="CharacterEllipsis" />
                <Border x:Name="ClassCountBadge"
                        Grid.Column="2"
                        VerticalAlignment="Center"
                        Padding="8,3"
                        CornerRadius="{DynamicResource DesignCornerRadiusMicro}">
                    <TextBlock x:Name="ClassCountTextBlock"
                               FontWeight="SemiBold"
                               TextTrimming="CharacterEllipsis" />
                </Border>
            </Grid>
            <ScrollViewer x:Name="ContentScrollViewer"
                          Grid.Row="1"
                          HorizontalScrollBarVisibility="Disabled"
                          VerticalScrollBarVisibility="Auto">
                <StackPanel x:Name="CourseListPanel"
                            Spacing="4" />
            </ScrollViewer>
            <TextBlock x:Name="StatusTextBlock"
                       Grid.Row="1"
                       HorizontalAlignment="Center"
                       VerticalAlignment="Center"
                       TextAlignment="Center"
                       IsVisible="False"
                       TextWrapping="Wrap" />
        </Grid>
    </Border>
</UserControl>
```

---

### Task 4: 重构 ClassScheduleWidget 渲染逻辑

**Files:**
- Modify: `LanMountainDesktop/Views/Components/ClassScheduleWidget.axaml.cs`

- [ ] **Step 1: 扩展 CourseItemViewModel**

在现有 record 中增加字段：

```csharp
private sealed record CourseItemViewModel(
    string Name,
    string TimeRange,
    string Detail,
    bool IsCurrent,
    TimeSpan StartTime,
    TimeSpan EndTime,
    double Progress);
```

- [ ] **Step 2: 修改 BuildCourseItemViewModels 计算进度**

在构建 ViewModel 时，对当前课程计算 Progress = (now - startTime) / (endTime - startTime)。

- [ ] **Step 3: 重写 CreateSingleItemControl**

新的课程项 UI 结构：

```
Grid (2列: 时间列 Auto + 卡片列 *)
├── StackPanel (时间列)
│   ├── TextBlock (开始时间, 如 "08:00")
│   └── TextBlock (结束时间, 如 "08:45", 较淡)
└── Border (卡片列, 科目色背景, 圆角 DesignCornerRadiusSm)
    ├── 左侧强调竖条 (当前课显示, 3px宽, 科目色)
    └── StackPanel
        ├── TextBlock (课程名, 科目色前景, 加粗)
        ├── TextBlock (教师/教室, 次要色)
        └── ProgressBar (当前课显示, 科目色)
```

关键改动点：
1. 移除圆点(Bullet)，改用时间轴左侧时间标签
2. 课程卡片使用 `SubjectColorService` 配色
3. 当前课程卡片左侧显示强调竖条 + 底部进度条
4. 课间区域用淡色分隔线标识
5. 颜色使用语义资源键（`AdaptiveTextPrimaryBrush` 等），科目色通过 `SubjectColorService` 获取

- [ ] **Step 4: 重写 ApplyAdaptiveLayout**

更新自适应布局逻辑：
- 头部日期/星期/课数徽章的字号和间距
- 移除旧的圆点、文字栈相关计算
- 新增时间列宽度、卡片圆角、进度条高度等计算
- 使用 `ComponentChromeCornerRadiusHelper` 获取圆角 Token

- [ ] **Step 5: 更新 IncrementalUpdateItems 和 IncrementalUpdateCurrentCourseHighlight**

适配新的 UI 结构：
- 更新进度条值
- 更新科目色背景
- 更新强调竖条可见性

- [ ] **Step 6: 更新 RefreshSchedule 中的时间计算**

在 `BuildCourseItemViewModels` 中传入 `StartTime`/`EndTime`/`Progress`。

- [ ] **Step 7: 验证编译通过**

Run: `dotnet build LanMountainDesktop/LanMountainDesktop.csproj -c Debug`

---

### Task 5: 验证与测试

- [ ] **Step 1: 运行项目查看效果**

Run: `dotnet run --project LanMountainDesktop/LanMountainDesktop.csproj`

- [ ] **Step 2: 运行相关测试**

Run: `dotnet test LanMountainDesktop.slnx -c Debug`

- [ ] **Step 3: 检查圆角规范合规**

确认 RootBorder 使用 `DesignCornerRadiusComponent`，内部卡片使用 `DesignCornerRadiusSm`/`DesignCornerRadiusMd`，无硬编码圆角值。

---

## 假设与决策

1. **科目配色**: 使用预定义调色板 + 哈希回退，不依赖 ClassIsland 数据中的科目颜色（因为 ClassIsland 不提供科目颜色字段）
2. **进度条**: 仅当前课程显示进度条，非当前课程不显示
3. **课间分隔**: 用 4px 间距 + 可选的淡色虚线分隔，不做复杂的课间休息区域
4. **Mock 优先**: 先完成 HTML Mock 确认视觉效果，再实现 Avalonia 代码
5. **编辑器不变**: ClassScheduleComponentEditor 不需要修改
6. **数据层不变**: ClassIslandScheduleDataService 和 Models 不需要修改
7. **接口兼容**: IDesktopComponentWidget、ITimeZoneAwareComponentWidget、IComponentPlacementContextAware 接口实现不变

## 验证步骤

1. HTML Mock 在浏览器中展示效果满意
2. Avalonia 项目编译通过
3. 运行项目，课程表组件显示新布局
4. 亮色/暗色主题切换正常
5. 当前课程高亮和进度条正常
6. 科目配色稳定（同一科目每次显示颜色一致）
7. 测试通过
