# 故障排除指南

> LanMountainDesktop 常见问题和解决方案

## 目录

- [构建问题](#构建问题)
- [运行时问题](#运行时问题)
- [Launcher 问题](#launcher-问题)
- [更新问题](#更新问题)
- [插件问题](#插件问题)
- [性能问题](#性能问题)
- [平台特定问题](#平台特定问题)

## 构建问题

### 问题: 编译错误 - 找不到 Windows.Win32 命名空间

**症状:**
```
error CS0246: The type or namespace name 'Windows' could not be found
```

**原因:** CsWin32 尚未生成 P/Invoke 代码

**解决方案:**
```bash
# 清理并重新构建
dotnet clean
dotnet restore
dotnet build
```

首次构建时 CsWin32 会自动生成代码,第二次构建应该成功。

---

### 问题: NuGet 包还原失败

**症状:**
```
error NU1102: Unable to find package 'XXX' with version (>= X.X.X)
```

**解决方案:**
```bash
# 清理 NuGet 缓存
dotnet nuget locals all --clear

# 强制还原
dotnet restore --force

# 重新构建
dotnet build
```

---

### 问题: 构建时提示 SDK 版本不匹配

**症状:**
```
error NETSDK1045: The current .NET SDK does not support targeting .NET 10.0
```

**解决方案:**
```bash
# 检查当前 SDK 版本
dotnet --version

# 应该显示 10.x.x
# 如果不是,请安装 .NET 10 SDK
```

下载地址: https://dotnet.microsoft.com/download/dotnet/10.0

---

### 问题: Avalonia 设计器无法加载

**症状:** XAML 预览显示错误或空白

**解决方案:**
1. 重启 IDE
2. 清理并重新构建项目
3. 检查 Avalonia 版本是否一致

```bash
dotnet clean
dotnet build
```

## 运行时问题

### 问题: 应用启动后立即崩溃

**诊断步骤:**

1. **检查日志文件:**
```
Windows: %LOCALAPPDATA%\LanMountainDesktop\logs\
Linux: ~/.local/share/LanMountainDesktop/logs/
macOS: ~/Library/Application Support/LanMountainDesktop/logs/
```

2. **以调试模式运行:**
```bash
dotnet run --project LanMountainDesktop/LanMountainDesktop.csproj
```

3. **检查依赖:**
```bash
# Windows: 确保安装了 .NET 10 Desktop Runtime
# Linux: 确保安装了必要的图形库
```

---

### 问题: 窗口无法显示或黑屏

**可能原因:**
- 显卡驱动问题
- 渲染模式不兼容

**解决方案:**

1. **切换渲染模式** (编辑配置文件):
```json
{
  "Win32RenderingMode": 1  // 尝试不同的值: 0, 1, 2, 3, 4
}
```

2. **禁用硬件加速:**
```bash
# 设置环境变量
set AVALONIA_RENDERING_MODE=Software
```

---

### 问题: 单实例锁定失败

**症状:** 提示"应用已在运行"但实际没有

**解决方案:**
```bash
# Windows
taskkill /F /IM LanMountainDesktop.exe

# Linux/macOS
pkill -9 LanMountainDesktop
```

如果问题持续,删除锁文件:
```
Windows: %TEMP%\LanMountainDesktop.lock
Linux: /tmp/LanMountainDesktop.lock
macOS: /tmp/LanMountainDesktop.lock
```

---

### 问题: 设置无法保存

**症状:** 修改设置后重启应用,设置恢复默认

**诊断:**
```bash
# 检查设置文件是否存在
# Windows: %LOCALAPPDATA%\LanMountainDesktop\settings.json
# Linux: ~/.local/share/LanMountainDesktop/settings.json
# macOS: ~/Library/Application Support/LanMountainDesktop/settings.json
```

**解决方案:**
1. 检查文件权限
2. 检查磁盘空间
3. 删除损坏的设置文件 (会重置为默认)

## Launcher 问题

### 问题: Launcher 找不到主程序

**症状:**
```
找不到有效的 LanMountainDesktop 版本，可能是安装已损坏。
```

**诊断:**
```bash
# 检查目录结构
ls "C:\Program Files\LanMountainDesktop\"

# 应该看到:
# - LanMountainDesktop.Launcher.exe
# - app-{version}/
```

**解决方案:**

1. **检查 app-* 目录是否存在:**
```bash
ls "C:\Program Files\LanMountainDesktop\app-*"
```

2. **检查主程序是否存在:**
```bash
ls "C:\Program Files\LanMountainDesktop\app-{version}\LanMountainDesktop.exe"
```

3. **重新安装应用**

---

### 问题: OOBE 窗口重复出现

**症状:** 每次启动都显示欢迎页面

**原因:** OOBE 完成标记文件丢失或无法创建

**解决方案:**
```bash
# 手动创建标记文件
# Windows:
New-Item -ItemType File -Path "$env:LOCALAPPDATA\LanMountainDesktop\.launcher\state\first_run_completed"

# Linux/macOS:
mkdir -p ~/.local/share/LanMountainDesktop/.launcher/state
touch ~/.local/share/LanMountainDesktop/.launcher/state/first_run_completed
```

---

### 问题: Splash 窗口卡住不消失

**症状:** 启动动画一直显示,主程序无法启动

**诊断:**
```bash
# 检查主程序是否启动
# Windows:
tasklist | findstr LanMountainDesktop

# Linux/macOS:
ps aux | grep LanMountainDesktop
```

**解决方案:**
1. 强制关闭 Launcher
2. 直接运行主程序测试:
```bash
"C:\Program Files\LanMountainDesktop\app-{version}\LanMountainDesktop.exe"
```
3. 检查日志文件

## 更新问题

### 问题: 更新下载失败

**症状:**
```
Failed to download update: The remote server returned an error
```

**可能原因:**
- 网络连接问题
- GitHub API 限流
- 代理设置问题

**解决方案:**

1. **检查网络连接:**
```bash
# 测试 GitHub 连接
curl https://api.github.com/repos/YourOrg/LanMountainDesktop/releases/latest
```

2. **配置代理** (如果需要):
```bash
# 设置环境变量
set HTTP_PROXY=http://proxy.example.com:8080
set HTTPS_PROXY=http://proxy.example.com:8080
```

3. **手动下载更新:**
- 访问 GitHub Releases 页面
- 下载安装包
- 重新安装

---

### 问题: 更新签名验证失败

**症状:**
```
Signature verification failed
```

**原因:**
- 文件损坏
- 公钥不匹配
- 文件被篡改

**解决方案:**

1. **删除损坏的更新文件:**
```bash
# Windows:
Remove-Item "$env:LOCALAPPDATA\LanMountainDesktop\.launcher\update\incoming\*"

# Linux/macOS:
rm -rf ~/.local/share/LanMountainDesktop/.launcher/update/incoming/*
```

2. **重新下载更新**

3. **如果问题持续,重新安装应用**

---

### 问题: 更新后应用无法启动

**症状:** 更新完成后,应用启动失败或崩溃

**解决方案:**

1. **版本回退:**
```bash
LanMountainDesktop.Launcher.exe update rollback
```

2. **检查更新快照:**
```bash
# Windows:
ls "$env:LOCALAPPDATA\LanMountainDesktop\.launcher\snapshots\"

# 查看快照内容
cat "$env:LOCALAPPDATA\LanMountainDesktop\.launcher\snapshots\{snapshot-id}.json"
```

3. **手动切换版本:**
```bash
# 删除新版本的 .current 标记
Remove-Item "C:\Program Files\LanMountainDesktop\app-{new}\\.current"

# 添加 .current 到旧版本
New-Item -ItemType File -Path "C:\Program Files\LanMountainDesktop\app-{old}\\.current"
```

---

### 问题: 增量更新文件哈希不匹配

**症状:**
```
File hash mismatch for 'XXX.dll'
```

**原因:**
- 文件下载不完整
- 文件损坏

**解决方案:**

1. **删除部分下载的更新:**
```bash
# 删除标记为 .partial 的目录
Remove-Item "C:\Program Files\LanMountainDesktop\app-*" -Recurse -Force -Include *.partial
```

2. **清理更新缓存:**
```bash
Remove-Item "$env:LOCALAPPDATA\LanMountainDesktop\.launcher\update\incoming\*"
```

3. **重新下载更新**

## 插件问题

### 问题: 插件无法加载

**症状:** 插件列表中看不到已安装的插件

**诊断:**
```bash
# 检查插件目录
ls "C:\Program Files\LanMountainDesktop\plugins\"

# 检查插件清单
cat "C:\Program Files\LanMountainDesktop\plugins\{plugin-id}\plugin.json"
```

**解决方案:**

1. **检查插件兼容性:**
- 插件 SDK 版本是否匹配
- 插件是否支持当前平台

2. **重新安装插件:**
```bash
LanMountainDesktop.Launcher.exe plugin install <path-to-plugin.laapp>
```

3. **检查日志文件** 查看插件加载错误

---

### 问题: 插件安装失败

**症状:**
```
Failed to install plugin: Invalid package format
```

**可能原因:**
- `.laapp` 文件损坏
- 插件包格式不正确
- 权限不足

**解决方案:**

1. **验证插件包:**
```bash
# .laapp 实际上是 ZIP 文件
unzip -t plugin.laapp
```

2. **检查权限:**
```bash
# 以管理员身份运行 Launcher
```

3. **手动解压安装:**
```bash
# 解压到插件目录
unzip plugin.laapp -d "C:\Program Files\LanMountainDesktop\plugins\{plugin-id}"
```

---

### 问题: 插件更新失败

**症状:** 插件升级队列处理失败

**解决方案:**

1. **清理升级队列:**
```bash
Remove-Item "$env:LOCALAPPDATA\LanMountainDesktop\.launcher\plugin-upgrades\*"
```

2. **手动更新插件:**
- 卸载旧版本
- 安装新版本

## 性能问题

### 问题: CPU 占用过高

**可能原因:**
- 渲染模式不当
- 组件更新频率过高
- 内存泄漏

**诊断:**
```bash
# Windows: 使用任务管理器查看详细信息
# Linux: top 或 htop
# macOS: Activity Monitor
```

**解决方案:**

1. **切换渲染模式** (参见"窗口无法显示"部分)

2. **禁用不必要的组件:**
- 减少桌面组件数量
- 降低组件更新频率

3. **检查是否有死循环或资源泄漏**

---

### 问题: 内存占用过高

**诊断:**
```bash
# 检查内存使用情况
# Windows: 任务管理器
# Linux: free -h
# macOS: Activity Monitor
```

**解决方案:**

1. **重启应用**

2. **减少组件数量**

3. **检查插件是否有内存泄漏**

4. **更新到最新版本** (可能包含内存优化)

---

### 问题: 启动速度慢

**可能原因:**
- 插件过多
- 磁盘 I/O 慢
- 首次启动需要初始化

**解决方案:**

1. **禁用不必要的插件**

2. **使用 SSD**

3. **清理缓存:**
```bash
Remove-Item "$env:LOCALAPPDATA\LanMountainDesktop\cache\*" -Recurse
```

## 平台特定问题

### Windows

#### 问题: WebView2 缺失

**症状:**
```
Microsoft Edge WebView2 Runtime is required
```

**解决方案:**
1. 下载并安装 WebView2 Runtime:
   https://go.microsoft.com/fwlink/p/?LinkId=2124703

2. 或使用安装包自动安装

---

#### 问题: 与窗口美化工具冲突

**症状:** 窗口显示异常、崩溃

**已知冲突工具:**
- Mica For Everyone
- TranslucentTB
- 其他修改窗口材质的工具

**解决方案:**
将 LanMountainDesktop 添加到这些工具的排除列表中。

---

### Linux

#### 问题: 缺少图形库依赖

**症状:**
```
error while loading shared libraries: libXXX.so
```

**解决方案:**

**Debian/Ubuntu:**
```bash
sudo apt install libx11-6 libice6 libsm6 libfontconfig1
```

**Fedora/RHEL:**
```bash
sudo dnf install libX11 libICE libSM fontconfig
```

**Arch Linux:**
```bash
sudo pacman -S libx11 libice libsm fontconfig
```

---

#### 问题: Wayland 兼容性

**症状:** 在 Wayland 下运行异常

**解决方案:**
```bash
# 强制使用 X11
export GDK_BACKEND=x11
./LanMountainDesktop.Launcher
```

或通过 XWayland 运行 (不保证所有功能正常)。

---

### macOS

#### 问题: 应用无法打开 - "来自身份不明的开发者"

**解决方案:**
```bash
# 移除隔离属性
xattr -cr /Applications/LanMountainDesktop.app
```

或在"系统偏好设置" > "安全性与隐私"中允许。

---

#### 问题: 权限问题

**症状:** 无法访问某些目录或功能

**解决方案:**
在"系统偏好设置" > "安全性与隐私" > "隐私"中授予必要权限:
- 文件和文件夹
- 辅助功能 (如果需要)

## 获取帮助

如果以上方案都无法解决问题:

1. **查看日志文件** (包含详细错误信息)
2. **搜索 GitHub Issues** - 可能已有解决方案
3. **提交新 Issue** - 包含:
   - 操作系统和版本
   - 应用版本
   - 详细错误信息
   - 重现步骤
   - 日志文件 (如果相关)

**GitHub Issues:** https://github.com/YourOrg/LanMountainDesktop/issues

## 相关文档

- [开发文档](DEVELOPMENT.md)
- [Launcher 架构](LAUNCHER.md)
- [更新系统](UPDATE_SYSTEM.md)
- [构建和部署](BUILD_AND_DEPLOY.md)
