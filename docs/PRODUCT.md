# 产品文档 / Product

## 中文

### 产品一句话

阑山桌面——你的桌面，不止一面。

### 产品定位

- 产品类型：跨平台桌面环境增强工具
- 技术基线：Avalonia UI + .NET 10
- 支持平台：Windows、Linux、macOS
- 仓库角色：本仓库是桌面宿主、插件运行时、Plugin SDK 与共享契约的权威来源

### 目标用户

- 学生用户：课程表、自习监测、计时、天气和日常信息聚合
- 办公用户：日历、资讯、最近文档、常用工具入口
- 效率和美化爱好者：自由布局、主题切换、插件扩展
- 中文用户：本地化界面、农历和节假日等本地语境支持

### 核心使用场景

- 学习辅助：课程表、自习环境监测、计时与知识卡片
- 信息聚合：天气、新闻、日历、热搜等信息集中展示
- 效率提升：最近文档、浏览器、工具组件与桌面快捷访问
- 个性化桌面：自由布局、多页桌面、主题与视觉风格配置
- 插件扩展：通过 `.laapp` 插件补充新的组件、设置页和集成功能

### 核心能力

- 桌面组件系统：内置组件与扩展组件统一注册、统一放置约束
- 插件系统：宿主加载插件、整合设置页、组件与市场安装流
- 外观系统：主题、玻璃层级、圆角与颜色资源统一管理
- 设置系统：独立设置窗口、设置页注册与分域持久化
- 跨平台运行：基于 Avalonia 的桌面宿主运行在 Windows、Linux、macOS

### 当前阶段

- 产品版本：`1.0.0`
- Plugin SDK API 基线：`4.0.0`
- 当前重点：持续完善宿主体验、设置页体验、组件能力与插件生态
- 近期需求入口：以 `.trae/specs/` 中的 feature spec 为准

### 生态边界

- 本仓库负责：宿主代码、插件运行时、SDK、共享契约、主题与设置基础设施
- `LanAirApp` 负责：插件市场元数据、开发者生态材料
- `LanMountainDesktop.SamplePlugin` 负责：官方示例插件实现

### 维护原则

- 产品事实只在本文件沉淀，不在多个根目录文档重复维护
- 代码结构和运行方式分别以 `docs/ARCHITECTURE.md` 与 `docs/DEVELOPMENT.md` 为准
- 专题规范以 `docs/VISUAL_SPEC.md`、`docs/CORNER_RADIUS_SPEC.md` 等专题文档为准

## English

LanMountainDesktop is a cross-platform desktop enhancement product built with Avalonia UI and .NET 10. It targets students, office users, and customization-focused users who want a programmable desktop surface for information, tools, and plugin-driven extensions.

This repository is the source of truth for the desktop host, plugin runtime, Plugin SDK, shared contracts, and core appearance/settings infrastructure. The current product version is `1.0.0`, and the active Plugin SDK baseline in this repository is `4.0.0`.
