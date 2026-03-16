# 移除视频壁纸功能 - 技术设计文档

## 1. 概述

### 1.1 设计目标

本设计文档描述如何从 LanMountainDesktop 项目中完全移除视频壁纸功能，包括：
- 移除 LibVLC 相关依赖
- 清理主窗口中的视频壁纸代码
- 简化壁纸设置页面
- 清理本地化资源

### 1.2 技术约束

- 保持现有图片壁纸和纯色壁纸功能完整
- 确保应用构建和运行正常
- 不引入新的外部依赖

---

## 2. 架构变更

### 2.1 变更概览图

```
┌─────────────────────────────────────────────────────────────────┐
│                        变更前架构                                │
├─────────────────────────────────────────────────────────────────┤
│  MainWindow                                                      │
│  ├── DesktopWallpaperLayer (背景层)                              │
│  │   ├── DesktopWallpaperImageLayer (图片层)                     │
│  │   ├── DesktopVideoWallpaperImage (视频海报层)                  │
│  │   └── DesktopVideoWallpaperView (VLC视频播放层)               │
│  ├── _libVlc, _videoWallpaperPlayer, _videoWallpaperMedia        │
│  └── StartVideoWallpaper(), StopVideoWallpaper()                 │
│                                                                  │
│  WallpaperSettingsPage                                           │
│  ├── 类型选择: Image | Video | SolidColor                        │
│  └── 视频预览区域                                                 │
└─────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────┐
│                        变更后架构                                │
├─────────────────────────────────────────────────────────────────┤
│  MainWindow                                                      │
│  ├── DesktopWallpaperLayer (背景层)                              │
│  │   └── DesktopWallpaperImageLayer (图片层)                     │
│  └── (移除所有视频相关字段和方法)                                  │
│                                                                  │
│  WallpaperSettingsPage                                           │
│  ├── 类型选择: Image | SolidColor                                │
│  └── (移除视频预览区域)                                           │
└─────────────────────────────────────────────────────────────────┘
```

### 2.2 组件变更清单

| 组件 | 变更类型 | 说明 |
|------|----------|------|
| LanMountainDesktop.csproj | 修改 | 移除 LibVLC 包引用 |
| MainWindow.axaml | 修改 | 移除视频控件和命名空间 |
| MainWindow.axaml.cs | 修改 | 移除视频相关字段和清理代码 |
| MainWindow.SettingsHardCut.Stubs.cs | 修改 | 移除视频壁纸方法 |
| AppearanceThemeService.cs | 修改 | 移除视频种子提取器 |
| WallpaperSettingsPage.axaml | 修改 | 移除视频类型UI |
| WallpaperSettingsPageViewModel.cs | 修改 | 移除视频相关属性 |
| SettingsContracts.cs | 修改 | 移除 Video 枚举值 |
| SettingsDomainServices.cs | 修改 | 移除视频扩展名检测 |
| zh-CN.json | 修改 | 移除视频相关本地化文本 |

---

## 3. 详细设计

### 3.1 项目依赖变更 (LanMountainDesktop.csproj)

#### 3.1.1 移除的包引用

```xml
<!-- 移除以下包引用 -->
<PackageReference Include="LibVLCSharp.Avalonia" Version="3.9.5" />
<PackageReference Include="VideoLAN.LibVLC.Windows" Version="3.0.23" Condition="..." />
<PackageReference Include="VideoLAN.LibVLC.Mac" Version="3.1.3.1" Condition="..." />
```

#### 3.1.2 变更影响

- 减少约 100MB+ 的依赖包大小
- 简化构建和发布流程
- 移除平台特定的原生库依赖

---

### 3.2 主窗口 XAML 变更 (MainWindow.axaml)

#### 3.2.1 移除命名空间声明

```xml
<!-- 移除此行 -->
xmlns:vlc="clr-namespace:LibVLCSharp.Avalonia;assembly=LibVLCSharp.Avalonia"
```

#### 3.2.2 移除视频壁纸控件

移除以下控件（约第126-137行）：

```xml
<!-- 移除 DesktopVideoWallpaperImage -->
<Image x:Name="DesktopVideoWallpaperImage"
       IsVisible="False"
       IsHitTestVisible="False"
       Stretch="UniformToFill"
       HorizontalAlignment="Stretch"
       VerticalAlignment="Stretch" />

<!-- 移除 DesktopVideoWallpaperView -->
<vlc:VideoView x:Name="DesktopVideoWallpaperView"
               IsVisible="False"
               IsHitTestVisible="False"
               HorizontalAlignment="Stretch"
               VerticalAlignment="Stretch" />
```

---

### 3.3 主窗口代码变更 (MainWindow.axaml.cs)

#### 3.3.1 移除 using 声明

```csharp
// 移除以下 using（如果存在）
using LibVLCSharp.Shared;
using LibVLCSharp.Avalonia;
```

#### 3.3.2 移除静态字段

```csharp
// 移除以下字段（约第68-71行）
private static readonly HashSet<string> SupportedVideoExtensions = new(StringComparer.OrdinalIgnoreCase)
{
    ".mp4", ".mkv", ".webm", ".avi", ".mov", ".m4v"
};
```

#### 3.3.3 移除实例字段

```csharp
// 移除以下字段（约第123-146行）
private Bitmap? _videoWallpaperPosterBitmap;
private string? _videoWallpaperPosterPath;
private string? _wallpaperVideoPath;
private LibVLC? _libVlc;
private MediaPlayer? _videoWallpaperPlayer;
private Media? _videoWallpaperMedia;
private readonly object _desktopVideoFrameSync = new();
private MediaPlayer.LibVLCVideoLockCb? _desktopVideoLockCallback;
private MediaPlayer.LibVLCVideoUnlockCb? _desktopVideoUnlockCallback;
private MediaPlayer.LibVLCVideoDisplayCb? _desktopVideoDisplayCallback;
private DispatcherTimer? _desktopVideoFrameRefreshTimer;
private IntPtr _desktopVideoFrameBufferPtr;
private byte[]? _desktopVideoStagingBuffer;
private WriteableBitmap? _desktopVideoBitmap;
private int _desktopVideoFrameWidth;
private int _desktopVideoFrameHeight;
private int _desktopVideoFramePitch;
private int _desktopVideoFrameBufferSize;
private int _desktopVideoFrameDirtyFlag;
```

#### 3.3.4 修改 OnClosed 方法

移除视频相关清理代码（约第336-350行）：

```csharp
// 移除以下代码行
StopVideoWallpaper();
_videoWallpaperMedia?.Dispose();
_videoWallpaperMedia = null;
_videoWallpaperPlayer?.Dispose();
_videoWallpaperPlayer = null;
_desktopVideoFrameRefreshTimer?.Stop();
_desktopVideoFrameRefreshTimer = null;
_videoWallpaperPosterBitmap?.Dispose();
_videoWallpaperPosterBitmap = null;
_videoWallpaperPosterPath = null;
_libVlc?.Dispose();
_libVlc = null;
```

---

### 3.4 主窗口 Stub 方法变更 (MainWindow.SettingsHardCut.Stubs.cs)

#### 3.4.1 移除 using 声明

```csharp
// 移除以下 using（第19-20行）
using LibVLCSharp.Shared;
using LibVLCSharp.Avalonia;
```

#### 3.4.2 移除方法

移除以下完整方法：

| 方法名 | 行号范围 | 说明 |
|--------|----------|------|
| `StartVideoWallpaper` | 337-383 | 启动视频壁纸播放 |
| `StopVideoWallpaper` | 385-395 | 停止视频壁纸播放 |
| `TryCaptureVideoWallpaperPosterFrame` | 666-751 | 捕获视频海报帧 |
| `ApplyVideoWallpaperPosterVisibility` | 647-664 | 控制视频海报可见性 |

#### 3.4.3 修改 UpdateWallpaperDisplay 方法

简化为仅处理图片壁纸：

```csharp
private void UpdateWallpaperDisplay()
{
    // 移除视频分支，仅保留图片处理
    StopVideoWallpaper();  // 移除此调用
    ApplyWallpaperBrush();
}
```

修改后：

```csharp
private void UpdateWallpaperDisplay()
{
    ApplyWallpaperBrush();
}
```

#### 3.4.4 修改 ApplyWallpaperBrush 方法

移除所有 `ApplyVideoWallpaperPosterVisibility` 调用：

```csharp
// 移除以下调用
ApplyVideoWallpaperPosterVisibility(showPoster: false);
ApplyVideoWallpaperPosterVisibility(showPoster: _videoWallpaperPosterBitmap is not null);
```

#### 3.4.5 修改 SetWallpaperState 方法

移除视频类型处理分支（约第238-247行）：

```csharp
// 移除以下代码块
var requestedTypeIsVideo = string.Equals(_wallpaperType, "Video", StringComparison.OrdinalIgnoreCase);
if (SupportedVideoExtensions.Contains(extension) || requestedTypeIsVideo)
{
    _wallpaperMediaType = WallpaperMediaType.Video;
    _wallpaperVideoPath = _wallpaperPath;
    _wallpaperDisplayState = File.Exists(_wallpaperPath)
        ? WallpaperDisplayState.CurrentValidWallpaper
        : WallpaperDisplayState.TemporarilyUnavailable;
    return;
}
```

---

### 3.5 外观主题服务变更 (AppearanceThemeService.cs)

#### 3.5.1 移除接口和类

移除以下代码（约第92-184行）：

```csharp
// 移除接口
internal interface IVideoWallpaperSeedExtractor
{
    IReadOnlyList<Color> ExtractSeedCandidates(string videoPath, MonetColorService monetColorService);
}

// 移除实现类
internal sealed class LibVlcVideoWallpaperSeedExtractor : IVideoWallpaperSeedExtractor
{
    // ... 整个类实现
}
```

---

### 3.6 壁纸设置页面 XAML 变更 (WallpaperSettingsPage.axaml)

#### 3.6.1 移除视频预览区域

移除以下代码（约第29-44行）：

```xml
<Border Background="#FFF6F7F9"
        IsVisible="{Binding IsVideo}">
    <StackPanel HorizontalAlignment="Center"
                VerticalAlignment="Center"
                Spacing="12">
        <fi:FluentIcon Icon="Video"
                       Width="72"
                       Height="72"
                       Foreground="{DynamicResource AdaptiveTextSecondaryBrush}" />
        <TextBlock Text="{Binding VideoModeHintText}"
                   Width="300"
                   TextAlignment="Center"
                   TextWrapping="Wrap"
                   Foreground="{DynamicResource AdaptiveTextSecondaryBrush}" />
    </StackPanel>
</Border>
```

#### 3.6.2 移除视频模式提示文本

移除以下代码（约第150-154行）：

```xml
<TextBlock Margin="0,8,0,0"
           IsVisible="{Binding IsVideo}"
           Foreground="{DynamicResource AdaptiveTextSecondaryBrush}"
           Text="{Binding VideoModeHintText}"
           TextWrapping="Wrap" />
```

#### 3.6.3 修改填充方式设置可见性绑定

```xml
<!-- 修改前 -->
IsVisible="{Binding IsImageOrVideo}"

<!-- 修改后 -->
IsVisible="{Binding IsImage}"
```

---

### 3.7 壁纸设置 ViewModel 变更 (WallpaperSettingsPageViewModel.cs)

#### 3.7.1 移除属性

```csharp
// 移除以下属性
[ObservableProperty]
private bool _isImageOrVideo;

[ObservableProperty]
private bool _isVideo;

[ObservableProperty]
private string _videoModeHintText = string.Empty;
```

#### 3.7.2 修改 CreateWallpaperTypes 方法

```csharp
// 修改前
private IReadOnlyList<SelectionOption> CreateWallpaperTypes()
{
    return
    [
        new SelectionOption("Image", L("settings.wallpaper.type.image", "Image")),
        new SelectionOption("Video", L("settings.wallpaper.type.video", "Video")),
        new SelectionOption("SolidColor", L("settings.wallpaper.type.solid_color", "Solid Color"))
    ];
}

// 修改后
private IReadOnlyList<SelectionOption> CreateWallpaperTypes()
{
    return
    [
        new SelectionOption("Image", L("settings.wallpaper.type.image", "Image")),
        new SelectionOption("SolidColor", L("settings.wallpaper.type.solid_color", "Solid Color"))
    ];
}
```

#### 3.7.3 修改 UpdateVisibility 方法

移除 IsVideo 和 IsImageOrVideo 的赋值：

```csharp
// 移除以下行
IsVideo = SelectedWallpaperType?.Value == "Video";
IsImageOrVideo = SelectedWallpaperType?.Value is "Image" or "Video";
```

#### 3.7.4 修改 RefreshLocalizedText 方法

```csharp
// 移除以下行
VideoModeHintText = L("settings.wallpaper.video_mode", "Video wallpaper uses automatic fill mode.");
```

---

### 3.8 设置契约变更 (SettingsContracts.cs)

#### 3.8.1 修改 WallpaperMediaType 枚举

```csharp
// 修改前
public enum WallpaperMediaType
{
    None,
    Image,
    Video
}

// 修改后
public enum WallpaperMediaType
{
    None,
    Image
}
```

---

### 3.9 设置域服务变更 (SettingsDomainServices.cs)

#### 3.9.1 移除视频扩展名集合

```csharp
// 移除以下字段（约第150-153行）
private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
{
    ".mp4", ".mkv", ".webm", ".avi", ".mov", ".m4v"
};
```

#### 3.9.2 修改 DetectMediaType 方法

```csharp
// 修改前
public WallpaperMediaType DetectMediaType(string? path)
{
    if (string.IsNullOrWhiteSpace(path))
    {
        return WallpaperMediaType.None;
    }

    var extension = Path.GetExtension(path.Trim());
    if (string.IsNullOrWhiteSpace(extension))
    {
        return WallpaperMediaType.None;
    }

    if (ImageExtensions.Contains(extension))
    {
        return WallpaperMediaType.Image;
    }

    if (VideoExtensions.Contains(extension))
    {
        return WallpaperMediaType.Video;
    }

    return WallpaperMediaType.None;
}

// 修改后
public WallpaperMediaType DetectMediaType(string? path)
{
    if (string.IsNullOrWhiteSpace(path))
    {
        return WallpaperMediaType.None;
    }

    var extension = Path.GetExtension(path.Trim());
    if (string.IsNullOrWhiteSpace(extension))
    {
        return WallpaperMediaType.None;
    }

    if (ImageExtensions.Contains(extension))
    {
        return WallpaperMediaType.Image;
    }

    return WallpaperMediaType.None;
}
```

---

### 3.10 本地化文件变更 (zh-CN.json)

#### 3.10.1 移除的本地化键

```json
// 移除以下键值对
"settings.wallpaper.type.video": "视频",
"settings.wallpaper.video_applied": "视频壁纸已应用。",
"settings.wallpaper.video_mode": "视频壁纸使用自动填充模式。",
"settings.wallpaper.video_restored": "已恢复保存的视频壁纸。",
"settings.wallpaper.video_not_found": "未找到视频壁纸文件。",
"settings.wallpaper.video_player_unavailable": "视频播放器不可用。",
"settings.wallpaper.video_play_failed_format": "播放视频壁纸失败：{0}"
```

#### 3.10.2 修改描述文本

```json
// 修改前
"settings.wallpaper.description": "选择图片或视频后可立即设为应用窗口壁纸。",

// 修改后
"settings.wallpaper.description": "选择图片后可立即设为应用窗口壁纸。",
```

---

## 4. 数据模型变更

### 4.1 WallpaperMediaType 枚举简化

```
变更前: None | Image | Video
变更后: None | Image
```

### 4.2 设置存储兼容性

现有用户设置中如果包含 `Type: "Video"` 的壁纸配置：
- 应用将无法识别该类型
- 将回退到纯色背景
- 用户需要重新选择图片壁纸

---

## 5. 风险评估

### 5.1 潜在风险

| 风险 | 影响 | 缓解措施 |
|------|------|----------|
| 现有视频壁纸用户设置失效 | 中 | 应用会自动回退到纯色背景 |
| 遗漏的视频相关代码引用 | 低 | 编译器会报告未定义类型错误 |
| 本地化键遗漏 | 低 | 运行时会显示键名而非翻译文本 |

### 5.2 回滚策略

如需回滚，可通过 Git 恢复以下文件：
- LanMountainDesktop.csproj
- MainWindow.axaml / .axaml.cs
- MainWindow.SettingsHardCut.Stubs.cs
- AppearanceThemeService.cs
- WallpaperSettingsPage.axaml
- WallpaperSettingsPageViewModel.cs
- SettingsContracts.cs
- SettingsDomainServices.cs
- zh-CN.json

---

## 6. 验证清单

### 6.1 编译验证

- [ ] 项目编译无错误
- [ ] 无 LibVLC 相关类型引用警告
- [ ] 无未使用变量警告

### 6.2 功能验证

- [ ] 应用正常启动
- [ ] 图片壁纸正常显示
- [ ] 纯色壁纸正常显示
- [ ] 壁纸设置页面正常打开
- [ ] 类型选择器仅显示"图片"和"纯色"
- [ ] 壁纸导入功能正常工作

### 6.3 清理验证

- [ ] 无 LibVLC 相关 DLL 在输出目录
- [ ] 无视频相关本地化文本残留
- [ ] 无视频相关 UI 控件残留
