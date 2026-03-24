# VoiceHubLanDesktop

VoiceHub 广播站排期插件，用于 LanMountainDesktop 桌面应用。

## 功能特性

- 📻 **排期显示**：展示 VoiceHub 广播站当日排期歌曲
- 🔄 **自动刷新**：支持自定义刷新间隔（5分钟 ~ 2小时）
- ⚙️ **灵活配置**：可自定义 API 地址、显示选项
- 🌐 **多语言支持**：支持中文和英文

## 安装

将 `.laapp` 包放入 LanMountainDesktop 的插件目录：
```
%LocalAppData%\LanMountainDesktop\Extensions\Plugins\
```

## 配置

在 LanMountainDesktop 设置中找到 "VoiceHub 设置"：

| 选项 | 说明 | 默认值 |
|-----|------|--------|
| API 地址 | VoiceHub 后端 API 地址 | `https://voicehub.lao-shui.top/api/songs/public` |
| 显示点歌人 | 是否显示点歌人信息 | 是 |
| 显示投票数 | 是否显示歌曲投票数 | 否 |
| 刷新间隔 | 自动刷新时间间隔 | 1小时 |

## 组件规格

- **最小尺寸**：3 × 4 网格
- **缩放模式**：等比例缩放
- **放置位置**：桌面

## 开发

### 构建

```bash
cd VoiceHubLanDesktop
dotnet build
```

### 打包

```bash
dotnet pack
# 或使用脚本
../scripts/Pack-PluginPackages.ps1
```

## 技术栈

- .NET 10
- Avalonia UI 11.3.12
- LanMountainDesktop.PluginSdk 4.0.0
- CommunityToolkit.Mvvm 8.2.1

## 许可证

MIT License
