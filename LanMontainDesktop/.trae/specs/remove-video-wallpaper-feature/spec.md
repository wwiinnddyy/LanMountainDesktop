# 移除视频壁纸功能规格说明书

## Why

当前 LanMountainDesktop 项目包含视频壁纸功能，该功能引入了以下复杂性和依赖：
1. 引入了 LibVLCSharp.Avalonia、VideoLAN.LibVLC.Windows、VideoLAN.LibVLC.Mac 等重型依赖
2. 在主窗口中残留大量视频壁纸相关代码和字段
3. 在设置页面中保留了视频类型选择器和相关 UI 元素
4. 在本地化文件中保留了视频壁纸相关文本
5. 增加了应用复杂度和维护成本

用户决定移除该功能以简化代码库。

## What Changes

- 移除 LibVLCSharp.Avalonia 及 VideoLAN.LibVLC.* NuGet 依赖
- 移除 AppearanceThemeService.cs 中的 LibVlcVideoWallpaperSeedExtractor 类和 IVideoWallpaperSeedExtractor 接口
- 移除 MainWindow.axaml.cs 中的视频壁纸相关字段和清理代码
- 移除 MainWindow.SettingsHardCut.Stubs.cs 中的视频壁纸相关方法
- 移除 MainWindow.axaml 中的 DesktopVideoWallpaperImage 和 DesktopVideoWallpaperView 控件
- 移除 WallpaperSettingsPage.axaml 中的视频类型选择器和视频模式提示
- 移除 WallpaperSettingsPageViewModel.cs 中的 IsVideo、VideoModeHintText 等属性
- 移除 SettingsContracts.cs 中 WallpaperMediaType 枚举的 Video 值
- 移除 SettingsDomainServices.cs 中 WallpaperMediaService 类的视频扩展名检测逻辑
- 移除本地化文件中的视频壁纸相关文本

## Impact

### Affected specs
- 壁纸设置功能规格
- 主窗口桌面层规格

### Affected code
- `LanMountainDesktop.csproj` - NuGet 依赖配置
- `Services/AppearanceThemeService.cs` - 视频壁纸种子提取器
- `Views/MainWindow.axaml.cs` - 主窗口字段和清理逻辑
- `Views/MainWindow.SettingsHardCut.Stubs.cs` - 视频壁纸控制方法
- `Views/MainWindow.axaml` - 视频壁纸 UI 控件
- `Views/SettingsPages/WallpaperSettingsPage.axaml` - 壁纸设置页面 UI
- `ViewModels/WallpaperSettingsPageViewModel.cs` - 壁纸设置 ViewModel
- `Services/Settings/SettingsContracts.cs` - 壁纸媒体类型枚举
- `Services/Settings/SettingsDomainServices.cs` - 壁纸媒体服务
- `Localization/zh-CN.json` - 本地化文本

---

## REMOVED Requirements

### Requirement: 视频壁纸播放功能

**Reason**: 用户决定移除视频壁纸功能以简化代码库，减少重型依赖

**Migration**: 
- 用户如需动态壁纸，可使用静态图片壁纸替代
- 现有视频壁纸设置将被重置为纯色背景

#### Scenario: 视频壁纸播放
- **GIVEN** 用户选择了视频文件作为壁纸
- **WHEN** 系统检测到视频格式
- **THEN** 系统不再支持视频壁纸播放
- **AND THEN** 系统提示用户该文件类型不受支持

### Requirement: LibVLC 依赖

**Reason**: 移除视频壁纸功能后不再需要 LibVLC 库

**Migration**: 从项目依赖中移除以下包：
- LibVLCSharp.Avalonia
- VideoLAN.LibVLC.Windows
- VideoLAN.LibVLC.Mac

### Requirement: 视频壁纸种子提取

**Reason**: 移除视频壁纸功能后不再需要从视频中提取颜色种子

**Migration**: 移除 `LibVlcVideoWallpaperSeedExtractor` 类和 `IVideoWallpaperSeedExtractor` 接口

### Requirement: 视频壁纸 UI 控件

**Reason**: 移除视频壁纸功能后不再需要视频显示控件

**Migration**: 移除 `DesktopVideoWallpaperImage` 和 `DesktopVideoWallpaperView` 控件

### Requirement: 视频类型选择器

**Reason**: 移除视频壁纸功能后不再需要视频类型选项

**Migration**: 从壁纸类型选择器中移除"视频"选项

---

## MODIFIED Requirements

### Requirement: 壁纸媒体类型检测

**当前**: 支持检测 None、Image、Video 三种类型

**修改后**: 仅支持检测 None、Image 两种类型

#### Scenario: 检测媒体类型
- **WHEN** 用户选择壁纸文件
- **THEN** 系统仅检测图片格式（.png, .jpg, .jpeg, .bmp, .gif, .webp）
- **AND THEN** 视频格式文件将被识别为不受支持的类型

### Requirement: 壁纸类型选项

**当前**: 提供图片、视频、纯色三种类型选项

**修改后**: 仅提供图片、纯色两种类型选项

#### Scenario: 壁纸类型选择
- **WHEN** 用户打开壁纸设置页面
- **THEN** 类型选择器仅显示"图片"和"纯色"选项
- **AND THEN** "视频"选项不再显示

### Requirement: 壁纸设置页面预览

**当前**: 根据类型显示图片预览、视频预览或纯色预览

**修改后**: 根据类型显示图片预览或纯色预览

#### Scenario: 预览显示
- **WHEN** 用户选择壁纸类型
- **THEN** 系统仅显示图片预览或纯色预览
- **AND THEN** 视频预览区域不再显示

### Requirement: 主窗口壁纸显示

**当前**: 支持显示静态图片壁纸和视频壁纸

**修改后**: 仅支持显示静态图片壁纸

#### Scenario: 壁纸显示更新
- **WHEN** 用户应用新壁纸
- **THEN** 系统仅处理静态图片壁纸显示
- **AND THEN** 视频壁纸播放逻辑不再执行

---

## ADDED Requirements

### Requirement: 清理残留代码

系统 SHALL 完全移除视频壁纸功能相关的所有代码和资源。

#### Scenario: 主窗口字段清理
- **WHEN** 执行代码清理
- **THEN** 移除以下字段：
  - `_videoWallpaperPosterBitmap`
  - `_videoWallpaperPosterPath`
  - `_libVlc`
  - `_videoWallpaperPlayer`
  - `_videoWallpaperMedia`
  - `_wallpaperVideoPath`

#### Scenario: 主窗口方法清理
- **WHEN** 执行代码清理
- **THEN** 移除以下方法：
  - `StartVideoWallpaper`
  - `StopVideoWallpaper`
  - `TryCaptureVideoWallpaperPosterFrame`
  - `ApplyVideoWallpaperPosterVisibility`
  - `UpdateWallpaperDisplay` 中的视频处理分支

#### Scenario: ViewModel 属性清理
- **WHEN** 执行代码清理
- **THEN** 移除以下属性：
  - `IsVideo`
  - `VideoModeHintText`
  - `IsImageOrVideo`（改为 `IsImage`）

#### Scenario: 本地化文本清理
- **WHEN** 执行代码清理
- **THEN** 移除以下本地化键：
  - `settings.wallpaper.type.video`
  - `settings.wallpaper.video_applied`
  - `settings.wallpaper.video_mode`
  - `settings.wallpaper.video_restored`
  - `settings.wallpaper.video_not_found`
  - `settings.wallpaper.video_player_unavailable`
  - `settings.wallpaper.video_play_failed_format`

### Requirement: 依赖项清理

系统 SHALL 从项目文件中移除 LibVLC 相关 NuGet 包引用。

#### Scenario: NuGet 包移除
- **WHEN** 执行依赖清理
- **THEN** 移除以下包引用：
  - `LibVLCSharp.Avalonia`
  - `VideoLAN.LibVLC.Windows`
  - `VideoLAN.LibVLC.Mac`

### Requirement: 构建验证

系统 SHALL 在移除视频壁纸功能后保持正常构建和运行。

#### Scenario: 构建成功
- **WHEN** 执行项目构建
- **THEN** 构建成功无错误
- **AND THEN** 所有现有测试通过

#### Scenario: 应用启动
- **WHEN** 启动应用程序
- **THEN** 应用正常启动
- **AND THEN** 壁纸设置功能正常工作（仅支持图片和纯色）
