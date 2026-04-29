# Checklist

- [ ] `SettingsWindow.ApplyChromeMode()` 不再使用 `ExtendClientAreaChromeHints` 和 `SystemDecorations`
- [ ] `ComponentEditorWindow.ApplyChromeMode()` 不再使用 `ExtendClientAreaChromeHints` 和 `SystemDecorations`
- [ ] 所有 `.axaml` 文件中的 `SystemDecorations` 已替换为 `WindowDecorations`
- [ ] `MainWindow.ComponentSystem.cs` 中 `centerLeft` 和 `positions` 变量已正确定义
- [ ] `MainWindow.DesktopPaging.cs` 中 `child` 和 `_isThreeFingerOrRightDragSwipeActive` 变量已正确定义
- [ ] `App.axaml.cs` 中 `BindingPlugins.DataValidators` 代码已移除
- [ ] `DesktopComponentFailureView.cs` 使用 `ClipboardExtensions.SetTextAsync`
- [ ] `MonetColorService.cs` 使用正确的 `Bitmap.CopyPixels` 签名
- [ ] `SettingsWindow.axaml.cs` 使用 `FluentIcons.Avalonia.FluentIcon` 替代 `SymbolIconSource`
- [ ] 所有 `TextBox.Watermark` 已替换为 `PlaceholderText`
- [ ] `dotnet build LanMountainDesktop.slnx -c Debug` 0 errors, 0 warnings（过时 API 警告）
- [ ] `dotnet test LanMountainDesktop.slnx -c Debug` 全部通过
