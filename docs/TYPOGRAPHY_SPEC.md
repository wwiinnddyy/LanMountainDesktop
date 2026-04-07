# 字体排版设计规范 (Typography Specification)

## 中文

本规范用于统一阑山桌面各组件（Widget）及页面的字体样式，解决目前组件间字体不协调、厚度不一的问题。通过引入标准化的设计 Token，确保在不同 DPI 和设备上呈现一致的高级感（Premium Look）。

### 1. 字体家族 (Font Family)

- **默认字体**：优先使用内置的 `MiSans VF` (Variable Font)。
- **回退顺序**：`MiSans VF` -> `MiSans` -> `Microsoft YaHei` -> `Sans-serif`。

### 2. 字重标准 (Font Weights)

为了达到“不粗不细”的协调感，我们采用 `Medium (500)` 作为默认正文字重，以应对复杂的背景环境。

| 角色 | Token | MiSans 权重 | 说明 |
| --- | --- | --- | --- |
| **Caption/Secondary** | `DesignFontWeightCaption` | `Normal (400)` | 用于不重要的补充说明信息 |
| **Body (Default)** | `DesignFontWeightBody` | `Medium (500)` | **核心全局字重**，用于所有常规正文 |
| **Title/Header** | `DesignFontWeightTitle` | `SemiBold (600)` | 用于卡片标题、分类标题 |
| **Display (Large)** | `DesignFontWeightDisplay` | `SemiBold (600)` | 用于超大号文本（如温度数字） |

> **注意**：除非极特殊艺术需求，应避免使用 `Thin`, `ExtraLight`, `Light` 或 `Bold (700)`, `Heavy`。

### 3. 字号标准 (Font Sizes)

| 角色 | Token | 数值 (px) | 典型应用场景 |
| --- | --- | --- | --- |
| **Caption** | `DesignFontSizeCaption` | 12 | 底部说明、状态提示 |
| **BodySmall** | `DesignFontSizeBodySmall` | 13 | 设置项描述、次要标签 |
| **Body** | `DesignFontSizeBody` | 14 | 标准文本、正文内容 |
| **BodyLarge** | `DesignFontSizeBodyLarge` | 16 | 加大正文、菜单项 |
| **Subtitle** | `DesignFontSizeSubtitle` | 18 | 小节标题、大按钮文字 |
| **Title** | `DesignFontSizeTitle` | 24 | 组件标题、大卡片标题 |
| **Headline** | `DesignFontSizeHeadline` | 32 | 重要数据指标 |
| **Display** | `DesignFontSizeDisplay` | 48 | 天气温度、时间分钟 |
| **DisplayLarge** | `DesignFontSizeDisplayLarge` | 54 | 诗词正文、欢迎语 |

### 4. 行高标准 (Line Heights)

统一行高可以增强视觉节奏感。

| Token | 数值 (倍率) | 应用场景 |
| --- | --- | --- |
| `DesignLineHeightStandard` | 1.2 | 单行标签、紧凑卡片 |
| `DesignLineHeightLoose` | 1.5 | 多行诗词、新闻摘要、说明文档 |

### 5. 使用规范

1. **禁止硬编码**：严禁在 `.axaml` 中直接写入 `FontSize="18"` 或 `FontWeight="Bold"`。
2. **动态资源绑定**：始终使用 `{DynamicResource DesignFontSize...}` 进行绑定。
3. **全局样式继承**：`App.axaml` 已经设置了 `TextBlock` 的默认 `FontWeight` 为 `Medium`，除非是 `Caption` 或 `Title`，否则无需重复声明。

---

## English (Summary)

- **Default Font**: MiSans VF.
- **Base Weight**: `Medium (500)` for better readability on glass/dark backgrounds.
- **Header Weight**: `SemiBold (600)` for a modern premium feel.
- **Line Height**: Standardized to 1.2x and 1.5x.
- **Tokens**: All components must use `DesignFontSize...` and `DesignFontWeight...` resource keys.
