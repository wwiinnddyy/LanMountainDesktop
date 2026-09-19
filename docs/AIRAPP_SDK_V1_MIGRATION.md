# AirApp SDK V1 迁移指南

> 从旧的插件系统（`LanMountainDesktop.PluginSdk` / `plugin.json` / API 5.0.0）迁移到统一的 AirApp 扩展系统（`LanMountainDesktop.AirAppSdk` / `airapp.json` / API 1.0.0）。

## 概览

AirApp 取代插件成为阑山桌面的统一扩展系统。旧插件不再被加载；请按本指南迁移。

**支持状态**：`LanMountainDesktop.PluginSdk`（4.x / 5.x）与 `plugin.json` 已停止支持。宿主不再识别
`plugin.json`，也不会加载基于 `IPlugin` / `PluginBase` / `[PluginEntrance]` 的程序集；`AirAppLoader`
里不存在任何回退到旧清单或旧入口契约的分支。`apiVersion` 的主版本必须等于 `AirAppSdkInfo.ApiVersion`
（当前 `1.0.0`），否则加载直接抛错——这也是仍停留在 PluginSdk 5.x 的轻应用在当前宿主上无法加载的原因。

代码中剩余的 `plugin` 字样哪些可改、哪些是有意保留，见 `docs/ai/NAMING_AND_FROZEN_IDENTIFIERS.md`。

| 项目 | 旧（插件系统） | 新（AirApp） |
|------|---------------|-------------|
| SDK 包 | `LanMountainDesktop.PluginSdk` | `LanMountainDesktop.AirAppSdk` |
| API 版本 | 5.0.0 | 1.0.0 |
| 清单文件 | `plugin.json` | `airapp.json` |
| 命名空间 | `LanMountainDesktop.PluginSdk` | `LanMountainDesktop.AirAppSdk` |
| 模板 | `dotnet new lmd-plugin` | `dotnet new lmd-airapp` |

## 关键 API 映射

| 旧 | 新 |
|----|----|
| `[PluginEntrance]` / `IPlugin` | `[AirAppEntrance]` / `IAirApp` |
| `PluginBase` | `AirAppBase` |
| `PluginManifest` | `AirAppManifest` |
| `IPluginRuntimeContext` | `IAirAppRuntimeContext` |
| `IPluginWorker` / `PluginWorkerBase` | `IAirAppWorker` / `AirAppWorkerBase` |
| `IPluginAppearanceContext` / `PluginAppearanceSnapshot` | `IAirAppAppearanceContext` / `AirAppAppearanceSnapshot` |
| `IPluginSettingsService` | `IAirAppSettingsService` |
| `PluginSettingsSectionBuilder` | `AirAppSettingsSectionBuilder` |
| `AddPluginDesktopComponent` | `AddAirAppComponent` |
| `AddPluginDesktopComponentEditor` | `AddAirAppComponentEditor` |
| `AddPluginSettingsSection` | `AddAirAppSettingsSection` |
| `AddPluginExport` | `AddAirAppExport` |
| `AddPluginPublicIpc` | `AddAirAppPublicIpc` |
| `IPluginMessageBus` | `IAirAppMessageBus` |
| `IPluginPackageManager` | `IAirAppPackageManager` |
| `PluginSdkInfo` | `AirAppSdkInfo` |

> 完整类型映射见代码库 `airapp/LanMountainDesktop.AirAppSdk/` 中的实际类型。

## 生命周期变化

`IAirApp` 在 `Initialize` 之外新增两个生命周期方法：

```csharp
[AirAppEntrance]
public sealed class MyAirApp : AirAppBase
{
    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        // 注册组件 / 窗口 / 设置
        services.AddAirAppComponent<MyComponent>(new AirAppComponentOptions
        {
            ComponentId = "my-component",
            DisplayName = "My Component"
        });
        services.AddAirAppWindow<MyWindow>("my-window", "My Window");
    }

    public override Task OnStartedAsync(IAirAppRuntimeContext context)
    {
        // 宿主启动完成后回调
        return Task.CompletedTask;
    }

    public override Task OnStoppingAsync()
    {
        // 宿主停止前回调
        return Task.CompletedTask;
    }
}
```

## plugin.json → airapp.json

```jsonc
// 旧 plugin.json
{
  "id": "com.example.myapp",
  "name": "My App",
  "apiVersion": "5.0.0",
  "entranceAssembly": "MyApp.dll",
  "runtime": { "mode": "in-proc" }
}
```

```jsonc
// 新 airapp.json
{
  "id": "com.example.myapp",
  "name": "My App",
  "apiVersion": "1.0.0",
  "entranceAssembly": "MyApp.dll",
  "runtime": { "mode": "in-process" },
  "components": [
    { "id": "my-component", "name": "My Component", "defaultWidth": 2, "defaultHeight": 2 }
  ],
  "windows": [
    { "id": "my-window", "name": "My Window" }
  ]
}
```

## 迁移步骤

1. 把 `plugin.json` 重命名为 `airapp.json`，字段按上表更新（`runtime.mode` 用 `in-process`，`apiVersion` 用 `1.0.0`，补充 `components` / `windows` 声明）。
2. 把 csproj 的 `PackageReference` 从 `LanMountainDesktop.PluginSdk` 改为 `LanMountainDesktop.AirAppSdk`（Version `1.0.0`），
   并加上 `ExcludeAssets="runtime"` `PrivateAssets="all"`。SDK 发布在 GitHub Packages，
   包源与认证配置见[环境准备](01-AirApp开发/01-快速开始/01-环境准备.md#配置包源)。
   同时删除工程里显式的 Avalonia 引用，版本交给 SDK 传递，否则会出现加载期类型冲突。
3. 全局替换命名空间与类型名（见 API 映射表）。
4. 在 `Initialize` 中改用 `AddAirAppComponent` / `AddAirAppWindow` / `AddAirAppSettingsSection`。
5. 把 `IPlugin.Initialize` 拆分为 `Initialize` + `OnStartedAsync` + `OnStoppingAsync`（如需要）。
6. 用 `dotnet new lmd-airapp` 生成新工程作参考，或直接升级现有工程。

## 容易静默出错的几处

这些改动不会让编译失败，但会让行为悄悄变错：

| 位置 | 旧 | 新 | 后果 |
|---|---|---|---|
| 宿主属性键 | `PluginSdkApiVersion` | `AirAppSdkApiVersion` | 只改常量名不改字面量时读不到值，回退到默认 |
| 宿主属性键 | `LanMountainDesktop.PluginDirectory` | `LanMountainDesktop.AirAppDirectory` | 本地化目录解析失败，全部文案回退 |
| 设置作用域 | `SettingsScope.Plugin` | `AirAppSettingsScope.AirApp` | 配置写到错误的作用域，升级后读不回来 |
| 设置页分类 | `SettingsPageCategory.Plugin` | `AirAppSettingsPageCategory.AirApps` | 设置页出现在错误的分组 |
| 组件尺寸 | `DefaultWidth` / `DefaultHeight` | `MinWidthCells` / `MinHeightCells` | 宿主无独立默认尺寸概念，Min 即初始尺寸 |
| 缩放模式 | `ResizeMode.Both` | 无此取值 | 宿主只有 `Proportional` 与 `Free` |

## 已移除的 API

下列类型在 AirApp SDK 1.0.0 中不存在，如果你参考过早期示例代码需要注意：

- `AirAppBase.RegisterComponent` / `RegisterWindow` / `RegisterService`
  —— 注册只能在 `Initialize` 中通过 `IServiceCollection` 完成
- `IAirAppWidget` / `AirAppWidgetBase` / `IAirAppComponentContext`
  —— 组件控件直接继承 Avalonia `Control`，上下文用 `AirAppComponentContext`
- `IAirAppWorker` / `AirAppWorkerBase` / `[AirAppWorkerEntrance]`
  —— 后台任务用标准的 `services.AddHostedService<T>()`
- `AddAirAppComponent<T>(id, name, Action<opts>)` 中的 `DefaultWidth` / `DefaultHeight` / `ResizeMode.Both`
  —— 见上表
