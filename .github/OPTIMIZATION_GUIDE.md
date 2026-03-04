# 包大小优化指南

## 问题诊断

打包产物过大且缺少.NET运行时的原因分析：

### 🔴 原始问题

1. **缺少代码修剪（Trimming）** - 构建包含了大量未使用的代码
2. **缺少即时编译优化（ReadyToRun）** - 未启用预编译
3. **调试符号未移除** - Release构建包含调试信息
4. **自包含运行时配置不完整** - `--self-contained` 标志但缺少确切配置

## ✅ 已实施的优化

### 1. 工作流发布命令优化（`.github/workflows/release.yml`）

所有三个平台现在都使用以下参数：

```powershell
# Windows (PowerShell)
dotnet publish LanMountainDesktop/LanMountainDesktop.csproj `
  -c Release `
  -o ./publish/windows-${{ matrix.arch }} `
  --self-contained `                      # 包含.NET运行时
  -r win-${{ matrix.arch }} `
  -p:PublishSingleFile=true `             # 单一可执行文件
  -p:SelfContained=true `                 # 明确启用自包含
  -p:DebugType=none `                     # 移除调试信息
  -p:DebugSymbols=false `                 # 移除调试符号
  -p:PublishTrimmed=true `                # 启用代码修剪
  -p:TrimMode=partial `                   # 安全的部分修剪
  -p:PublishReadyToRun=true               # 启用预编译
```

```bash
# Linux/macOS (Bash)
dotnet publish LanMountainDesktop/LanMountainDesktop.csproj \
  -c Release \
  -o ./publish/linux-x64 \
  --self-contained \
  -r linux-x64 \
  -p:PublishSingleFile=true \
  -p:SelfContained=true \
  -p:DebugType=none \
  -p:DebugSymbols=false \
  -p:PublishTrimmed=true \
  -p:TrimMode=partial \
  -p:PublishReadyToRun=true
```

### 2. 项目文件优化（`LanMountainDesktop/LanMountainDesktop.csproj`）

添加了条件化的Release配置（应已执行）：

```xml
<!-- Release build optimizations -->
<PropertyGroup Condition="'$(Configuration)' == 'Release'">
  <PublishSingleFile>true</PublishSingleFile>
  <PublishTrimmed>true</PublishTrimmed>
  <TrimMode>partial</TrimMode>
  <PublishReadyToRun>true</PublishReadyToRun>
  <DebugSymbols>false</DebugSymbols>
  <DebugType>none</DebugType>
</PropertyGroup>

<!-- Self-contained runtime -->
<PropertyGroup Condition="'$(RuntimeIdentifier)' != ''">
  <SelfContained>true</SelfContained>
</PropertyGroup>
```

### 3. 修剪配置文件（`LanMountainDesktop/TrimmerRoots.xml`）

创建了修剪根描述文件，保护以下关键程序集不被修剪：

- **UI Framework**: Avalonia, Avalonia.Controls, Avalonia.Desktop, Avalonia.Themes.Fluent
- **Fluent Design**: FluentAvaloniaUI, FluentIcons
- **Media**: LibVLCSharp, WebView.Avalonia
- **MVVM**: CommunityToolkit.Mvvm
- **System Libraries**: System.Reflection, System.ComponentModel.TypeConverter等

## 📊 预期的优化效果

| 优化项 | 效果 | 预期减少 |
|--------|------|---------|
| **代码修剪** | 移除未使用的代码 | 30-50% |
| **ReadyToRun** | 预编译IL到机器代码 | 10-20% |
| **移除调试符号** | 删除.pdb和调试信息 | 5-15% |
| **SingleFile** | 打包为单一可执行文件 | 10-15% |
| **总体效果** | 综合优化 | **40-60%** |

## 🔧 包大小参考

### 优化前（预期）
- Windows x64: ~500-800 MB
- Linux x64: ~450-700 MB
- macOS x64: ~450-700 MB

### 优化后（预期）
- Windows x64: ~200-350 MB
- Linux x64: ~180-320 MB
- macOS x64: ~180-320 MB

## 🎯 关键指标验证

发布后，检查以下指标确保优化生效：

### 1. 文件大小
```bash
# 检查发布文件大小
ls -lh publish/windows-x64/
ls -lh publish/linux-x64/
ls -lh publish/macos-x64/
```

### 2. 文件数量
```bash
# 单文件模式应该只有一个可执行文件
find publish/windows-x64 -type f | wc -l  # 应该是1
```

### 3. .NET Runtime 验证
```bash
# Windows - 检查dotnet运行时
file publish/windows-x64/LanMountainDesktop.exe
strings publish/windows-x64/LanMountainDesktop.exe | grep -i ".net"

# Linux - 检查elf二进制
file publish/linux-x64/LanMountainDesktop
```

### 4. 依赖检查
```bash
# 验证没有外部.NET依赖
ldd ./publish/linux-x64/LanMountainDesktop | grep -i "not found"  # 不应该有输出

# Windows - 检查是否依赖系统.NET
dumpbin /imports publish/windows-x64/LanMountainDesktop.exe | grep -i mscoree
```

## ⚙️ 手动本地测试（可选）

在本地测试构建优化：

```bash
# Windows
dotnet publish LanMountainDesktop/LanMountainDesktop.csproj `
  -c Release `
  -r win-x64 `
  --self-contained `
  -p:PublishSingleFile=true `
  -p:PublishTrimmed=true `
  -p:TrimMode=partial `
  -p:PublishReadyToRun=true `
  -p:DebugType=none `
  -p:DebugSymbols=false `
  -o ./test-publish

# 检查输出大小
dir /s test-publish
```

```bash
# Linux/macOS
dotnet publish LanMountainDesktop/LanMountainDesktop.csproj \
  -c Release \
  -r linux-x64 \
  --self-contained \
  -p:PublishSingleFile=true \
  -p:PublishTrimmed=true \
  -p:TrimMode=partial \
  -p:PublishReadyToRun=true \
  -p:DebugType=none \
  -p:DebugSymbols=false \
  -o ./test-publish

# 检查输出
du -sh test-publish/
find test-publish -type f
```

## 🚀 CI/CD 发布测试

1. 推送测试标签触发Release工作流：
   ```bash
   git tag v1.0.0-optimization-test
   git push origin v1.0.0-optimization-test
   ```

2. 在GitHub Actions中监视日志，检查：
   - ✅ 发布步骤是否成功
   - ✅ 打包步骤是否成功
   - ✅ Artifacts是否已上传

3. 下载发布的包并验证大小和完整性

## ⚠️ 注意事项

### 修剪相关

1. **TrimMode=partial** 使用，比fully safer但仍可能移除需要的代码
2. 如果遇到运行时错误（如缺少类型或方法），可能是过度修剪
3. TrimmerRoots.xml 中已保护了主要的Avalonia和依赖库

### 自包含相关

1. 自包含包会包含完整的.NET运行时（增加大小）
2. 优势是用户无需安装.NET运行时
3. 如果想要更小的包，可以改用依赖框架的模式（需要系统.NET 10）

### 平台特定

- **arm64 macOS**: 优化一样有效
- **x86 Windows**: 也会应用相同的优化
- **Linux**: 所有优化都适用

## 📝 故障排除

### 发布后应用无法启动

原因：过度修剪导致必要的代码被移除

解决方案：
1. 查看TrimmerRoots.xml，确认相关程序集被保护
2. 检查应用日志寻找MissingMethod或MissingType异常
3. 向TrimmerRoots.xml添加需要的程序集

### 包仍然很大

原因：
1. PublishTrimmed 可能未成功应用
2. ReadyToRun 可能存在问题

解决方案：
1. 检查构建日志中的警告
2. 确认 .csproj 配置生效
3. 验证TrimMode设置

### 自包含包找不到运行时

原因：`--self-contained` 未正确应用

解决方案：
1. 检查发布命令是否包含 `--self-contained`
2. 确认 `-r` 运行时标识符正确（win-x64, linux-x64, osx-x64等）
3. 检查工作流日志是否有错误

## 参考文档

- [MSBuild 发布选项](https://learn.microsoft.com/en-us/dotnet/core/deploying/publish-options-msbuild)
- [.NET 应用修剪](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/trim-self-contained)
- [Avalonia 打包指南](https://docs.avaloniaui.net/docs/getting-started/ide-support/jetbrains-rider)
