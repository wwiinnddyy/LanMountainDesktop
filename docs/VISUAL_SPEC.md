# 视觉规范

## 中文

本规范用于统一阑山桌面的主题色、玻璃效果和基础视觉语义。

### 颜色角色

- `Primary`：品牌主色
- `Secondary`：辅助色
- `Accent`：强调色与选中态主色
- `OnAccent`：强调色背景上的文字或图标
- `SurfaceBase` / `SurfaceRaised` / `SurfaceOverlay`：背景层级
- `TextPrimary` / `TextSecondary` / `TextMuted` / `TextAccent`：文本层级

### 使用规则

- 主按钮和主要导航选中态使用 `Accent + OnAccent`
- 次级操作和输入控件优先使用语义背景色，不直接写死颜色
- 页面层只使用资源键和语义类名，不写业务颜色常量

### 玻璃效果层级

- `glass-overlay`：最外层遮罩
- `glass-strong`：主要大容器
- `glass-panel`：子区域、小面板、卡片

### 可访问性

- 正文对比度目标不低于 `4.5:1`
- 大号文字和重点文字不低于 `3.0:1`
- 主题服务负责对前景色做自动对比度修正

## English

This specification defines the visual language of LanMountainDesktop, including theme roles, glass layers, and semantic color usage.

### Key rules

- use semantic resource keys instead of hard-coded colors
- keep glass layers visually distinct
- maintain contrast targets for readability
