# 智教Hub组件实现总结

## 组件概述

智教Hub组件是一个图片展示组件，从两个GitHub仓库获取社区图片：
- **ClassIsland Hub**: https://github.com/ClassIsland/classisland-hub
- **SECTL Hub**: https://github.com/SECTL/SECTL-hub

## 功能特性

- ✅ 最小尺寸 2×2 cells
- ✅ 允许自由调整大小 (ResizeMode.Free)
- ✅ 支持两个数据源切换
- ✅ 自动刷新功能（可配置间隔）
- ✅ 图片左右导航
- ✅ 左下角显示图片名称
- ✅ 悬停显示导航按钮和指示器

## 文件清单

### 1. 数据模型和配置
- `LanMountainDesktop/Models/ComponentSettingsSnapshot.cs`
  - 添加智教Hub配置字段
  - 添加 `ZhiJiaoHubSources` 常量类

### 2. 数据服务
- `LanMountainDesktop/Services/IRecommendationDataService.cs`
  - 添加 `ZhiJiaoHubQuery`, `ZhiJiaoHubImageItem`, `ZhiJiaoHubSnapshot` 类型
  - 添加 `GetZhiJiaoHubImagesAsync` 接口方法
  - 添加 GitHub API URL 配置

- `LanMountainDesktop/Services/RecommendationDataService.cs`
  - 实现 `GetZhiJiaoHubImagesAsync` 方法
  - 实现 GitHub API 图片列表获取
  - 实现缓存机制（1小时缓存）

### 3. 组件实现
- `LanMountainDesktop/Views/Components/ZhiJiaoHubWidget.axaml`
  - 组件UI布局（图片、渐变遮罩、名称、导航按钮、指示器）

- `LanMountainDesktop/Views/Components/ZhiJiaoHubWidget.axaml.cs`
  - 组件逻辑实现
  - 图片加载和显示
  - 导航功能（上一张/下一张）
  - 自动刷新
  - 设置持久化

### 4. 设置编辑器
- `LanMountainDesktop/Views/ComponentEditors/ZhiJiaoHubComponentEditor.axaml`
  - 设置界面布局

- `LanMountainDesktop/Views/ComponentEditors/ZhiJiaoHubComponentEditor.axaml.cs`
  - 数据源选择
  - 自动刷新开关
  - 刷新间隔设置

### 5. 组件注册
- `LanMountainDesktop/ComponentSystem/BuiltInComponentIds.cs`
  - 添加 `DesktopZhiJiaoHub` 常量

- `LanMountainDesktop/ComponentSystem/ComponentRegistry.cs`
  - 注册组件定义（2×2最小尺寸，Free调整模式）

- `LanMountainDesktop/Views/Components/DesktopComponentRuntimeRegistry.cs`
  - 注册组件运行时

- `LanMountainDesktop/Services/DesktopComponentEditorRegistryFactory.cs`
  - 注册组件设置编辑器

- `LanMountainDesktop/Views/MainWindow.ComponentSystem.cs`
  - 添加比例约束（允许自由调整大小）

## 技术实现细节

### 图片获取流程

```
1. 调用 GitHub API 获取仓库图片目录
   - ClassIsland: /repos/ClassIsland/classisland-hub/contents/images
   - SECTL: /repos/SECTL/SECTL-hub/contents/docs/.vuepress/public/images

2. 解析 JSON 响应，提取图片文件信息
   - 文件名（解码URL编码）
   - 下载URL

3. 过滤非图片文件（只保留 .png, .jpg, .jpeg, .gif, .webp）

4. 缓存图片列表（1小时）

5. 按需加载单个图片
```

### 数据源配置

```csharp
public static class ZhiJiaoHubSources
{
    public const string ClassIsland = "classisland";
    public const string Sectl = "sectl";
}
```

### 组件配置项

```csharp
public string ZhiJiaoHubSource { get; set; } = ZhiJiaoHubSources.ClassIsland;
public bool ZhiJiaoHubAutoRefreshEnabled { get; set; } = true;
public int ZhiJiaoHubAutoRefreshIntervalMinutes { get; set; } = 30;
public int ZhiJiaoHubCurrentImageIndex { get; set; } = 0;
```

## 使用说明

### 添加组件到桌面

1. 进入桌面编辑模式
2. 从组件库选择 "ZhiJiao Hub"
3. 组件最小尺寸为 2×2，可以自由调整大小

### 切换数据源

1. 选中组件，点击设置按钮
2. 在设置面板中选择 "Image Source"
3. 可选：ClassIsland Hub 或 SECTL Hub

### 配置自动刷新

1. 在设置面板中开启/关闭 "Auto Refresh"
2. 设置刷新间隔（5-1440分钟）

### 浏览图片

- **自动**: 组件会自动轮播图片
- **手动**: 鼠标悬停显示左右箭头，点击切换
- **指示器**: 底部圆点显示当前位置

## 图片源信息

### ClassIsland Hub
- **仓库**: https://github.com/ClassIsland/classisland-hub
- **图片路径**: `/images/`
- **内容**: ClassIsland交流群/频道的有趣内容
- **数量**: 约70张图片

### SECTL Hub
- **仓库**: https://github.com/SECTL/SECTL-hub
- **图片路径**: `/docs/.vuepress/public/images/`
- **内容**: SECTL交流群的趣图
- **数量**: 约78张图片

## 后续优化建议

1. **本地缓存**: 将下载的图片缓存到本地，减少网络请求
2. **缩略图**: 生成缩略图提高加载速度
3. **收藏功能**: 允许用户收藏喜欢的图片
4. **分享功能**: 支持分享图片链接
5. **更多源**: 添加更多教育技术社区图片源

## 构建状态

✅ 构建成功，无错误
