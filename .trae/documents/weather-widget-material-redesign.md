# 天气组件 Material Design 重设计计划

> **目标：** 全面重构阑山桌面天气组件的视觉设计，遵循 Material Design 3 规范，参考 Google Weather、几何天气、Breez 天气和柠檬天气的设计语言。

---

## 当前状态分析

### 现有组件
1. **WeatherWidget** - 基础天气（温度+天气状况+位置）
2. **ExtendedWeatherWidget** - 扩展天气（含指标、逐小时、逐日预报）
3. **HourlyWeatherWidget** - 逐小时天气
4. **MultiDayWeatherWidget** - 多日天气
5. **WeatherClockWidget** - 天气时钟

### 现有问题
- 排版层次不清晰，文字大小对比不够
- 布局过于紧凑，缺乏呼吸感
- 内部卡片使用简单纯色背景，缺乏 Material 风格
- 背景场景和前景内容缺乏深度分离
- 圆角和间距不统一

### 现有视觉系统
- 4套调色板：Google（默认）、Geometric、Breezy、LemonFlutter
- 动态背景场景：MaterialWeatherSceneControl 绘制渐变+装饰
- 图标系统：WeatherIconView + WeatherIconAssetResolver

---

## 设计方向

### 核心原则
1. **Material Design 3** - 使用 M3 的排版、颜色、间距和形状规范
2. **信息层级清晰** - 大字体温度、次要信息弱化
3. **呼吸感** - 合理的间距和留白
4. **深度感** - 前景卡片与背景场景分离
5. **圆角一致性** - 遵循 DesignCornerRadius 规范

### 参考风格
- **Google Weather** - 大字体温度、清晰层级、圆角卡片、柔和渐变
- **几何天气** - 几何装饰、现代感
- **Breez** - 清新留白、柔和色彩
- **柠檬天气** - 活泼明亮

---

## 具体改动计划

### Task 1: 优化 MaterialWeatherPalette 和调色板系统

**文件：** `LanMountainDesktop/Views/Components/MaterialWeatherVisualTheme.cs`

**改动：**
- 调整所有调色板的对比度，确保文字可读性
- 优化背景渐变色彩，更加柔和自然
- 统一文字主色和次色的对比度比例
- 为每个风格增加 `SurfaceColor` 和 `SurfaceVariantColor` 用于卡片背景

**当前调色板字段：**
```csharp
public sealed record MaterialWeatherPalette(
    Color BackgroundTop,
    Color BackgroundBottom,
    Color PrimaryShape,
    Color SecondaryShape,
    Color AccentShape,
    Color TextPrimary,
    Color TextSecondary,
    Color SurfaceTint,
    Color OverlayTint);
```

**新增字段：**
```csharp
    Color SurfaceColor,        // 卡片表面色（低透明度白色/黑色）
    Color SurfaceVariantColor, // 变体表面色
    Color OutlineColor         // 分割线/边框色
```

---

### Task 2: 重构 WeatherWidget（基础天气组件）

**文件：**
- `LanMountainDesktop/Views/Components/WeatherWidget.axaml`
- `LanMountainDesktop/Views/Components/WeatherWidget.axaml.cs`

**设计目标：**
- 大字体温度显示（类似 Google Weather）
- 天气状况文字清晰可读
- 位置和温度范围弱化显示
- 图标与文字对齐优化

**XAML 改动：**
```xml
<Border x:Name="RootBorder"
        CornerRadius="{DynamicResource DesignCornerRadiusComponent}"
        ClipToBounds="True">
    <Grid>
        <components:MaterialWeatherSceneControl x:Name="Scene" />
        <Border x:Name="OverlayBorder" />
        
        <!-- 主内容区 -->
        <Grid x:Name="ContentGrid" 
              RowDefinitions="*,Auto" 
              Margin="20,16,20,14">
            
            <!-- 上半区：温度 + 图标 -->
            <Grid ColumnDefinitions="*,Auto">
                <StackPanel VerticalAlignment="Center" Spacing="4">
                    <!-- 温度：超大字体 -->
                    <TextBlock x:Name="TemperatureTextBlock" 
                               Text="--°" 
                               FontSize="72" 
                               FontWeight="Bold"
                               LineHeight="72" />
                    <!-- 天气状况 -->
                    <TextBlock x:Name="ConditionTextBlock" 
                               Text="Loading" 
                               FontSize="18" 
                               FontWeight="SemiBold" 
                               TextTrimming="CharacterEllipsis" />
                </StackPanel>
                
                <!-- 右侧图标 -->
                <components:WeatherIconView x:Name="MainIcon" 
                                            Grid.Column="1" 
                                            Width="72" 
                                            Height="72" 
                                            HorizontalAlignment="Right"
                                            VerticalAlignment="Center" />
            </Grid>
            
            <!-- 底部信息栏 -->
            <Grid Grid.Row="1" ColumnDefinitions="*,Auto">
                <TextBlock x:Name="LocationTextBlock" 
                           Text="Weather" 
                           FontSize="13" 
                           FontWeight="Medium" 
                           TextTrimming="CharacterEllipsis"
                           VerticalAlignment="Bottom" />
                <TextBlock x:Name="RangeTextBlock" 
                           Grid.Column="1"
                           Text="-- / --" 
                           FontSize="13" 
                           FontWeight="Medium" 
                           HorizontalAlignment="Right"
                           VerticalAlignment="Bottom" />
            </Grid>
        </Grid>
    </Grid>
</Border>
```

**CS 改动：**
- 调整响应式布局的字体缩放比例
- 更新颜色绑定使用新的调色板字段

---

### Task 3: 重构 ExtendedWeatherWidget（扩展天气组件）

**文件：**
- `LanMountainDesktop/Views/Components/ExtendedWeatherWidget.axaml`
- `LanMountainDesktop/Views/Components/ExtendedWeatherWidget.axaml.cs`

**设计目标：**
- 顶部区域：位置+温度+图标横向排列
- 指标区域：使用 Material 3 风格的标签卡片
- 逐小时预报：水平滚动卡片，时间+图标+温度
- 逐日预报：列表式布局，日期+图标+高低温

**XAML 改动：**
```xml
<Border x:Name="RootBorder"
        CornerRadius="{DynamicResource DesignCornerRadiusComponent}"
        ClipToBounds="True">
    <Grid>
        <components:MaterialWeatherSceneControl x:Name="Scene" />
        <Border x:Name="OverlayBorder" />
        
        <Grid x:Name="ContentGrid" 
              RowDefinitions="Auto,Auto,Auto,Auto" 
              Margin="20,16,20,14" 
              RowSpacing="12">
            
            <!-- 顶部：位置 + 图标 + 温度 -->
            <Grid ColumnDefinitions="*,Auto,Auto" VerticalAlignment="Center">
                <StackPanel VerticalAlignment="Center">
                    <TextBlock x:Name="LocationTextBlock" 
                               Text="Weather" 
                               FontSize="13" 
                               FontWeight="Medium" 
                               TextTrimming="CharacterEllipsis"
                               Opacity="0.72" />
                    <TextBlock x:Name="ConditionTextBlock" 
                               Text="Loading" 
                               FontSize="16" 
                               FontWeight="SemiBold" 
                               TextTrimming="CharacterEllipsis" />
                </StackPanel>
                <components:WeatherIconView x:Name="MainIcon" 
                                            Grid.Column="1" 
                                            Width="56" 
                                            Height="56" 
                                            Margin="0,0,10,0" />
                <TextBlock x:Name="TemperatureTextBlock" 
                           Grid.Column="2" 
                           Text="--°" 
                           FontSize="56" 
                           FontWeight="Bold"
                           VerticalAlignment="Center" />
            </Grid>
            
            <!-- 指标区域 -->
            <UniformGrid x:Name="MetricGrid" Grid.Row="1" Rows="1" Columns="3" />
            
            <!-- 逐小时预报 -->
            <Border Grid.Row="2" 
                    Background="{DynamicResource SurfaceColor}" 
                    CornerRadius="{DynamicResource DesignCornerRadiusMd}"
                    Padding="10,8">
                <UniformGrid x:Name="HourlyGrid" Rows="1" Columns="6" />
            </Border>
            
            <!-- 逐日预报 -->
            <Border Grid.Row="3" 
                    Background="{DynamicResource SurfaceColor}" 
                    CornerRadius="{DynamicResource DesignCornerRadiusMd}"
                    Padding="10,8">
                <UniformGrid x:Name="DailyGrid" Rows="1" Columns="5" />
            </Border>
        </Grid>
    </Grid>
</Border>
```

**CS 改动：**
- `CreateMetric` 方法：使用圆角卡片，Material 3 风格标签
- `BuildHourlyItems` 方法：改进卡片样式，统一圆角
- `BuildDailyItems` 方法：改进卡片样式，统一圆角

---

### Task 4: 重构 HourlyWeatherWidget（逐小时天气组件）

**文件：**
- `LanMountainDesktop/Views/Components/HourlyWeatherWidget.axaml`
- `LanMountainDesktop/Views/Components/HourlyWeatherWidget.axaml.cs`

**设计目标：**
- 顶部简洁信息栏
- 逐小时预报使用 Material 卡片风格
- 时间、图标、温度垂直排列

**XAML 改动：**
```xml
<Border x:Name="RootBorder"
        CornerRadius="{DynamicResource DesignCornerRadiusComponent}"
        ClipToBounds="True">
    <Grid>
        <components:MaterialWeatherSceneControl x:Name="Scene" />
        <Border x:Name="OverlayBorder" />
        
        <Grid x:Name="ContentGrid" 
              RowDefinitions="Auto,*" 
              Margin="18,14" 
              RowSpacing="12">
            
            <!-- 顶部信息栏 -->
            <Grid ColumnDefinitions="Auto,*,Auto,Auto" VerticalAlignment="Center">
                <TextBlock x:Name="TemperatureTextBlock" 
                           Text="--°" 
                           FontSize="42" 
                           FontWeight="Bold" 
                           VerticalAlignment="Center" />
                <StackPanel Grid.Column="1" Margin="12,0,0,0" VerticalAlignment="Center">
                    <TextBlock x:Name="ConditionTextBlock" 
                               Text="Loading" 
                               FontSize="15" 
                               FontWeight="SemiBold" 
                               TextTrimming="CharacterEllipsis" />
                    <TextBlock x:Name="LocationTextBlock" 
                               Text="Weather" 
                               FontSize="12" 
                               FontWeight="Medium" 
                               Opacity="0.72" 
                               TextTrimming="CharacterEllipsis" />
                </StackPanel>
                <TextBlock x:Name="RangeTextBlock" 
                           Grid.Column="2" 
                           Text="-- / --" 
                           FontSize="12" 
                           FontWeight="Medium" 
                           VerticalAlignment="Center" 
                           Opacity="0.72" 
                           Margin="0,0,10,0" />
                <components:WeatherIconView x:Name="MainIcon" 
                                            Grid.Column="3" 
                                            Width="48" 
                                            Height="48" />
            </Grid>
            
            <!-- 逐小时预报卡片容器 -->
            <Border Grid.Row="1" 
                    Background="{DynamicResource SurfaceColor}" 
                    CornerRadius="{DynamicResource DesignCornerRadiusMd}"
                    Padding="8,6">
                <UniformGrid x:Name="HourlyGrid" Rows="1" Columns="6" />
            </Border>
        </Grid>
    </Grid>
</Border>
```

---

### Task 5: 重构 MultiDayWeatherWidget（多日天气组件）

**文件：**
- `LanMountainDesktop/Views/Components/MultiDayWeatherWidget.axaml`
- `LanMountainDesktop/Views/Components/MultiDayWeatherWidget.axaml.cs`

**设计目标：**
- 左侧：当前天气信息（图标+温度+状况+位置）
- 右侧：多日预报列表，使用行式布局

**XAML 改动：**
```xml
<Border x:Name="RootBorder"
        CornerRadius="{DynamicResource DesignCornerRadiusComponent}"
        ClipToBounds="True">
    <Grid>
        <components:MaterialWeatherSceneControl x:Name="Scene" />
        <Border x:Name="OverlayBorder" />
        
        <Grid x:Name="ContentGrid" 
              ColumnDefinitions="1.2*,1.6*" 
              Margin="18,14" 
              ColumnSpacing="14">
            
            <!-- 左侧当前天气 -->
            <StackPanel VerticalAlignment="Center" Spacing="6">
                <components:WeatherIconView x:Name="MainIcon" 
                                            Width="64" 
                                            Height="64" 
                                            HorizontalAlignment="Left" />
                <TextBlock x:Name="TemperatureTextBlock" 
                           Text="--°" 
                           FontSize="42" 
                           FontWeight="Bold" />
                <TextBlock x:Name="ConditionTextBlock" 
                           Text="Loading" 
                           FontSize="15" 
                           FontWeight="SemiBold" 
                           TextTrimming="CharacterEllipsis" />
                <TextBlock x:Name="LocationTextBlock" 
                           Text="Weather" 
                           FontSize="12" 
                           FontWeight="Medium" 
                           Opacity="0.72" 
                           TextTrimming="CharacterEllipsis" />
            </StackPanel>
            
            <!-- 右侧多日预报 -->
            <Border Grid.Column="1" 
                    Background="{DynamicResource SurfaceColor}" 
                    CornerRadius="{DynamicResource DesignCornerRadiusMd}"
                    Padding="10,8">
                <ItemsControl x:Name="DailyItemsControl" />
            </Border>
        </Grid>
    </Grid>
</Border>
```

---

### Task 6: 重构 WeatherClockWidget（天气时钟组件）

**文件：**
- `LanMountainDesktop/Views/Components/WeatherClockWidget.axaml`
- `LanMountainDesktop/Views/Components/WeatherClockWidget.axaml.cs`

**设计目标：**
- 左侧：大字体时间+日期
- 右侧：天气图标+温度+状况
- 信息层级清晰

**XAML 改动：**
```xml
<Border x:Name="RootBorder"
        CornerRadius="{DynamicResource DesignCornerRadiusComponent}"
        ClipToBounds="True">
    <Grid>
        <components:MaterialWeatherSceneControl x:Name="Scene" />
        <Border x:Name="OverlayBorder" />
        
        <Grid x:Name="ContentGrid" 
              ColumnDefinitions="*,Auto" 
              Margin="18,12" 
              ColumnSpacing="12">
            
            <!-- 左侧时间 -->
            <StackPanel VerticalAlignment="Center" Spacing="2">
                <TextBlock x:Name="TimeTextBlock" 
                           Text="--:--" 
                           FontSize="38" 
                           FontWeight="Bold"
                           LineHeight="38" />
                <TextBlock x:Name="DateTextBlock" 
                           Text="Weather" 
                           FontSize="12" 
                           FontWeight="Medium" 
                           Opacity="0.72" 
                           TextTrimming="CharacterEllipsis" />
            </StackPanel>
            
            <!-- 右侧天气 -->
            <StackPanel Grid.Column="1" 
                        VerticalAlignment="Center" 
                        HorizontalAlignment="Right" 
                        Spacing="1">
                <components:WeatherIconView x:Name="MainIcon" 
                                            Width="44" 
                                            Height="44" 
                                            HorizontalAlignment="Right" />
                <TextBlock x:Name="TemperatureTextBlock" 
                           Text="--°" 
                           FontSize="20" 
                           FontWeight="SemiBold" 
                           HorizontalAlignment="Right" />
                <TextBlock x:Name="ConditionTextBlock" 
                           Text="Loading" 
                           FontSize="11" 
                           FontWeight="Medium" 
                           HorizontalAlignment="Right" 
                           TextTrimming="CharacterEllipsis" 
                           MaxWidth="100" 
                           Opacity="0.82" />
            </StackPanel>
        </Grid>
    </Grid>
</Border>
```

---

### Task 7: 更新 ExtendedWeatherWidget 的代码后置文件

**文件：** `LanMountainDesktop/Views/Components/ExtendedWeatherWidget.axaml.cs`

**改动：**
- `CreateMetric` 方法改进：
  - 使用 `DesignCornerRadiusSm` 圆角
  - 使用新的 `SurfaceColor` 作为卡片背景
  - 优化字体大小和间距

- `BuildHourlyItems` 方法改进：
  - 使用 `DesignCornerRadiusSm` 圆角
  - 使用 `SurfaceColor` 作为卡片背景
  - 时间、图标、温度垂直排列，居中对齐

- `BuildDailyItems` 方法改进：
  - 使用 `DesignCornerRadiusSm` 圆角
  - 使用 `SurfaceColor` 作为卡片背景
  - 日期、图标、高低温垂直排列

---

### Task 8: 更新 HourlyWeatherWidget 的代码后置文件

**文件：** `LanMountainDesktop/Views/Components/HourlyWeatherWidget.axaml.cs`

**改动：**
- `CreateChip` 方法改进：
  - 使用 `DesignCornerRadiusSm` 圆角
  - 使用 `SurfaceColor` 作为卡片背景
  - 优化垂直排列的间距

---

### Task 9: 更新 MultiDayWeatherWidget 的代码后置文件

**文件：** `LanMountainDesktop/Views/Components/MultiDayWeatherWidget.axaml.cs`

**改动：**
- `CreateRow` 方法改进：
  - 添加底部分割线（除最后一行）
  - 优化列间距和对齐
  - 高低温使用不同透明度区分

---

### Task 10: 更新 MaterialWeatherVisualTheme 调色板

**文件：** `LanMountainDesktop/Views/Components/MaterialWeatherVisualTheme.cs`

**改动：**
- 为 `MaterialWeatherPalette` 添加新字段：
  - `SurfaceColor` - 用于卡片表面
  - `SurfaceVariantColor` - 用于变体表面
  - `OutlineColor` - 用于分割线

- 更新所有调色板生成方法：
  - `ResolveGooglePalette`
  - `ResolveGeometricPalette`
  - `ResolveBreezyPalette`
  - `ResolveLemonPalette`

- 每个调色板需要为白天/夜晚模式提供合适的 SurfaceColor：
  - 白天：低透明度白色（如 `#14FFFFFF`）
  - 夜晚：低透明度黑色（如 `#1A000000`）

---

### Task 11: 构建和测试

**命令：**
```bash
dotnet build LanMountainDesktop.slnx -c Debug
dotnet test LanMountainDesktop.slnx -c Debug
```

**验证清单：**
- [ ] 所有天气组件正常编译
- [ ] 运行时无异常
- [ ] 4套视觉风格正常切换
- [ ] 响应式布局正常工作
- [ ] 圆角资源正确应用

---

## 文件改动汇总

| 文件 | 改动类型 | 说明 |
|------|---------|------|
| `MaterialWeatherVisualTheme.cs` | 修改 | 添加 SurfaceColor 等字段，更新所有调色板 |
| `WeatherWidget.axaml` | 修改 | 重构布局，优化排版 |
| `WeatherWidget.axaml.cs` | 修改 | 调整响应式布局和颜色绑定 |
| `ExtendedWeatherWidget.axaml` | 修改 | 重构布局，添加卡片容器 |
| `ExtendedWeatherWidget.axaml.cs` | 修改 | 改进卡片创建方法 |
| `HourlyWeatherWidget.axaml` | 修改 | 重构布局，添加卡片容器 |
| `HourlyWeatherWidget.axaml.cs` | 修改 | 改进卡片创建方法 |
| `MultiDayWeatherWidget.axaml` | 修改 | 重构布局，添加卡片容器 |
| `MultiDayWeatherWidget.axaml.cs` | 修改 | 改进行创建方法 |
| `WeatherClockWidget.axaml` | 修改 | 重构布局，优化排版 |
| `WeatherClockWidget.axaml.cs` | 修改 | 调整响应式布局 |

---

## 设计规范检查清单

- [ ] 所有组件根容器使用 `DesignCornerRadiusComponent`
- [ ] 内部卡片使用 `DesignCornerRadiusMd` 或 `DesignCornerRadiusSm`
- [ ] 不使用硬编码圆角值
- [ ] 文字对比度符合 VISUAL_SPEC 要求
- [ ] 间距使用一致的倍数（4px 基线）
- [ ] 字体层级：温度(64-72px) > 状况(16-18px) > 位置/范围(12-13px)
