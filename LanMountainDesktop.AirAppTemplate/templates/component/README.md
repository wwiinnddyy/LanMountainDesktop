# LanMountainDesktop.AirApp.ComponentTemplate

A desktop component AirApp for LanMountainDesktop.

## Build

```bash
dotnet build -c Release
```

This will produce a `.laapp` package in `bin/Release/net10.0/`.

## Install

Copy the `.laapp` file to LanMountainDesktop's plugins directory or install via the AirApp Market.

## Development

To test your component during development:

1. Build the project
2. Run LanMountainDesktop with debug mode:
   ```bash
   dotnet run --project path/to/LanMountainDesktop.csproj -- --debug-airapp path/to/your/bin/Debug/net10.0
   ```

## Customize

- Edit `MyWidget.cs` to modify the component UI and behavior
- Edit `airapp.json` to change metadata
- Add more components by creating additional widget classes and registering them in `MyAirApp.cs`
