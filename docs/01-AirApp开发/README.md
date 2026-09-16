# 轻应用（AirApp）开发指南

阑山桌面的扩展形态只有一种：**AirApp（轻应用）**。
桌面组件和窗口轻应用都由同一个 SDK 提供，不存在第二套插件 API。

> **重要**：旧的 `LanMountainDesktop.PluginSdk`（4.x / 5.x）与 `plugin.json`
> 已经停止支持，宿主不再识别 `plugin.json`，也不会加载基于 `IPlugin` / `PluginBase`
> 的程序集。已有插件请参考 [AirApp SDK 迁移指南](../AIRAPP_SDK_V1_MIGRATION.md)。

## 唯一基线

| 项 | 值 |
|---|---|
| SDK 包 | `LanMountainDesktop.AirAppSdk` |
| SDK / API 版本 | `1.0.0` |
| 传递依赖 | `LanMountainDesktop.Core` `1.0.0` |
| 清单文件 | `airapp.json` |
| 包格式 | `.laapp` |
| 目标框架 | `net10.0` |
| 模板 | `dotnet new lmd-airapp` |
| 包源 | GitHub Packages（需认证，见[环境准备](01-快速开始/01-环境准备.md)） |

宿主按 **API 主版本号** 匹配轻应用：`airapp.json` 中 `apiVersion` 的主版本
必须与宿主 SDK 的主版本相同，否则加载与市场安装都会被拒绝。

## 一个轻应用能做什么

- 提供**桌面组件**，在宿主主进程内渲染，可放置到桌面网格
- 提供**窗口轻应用**，由独立的 AirAppHost 进程承载，与宿主崩溃隔离
- 注册**设置页**，声明式生成或提供自定义 AXAML 视图
- 运行**后台服务**（标准 `IHostedService`）
- 通过**消息总线**与其他轻应用通信
- 通过**导出契约**向其他轻应用暴露强类型服务
- 通过**公共 IPC**向宿主外部进程提供服务

## 文档

### 快速开始

1. [环境准备](01-快速开始/01-环境准备.md) —— .NET SDK、包源认证、安装模板
2. [创建第一个轻应用](01-快速开始/02-创建第一个轻应用.md) —— 从模板到装进宿主

### 核心概念

1. [轻应用生命周期](02-核心概念/01-轻应用生命周期.md) —— 发现、加载、启动、停止
2. [组件系统](02-核心概念/02-组件系统.md) —— 注册桌面组件与组件上下文
3. [设置系统](02-核心概念/03-设置系统.md) —— 声明式设置与自定义设置页

### API 参考

1. [IAirApp 接口](03-API参考/01-IAirApp接口.md) —— 入口契约与注册扩展方法
2. [IAirAppRuntimeContext](03-API参考/02-IAirAppRuntimeContext.md) —— 运行时上下文

### 相关文档

- [AirApp 运行时架构](../02-AirApp运行时/README.md) —— 宿主、Runtime、AirAppHost 三进程拓扑
- [AirApp SDK 迁移指南](../AIRAPP_SDK_V1_MIGRATION.md) —— 从 PluginSdk 迁移
- [组件设计规范](../03-组件设计规范/README.md) —— 视觉、布局、圆角规范

## 架构速览

```
┌──────────────────────────────────────────────┐
│ LanMountainDesktop（宿主主进程）                │
│  ├─ 组件系统：渲染 AirApp 贡献的桌面组件          │
│  ├─ 设置窗口：渲染 AirApp 贡献的设置页            │
│  └─ AirAppLoader：按 airapp.json 加载程序集      │
│        每个轻应用一个可回收 AssemblyLoadContext   │
└───────────────┬──────────────────────────────┘
                │ 打开窗口请求（IPC）
┌───────────────▼──────────────┐
│ AirAppRuntime（独立进程）       │  实例去重、生命周期协调
└───────────────┬──────────────┘
                │ 启动/激活
┌───────────────▼──────────────┐
│ AirAppHost（每实例一个进程）     │  渲染窗口轻应用内容
└──────────────────────────────┘
```

桌面组件运行在宿主进程内，窗口轻应用运行在独立进程中。
清单里的 `runtime.mode` 目前仅 `in-proc`（等价写法 `in-process`）被宿主实际执行，
`isolated-background` / `isolated-window` 属于预留取值，声明后不改变当前加载行为。
