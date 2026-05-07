# 数据设置页设计文档

## 概述

在设置窗口中新增「数据」设置页，用于可视化展示和管理阑山桌面产生的各类本地数据。采用 Fluent Design 风格的横向堆叠条形图展示存储分布。

## 设计目标

1. 让用户直观了解阑山桌面占用的存储空间
2. 提供各类数据的占比可视化
3. 支持按类别清理数据
4. 显示相对于磁盘总容量的占比

## 页面结构

### 存储概览区域

顶部一个卡片，包含：
- **横向堆叠条形图** — 各类数据用不同颜色的分段表示
- **总占用大小** — 阑山桌面数据总大小（如 "1.2 GB"）
- **磁盘占比** — 占总磁盘空间的百分比（如 "占 C 盘 0.5%"）
- **图例** — 各颜色对应的数据类型

### 数据类型详情列表

下方列表展示每类数据：
- 图标 + 名称
- 占用大小
- 描述/路径提示
- 「清理」按钮（如适用）

### 操作按钮

- 「刷新」— 重新扫描数据大小
- 「一键清理」— 清理所有可清理的数据

## 数据类型

| 类型 | 颜色 | 可清理 | 路径 |
|------|------|--------|------|
| 日志文件 | 灰色 | 是 | `log/` |
| 白板笔记 | 橙色 | 是（过期） | `Whiteboards/` |
| 插件数据 | 蓝色 | 是 | `Extensions/Plugins/` |
| 插件市场缓存 | 紫色 | 是 | `PluginMarket/` |
| 壁纸文件 | 粉色 | 是 | `Wallpapers/` |
| 设置文件 | 绿色 | 否 | `settings.json` |

## 技术实现

### 新增文件

- `LanMountainDesktop/Views/SettingsPages/DataSettingsPage.axaml` — 页面视图
- `LanMountainDesktop/Views/SettingsPages/DataSettingsPage.axaml.cs` — 页面代码隐藏
- `LanMountainDesktop/ViewModels/DataSettingsPageViewModel.cs` — 视图模型
- `LanMountainDesktop/Services/DataStorageService.cs` — 数据扫描服务

### 修改文件

- `LanMountainDesktop/Views/SettingsWindow.axaml.cs` — 图标映射（MapIcon）添加 Database 图标

### 设置页注册

```csharp
[SettingsPageInfo(
    "data",
    "Data",
    SettingsPageCategory.General,
    IconKey = "Database",
    SortOrder = 5,
    TitleLocalizationKey = "settings.data.title",
    DescriptionLocalizationKey = "settings.data.description")]
```

## 视觉设计

### 堆叠条形图

- 高度：24-32dp
- 圆角：使用 `DesignCornerRadiusSm`
- 分段间距：2dp
- 未占用空间：透明或浅色背景

### 颜色方案

使用 Material Design 颜色，与主题协调：
- 日志：Gray / BlueGray
- 白板：Orange / Amber
- 插件：Blue / Indigo
- 缓存：Purple / DeepPurple
- 壁纸：Pink
- 设置：Green / Teal

## 交互行为

1. 页面加载时自动扫描数据大小（异步）
2. 显示加载指示器
3. 清理操作需要确认对话框
4. 清理完成后自动刷新数据

## 安全考虑

- 清理前确认用户意图
- 设置文件不可清理（防止误删配置）
- 清理操作记录日志
