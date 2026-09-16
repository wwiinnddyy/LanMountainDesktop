# __AIRAPP_NAME__

由阑山桌面官方模板生成的轻应用（AirApp）工程。

## 构建

```powershell
dotnet build -c Release
```

`LanMountainDesktop.AirAppSdk` 的 MSBuild targets 会自动产出：

- 构建输出：`bin/<Configuration>/<TFM>/`
- 轻应用包：项目根目录下的 `<AssemblyName>.<Version>.laapp`

打包会强制校验 `airapp.json` 与 `.deps.json` 存在，缺失时构建直接失败，
避免产出宿主无法加载的包。

## 清单

发布前按需修改 `airapp.json`：

- `id`：全局唯一，与市场条目一致
- `name` / `description` / `author`
- `version`：轻应用自身版本
- `apiVersion`：所用 SDK 的 API 版本，宿主按**主版本号**匹配，不要随意改
- `entranceAssembly`：入口程序集文件名，重命名项目后必须同步
- `components[]` / `windows[]`：声明本轻应用提供的组件与窗口

## 代码结构

| 文件 | 作用 |
|---|---|
| `AirApp.cs` | 入口类，带 `[AirAppEntrance]`，在 `Initialize` 中注册组件与窗口 |
| `MyComponent.cs` | 桌面组件，构造函数可注入 `AirAppComponentContext` |
| `MyWindow.cs` | 窗口轻应用，由独立的 AirAppHost 进程承载 |
| `Localization/*.json` | 多语言文案，通过 `AirAppLocalizer` 读取 |

注册只发生在 `Initialize(HostBuilderContext, IServiceCollection)` 里，
使用 `services.AddAirAppComponent<T>(...)` 与 `services.AddAirAppWindow<T>(...)`。
`OnStartedAsync` 用于拿到运行时上下文做初始化，`OnStoppingAsync` 用于清理。

## 本地调试

把构建输出目录或 `.laapp` 放到宿主的轻应用目录：

```
%LOCALAPPDATA%\LanMountainDesktop\Extensions\AirApps\
```

或用宿主的 `--dev-airapp <路径>` 参数直接指向本工程的构建输出，
再附加调试器到 `LanMountainDesktop.exe`。
