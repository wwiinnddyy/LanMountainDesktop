# Launcher AOT 单文件发布指南

## 什么是 AOT？

AOT（Ahead-of-Time）编译将 .NET 代码在构建时直接编译为本地机器码，而不是在运行时通过 JIT 编译。

### AOT 的优势

| 特性 | JIT 模式 | AOT 模式 |
|------|---------|---------|
| 启动速度 | 慢（需要编译） | 快（直接执行） |
| 依赖文件 | 多（.dll, runtimeconfig.json） | 少（单文件） |
| 需要 .NET Runtime | 是 | 否 |
| 文件体积 | 小 | 稍大（但单文件更方便） |
| 反编译难度 | 容易 | 困难 |

## 发布方式

### 方式一：使用 PowerShell 脚本（推荐）

```powershell
# 默认发布（win-x64，单文件，自包含）
.\scripts\Publish-AOT.ps1

# 指定运行时
.\scripts\Publish-AOT.ps1 -RuntimeIdentifier win-x64

# 不压缩（体积更大但启动更快）
.\scripts\Publish-AOT.ps1 -Compress:$false
```

### 方式二：使用 dotnet CLI

```bash
# 基本 AOT 发布
dotnet publish LanMountainDesktop.Launcher/LanMountainDesktop.Launcher.csproj `
  -c Release `
  -r win-x64 `
  --self-contained `
  -p:PublishAot=true `
  -p:PublishSingleFile=true `
  -p:EnableCompressionInSingleFile=true

# 输出目录
# bin/Release/net10.0/win-x64/publish/
```

### 方式三：使用 MSBuild

```bash
msbuild LanMountainDesktop.Launcher/LanMountainDesktop.Launcher.csproj `
  /t:Publish `
  /p:Configuration=Release `
  /p:RuntimeIdentifier=win-x64 `
  /p:PublishAot=true `
  /p:PublishSingleFile=true
```

## 支持的运行时

| 运行时标识符 | 说明 |
|-------------|------|
| `win-x64` | Windows 64位（推荐） |
| `win-x86` | Windows 32位 |
| `win-arm64` | Windows ARM64 |
| `linux-x64` | Linux 64位 |
| `linux-arm64` | Linux ARM64 |
| `osx-x64` | macOS 64位 |
| `osx-arm64` | macOS ARM64 (Apple Silicon) |

## 文件体积对比

### 普通发布（非 AOT）
```
LanMountainDesktop.Launcher.exe       150 KB
LanMountainDesktop.Launcher.dll       200 KB
Avalonia.dll                          1.2 MB
...（数十个依赖文件）
总计: ~15 MB
```

### AOT 单文件发布
```
LanMountainDesktop.Launcher.exe       8-12 MB（单文件，包含所有依赖）
```

## 注意事项

### 1. 修剪（Trimming）

AOT 会自动移除未使用的代码以减小体积。某些反射代码可能需要特殊处理：

```csharp
// 如果类型被反射使用，需要保留
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public class MyClass { }
```

### 2. Avalonia 兼容性

- ✅ Avalonia 11.x 完全支持 AOT
- ✅ 使用 Compiled Bindings（已在项目中启用）
- ✅ 避免动态 XAML 加载

### 3. Json 序列化

使用 `JsonSerializer` 时需要源生成器：

```csharp
[JsonSerializable(typeof(MyType))]
internal partial class MyJsonContext : JsonSerializerContext { }
```

### 4. 单文件特殊处理

某些文件需要嵌入到单文件中：

```xml
<ItemGroup>
  <EmbeddedResource Include="Assets\logo.ico" />
</ItemGroup>
```

## 故障排除

### 发布失败

1. **检查 .NET SDK 版本**
   ```bash
   dotnet --version  # 需要 10.0 或更高
   ```

2. **安装 AOT 工作负载**
   ```bash
   dotnet workload install wasm-tools  # 如果需要 WebAssembly AOT
   ```

3. **Visual Studio 要求**
   - 需要 VS 2022 17.8+ 或 VS Code + C# Dev Kit

### 运行时错误

1. **缺少类型**
   - 在 `.csproj` 中添加 `<TrimmerRootAssembly>`

2. **反射失败**
   - 使用 `[DynamicallyAccessedMembers]` 标记

3. **DllNotFoundException**
   - 确保所有 native 库都包含在发布中

## 性能对比

| 指标 | JIT | AOT | 提升 |
|------|-----|-----|------|
| 启动时间 | 2-3 秒 | 0.5-1 秒 | 2-3x |
| 内存占用 | 较高 | 较低 | 20-30% |
| 首次响应 | 慢 | 快 | 显著 |

## 推荐配置

对于 Launcher 项目，推荐使用以下配置：

```xml
<PublishAot>true</PublishAot>
<PublishTrimmed>true</PublishTrimmed>
<TrimMode>partial</TrimMode>
<SelfContained>true</SelfContained>
<PublishSingleFile>true</PublishSingleFile>
<IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
<EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
```

这样发布的结果：
- ✅ 单文件可执行
- ✅ 无需 .NET Runtime
- ✅ 启动速度快
- ✅ 文件体积合理（8-12 MB）
