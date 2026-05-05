# DotNetCampus.InkCanvas Avalonia 12 Compatibility Fork

This source is vendored from `dotnet-campus/DotNetCampus.InkCanvas` at commit
`e4383cadc3ae206dd96f5b72ba889a007ebc44fa`, matching the
`DotNetCampus.AvaloniaInkCanvas` 1.0.1 NuGet package previously used by the app.

The local project keeps the assembly name `DotNetCampus.AvaloniaInkCanvas` so the
host code can continue using the existing namespaces and APIs.

Local compatibility changes:

- Replace Avalonia 11 `Visual.VisualRoot` render scaling access with
  `TopLevel.GetTopLevel(this)?.RenderScaling`.
- Reference Avalonia 12 packages from a local project instead of the
  Avalonia 11-targeted NuGet package.
- Import the Avalonia 12 optional feature extension namespace for Skia custom
  drawing operations.
