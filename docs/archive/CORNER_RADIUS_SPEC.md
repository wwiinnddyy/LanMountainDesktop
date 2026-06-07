# 圆角设计规范 (LanMountain Desktop Corner Radius Spec)

## 核心理念 (Core Philosophy)

为了确保桌面组件在不同尺寸、缩放比例下都能保持视觉一致性和美感，阑山桌面采用了 **固定圆角风格预设 (Fixed Corner Radius Styles)**，全面参考小米澎湃OS (Xiaomi HyperOS) 的设计语言。

此外，在系统管理与控制面板等特定区域，阑山桌面引入了 **Fluent** 预设，完全遵循 Microsoft Fluent Design System 规范，以便与宿主操作系统的应用视觉保持一致。

所有的组件和容器必须使用统一的资源键，禁止在 XAML 或代码中使用硬编码的像素值。

## 预设风格 (Preset Styles)

用户可以在设置中选择以下五种风格之一。系统会自动根据选中的风格动态映射全局圆角 Token。

| 风格 (ID) | 名称 (Local) | 组件圆角 (Component) | 设计语义 |
| :--- | :--- | :--- | :--- |
| **Sharp** | 锐利 | 20px | 紧凑、精确、利落 |
| **Balanced** | 平衡 | 24px | **默认值**。和谐、自然、普适 |
| **Rounded** | 圆润 | 28px | 保守、柔和、亲切 |
| **Open** | 开放 | 32px | 现代、沉浸、夸张 |
| **Fluent** | Fluent | 8px | Microsoft Fluent Design System。标准、规范、一致 |

## Token 阶梯映射 (Token Step Mapping)

每个风格都定义了一套完整的圆角阶梯，以确保在大容器包裹小元素时满足 **圆角嵌套一致性 (Nesting Consistency)**。

| Token | Sharp | Balanced | Rounded | Open | Fluent | 典型场景 |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **Micro** | 4px | 6px | 8px | 10px | 2px | 小图标容器、角标 (Badge) |
| **Xs** | 8px | 12px | 14px | 16px | 4px | 小标签 (Tag)、输入框 |
| **Sm** | 10px | 14px | 16px | 20px | 4px | 普通按钮、搜索栏、复选框 |
| **Md** | 14px | 20px | 24px | 28px | 8px | 悬浮菜单、小提示框、子卡片 |
| **Lg** | 20px | 28px | 32px | 36px | 8px | 普通面板、对话框内容区 |
| **Xl** | 24px | 32px | 36px | 40px | 12px | 大尺寸容器、设置中心页面 |
| **Island** | 28px | 36px | 40px | 44px | 16px | 任务栏、全局大悬浮容器 |
| **Component** | **20px** | **24px** | **28px** | **32px** | **8px** | **所有桌面组件 (Widget) 的主边框** |

## 系统设计特例约束 (System Design Exceptions)

> [!IMPORTANT]
> **局部作用域隔离原则 (Scope Isolation)**
> 为了确保系统级配置面板、向导及管理界面的设计规范性，部分特例区域必须**始终使用 Microsoft Fluent Design System 预设**，不受用户在“外观设置 -> 全局圆角”中所选风格的影响：
> 
> 1. **设置窗口 (`SettingsWindow`)**：作为主配置中心，强制应用 Fluent 圆角，使其展现标准 Windows 应用的高级感与一致性。
> 2. **融合桌面组件库 (`FusedDesktopComponentLibraryWindow` / `FusedDesktopComponentLibraryControl`)**：小组件库的管理添加窗口本身属于系统级向导，强制采用 Fluent 圆角设计（如外壳圆角为 `DesignCornerRadiusLg`，内部按钮为 `DesignCornerRadiusSm`），保证交互的高级感与系统级管理界面对齐。
> 3. **系统弹出对话框 (`ContentDialog` / `FAContentDialog`)**：例如设置界面的重启确认、编辑桌面时的删除页面二级确认、电源菜单的二次确认等，通过全局 XAML 样式统一覆盖其所使用的 `OverlayCornerRadius` (8px)、`ControlCornerRadius` (4px) 以及相关的 `DesignCornerRadiusXxx` 令牌，以确保这些高优先级确认弹窗在任意窗口上层弹出时均保持 Fluent 风格。
> 4. **多开提示窗口 (`MultiInstancePromptWindow`)**：当多次启动软件时弹出的二级拦截警示窗口，属于独立启动器进程中的系统级安全提示，强制在 Window Resources 中硬编码重载为 Fluent 风格对应的圆角参数（如边角 8px，交互按钮 4px）。

### 实现机制 (Implementation Mechanism)

在上述特例窗口的初始化过程中，通过在其根网格/容器元素（如 `RootGrid`）下调用 `ApplyFluentCornerRadius()`，在局部作用域内覆盖所有的 `DesignCornerRadiusXxx` 资源键为 Fluent 阶梯对应的值：

```csharp
private void ApplyFluentCornerRadius()
{
    if (RootGrid is null) return;

    var tokens = AppearanceCornerRadiusTokenFactory.Create(
        GlobalAppearanceSettings.CornerRadiusStyleFluent);
    
    RootGrid.Resources["DesignCornerRadiusMicro"] = tokens.Micro;
    RootGrid.Resources["DesignCornerRadiusXs"] = tokens.Xs;
    RootGrid.Resources["DesignCornerRadiusSm"] = tokens.Sm;
    RootGrid.Resources["DesignCornerRadiusMd"] = tokens.Md;
    RootGrid.Resources["DesignCornerRadiusLg"] = tokens.Lg;
    RootGrid.Resources["DesignCornerRadiusXl"] = tokens.Xl;
    RootGrid.Resources["DesignCornerRadiusIsland"] = tokens.Island;
    RootGrid.Resources["DesignCornerRadiusComponent"] = tokens.Component;
}
```

这样使得所有内部子控件使用 `DynamicResource` 引用这些圆角资源时，解析到的都是隔离后且固定的 Fluent 设计弧度，实现不受全局用户偏好影响的精准渲染。

## 开发准则 (Implementation Rules)

> [!IMPORTANT]
> **1. 桌面组件强制约束**：
> 所有桌面普通组件（Widget / Desktop Component）的根容器边框在设计时，必须统一且仅使用 `{DynamicResource DesignCornerRadiusComponent}`。严禁对其进行任何比例运算或系数乘积（如 `* scale`），以确保用户的全局圆角缩放设置能被正确、成比例地应用。

> [!TIP]
> **2. 圆角嵌套规则**：
> 当一个容器包裹另一个元素时，外层圆角应比内层圆角大一个阶梯。例如：
> - 外部大容器使用 `DesignCornerRadiusLg`
> - 内部小卡片使用 `DesignCornerRadiusMd`
> - 内部紧贴边缘的小图标或按钮使用 `DesignCornerRadiusSm`
> 这样可以保证两条圆弧的圆心趋于重合，视觉重心更稳固。

> [!CAUTION]
> **3. 禁止硬编码 (No Hardcoding)**：
> 禁止写死数字（如 `CornerRadius="24"`）或私有资源。如果现有 Token 无法满足需求，应优先考虑使用 `SafeValue` 辅助方法封装，但必须声明理由。

## 常用资源键 (Common Resource Keys)

- `DesignCornerRadiusComponent` (桌面组件主框专用)
- `DesignCornerRadiusMicro`
- `DesignCornerRadiusSm`
- `DesignCornerRadiusMd`
- `DesignCornerRadiusLg`
- `DesignCornerRadiusXl`
