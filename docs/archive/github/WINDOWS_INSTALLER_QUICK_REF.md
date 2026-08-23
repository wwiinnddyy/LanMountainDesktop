# 🎯 Windows 安装包 - 快速参考

## 变更摘要

✅ **Windows打包已改为生成 .exe 安装程序**

| 项目 | 值 |
|-----|-----|
| 输出格式 | `*.exe` (Inno Setup安装程序) |
| 文件名格式 | `LanMountainDesktop-Setup-{Version}-{Arch}.exe` |
| 示例 | `LanMountainDesktop-Setup-1.0.0-x64.exe` |
| 支持架构 | x64, x86 |
| 压缩方式 | LZMA2 ultra (35-50% 压缩率) |

## 工作流更新

### 新增步骤

```yaml
- name: Install Inno Setup
  run: choco install innosetup -y --no-progress
  
- name: Build Installer
  run: |
    # 使用iscc.exe编译Inno Setup脚本
    # 生成.exe安装程序
    
- name: Upload Installer
  path: build-installer/*.exe
```

## 文件修改

### 1. `.github/workflows/release.yml`
- ✅ 添加"Install Inno Setup"步骤
- ✅ 添加"Build Installer"步骤（替代旧的"Package"）
- ✅ 添加"Upload Installer"步骤
- ✅ 移除旧的zip压缩逻辑
- ✅ 更新发布说明中的Windows描述

### 2. `LanMountainDesktop/installer/LanMountainDesktop.iss`
- ✅ OutputBaseFilename: `{#MyAppName}-Setup-{#MyAppVersion}-{#MyAppArch}`
- ✅ 添加x86架构支持

### 3. `.github/WINDOWS_INSTALLER_SETUP.md` (新)
- 详细的配置和使用说明

## 安装程序功能

✅ 一键安装
✅ 开始菜单快捷方式
✅ 可选：桌面快捷方式  
✅ 可选：安装后启动应用
✅ 系统卸载功能（控制面板）
✅ 管理员权限保护
✅ LZMA2压缩（内置于exe）

## 测试启动

```bash
# 推送测试版本
git tag v1.0.0-test
git push origin v1.0.0-test

# 监察 GitHub Actions
# 下载 LanMountainDesktop-Setup-1.0.0-x64.exe 
# 双击运行测试
```

## 本地测试

```powershell
# 需要先发布应用
dotnet publish LanMountainDesktop\LanMountainDesktop.csproj `
  -c Release -r win-x64 --self-contained `
  -o publish\windows-x64

# 编译安装程序
$iscc = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
& $iscc /DMyAppVersion=1.0.0 `
  /DPublishDir=.\publish\windows-x64 `
  /DMyOutputDir=.\build-installer `
  /DMyAppArch=x64 `
  .\LanMountainDesktop\installer\LanMountainDesktop.iss

# 运行安装程序
.\build-installer\LanMountainDesktop-Setup-1.0.0-x64.exe
```

## 自定义安装程序

编辑 `LanMountainDesktop/installer/LanMountainDesktop.iss`：

```ini
[Setup]
DefaultDirName={autopf}\{#MyAppName}        ; 安装目录
Compression=lzma2/ultra64                   ; 压缩类型
PrivilegesRequired=admin                    ; 权限要求

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}" ; 文件来源

[Icons]
; 快捷方式位置
Name: "{autoprograms}\{#MyAppName}"         ; 开始菜单
Name: "{autodesktop}\{#MyAppName}"          ; 桌面(可选)

[Dirs]
; 创建目录

[Registry]
; 注册表项
```

## 故障排除

| 问题 | 解决方案 |
|------|--------|
| Inno Setup未找到 | Windows Runner会自动安装，本地需手动: `choco install innosetup` |
| 编译失败 | 检查publish目录是否存在和包含可执行文件 |
| 安装程序损坏 | 检查Inno Setup脚本语法，查看编译日志 |
| 找不到应用 | 安装到: `C:\Program Files\LanMountainDesktop` |

## 相关文档

- 📖 [详细配置指南](./WINDOWS_INSTALLER_SETUP.md)
- 📖 [工作流定义](./.github/workflows/release.yml)
- 📖 [Inno Setup官方文档](https://jrsoftware.org)

---

**状态**: ✅ 已完成并就绪

Windows用户现在将获得标准的.exe安装程序体验！🚀
