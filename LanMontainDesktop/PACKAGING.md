# Desktop Packaging Guide

## Prerequisites
- Install `.NET SDK 10`
- Windows installer build only:
  - Install `Inno Setup 6` (`ISCC.exe`)

## Local packaging commands

### Windows installer (`win-x64`)
```powershell
.\scripts\package.ps1 -RuntimeIdentifier win-x64 -Version 1.0.1
```

Output:
- Published files: `artifacts/publish/win-x64`
- Installer: `artifacts/installer`

### Linux package (`linux-x64`)
```powershell
pwsh ./scripts/package.ps1 -RuntimeIdentifier linux-x64 -Version 1.0.1
```

Output:
- Published files: `artifacts/publish/linux-x64`
- Zip package: `artifacts/packages/LanMontainDesktop-1.0.1-linux-x64.zip`

### macOS package (`osx-x64`)
```powershell
pwsh ./scripts/package.ps1 -RuntimeIdentifier osx-x64 -Version 1.0.1
```

Output:
- Published files: `artifacts/publish/osx-x64`
- Zip package: `artifacts/packages/LanMontainDesktop-1.0.1-osx-x64.zip`

## Optional script flags
```powershell
# Publish only (skip Windows installer step)
.\scripts\package.ps1 -RuntimeIdentifier win-x64 -SkipInstaller

# Publish only (skip Linux/macOS zip package step)
pwsh ./scripts/package.ps1 -RuntimeIdentifier linux-x64 -SkipArchive
```

## Runtime dependency notes
- Linux build does not bundle a native `libvlc` package from NuGet.
  - Install VLC runtime on target machine, for example:
    - Ubuntu/Debian: `sudo apt install vlc libvlc-dev`
- macOS packaging target in CI is currently `osx-x64`.

## CI workflow
- Workflow file: `.github/workflows/windows-ci.yml`
- Workflow name: `Desktop CI`

Jobs:
- `Validate Build (Windows)` runs on every push and pull request.
- Package flow runs on manual trigger or `v*` tag push:
  - `Resolve Package Version` (single shared version source)
  - `Package (Windows)` (`win-x64` installer)
  - `Package (Linux)` (`linux-x64` zip)
  - `Package (macOS)` (`osx-x64` zip)
- On `v*` tags, `Attach Artifacts to GitHub Release` uploads Windows/Linux/macOS packages to the release.

### Trigger manual packaging
1. Open GitHub Actions.
2. Choose `Desktop CI`.
3. Click `Run workflow`.
4. Optional: set `version` input, for example `1.0.1`.

### Trigger by tag
```powershell
git tag v1.0.1
git push origin v1.0.1
```
