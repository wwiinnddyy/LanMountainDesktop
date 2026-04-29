# Avalonia 12 迁移计划

## 当前状态

项目已完成以下迁移准备：

* `Directory.Packages.props` 中 Avalonia 包已升级到 `12.0.1`

* `FluentAvaloniaUI` 已升级到 `3.0.0-preview1`

* `Avalonia.Diagnostics` 已替换为 `AvaloniaUI.DiagnosticsSupport`

* `Avalonia.Controls.WebView` 已升级到 `12.0.0`

* `ClassIsland.Markdown.Avalonia` 已升级到 `12.0.0`

## 构建错误清单（26 errors）

### 1. 窗口装饰 API 移除（8 errors）

**文件**：`LanMountainDesktop/Views/SettingsWindow.axaml.cs`（4 errors）

* `ExtendClientAreaChromeHints` 不存在（line 166, 179）

* `SystemDecorations` 已过时，需改用 `WindowDecorations`（line 168, 177）

**文件**：`LanMountainDesktop/Views/ComponentEditorWindow.axaml.cs`（4 errors）

* `ExtendClientAreaChromeHints` 不存在（line 63, 72）

* `SystemDecorations` 已过时，需改用 `WindowDecorations`（line 65, 70）

**AXAML 文件**：13 个文件使用 `SystemDecorations` 属性（编译警告）

### 2. 变量/字段未找到（8 errors）

**文件**：`LanMountainDesktop/Views/MainWindow.ComponentSystem.cs`

* `centerLeft` 不存在（line 759, 766, 778）

* `positions` 不存在（line 1266）

**文件**：`LanMountainDesktop/Views/MainWindow.DesktopPaging.cs`

* `child` 不存在（line 312）

* `_isThreeFingerOrRightDragSwipeActive` 不存在（line 517, 828, 847, 850）

### 3. API 变更（3 errors）

**文件**：`LanMountainDesktop/App.axaml.cs`

* `BindingPlugins` 不可访问（line 532, 537）

**文件**：`LanMountainDesktop/Views/Components/DesktopComponentFailureView.cs`

* `IClipboard.SetTextAsync` 不存在（line 187）

**文件**：`LanMountainDesktop/Services/MonetColorService.cs`

* `Bitmap.CopyPixels` 参数不匹配（line 91）

### 4. 第三方库变更（1 error）

**文件**：`LanMountainDesktop/Views/SettingsWindow.axaml.cs`

* `FluentIcons.Avalonia.SymbolIconSource` 不存在（line 215）

### 5. 过时属性警告（需同步修复）

* `TextBox.Watermark` → `PlaceholderText`（7 处 .cs + 7 处 .axaml）

## 迁移步骤

### Phase 1: 修复窗口装饰 API（高优先级）

1. 重写 `SettingsWindow.ApplyChromeMode()` 使用 Avalonia 12 新 API
2. 重写 `ComponentEditorWindow.ApplyChromeMode()` 使用 Avalonia 12 新 API
3. 批量替换所有 `.axaml` 中的 `SystemDecorations` → `WindowDecorations`

### Phase 2: 修复 MainWindow 编译错误（高优先级）

1. 检查 `MainWindow.ComponentSystem.cs` 中 `centerLeft` 和 `positions` 的作用域问题
2. 检查 `MainWindow.DesktopPaging.cs` 中 `child` 和 `_isThreeFingerOrRightDragSwipeActive` 的作用域问题
3. 确认这些变量是否被意外删除或重命名

### Phase 3: 修复 Avalonia 12 API 变更（中优先级）

1. `App.axaml.cs`: 替换 `BindingPlugins.DataValidators` 的访问方式
2. `DesktopComponentFailureView.cs`: 使用新的剪贴板 API
3. `MonetColorService.cs`: 更新 `Bitmap.CopyPixels` 调用签名

### Phase 4: 修复第三方库变更（中优先级）

1. `SettingsWindow.axaml.cs`: 替换 `FluentIcons.Avalonia.SymbolIconSource` 为 v3 等效 API

### Phase 5: 清理过时属性（低优先级）

1. 批量替换 `Watermark` → `PlaceholderText`（所有 .cs 和 .axaml）

## 验证步骤

* 每阶段修复后运行 `dotnet build LanMountainDesktop.slnx -c Debug`

* 最终运行 `dotnet test LanMountainDesktop.slnx -c Debug`

