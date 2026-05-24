# Runtime Packaging Fix

Windows releases use the launcher as the only self-contained bootstrapper. The
desktop host and AirAppHost are framework-dependent and rely on an
architecture-matched .NET 10 Desktop Runtime installed by the Inno setup flow.

Acceptance:

- Windows installer payload does not bundle .NET shared runtime files.
- Inno Setup downloads and silently installs the matching .NET 10 Desktop Runtime.
- Launcher blocks framework-dependent host startup with `dotnet_runtime_missing` when the runtime is unavailable.
- AirAppHost startup uses packaged executables or an explicit architecture-matched dotnet host for DLL fallback.
