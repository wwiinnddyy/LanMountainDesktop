# 移除视频壁纸功能 - 编码任务清单

## 任务概览

本文档将技术设计分解为可执行的编码任务，按依赖关系排序执行。

---

## 任务 1: 移除项目依赖

**优先级**: P0 (最高)  
**依赖**: 无  
**预估工作量**: 5 分钟

### 描述

从项目文件中移除 LibVLC 相关的 NuGet 包引用。

### 输入

- `LanMountainDesktop/LanMountainDesktop.csproj`

### 输出

- 修改后的 `LanMountainDesktop.csproj`，移除以下包引用：
  - `LibVLCSharp.Avalonia`
  - `VideoLAN.LibVLC.Windows`
  - `VideoLAN.LibVLC.Mac`

### 验收标准

- [ ] 项目文件中不再包含 LibVLC 相关包引用
- [ ] 执行 `dotnet restore` 成功

### 执行提示

```
编辑 LanMountainDesktop.csproj，移除以下 PackageReference 节点：
1. <PackageReference Include="LibVLCSharp.Avalonia" Version="3.9.5" />
2. <PackageReference Include="VideoLAN.LibVLC.Windows" ... />
3. <PackageReference Include="VideoLAN.LibVLC.Mac" ... />
```

---

## 任务 2: 移除主窗口 XAML 视频控件

**优先级**: P0  
**依赖**: 任务 1  
**预估工作量**: 10 分钟

### 描述

从 MainWindow.axaml 中移除视频壁纸相关的 XAML 控件和命名空间声明。

### 输入

- `LanMountainDesktop/Views/MainWindow.axaml`

### 输出

- 移除 LibVLC 命名空间声明
- 移除 `DesktopVideoWallpaperImage` 控件
- 移除 `DesktopVideoWallpaperView` 控件

### 验收标准

- [ ] XAML 中无 `xmlns:vlc` 命名空间
- [ ] XAML 中无 `DesktopVideoWallpaperImage` 元素
- [ ] XAML 中无 `DesktopVideoWallpaperView` 元素

### 执行提示

```
编辑 MainWindow.axaml：
1. 移除第 9 行: xmlns:vlc="clr-namespace:LibVLCSharp.Avalonia;assembly=LibVLCSharp.Avalonia"
2. 移除第 126-131 行: <Image x:Name="DesktopVideoWallpaperImage" ... />
3. 移除第 133-137 行: <vlc:VideoView x:Name="DesktopVideoWallpaperView" ... />
```

---

## 任务 3: 移除主窗口代码视频字段

**优先级**: P0  
**依赖**: 任务 1  
**预估工作量**: 15 分钟

### 描述

从 MainWindow.axaml.cs 中移除视频壁纸相关的字段声明。

### 输入

- `LanMountainDesktop/Views/MainWindow.axaml.cs`

### 输出

- 移除 `SupportedVideoExtensions` 静态字段
- 移除所有视频相关实例字段

### 验收标准

- [ ] 无 `SupportedVideoExtensions` 字段
- [ ] 无 `_videoWallpaperPosterBitmap` 字段
- [ ] 无 `_videoWallpaperPosterPath` 字段
- [ ] 无 `_wallpaperVideoPath` 字段
- [ ] 无 `_libVlc` 字段
- [ ] 无 `_videoWallpaperPlayer` 字段
- [ ] 无 `_videoWallpaperMedia` 字段
- [ ] 无 `_desktopVideoFrameSync` 及相关视频帧处理字段

### 执行提示

```
编辑 MainWindow.axaml.cs：
1. 移除第 68-71 行的 SupportedVideoExtensions 定义
2. 移除第 123-146 行的所有视频相关字段
```

---

## 任务 4: 移除主窗口 OnClosed 清理代码

**优先级**: P0  
**依赖**: 任务 3  
**预估工作量**: 5 分钟

### 描述

从 MainWindow.axaml.cs 的 OnClosed 方法中移除视频相关清理代码。

### 输入

- `LanMountainDesktop/Views/MainWindow.axaml.cs` (OnClosed 方法)

### 输出

- 简化的 OnClosed 方法，无视频清理逻辑

### 验收标准

- [ ] OnClosed 方法中无 `StopVideoWallpaper()` 调用
- [ ] OnClosed 方法中无 `_videoWallpaperMedia` 相关清理
- [ ] OnClosed 方法中无 `_videoWallpaperPlayer` 相关清理
- [ ] OnClosed 方法中无 `_libVlc` 相关清理

### 执行提示

```
编辑 MainWindow.axaml.cs 的 OnClosed 方法，移除以下代码行：
- StopVideoWallpaper();
- _videoWallpaperMedia?.Dispose(); _videoWallpaperMedia = null;
- _videoWallpaperPlayer?.Dispose(); _videoWallpaperPlayer = null;
- _desktopVideoFrameRefreshTimer?.Stop(); _desktopVideoFrameRefreshTimer = null;
- _videoWallpaperPosterBitmap?.Dispose(); _videoWallpaperPosterBitmap = null;
- _videoWallpaperPosterPath = null;
- _libVlc?.Dispose(); _libVlc = null;
```

---

## 任务 5: 移除主窗口 Stub 方法

**优先级**: P0  
**依赖**: 任务 1  
**预估工作量**: 20 分钟

### 描述

从 MainWindow.SettingsHardCut.Stubs.cs 中移除视频壁纸相关方法和 using 声明。

### 输入

- `LanMountainDesktop/Views/MainWindow.SettingsHardCut.Stubs.cs`

### 输出

- 移除 LibVLC using 声明
- 移除 `StartVideoWallpaper` 方法
- 移除 `StopVideoWallpaper` 方法
- 移除 `TryCaptureVideoWallpaperPosterFrame` 方法
- 移除 `ApplyVideoWallpaperPosterVisibility` 方法

### 验收标准

- [ ] 无 `using LibVLCSharp.Shared;`
- [ ] 无 `using LibVLCSharp.Avalonia;`
- [ ] 无 `StartVideoWallpaper` 方法定义
- [ ] 无 `StopVideoWallpaper` 方法定义
- [ ] 无 `TryCaptureVideoWallpaperPosterFrame` 方法定义
- [ ] 无 `ApplyVideoWallpaperPosterVisibility` 方法定义

### 执行提示

```
编辑 MainWindow.SettingsHardCut.Stubs.cs：
1. 移除第 19-20 行的 using 声明
2. 移除 StartVideoWallpaper 方法（第 337-383 行）
3. 移除 StopVideoWallpaper 方法（第 385-395 行）
4. 移除 ApplyVideoWallpaperPosterVisibility 方法（第 647-664 行）
5. 移除 TryCaptureVideoWallpaperPosterFrame 方法（第 666-751 行）
```

---

## 任务 6: 简化壁纸状态处理逻辑

**优先级**: P0  
**依赖**: 任务 5  
**预估工作量**: 15 分钟

### 描述

修改 MainWindow.SettingsHardCut.Stubs.cs 中的壁纸状态处理方法，移除视频类型分支。

### 输入

- `LanMountainDesktop/Views/MainWindow.SettingsHardCut.Stubs.cs`

### 输出

- 简化的 `SetWallpaperState` 方法
- 简化的 `UpdateWallpaperDisplay` 方法
- 简化的 `ApplyWallpaperBrush` 方法

### 验收标准

- [ ] `SetWallpaperState` 中无视频类型检测分支
- [ ] `SetWallpaperState` 中无 `_wallpaperVideoPath` 赋值
- [ ] `UpdateWallpaperDisplay` 中无 `StopVideoWallpaper()` 调用
- [ ] `ApplyWallpaperBrush` 中无 `ApplyVideoWallpaperPosterVisibility` 调用

### 执行提示

```
编辑 MainWindow.SettingsHardCut.Stubs.cs：

1. SetWallpaperState 方法：
   - 移除 requestedTypeIsVideo 变量定义
   - 移除视频类型检测 if 块（SupportedVideoExtensions.Contains 检查）

2. UpdateWallpaperDisplay 方法：
   - 移除视频类型分支，仅保留 ApplyWallpaperBrush() 调用

3. ApplyWallpaperBrush 方法：
   - 移除所有 ApplyVideoWallpaperPosterVisibility 调用
```

---

## 任务 7: 移除外观主题服务视频提取器

**优先级**: P1  
**依赖**: 任务 1  
**预估工作量**: 10 分钟

### 描述

从 AppearanceThemeService.cs 中移除视频壁纸种子提取器接口和实现类。

### 输入

- `LanMountainDesktop/Services/AppearanceThemeService.cs`

### 输出

- 移除 `IVideoWallpaperSeedExtractor` 接口
- 移除 `LibVlcVideoWallpaperSeedExtractor` 类

### 验收标准

- [ ] 无 `IVideoWallpaperSeedExtractor` 接口定义
- [ ] 无 `LibVlcVideoWallpaperSeedExtractor` 类定义

### 执行提示

```
编辑 AppearanceThemeService.cs：
移除第 92-184 行的接口和类定义：
- IVideoWallpaperSeedExtractor 接口
- LibVlcVideoWallpaperSeedExtractor 类
```

---

## 任务 8: 简化壁纸设置页面 XAML

**优先级**: P1  
**依赖**: 无  
**预估工作量**: 10 分钟

### 描述

从 WallpaperSettingsPage.axaml 中移除视频预览区域和相关 UI 元素。

### 输入

- `LanMountainDesktop/Views/SettingsPages/WallpaperSettingsPage.axaml`

### 输出

- 移除视频预览 Border 区域
- 移除视频模式提示 TextBlock
- 修改填充方式可见性绑定

### 验收标准

- [ ] 无视频预览 Border（IsVisible="{Binding IsVideo}"）
- [ ] 无 VideoModeHintText 绑定的 TextBlock
- [ ] 填充方式设置绑定改为 `IsVisible="{Binding IsImage}"`

### 执行提示

```
编辑 WallpaperSettingsPage.axaml：
1. 移除第 29-44 行的视频预览 Border
2. 移除第 150-154 行的视频模式提示 TextBlock
3. 修改第 132 行: IsVisible="{Binding IsImageOrVideo}" 改为 IsVisible="{Binding IsImage}"
```

---

## 任务 9: 简化壁纸设置 ViewModel

**优先级**: P1  
**依赖**: 任务 8  
**预估工作量**: 15 分钟

### 描述

从 WallpaperSettingsPageViewModel.cs 中移除视频相关属性和方法逻辑。

### 输入

- `LanMountainDesktop/ViewModels/WallpaperSettingsPageViewModel.cs`

### 输出

- 移除 `_isImageOrVideo`、`_isVideo`、`_videoModeHintText` 属性
- 修改 `CreateWallpaperTypes` 方法
- 修改 `UpdateVisibility` 方法
- 修改 `RefreshLocalizedText` 方法

### 验收标准

- [ ] 无 `IsImageOrVideo` 属性
- [ ] 无 `IsVideo` 属性
- [ ] 无 `VideoModeHintText` 属性
- [ ] `CreateWallpaperTypes` 仅返回 Image 和 SolidColor 选项
- [ ] `UpdateVisibility` 中无 IsVideo、IsImageOrVideo 赋值
- [ ] `RefreshLocalizedText` 中无 VideoModeHintText 赋值

### 执行提示

```
编辑 WallpaperSettingsPageViewModel.cs：
1. 移除第 76-77 行的 _isImageOrVideo 字段和属性
2. 移除第 85-86 行的 _isVideo 字段和属性
3. 移除第 94-95 行的 _videoModeHintText 字段和属性
4. 修改 CreateWallpaperTypes 方法，移除 Video 选项
5. 修改 UpdateVisibility 方法，移除 IsVideo 和 IsImageOrVideo 赋值
6. 修改 RefreshLocalizedText 方法，移除 VideoModeHintText 赋值
```

---

## 任务 10: 简化壁纸媒体类型枚举

**优先级**: P1  
**依赖**: 无  
**预估工作量**: 5 分钟

### 描述

从 SettingsContracts.cs 中移除 WallpaperMediaType 枚举的 Video 值。

### 输入

- `LanMountainDesktop/Services/Settings/SettingsContracts.cs`

### 输出

- 简化的 `WallpaperMediaType` 枚举

### 验收标准

- [ ] `WallpaperMediaType` 枚举仅包含 `None` 和 `Image`

### 执行提示

```
编辑 SettingsContracts.cs：
修改第 11-16 行的枚举定义：
public enum WallpaperMediaType
{
    None,
    Image
}
```

---

## 任务 11: 简化壁纸媒体服务

**优先级**: P1  
**依赖**: 任务 10  
**预估工作量**: 10 分钟

### 描述

从 SettingsDomainServices.cs 中移除视频扩展名检测逻辑。

### 输入

- `LanMountainDesktop/Services/Settings/SettingsDomainServices.cs`

### 输出

- 移除 `VideoExtensions` 字段
- 简化 `DetectMediaType` 方法

### 验收标准

- [ ] 无 `VideoExtensions` 字段定义
- [ ] `DetectMediaType` 方法中无视频扩展名检测逻辑

### 执行提示

```
编辑 SettingsDomainServices.cs：
1. 移除第 150-153 行的 VideoExtensions 字段定义
2. 修改 DetectMediaType 方法，移除视频检测分支
```

---

## 任务 12: 清理本地化文件

**优先级**: P2  
**依赖**: 无  
**预估工作量**: 5 分钟

### 描述

从 zh-CN.json 中移除视频壁纸相关的本地化文本。

### 输入

- `LanMountainDesktop/Localization/zh-CN.json`

### 输出

- 移除视频相关本地化键
- 修改壁纸描述文本

### 验收标准

- [ ] 无 `settings.wallpaper.type.video` 键
- [ ] 无 `settings.wallpaper.video_applied` 键
- [ ] 无 `settings.wallpaper.video_mode` 键
- [ ] 无 `settings.wallpaper.video_restored` 键
- [ ] 无 `settings.wallpaper.video_not_found` 键
- [ ] 无 `settings.wallpaper.video_player_unavailable` 键
- [ ] 无 `settings.wallpaper.video_play_failed_format` 键
- [ ] `settings.wallpaper.description` 文本已更新

### 执行提示

```
编辑 zh-CN.json：
1. 移除以下键值对：
   - "settings.wallpaper.type.video"
   - "settings.wallpaper.video_applied"
   - "settings.wallpaper.video_mode"
   - "settings.wallpaper.video_restored"
   - "settings.wallpaper.video_not_found"
   - "settings.wallpaper.video_player_unavailable"
   - "settings.wallpaper.video_play_failed_format"

2. 修改描述文本：
   "settings.wallpaper.description": "选择图片后可立即设为应用窗口壁纸。"
```

---

## 任务 13: 构建验证

**优先级**: P0  
**依赖**: 任务 1-12 全部完成  
**预估工作量**: 10 分钟

### 描述

验证项目在移除视频壁纸功能后能够正常构建。

### 输入

- 整个项目

### 输出

- 构建成功确认

### 验收标准

- [ ] `dotnet build` 执行成功，无编译错误
- [ ] 无 LibVLC 相关类型未定义错误
- [ ] 无未使用变量警告（或已处理）

### 执行提示

```
在项目根目录执行：
dotnet build LanMountainDesktop/LanMountainDesktop.csproj

检查输出：
- 确认无编译错误
- 确认无 LibVLC 相关类型引用错误
```

---

## 任务 14: 功能验证

**优先级**: P0  
**依赖**: 任务 13  
**预估工作量**: 15 分钟

### 描述

验证应用在移除视频壁纸功能后核心功能正常工作。

### 输入

- 构建后的应用

### 输出

- 功能验证报告

### 验收标准

- [ ] 应用正常启动
- [ ] 图片壁纸正常显示
- [ ] 纯色壁纸正常显示
- [ ] 壁纸设置页面正常打开
- [ ] 类型选择器仅显示"图片"和"纯色"选项
- [ ] 壁纸导入功能正常工作

### 执行提示

```
运行应用：
dotnet run --project LanMountainDesktop/LanMountainDesktop.csproj

手动验证：
1. 应用启动无崩溃
2. 打开设置 -> 壁纸页面
3. 确认类型选择器仅有"图片"和"纯色"
4. 测试选择图片壁纸
5. 测试选择纯色壁纸
```

---

## 任务依赖关系图

```
任务 1 (移除依赖)
  ├── 任务 2 (XAML控件)
  ├── 任务 3 (代码字段)
  │     └── 任务 4 (OnClosed清理)
  ├── 任务 5 (Stub方法)
  │     └── 任务 6 (状态处理逻辑)
  └── 任务 7 (主题服务)

任务 8 (设置页面XAML)
  └── 任务 9 (设置ViewModel)

任务 10 (枚举简化)
  └── 任务 11 (媒体服务)

任务 12 (本地化) - 独立

任务 13 (构建验证) - 依赖所有任务
  └── 任务 14 (功能验证)
```

---

## 执行顺序建议

按以下顺序执行可确保依赖关系正确：

1. **第一批** (可并行): 任务 1, 任务 8, 任务 10, 任务 12
2. **第二批** (可并行): 任务 2, 任务 3, 任务 5, 任务 7, 任务 9, 任务 11
3. **第三批** (可并行): 任务 4, 任务 6
4. **第四批**: 任务 13
5. **第五批**: 任务 14
