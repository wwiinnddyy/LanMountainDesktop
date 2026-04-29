# Avalonia 12 Full Stack Migration

## Summary

LanMountainDesktop has moved its desktop stack to the Avalonia 12 baseline. The migration covers the main host, Launcher, Plugin SDK, plugin runtime loading policy, official WebView usage, ClassIsland Markdown, FluentAvalonia, FluentIcons, and Material-related dependencies.

## Requirements

### Requirement: Centralized Avalonia 12 dependency baseline

The solution SHALL use central package management for direct Avalonia-facing projects and keep the core UI dependency baseline on Avalonia `12.0.1`.

Required package baseline:

- `Avalonia*` `12.0.1`
- `Avalonia.Controls.WebView` `12.0.0`
- `ClassIsland.Markdown.Avalonia` `12.0.0`
- `FluentAvaloniaUI` `3.0.0-preview1`
- `FluentIcons.Avalonia` `2.1.325`
- `Material.Avalonia` `3.16.0`
- `Material.Icons.Avalonia` `3.0.2`

### Requirement: Official WebView

The host SHALL use `Avalonia.Controls.NativeWebView` for the browser widget and SHALL NOT reference `WebView.Avalonia`, `AvaloniaWebView`, or `.UseDesktopWebView()`.

Windows WebView2 user data configuration SHALL be supplied through `EnvironmentRequested` using `WindowsWebView2EnvironmentRequestedEventArgs.UserDataFolder`.

### Requirement: Plugin SDK v5

The Plugin SDK API baseline SHALL be `5.0.0`. SDK v4 plugins are treated as incompatible until rebuilt.

The SDK SHALL keep the existing public UI extension shape, including `SettingsPageBase` and Avalonia `Control` based desktop components.

### Requirement: Launcher data location stability

Launcher data location configuration SHALL be read from a fixed bootstrap Launcher data directory so resolving the selected data root cannot recursively require resolving itself.

### Requirement: OOBE state compatibility

The Launcher SHALL read current OOBE state from the resolved `Launcher/state` directory and SHALL continue to migrate the legacy `.launcher/state/first_run_completed` marker.

## Acceptance

- `dotnet build LanMountainDesktop.slnx -c Debug` completes with 0 errors.
- `OobeStateServiceTests` pass.
- Full `dotnet test LanMountainDesktop.slnx -c Debug` no longer aborts from `DataLocationResolver` recursion.
- Plugin template defaults to SDK package version `5.0.0` and manifest `apiVersion` `5.0.0`.
- Current developer documentation points to SDK v5 and Avalonia 12.
