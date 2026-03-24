# 阑山桌面 / LanMountainDesktop

> 你的桌面，不止一面

[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com/)
[![Avalonia UI](https://img.shields.io/badge/Avalonia%20UI-11.2-blue)](https://avaloniaui.net/)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

> [!IMPORTANT]
> **温馨提示**：本项目有部分成分由**氛围编程 (Vibe Coding)** 方式编写。
>
> 如果您对此类项目有固有的排斥感，请无视此项目，谢谢。

## 简介

**阑山桌面**是一个跨平台桌面环境增强工具，面向需要高频查看信息、追求桌面效率与个性化体验的用户。

基于 Avalonia UI 和 .NET 10 构建，支持 Windows、Linux、macOS 三大平台。

![Platform](https://img.shields.io/badge/Windows-✓-0078D4)
![Platform](https://img.shields.io/badge/Linux-✓-FCC624?logo=linux&logoColor=black)
![Platform](https://img.shields.io/badge/macOS-✓-000000?logo=apple)

## 核心特性

### 📊 信息聚合
- 课程表、日历、天气、新闻、热搜
- 所有信息一目了然，无需频繁切换窗口

### 🎯 效率工具
- 自习环境监测、计时器、知识卡片
- 最近文档、浏览器快捷入口
- 常用工具组件一键触达

### 🎨 个性化桌面
- 自由布局，随心所欲摆放组件
- 多页桌面，工作学习场景分离
- 主题切换、玻璃效果、圆角风格

### 🔌 插件生态
- 通过 `.laapp` 插件扩展功能
- 官方 Plugin SDK 支持自定义组件
- 设置页、组件、集成功能一站式接入

## 为谁而设计

| 用户类型 | 典型场景 |
|---------|---------|
| 🎓 学生用户 | 课程表、自习监测、计时、天气和日常信息聚合 |
| 💼 办公用户 | 日历、资讯、最近文档、常用工具入口 |
| 🎨 效率爱好者 | 自由布局、主题切换、插件扩展 |
| 🇨🇳 中文用户 | 本地化界面、农历和节假日等本地语境支持 |

## 快速开始

### 环境要求
- .NET SDK 10

### 构建与运行

```bash
# 还原依赖
dotnet restore

# 构建项目
dotnet build LanMountainDesktop.slnx -c Debug

# 运行桌面宿主
dotnet run --project LanMountainDesktop/LanMountainDesktop.csproj
```

### 运行测试

```bash
dotnet test LanMountainDesktop.slnx -c Debug
```

## 插件开发

阑山桌面支持通过 Plugin SDK 开发自定义插件：

```bash
# 安装插件模板
dotnet new install LanMountainDesktop.PluginTemplate

# 创建新插件
dotnet new lmd-plugin -n MyPlugin
```

- **Plugin SDK**: `LanMountainDesktop.PluginSdk` (API 4.0.0)
- **共享契约**: `LanMountainDesktop.Shared.Contracts`
- **迁移指南**: [PLUGIN_SDK_V4_MIGRATION.md](docs/PLUGIN_SDK_V4_MIGRATION.md)

## 项目结构

```
LanMountainDesktop/
├── LanMountainDesktop/              # 桌面宿主应用
├── LanMountainDesktop.PluginSdk/    # 官方插件 SDK
├── LanMountainDesktop.Shared.Contracts/  # 宿主与插件共享契约
├── LanMountainDesktop.Appearance/   # 主题与外观基础设施
├── LanMountainDesktop.Settings.Core/# 设置持久化基础设施
└── LanMountainDesktop.Tests/        # 测试项目
```

## 生态边界

| 项目 | 职责 |
|-----|------|
| **本仓库** | 桌面宿主、插件运行时、Plugin SDK、共享契约 |
| [LanAirApp](https://github.com/yourorg/LanAirApp) | 插件市场元数据、开发者生态材料 |
| [LanMountainDesktop.SamplePlugin](https://github.com/yourorg/LanMountainDesktop.SamplePlugin) | 官方示例插件 |

## 文档索引

- [产品定位](docs/PRODUCT.md) - 产品愿景与目标用户
- [架构说明](docs/ARCHITECTURE.md) - 仓库结构与运行时主线
- [开发指南](docs/DEVELOPMENT.md) - 构建、测试、调试
- [视觉规范](docs/VISUAL_SPEC.md) - 主题、颜色、玻璃层级
- [圆角规范](docs/CORNER_RADIUS_SPEC.md) - 圆角层级与动态规则
- [贡献指南](docs/CONTRIBUTING.md) - PR、spec、文档协作规则

## 技术栈

- **UI 框架**: [Avalonia UI](https://avaloniaui.net/)
- **开发平台**: [.NET 10](https://dotnet.microsoft.com/)
- **支持平台**: Windows 10+, Linux, macOS

## 许可证

[MIT](LICENSE)


