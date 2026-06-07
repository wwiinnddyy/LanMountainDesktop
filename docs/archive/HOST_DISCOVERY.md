# 主程序发现配置指南

Launcher 支持灵活的主程序发现机制，可以通过多种方式配置主程序路径。

## 发现优先级

1. **环境变量** (`LMD_HOST_PATH`) - 最高优先级
2. **配置文件** (`host-discovery.json`)
3. **开发模式保存的路径** - 通过调试窗口选择
4. **部署目录** (`app-*`)
5. **开发路径** - 自动搜索解决方案中的 bin 目录
6. **额外配置路径** - 自定义搜索路径
7. **递归搜索** - 如果启用

## 配置方式

### 1. 环境变量

设置 `LMD_HOST_PATH` 环境变量指向主程序可执行文件：

```powershell
$env:LMD_HOST_PATH = "C:\MyApp\LanMountainDesktop.exe"
```

### 2. 配置文件

在应用根目录创建 `host-discovery.json`：

```json
{
  "HostPath": "C:\\Custom\\Path\\LanMountainDesktop.exe",
  "AdditionalPaths": [
    "${AppRoot}/custom",
    "${UserProfile}/dev/build",
    "C:/Program Files/LanMountainDesktop/*"
  ]
}
```

### 3. 开发模式

在错误窗口中按 `Ctrl+Shift+D` 打开调试窗口，启用开发模式并选择自定义路径。路径会自动保存，下次启动时优先使用。

## 路径变量

配置文件支持以下变量：

- `${AppRoot}` - 应用根目录
- `${BaseDirectory}` - Launcher 所在目录
- `${UserProfile}` - 用户主目录
- `${LocalAppData}` - 本地应用数据目录

## 通配符支持

`AdditionalPaths` 支持通配符：

```json
{
  "AdditionalPaths": [
    "C:/Builds/*/LanMountainDesktop.exe",
    "${AppRoot}/versions/*/app.exe"
  ]
}
```

## 递归搜索

启用递归搜索可以自动在子目录中查找主程序：

```csharp
var options = new HostDiscoveryOptions
{
    RecursiveSearch = true,
    MaxRecursionDepth = 3
};
```

注意：递归搜索可能影响启动性能，建议仅在必要时启用。
