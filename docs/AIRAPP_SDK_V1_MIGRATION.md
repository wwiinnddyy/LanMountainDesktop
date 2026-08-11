# AirApp SDK V1 迁移指南

> 从旧的插件系统（`LanMountainDesktop.PluginSdk` / `plugin.json` / API 5.0.0）迁移到统一的 AirApp 扩展系统（`LanMountainDesktop.AirAppSdk` / `airapp.json` / API 1.0.0）。

## 概览

AirApp 取代插件成为阑山桌面的统一扩展系统。旧插件不再被加载；请按本指南迁移。

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
2. 把 csproj 的 `PackageReference` 从 `LanMountainDesktop.PluginSdk` 改为 `LanMountainDesktop.AirAppSdk`（Version `1.0.0`）。
3. 全局替换命名空间与类型名（见 API 映射表）。
4. 在 `Initialize` 中改用 `AddAirAppComponent` / `AddAirAppWindow` / `AddAirAppSettingsSection`。
5. 把 `IPlugin.Initialize` 拆分为 `Initialize` + `OnStartedAsync` + `OnStoppingAsync`（如需要）。
6. 用 `dotnet new lmd-airapp` 生成新工程作参考，或直接升级现有工程。
