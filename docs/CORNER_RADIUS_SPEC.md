# 圆角设计规范

## 中文

本规范用于统一阑山桌面不同层级容器和控件的圆角尺度。

### 基础层级

- Level 1：12px，小元素和图标容器
- Level 2：16px，小型色块和紧凑控件
- Level 3：20px，普通按钮
- Level 4：24px，输入面板和小型容器
- Level 5：28px，普通玻璃面板
- Level 6：32px，强化容器
- Level 7：36px，大容器、窗口、任务栏

### 使用建议

- 同层级元素保持相同圆角。
- 大容器的圆角大于内部子面板。
- 动态尺寸组件可按 `cellSize` 计算圆角，但仍要落在统一范围内。

### 动态圆角建议

```csharp
var cornerRadius = Math.Clamp(cellSize * 0.45, 24, 44);
```

## English

This specification keeps corner radius usage consistent across containers and controls.

### Reference levels

- 12px for small elements
- 20px for common buttons
- 28px for normal glass panels
- 36px for large containers and windows
