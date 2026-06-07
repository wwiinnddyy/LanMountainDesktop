# 03-设置API详解

设置 API 允许插件添加配置页面和持久化用户设置。

---

## 🎯 API 概览

### 声明式设置

```csharp
services.AddPluginSettingsSection(
    string sectionId,
    string displayName,
    Action<PluginSettingsSectionBuilder> configure,
    string iconKey);
```

### 自定义设置页

```csharp
services.AddPluginSettingsSection<TPage>(
    string sectionId,
    string displayName,
    string iconKey)
    where TPage : SettingsPageBase;
```

---

## 📋 声明式设置详解

### 基本用法

```csharp
services.AddPluginSettingsSection(
    "myplugin-settings",
    "我的设置",
    section => section
        .AddToggle("enabled", "启用", defaultValue: true)
        .AddText("name", "名称", defaultValue: ""),
    iconKey: "Settings");
```

### 设置类型

#### Toggle（开关）

```csharp
.AddToggle(
    key: "auto_update",
    displayName: "自动更新",
    defaultValue: true,
    description: "启动时检查更新")
```

#### Text（文本）

```csharp
.AddText(
    key: "api_key",
    displayName: "API密钥",
    defaultValue: "",
    placeholder: "请输入",
    isPassword: false)
```

#### Number（数值）

```csharp
.AddNumber(
    key: "interval",
    displayName: "刷新间隔",
    defaultValue: 60,
    minimum: 10,
    maximum: 3600,
    increment: 10)
```

#### Select（选择）

```csharp
.AddSelect(
    key: "theme",
    displayName: "主题",
    choices: new[]
    {
        new SettingsOptionChoice("light", "浅色"),
        new SettingsOptionChoice("dark", "深色")
    },
    defaultValue: "light")
```

#### Path（路径）

```csharp
.AddPath(
    key: "save_path",
    displayName: "保存路径",
    defaultValue: "",
    pathType: SettingsPathType.Folder,
    dialogTitle: "选择文件夹")
```

---

## 🔧 读取和保存设置

### 使用 IPluginSettingsService

```csharp
public class MyService
{
    private readonly IPluginSettingsService _settings;
    
    public MyService(IPluginSettingsService settings)
    {
        _settings = settings;
        
        // 读取
        var value = _settings.GetValue<string>("key", "default");
        
        // 保存
        _settings.SetValue("key", "new value");
        
        // 监听变化
        _settings.SettingsChanged += (s, e) =>
        {
            if (e.Key == "key")
            {
                HandleChange(e.NewValue);
            }
        };
    }
}
```

---

## 📚 参考资源

- [IPluginSettingsService 源码](../../LanMountainDesktop.PluginSdk/IPluginSettingsService.cs)
- [03-设置系统集成](../02-核心概念与原理/03-设置系统集成.md)

---

*最后更新：2026年4月*
