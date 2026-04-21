# Launcher 打包分发指南

## 目录结构

打包给用户的 Launcher 应该包含以下结构：

```
LanMountainDesktop/
├── LanMountainDesktop.Launcher.exe    # 启动器可执行文件
├── LanMountainDesktop.Launcher.dll    # 启动器依赖
├── ...                                 # 其他启动器依赖文件
├── app-1.0.0/                         # 主程序部署目录
│   ├── LanMountainDesktop.exe         # 主程序可执行文件
│   ├── LanMountainDesktop.dll         # 主程序依赖
│   ├── version.json                   # 版本信息文件
│   └── .current                       # 当前版本标记文件
└── plugins/                           # 插件目录（可选）
```

## 打包步骤

### 1. 构建 Launcher

```bash
dotnet build LanMountainDesktop.Launcher/LanMountainDesktop.Launcher.csproj -c Release
```

### 2. 构建主程序

```bash
dotnet build LanMountainDesktop/LanMountainDesktop.csproj -c Release
```

### 3. 创建部署目录

```powershell
# 创建版本目录
New-Item -ItemType Directory -Path "dist/app-1.0.0" -Force

# 复制主程序文件
Copy-Item "LanMountainDesktop/bin/Release/net10.0/*" "dist/app-1.0.0/" -Recurse

# 创建版本标记
New-Item -ItemType File -Path "dist/app-1.0.0/.current" -Force
```

### 4. 复制 Launcher

```powershell
# 复制启动器文件
Copy-Item "LanMountainDesktop.Launcher/bin/Release/net10.0/*" "dist/" -Recurse
```

### 5. 创建安装包

可以使用以下工具创建安装包：
- **Inno Setup** - Windows 安装程序
- **WiX Toolset** - Windows Installer
- **MSIX** - Windows 应用包
- **Zip** - 便携版

## 用户数据存储位置

Launcher 会将用户配置存储在以下位置：

```
%LOCALAPPDATA%\LanMountainDesktop\.launcher\
├── devmode.config              # 开发模式状态
└── custom-host-path.config     # 自定义主程序路径
```

这些文件：
- **不会**随应用更新而删除
- **不会**随应用卸载而删除（除非用户手动清理）
- 在重装应用后会自动恢复之前的配置

## 生产环境行为

### 正常启动流程

1. 用户双击 `LanMountainDesktop.Launcher.exe`
2. Launcher 查找 `app-*` 目录中的主程序
3. 启动主程序并传递版本信息
4. 主程序显示正确的版本和开发代号

### 更新流程

1. 新版本下载到 `app-{new-version}/`
2. 创建 `.current` 标记指向新版本
3. 旧版本标记为 `.destroy`
4. 下次启动时自动使用新版本

## 开发环境配置

### 启用开发模式

1. 启动 Launcher，如果找不到主程序会显示错误窗口
2. 按 `Ctrl+Shift+D` 打开调试窗口
3. 勾选"启用开发模式"
4. 选择自定义主程序路径
5. 关闭窗口，配置会自动保存

### 开发模式优先级

开发模式的配置**不会**影响生产环境：
- 生产环境优先使用 `app-*` 目录
- 开发模式仅在找不到部署目录时生效
- 开发模式配置保存在用户数据目录，不影响其他用户

## 故障排除

### Launcher 找不到主程序

1. 检查 `app-*` 目录是否存在
2. 检查 `.current` 标记文件是否存在
3. 检查主程序可执行文件是否存在
4. 查看 `%LOCALAPPDATA%\LanMountainDesktop\.launcher\` 下的配置

### 版本信息不正确

1. 检查 `app-*/version.json` 是否存在
2. 检查 `version.json` 内容是否正确
3. 重新构建主程序生成新的 `version.json`

### 开发模式配置丢失

1. 检查 `%LOCALAPPDATA%\LanMountainDesktop\.launcher\` 目录权限
2. 检查磁盘空间是否充足
3. 手动删除配置目录后重新配置
