# 04-外观API详解

外观 API 提供圆角、主题等视觉相关的功能。

---

## 🎯 IPluginAppearanceContext

```csharp
public interface IPluginAppearanceContext
{
    // 获取圆角值
    CornerRadius ResolveCornerRadius(PluginCornerRadiusPreset preset);
    
    // 获取带限制的圆角值
    CornerRadius ResolveCornerRadius(
        PluginCornerRadiusPreset preset,
        CornerRadius? minimum,
        CornerRadius? maximum);
    
    // 获取缩放后的圆角值
    CornerRadius ResolveScaledCornerRadius(
        double baseRadius,
        double? minimum,
        double? maximum);
    
    // 外观变化事件
    event EventHandler? AppearanceChanged;
}
```

---

## 📐 圆角 API

### 获取圆角值

```csharp
public MyWidget(PluginDesktopComponentContext context)
{
    // 使用预设
    CornerRadius = context.Appearance.ResolveCornerRadius(
        PluginCornerRadiusPreset.Component);
}
```

### 带限制的圆角

```csharp
var radius = context.Appearance.ResolveCornerRadius(
    PluginCornerRadiusPreset.Component,
    minimum: new CornerRadius(8),
    maximum: new CornerRadius(24));
```

### 缩放圆角

```csharp
var radius = context.Appearance.ResolveScaledCornerRadius(
    baseRadius: 16,
    minimum: 8,
    maximum: 32);
```

---

## 🎨 圆角预设

| 预设 | 值 | 用途 |
|-----|---|------|
| Micro | 6px | 微小元素 |
| Xs | 12px | 小元素 |
| Sm | 14px | 小卡片 |
| Md | 20px | 普通按钮 |
| Lg | 28px | 大面板 |
| Xl | 32px | 强调容器 |
| Island | 36px | 大型容器 |
| Component | 18px | 桌面组件 |
| Default | 自适应 | 自动计算 |

---

## 🔄 响应外观变化

```csharp
public MyWidget(PluginDesktopComponentContext context)
{
    context.Appearance.AppearanceChanged += (_, _) =>
    {
        // 重新应用圆角
        CornerRadius = context.Appearance.ResolveCornerRadius(
            PluginCornerRadiusPreset.Component);
    };
}
```

---

## 📚 参考资源

- [IPluginAppearanceContext 源码](../../LanMountainDesktop.PluginSdk/IPluginAppearanceContext.cs)
- [04-外观与主题系统](../02-核心概念与原理/04-外观与主题系统.md)

---

*最后更新：2026年4月*
