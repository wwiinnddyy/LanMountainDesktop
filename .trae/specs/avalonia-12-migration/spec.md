# Avalonia 12 迁移规格

## Why

Avalonia 12 带来性能改进（SkiaSharp 3.0、编译绑定默认开启）、新的窗口装饰体系（WindowDrawnDecorations）和更简洁的 API 设计。项目当前已升级包引用，但存在 18 个编译错误和若干过时 API 警告，需要系统性修复以确保构建通过。

## What Changes

- **BREAKING**: 移除 `ExtendClientAreaChromeHints` 和 `SystemDecorations` 的使用，迁移到 `WindowDecorations`
- **BREAKING**: 移除 `BindingPlugins.DataValidators` 的使用（v12 已移除绑定插件体系）
- **BREAKING**: 替换 `IClipboard.SetTextAsync` 为 `ClipboardExtensions.SetTextAsync`
- **BREAKING**: 更新 `Bitmap.CopyPixels` 调用签名（移除 `AlphaFormat` 参数）
- **BREAKING**: 替换 `FluentIcons.Avalonia.SymbolIconSource` 为 v3 等效 API
- 修复 `MainWindow.ComponentSystem.cs` 和 `MainWindow.DesktopPaging.cs` 中缺失的字段/变量
- 批量替换 `TextBox.Watermark` → `PlaceholderText`

## Impact

- 受影响代码：
  - `LanMountainDesktop/Views/SettingsWindow.axaml.cs`
  - `LanMountainDesktop/Views/ComponentEditorWindow.axaml.cs`
  - `LanMountainDesktop/Views/MainWindow.ComponentSystem.cs`
  - `LanMountainDesktop/Views/MainWindow.DesktopPaging.cs`
  - `LanMountainDesktop/App.axaml.cs`
  - `LanMountainDesktop/Views/Components/DesktopComponentFailureView.cs`
  - `LanMountainDesktop/Services/MonetColorService.cs`
  - 13 个 `.axaml` 文件（`SystemDecorations` → `WindowDecorations`）
  - 7 个 `.cs` 文件 + 7 个 `.axaml` 文件（`Watermark` → `PlaceholderText`）
- 受影响规格：无现有规格直接关联

## ADDED Requirements

### Requirement: 窗口装饰 API 迁移
系统 SHALL 使用 Avalonia 12 的 `WindowDecorations` 属性替代已移除的 `SystemDecorations` 和 `ExtendClientAreaChromeHints`。

#### Scenario: SettingsWindow 无边框模式
- **WHEN** `ApplyChromeMode(false)` 被调用
- **THEN** `WindowDecorations = WindowDecorations.BorderOnly` 且 `ExtendClientAreaToDecorationsHint = true`

#### Scenario: SettingsWindow 系统 Chrome 模式
- **WHEN** `ApplyChromeMode(true)` 被调用
- **THEN** `WindowDecorations = WindowDecorations.Full` 且 `ExtendClientAreaToDecorationsHint = true`

### Requirement: 剪贴板 API 迁移
系统 SHALL 使用 Avalonia 12 的 `ClipboardExtensions.SetTextAsync` 替代已移除的 `IClipboard.SetTextAsync`。

### Requirement: Bitmap.CopyPixels 签名更新
系统 SHALL 使用新的 `CopyPixels` 签名，不再传入 `AlphaFormat` 参数。

### Requirement: FluentIcons v3 API 适配
系统 SHALL 使用 `FluentIcons.Avalonia.FluentIcon` 替代已移除的 `SymbolIconSource`。

## MODIFIED Requirements

### Requirement: 编译绑定验证
- **修改前**：`BindingPlugins.DataValidators.RemoveAt(0)` 移除默认数据注解验证插件
- **修改后**：v12 默认禁用数据注解验证插件，无需手动移除

## REMOVED Requirements

### Requirement: ExtendClientAreaChromeHints 配置
**Reason**: Avalonia 12 移除此属性，由 `WindowDecorations` 统一管理
**Migration**: 删除所有 `ExtendClientAreaChromeHints` 赋值代码
