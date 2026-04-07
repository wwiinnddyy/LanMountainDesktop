# 圆角设计规范 (LanMountain Desktop Corner Radius Spec)

## 核心理念 (Core Philosophy)

为了确保桌面组件在不同尺寸、缩放比例下都能保持视觉一致性和美感，阑山桌面采用了 **固定圆角风格预设 (Fixed Corner Radius Styles)**，全面参考小米澎湃OS (Xiaomi HyperOS) 的设计语言。

所有的组件和容器必须使用统一的资源键，禁止在 XAML 或代码中使用硬编码的像素值。

## 预设风格 (Preset Styles)

用户可以在设置中选择以下四种风格之一。系统会自动根据选中的风格动态映射全局圆角 Token。

| 风格 (ID) | 名称 (Local) | 组件圆角 (Component) | 设计语义 |
| :--- | :--- | :--- | :--- |
| **Sharp** | 锐利 | 20px | 紧凑、精确、利落 |
| **Balanced** | 平衡 | 24px | **默认值**。和谐、自然、普适 |
| **Rounded** | 圆润 | 28px | 保守、柔和、亲切 |
| **Open** | 开放 | 32px | 现代、沉浸、夸张 |

## Token 阶梯映射 (Token Step Mapping)

每个风格都定义了一套完整的圆角阶梯，以确保在大容器包裹小元素时满足 **圆角嵌套一致性 (Nesting Consistency)**。

| Token | Sharp | Balanced | Rounded | Open | 典型场景 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Micro** | 4px | 6px | 8px | 10px | 小图标容器、角标 (Badge) |
| **Xs** | 8px | 12px | 14px | 16px | 小标签 (Tag)、输入框 |
| **Sm** | 10px | 14px | 16px | 20px | 普通按钮、搜索栏、复选框 |
| **Md** | 14px | 20px | 24px | 28px | 悬浮菜单、小提示框、子卡片 |
| **Lg** | 20px | 28px | 32px | 36px | 普通面板、对话框内容区 |
| **Xl** | 24px | 32px | 36px | 40px | 大尺寸容器、设置中心页面 |
| **Island** | 28px | 36px | 40px | 44px | 任务栏、全局大悬浮容器 |
| **Component** | **20px** | **24px** | **28px** | **32px** | **所有桌面组件 (Widget) 的主边框** |

## 开发准则 (Implementation Rules)

> [!IMPORTANT]
> **1. 桌面组件强制约束**：
> 所有桌面组件（Widget / Desktop Component）的根容器边框必须使用 `{DynamicResource DesignCornerRadiusComponent}`。严禁对其进行任何比例运算或系数乘积（如 `* scale`），必须保持固定。

> [!TIP]
> **2. 圆角嵌套规则**：
> 当一个容器包裹另一个元素时，外层圆角应比内层圆角大一个阶梯。例如：
> - 外部使用 `DesignCornerRadiusLg`
> - 内部紧贴边缘的内容应使用 `DesignCornerRadiusMd`
> 这样可以保证两条圆弧的圆心趋于重合，视觉重心更稳固。

> [!CAUTION]
> **3. 禁止硬编码 (No Hardcoding)**：
> 禁止写死数字（如 `CornerRadius="24"`）或私有资源。如果现有 Token 无法满足需求，应优先考虑使用 `SafeValue` 辅助方法封装，但必须声明理由。

## 常用资源键 (Common Resource Keys)

- `DesignCornerRadiusComponent` (最常用)
- `DesignCornerRadiusMicro`
- `DesignCornerRadiusSm`
- `DesignCornerRadiusMd`
- `DesignCornerRadiusLg`
- `DesignCornerRadiusXl`
