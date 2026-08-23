# 共享库跨平台依赖审计报告

> 审计目标：确认以下共享库可被 Android head（net10.0-android）安全引用。
> 审计方法：源码级扫描 `DllImport`/`LibraryImport`/`Microsoft.Win32`/`System.Drawing`/`WindowsRuntime`/`System.Management` + csproj 包引用检查。

## 结论总览

| 项目（合并后） | 状态 | 说明 |
|---|---|---|
| LanMountainDesktop.Core（含 Shared.Contracts / Shared.IPC / PluginPackaging） | ⚠️ 可编译，运行时受限 | Shared.IPC 依赖 dotnetCampus.Ipc（命名管道）。Android 上可编译但命名管道不可用，移动端必须走进程内传输 |
| LanMountainDesktop.PluginSdk（含 PluginIsolation / Host.Abstractions / Settings.Core / Appearance） | ⚠️ 需验证 | 引用 FluentAvaloniaUI / FluentIcons.Avalonia（ExcludeAssets=runtime，仅编译期）与 dotnetCampus.Ipc；Android 引用时需实测还原 |

> 注：本报告基于合并前的项目结构（Shared.Contracts、Shared.IPC、Settings.Core、Appearance、DesktopComponents.Runtime、Host.Abstractions、PluginIsolation.*），上述结论对合并后的 Core / PluginSdk 仍然成立。DesktopComponents.Runtime 为空壳项目，已删除。

## 详细发现

### 1. 源码扫描

合并后两个共享库（`LanMountainDesktop.Core`、`LanMountainDesktop.PluginSdk`）的 `.cs` 源码中均未发现：
- `DllImport` / `LibraryImport`（P/Invoke）
- `Microsoft.Win32`（注册表）
- `System.Drawing`
- `System.Runtime.WindowsRuntime`
- `System.Management`

平台专属代码全部集中在 `platform/LanMountainDesktop.Platform/`（原 Platform.Windows 的 Windows 实现，已从主工程迁出）。

### 2. dotnetCampus.Ipc（Core 的 Shared.IPC 部分、PluginSdk）

- 编译期：跨平台 net 库，Android 目标可编译。
- 运行期：传输基于命名管道，Android 不可用。
- 处置：移动端一律使用 PluginIsolation 的进程内传输（InProc transport）；
  Core 中的 IPC 在移动端应仅作为契约/类型来源，不建立管道连接。
- 后续（可选）：若希望编译期硬隔离，可把"管道传输"从 Core 拆到独立子项目，移动端不引用。当前不阻塞。

### 3. FluentAvaloniaUI / FluentIcons.Avalonia（PluginSdk）

- `ExcludeAssets="runtime"`，仅编译期类型引用。
- 两者均为纯托管 Avalonia 库，预期可在 Android 还原；由 Mobile head 构建实测确认。

### 4. 面向未来移动 AOT 的裁剪风险（仅报告，不修复）

- 主工程使用 YamlDotNet、反射式配置读取——不在本次共享库范围内，
  但如后续把相关逻辑下沉共享层，需加 `DynamicallyAccessedMembers` 注解或源生成序列化。
- PluginSdk 中合并自 Settings.Core 的代码若存在基于 `Activator.CreateInstance` 的设置实例化路径，
  迁移到移动端 AOT（未来 iOS）前需要审计；Android（JIT/interp 可用）不阻塞。

## 修复记录

本次审计未发现需要修改共享库源码的问题，未做代码改动。
