# LanMountainDesktop.AirAppSdk

Official SDK for developing AirApps (Lightweight Applications) for LanMountainDesktop.

## What is an AirApp?

AirApp is the next-generation application framework for LanMountainDesktop. It provides a unified development experience for creating:

- **Desktop Components** - Widgets that live on the desktop
- **Window Applications** - Standalone windowed apps
- **Background Services** - Services that run in the background
- **Hybrid Apps** - Apps that combine multiple modes

## Quick Start

### Installation

```bash
# Install the SDK package
dotnet add package LanMountainDesktop.AirAppSdk
```

### Create Your First AirApp

1. **Create a new project**

```bash
dotnet new classlib -n MyFirstAirApp
cd MyFirstAirApp
dotnet add package LanMountainDesktop.AirAppSdk
```

2. **Create the entry point**

```csharp
using LanMountainDesktop.AirAppSdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MyFirstAirApp;

[AirAppEntrance]
public class MyAirApp : AirAppBase
{
    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        // Register a desktop component
        services.AddAirAppComponent<MyWidget>("my-widget", "My Widget");
    }
}
```

3. **Create a widget**

```csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LanMountainDesktop.AirAppSdk;

namespace MyFirstAirApp;

public class MyWidget : AirAppWidgetBase
{
    public MyWidget()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        // Simple widget with a TextBlock
        Content = new TextBlock
        {
            Text = "Hello from AirApp!",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
    }

    protected override void OnAttachedCore()
    {
        // Called when widget is added to desktop
        Context.Logger.Info("My widget attached!");
    }
}
```

4. **Create manifest file** (`airapp.json`)

```json
{
  "id": "com.example.myfirstairapp",
  "name": "My First AirApp",
  "version": "1.0.0",
  "apiVersion": "6.0.0",
  "author": "Your Name",
  "description": "My first AirApp for LanMountainDesktop",
  "entranceAssembly": "MyFirstAirApp.dll",
  "runtime": {
    "mode": "in-process"
  }
}
```

5. **Build the project**

```bash
dotnet build -c Release
```

This will produce a `.laapp` package in `bin/Release/net10.0/MyFirstAirApp.laapp`.

## Core Concepts

### AirAppBase

The entry point for your AirApp. Override `Initialize()` to register components and services:

```csharp
[AirAppEntrance]
public class MyAirApp : AirAppBase
{
    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        // Register components
        services.AddAirAppComponent<MyWidget>("widget-id", "Widget Name");
        
        // Register windows
        services.AddAirAppWindow<MyWindow>("window-id", "Window Name");
        
        // Register your services
        services.AddSingleton<IMyService, MyService>();
    }

    public override async Task OnStartedAsync(IAirAppRuntimeContext context)
    {
        // Runtime initialization
        context.Logger.Info("AirApp started!");
    }
}
```

### Desktop Components

Create widgets that appear on the desktop:

```csharp
public class ClockWidget : AirAppWidgetBase
{
    private TextBlock _timeText;

    public ClockWidget()
    {
        _timeText = new TextBlock();
        Content = _timeText;
        
        // Update every second
        DispatcherTimer.Run(() =>
        {
            _timeText.Text = DateTime.Now.ToString("HH:mm:ss");
            return true;
        }, TimeSpan.FromSeconds(1));
    }

    protected override void OnAppearanceChangedCore(AirAppAppearanceSnapshot snapshot)
    {
        // Respond to theme changes
        _timeText.Foreground = new SolidColorBrush(snapshot.ForegroundColor);
    }
}
```

### Windows

Create standalone windows:

```csharp
public class MyWindow : AirAppWindowBase
{
    public override AirAppWindowDescriptor Descriptor => new()
    {
        Title = "My Window",
        Width = 800,
        Height = 600,
        ChromeMode = AirAppWindowChromeMode.Standard,
        CanResize = true
    };

    public MyWindow()
    {
        Content = new TextBlock { Text = "Hello from window!" };
    }

    public override async Task OnWindowOpeningAsync()
    {
        // Async initialization before window opens
        await LoadDataAsync();
    }
}
```

### Runtime Context

Access runtime services:

```csharp
protected override async Task OnStartedAsync(IAirAppRuntimeContext context)
{
    // Get data directories
    var dataDir = context.DataDirectory;
    var cacheDir = context.CacheDirectory;
    
    // Use services
    var myService = context.Services.GetService<IMyService>();
    
    // Log messages
    context.Logger.Info("AirApp started!");
    
    // Open a window
    await context.OpenWindowAsync("my-window");
    
    // Subscribe to messages
    context.MessageBus.Subscribe("theme-changed", payload =>
    {
        context.Logger.Info("Theme changed!");
    });
}
```

## API Reference

### Core Interfaces

- `IAirApp` - AirApp entry point
- `IAirAppWidget` - Desktop component widget
- `IAirAppWindow` - Window application
- `IAirAppRuntimeContext` - Runtime services and context
- `IAirAppComponentContext` - Component instance context

### Base Classes

- `AirAppBase` - Base implementation of IAirApp
- `AirAppWidgetBase` - Base class for widgets
- `AirAppWindowBase` - Base class for windows

### Configuration

- `AirAppManifest` - Manifest file structure
- `AirAppComponentOptions` - Component registration options
- `AirAppWindowDescriptor` - Window configuration
- `AirAppRuntimeMode` - Runtime isolation modes

### Services

- `IAirAppLogger` - Logging service
- `IAirAppMessageBus` - Inter-app messaging
- `IAirAppAppearanceContext` - Theme and appearance

## Runtime Modes

### In-Process (Default)

Best performance, runs in the host process:

```json
{
  "runtime": {
    "mode": "in-process"
  }
}
```

### Isolated Background

Runs in a separate background process:

```json
{
  "runtime": {
    "mode": "isolated-background"
  }
}
```

### Isolated Window

Runs in a completely isolated window process:

```json
{
  "runtime": {
    "mode": "isolated-window"
  }
}
```

## Packaging

Your AirApp is automatically packaged as a `.laapp` file when you build:

```bash
dotnet build -c Release
```

The package includes:
- All assemblies
- The `airapp.json` manifest
- Any additional resources

## Migration from Plugin SDK v5

If you're migrating from the older Plugin SDK:

1. Update package reference:
   ```xml
   <!-- Old -->
   <PackageReference Include="LanMountainDesktop.PluginSdk" Version="5.0.0" />
   
   <!-- New -->
   <PackageReference Include="LanMountainDesktop.AirAppSdk" Version="6.0.0" />
   ```

2. Update manifest file: `plugin.json` → `airapp.json`

3. Update namespaces:
   ```csharp
   // Old
   using LanMountainDesktop.PluginSdk;
   [PluginEntrance]
   public class Plugin : PluginBase { }
   
   // New
   using LanMountainDesktop.AirAppSdk;
   [AirAppEntrance]
   public class MyAirApp : AirAppBase { }
   ```

4. Update API calls (mostly compatible, minor naming changes)

## Examples

See the `samples/` directory for complete examples:

- **SimpleWidget** - Basic desktop component
- **ClockWidget** - Time display with auto-update
- **WindowApp** - Standalone window application
- **HybridApp** - Component + window combination

## Documentation

- [Full API Documentation](https://docs.lanmountain.com/airapp-sdk)
- [Development Guide](https://docs.lanmountain.com/airapp-dev-guide)
- [Best Practices](https://docs.lanmountain.com/airapp-best-practices)

## Support

- GitHub Issues: https://github.com/LanMountain/LanMountainDesktop/issues
- Discord: https://discord.gg/lanmountain
- Documentation: https://docs.lanmountain.com

## License

MIT License - See LICENSE file for details
