# 阑山桌面LanMountainDesktop

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

### 🔌 轻应用生态
- 通过 `.laapp` 轻应用扩展功能
- 官方 AirApp SDK 支持桌面组件与窗口轻应用
- 设置页、组件、窗口、集成功能一站式接入

## 为谁而设计

| 用户类型 | 典型场景 |
|---------|---------|
| 🎓 学生用户 | 课程表、自习监测、计时、天气和日常信息聚合 |
| 💼 办公用户 | 日历、资讯、最近文档、常用工具入口 |
| 🎨 效率爱好者 | 自由布局、主题切换、轻应用扩展 |
| 🇨🇳 中文用户 | 本地化界面、农历和节假日等本地语境支持 |

## 快速开始

### 环境要求
- .NET SDK 10

### 构建与运行

**开发模式 (推荐):**
```bash
# 还原依赖
dotnet restore

# 构建项目
dotnet build LanMountainDesktop.slnx -c Debug

# 直接运行主程序 (跳过 Launcher,快速开发)
dotnet run --project LanMountainDesktop/LanMountainDesktop.csproj
```

**生产模式 (完整流程):**
```bash
# 通过 Launcher 启动 (包含 OOBE、Splash、版本管理)
dotnet run --project LanMountainDesktop.Launcher/LanMountainDesktop.Launcher.csproj -- launch
```

详细说明请参考 [开发文档](docs/archive/DEVELOPMENT.md)。

### 运行测试

```bash
dotnet test LanMountainDesktop.slnx -c Debug
```

## 轻应用开发

阑山桌面通过统一的 AirApp SDK 支持轻应用扩展（桌面组件 + 窗口轻应用）：

```bash
# 安装轻应用模板
dotnet new install LanMountainDesktop.AirAppTemplate

# 创建新轻应用
dotnet new lmd-airapp -n MyAirApp
```

- **AirApp SDK**: `LanMountainDesktop.AirAppSdk` (API 1.0.0)
- **共享契约**: `LanMountainDesktop.Core`
- **迁移指南**: [AIRAPP_SDK_V1_MIGRATION.md](docs/AIRAPP_SDK_V1_MIGRATION.md)

## 项目结构

```
LanMountainDesktop/
├── core/                            # 共享基础：契约、IPC、打包
│   └── LanMountainDesktop.Core/
├── platform/                        # 平台差异层（接口 + Windows/macOS 实现）
│   └── LanMountainDesktop.Platform/
├── airapp/                          # AirApp 统一扩展系统
│   ├── LanMountainDesktop.AirAppSdk/     # 统一 SDK（组件 + 窗口轻应用）
│   ├── LanMountainDesktop.AirAppRuntime/ # 轻应用运行时进程
│   ├── LanMountainDesktop.AirAppHost/    # 轻应用窗口宿主进程
│   ├── LanMountainDesktop.AirAppTemplate/   # dotnet new lmd-airapp 模板
│   └── LanMountainDesktop.AirAppDevServer/  # 轻应用开发工具
├── desktop/                         # 桌面宿主
│   ├── LanMountainDesktop/          # 桌面宿主应用
│   └── LanMountainDesktop.Launcher/ # 启动器 (OOBE、Splash、版本管理、更新)
├── mobile/                          # 移动端壳（不参与桌面构建）
│   ├── LanMountainDesktop.Mobile/
│   └── LanMountainDesktop.Mobile.Android/
├── install/                         # 在线安装器
│   └── LanDesktopPLONDS.installer/
└── tests/                           # 测试项目
    └── LanMountainDesktop.Tests/
```

## 生态边界

| 项目 | 职责 |
|-----|------|
| **本仓库** | 桌面宿主、AirApp 运行时、AirApp SDK、共享契约 |
| [LanAirApp](https://github.com/yourorg/LanAirApp) | 轻应用市场元数据、开发者生态材料 |
| [LanMountainDesktop.SamplePlugin](https://github.com/yourorg/LanMountainDesktop.SamplePlugin) | 官方示例轻应用 |

## 文档索引

- [项目介绍](docs/00-快速开始/01-项目介绍.md) - 产品愿景与目标用户
- [整体架构](docs/04-架构与实现/01-整体架构.md) - 仓库结构与运行时主线
- [开发指南](docs/archive/DEVELOPMENT.md) - 构建、测试、调试
- [AirApp 开发指南](docs/01-AirApp开发/README.md) - 轻应用组件与窗口开发
- [AirApp 迁移指南](docs/AIRAPP_SDK_V1_MIGRATION.md) - 插件迁移到 AirApp
- [视觉规范](docs/03-组件设计规范/02-视觉规范.md) - 主题、颜色、玻璃层级
- [圆角规范](docs/archive/CORNER_RADIUS_SPEC.md) - 圆角层级与动态规则
- [贡献指南](docs/archive/CONTRIBUTING.md) - PR、spec、文档协作规则
- [跨平台架构](docs/CROSS_PLATFORM.md) - 桌面 + 移动分层约束
- [技术文档总览](docs/README.md) - 完整文档导航

## 技术栈

- **UI 框架**: [Avalonia UI](https://avaloniaui.net/)
- **开发平台**: [.NET 10](https://dotnet.microsoft.com/)
- **支持平台**: Windows 10+, Linux, macOS

## 许可证

[MIT](LICENSE)


