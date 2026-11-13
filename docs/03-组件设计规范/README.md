# 组件设计规范

欢迎来到阑山桌面组件设计规范文档。本章节将帮助你设计出**美观、统一、专业**的桌面组件。

## 📐 设计哲学

阑山桌面的设计遵循以下核心原则：

- **🎨 现代简约** - 简洁的视觉语言，去除多余装饰
- **🌈 优雅融合** - 与 Windows 11 Fluent Design 无缝融合
- **🔄 一致体验** - 统一的视觉元素和交互模式
- **♿ 无障碍** - 易读、易用，支持不同用户需求
- **🌓 主题友好** - 完美适配亮色和暗色主题

## 📚 规范内容

### [0. 设计 Token 与资源真源](00-设计token真源.md) ⭐
- **键名与数值的唯一真源**（27 个 `Adaptive*` 键、圆角两档、动效时长、字体、表面类）
- 改任何视觉前先看这份

### [1. 设计系统概述](01-设计系统概述.md)
- 设计原则
- 设计语言
- 设计工作流

### [2. 视觉规范](02-视觉规范.md)
- 颜色系统
- 字体排版
- 图标规范
- 阴影与圆角
- 透明与模糊

### [3. 布局规范](03-布局规范.md)
- 安全区域 ⭐
- 间距系统
- 网格系统
- 组件尺寸标准
- 响应式布局

### [4. 交互规范](04-交互规范.md)
- 交互状态
- 动画与过渡
- 反馈机制
- 拖拽与调整

### [5. 主题系统](05-主题系统.md)
- 自适应主题模型
- 主题切换
- 图标与图片适配

## 🎯 快速开始

### 新手开发者

如果你是第一次设计桌面组件，建议按以下顺序学习：

1. ✅ **必读**: [布局规范](03-布局规范.md) - 理解安全区域和间距
2. ✅ **必读**: [视觉规范](02-视觉规范.md) - 掌握颜色和字体
3. 📖 **推荐**: [设计系统概述](01-设计系统概述.md) - 理解设计原则
4. 📖 **推荐**: [主题系统](05-主题系统.md) - 支持主题切换

### 经验开发者

如果你已有 UI 设计经验，可以：

1. 🔍 快速浏览 [设计系统概述](01-设计系统概述.md)
2. 📋 查阅 [布局规范](03-布局规范.md) 了解安全区域
3. 🎨 参考 [视觉规范](02-视觉规范.md) 使用设计资源
4. 💻 直接开始开发，遇到问题时查阅相关章节

## 📖 设计规范速查

### 核心尺寸

| 项目 | 值 | 说明 |
|-----|---|------|
| **最小组件宽度** | 120px | 保证可读性 |
| **最小组件高度** | 80px | 保证可用性 |
| **推荐组件宽度** | 200-400px | 常规信息展示 |
| **推荐组件高度** | 150-300px | 常规信息展示 |
| **安全边距** | 16px | 内容到边缘的最小距离 |
| **圆角半径** | `DesignCornerRadiusComponent` | 玻璃档 18px / 纯色回退档 8px，见[真源](00-设计token真源.md) |

### 核心颜色

**颜色不是固定色值**，而是由主题色系统从材质色/壁纸运行时派生的自适应画笔。用键名取用，不要写死颜色、也不要自造键名。

| 用途 | 资源键 |
|-----|--------|
| 表面（底 / 覆盖 / 抬升） | `AdaptiveSurfaceBaseBrush` / `AdaptiveSurfaceOverlayBrush` / `AdaptiveSurfaceRaisedBrush` |
| 主要文本 | `AdaptiveTextPrimaryBrush` |
| 次要文本 | `AdaptiveTextSecondaryBrush` |
| 强调色 | `AdaptiveAccentBrush` |
| 玻璃面板背景 / 边框 | `AdaptiveGlassPanelBackgroundBrush` / `AdaptiveGlassPanelBorderBrush` |

完整 27 个键见[设计 Token 与资源真源](00-设计token真源.md)。

### 核心字体

| 用途 | 字号 | 字重 |
|-----|------|------|
| **大标题** | 24px | SemiBold (600) |
| **标题** | 16-18px | SemiBold (600) |
| **正文** | 14px | Regular (400) |
| **辅助文字** | 12px | Regular (400) |
| **数字** | 32-48px | Bold (700) |

### 核心间距

| 场景 | 间距值 | 说明 |
|-----|--------|------|
| **安全边距** | 16px | 内容到组件边缘 |
| **元素间距** | 8px | 相关元素之间 |
| **区块间距** | 16px | 不同区块之间 |
| **紧密间距** | 4px | 标签、图标间距 |

## 🎨 设计资源

### 设计资源（AXAML）

系统通过自适应主题注册资源键（**不是**一份固定色值的 `ResourceDictionary`）：

```xml
<Border Background="{DynamicResource AdaptiveSurfaceRaisedBrush}"
        BorderBrush="{DynamicResource AdaptiveGlassPanelBorderBrush}"
        BorderThickness="1"
        CornerRadius="{DynamicResource DesignCornerRadiusComponent}">
  <TextBlock Text="Hello"
             Foreground="{DynamicResource AdaptiveTextPrimaryBrush}"
             FontSize="14"/>
</Border>
```

要点：

- 颜色键**只有** `Adaptive*` 一族（27 个），键名清单见[真源文档](00-设计token真源.md)。
- 圆角用 `DesignCornerRadiusComponent` 及其层级 Token；**没有** `DesignPaddingComponent`、`DesignFontFamilyBase` 这类键——内边距与字号直接写字面值。
- 字体家族由 `AppFontFamily`（MiSans VF）统一提供，窗口级已默认绑定。

## ✅ 设计检查清单

在发布组件前，请确保：

### 视觉检查
- [ ] 遵循 16px 安全边距
- [ ] 只用真源清单里的 `Adaptive*` 颜色键
- [ ] 字体沿用 `AppFontFamily`，字号在约定档位内
- [ ] 根容器圆角用 `DesignCornerRadiusComponent`，内部元素用层级 Token
- [ ] 没有对圆角资源乘 `scale`

### 主题检查
- [ ] 亮色主题下文字清晰可读
- [ ] 暗色主题下文字清晰可读
- [ ] 使用 `DynamicResource` 而非硬编码颜色
- [ ] 图标和图片适配主题

### 布局检查
- [ ] 最小宽度不小于 120px
- [ ] 最小高度不小于 80px
- [ ] 内容不会溢出边界
- [ ] 长文本正确换行或截断
- [ ] 响应式布局适配不同尺寸

### 交互检查
- [ ] 按钮有明确的悬停效果
- [ ] 可交互元素有视觉反馈
- [ ] 加载状态有明确提示
- [ ] 错误状态有清晰说明

### 性能检查
- [ ] 图片使用合适的分辨率
- [ ] 动画流畅不卡顿
- [ ] 更新频率合理
- [ ] 无内存泄漏

## 🔗 相关资源

### 内部文档
- [AirApp 开发 - 组件系统](../01-AirApp开发/02-核心概念/02-组件系统.md)
- [AirApp 开发 - 设置系统](../01-AirApp开发/02-核心概念/03-设置系统.md)
- [组件系统（架构）](../04-架构与实现/05-组件系统.md)

### 外部参考
- [Fluent Design System](https://www.microsoft.com/design/fluent/)
- [Windows 11 Design Principles](https://learn.microsoft.com/en-us/windows/apps/design/)
- [Avalonia UI Documentation](https://docs.avaloniaui.net/)

## 💡 设计建议

### 保持简洁
- 只展示最重要的信息
- 避免信息过载
- 使用清晰的视觉层级

### 注重细节
- 对齐每一个像素
- 统一间距和尺寸
- 保持视觉一致性

### 考虑场景
- 组件会在桌面长期显示
- 用户可能会添加多个组件
- 组件应该低调但有用

### 用户体验
- 信息一目了然
- 交互符合直觉
- 错误处理友好

---

**开始设计你的第一个组件**: [布局规范 - 安全区域](03-布局规范.md)
