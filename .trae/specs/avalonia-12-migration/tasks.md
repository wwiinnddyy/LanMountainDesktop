# Tasks

- [ ] Task 1: 修复窗口装饰 API（Phase 1）
  - [x] SubTask 1.1: 重写 `SettingsWindow.ApplyChromeMode()` 移除 `ExtendClientAreaChromeHints`
  - [x] SubTask 1.2: 重写 `ComponentEditorWindow.ApplyChromeMode()` 移除 `ExtendClientAreaChromeHints`
  - [x] SubTask 1.3: 批量替换所有 `.axaml` 中的 `SystemDecorations` → `WindowDecorations`
  - [ ] SubTask 1.4: 验证构建错误减少

- [ ] Task 2: 修复 MainWindow 编译错误（Phase 2）
  - [ ] SubTask 2.1: 修复 `MainWindow.ComponentSystem.cs` 中 `centerLeft` 和 `positions` 未定义错误
  - [ ] SubTask 2.2: 修复 `MainWindow.DesktopPaging.cs` 中 `child` 和 `_isThreeFingerOrRightDragSwipeActive` 未定义错误
  - [ ] SubTask 2.3: 验证构建错误减少

- [ ] Task 3: 修复 Avalonia 12 API 变更（Phase 3）
  - [ ] SubTask 3.1: 移除 `App.axaml.cs` 中 `BindingPlugins.DataValidators` 代码
  - [ ] SubTask 3.2: 替换 `DesktopComponentFailureView.cs` 中 `IClipboard.SetTextAsync` 为 `ClipboardExtensions.SetTextAsync`
  - [ ] SubTask 3.3: 更新 `MonetColorService.cs` 中 `Bitmap.CopyPixels` 调用签名
  - [ ] SubTask 3.4: 验证构建错误减少

- [ ] Task 4: 修复第三方库变更（Phase 4）
  - [ ] SubTask 4.1: 替换 `SettingsWindow.axaml.cs` 中 `FluentIcons.Avalonia.SymbolIconSource` 为 `FluentIcon`
  - [ ] SubTask 4.2: 验证构建错误减少

- [ ] Task 5: 清理过时属性（Phase 5）
  - [ ] SubTask 5.1: 批量替换 `.cs` 文件中 `Watermark` → `PlaceholderText`
  - [ ] SubTask 5.2: 批量替换 `.axaml` 文件中 `Watermark` → `PlaceholderText`
  - [ ] SubTask 5.3: 验证无过时警告

- [ ] Task 6: 最终验证
  - [ ] SubTask 6.1: `dotnet build LanMountainDesktop.slnx -c Debug` 0 errors
  - [ ] SubTask 6.2: `dotnet test LanMountainDesktop.slnx -c Debug` 通过

# Task Dependencies

- Task 2 不依赖 Task 1（可并行）
- Task 3 不依赖 Task 1/2（可并行）
- Task 4 不依赖 Task 1/2/3（可并行）
- Task 5 依赖 Task 1/2/3/4（低优先级，最后执行）
- Task 6 依赖所有前置任务
