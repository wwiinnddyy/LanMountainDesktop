# Runtime Packaging

## Windows

- Windows installers do not bundle the .NET shared runtime.
- `LanMountainDesktop.Launcher.exe` is the package-root bootstrapper and remains Native AOT/self-contained.
- `LanMountainDesktop.exe` and `LanMountainDesktop.AirAppHost.exe` are framework-dependent, RID-specific apps under `app-<version>/`.
- Inno Setup downloads and silently installs the matching .NET 10 Desktop Runtime before continuing:
  - x64 installer: `https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe`
  - x86 installer: `https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x86.exe`
- Launcher runtime probing validates the architecture-matched `Microsoft.NETCore.App 10.*` shared framework before starting framework-dependent processes.

If the launcher returns `dotnet_runtime_missing`, verify the runtime architecture:

```powershell
dotnet --list-runtimes
Test-Path "C:\Program Files\dotnet\shared\Microsoft.NETCore.App"
Test-Path "C:\Program Files (x86)\dotnet\shared\Microsoft.NETCore.App"
```
