# Multi-Platform Build Guide

This document explains how to build LanMontainDesktop for Windows, Linux, and macOS.

## Overview

LanMontainDesktop supports self-contained builds for:
- **Windows**: x64 (64-bit) and x86 (32-bit)
- **Linux**: x64 only (AppImage/snap support planned)
- **macOS**: x64 (Intel) and arm64 (Apple Silicon M1/M2/M3)

## Build Matrices in CI

The GitHub Actions workflow uses a build matrix to automatically build all combinations:

```yaml
Windows builds:  win-x64, win-x86 (on windows-latest)
Linux builds:    linux-x64 (on ubuntu-latest)
macOS builds:    osx-x64, osx-arm64 (on macos-latest)
```

Each build:
- ✅ Restores dependencies
- ✅ Updates version in csproj
- ✅ Publishes with optimizations
- ✅ Creates platform-specific packages
- ✅ Uploads to release artifacts

## Local Building

### Prerequisites

**All Platforms:**
```bash
# Install .NET 10 SDK
dotnet --version  # Should show 10.0.x
```

**Linux (Debian/Ubuntu):**
```bash
# Install required dependencies
sudo apt-get update
sudo apt-get install -y \
    libfontconfig1 \
    libfreetype6 \
    libx11-6 \
    libxrandr2 \
    libxinerama1 \
    libxi6 \
    libxcursor1 \
    libxext6 \
    libxrender1 \
    libxkbcommon-x11-0
```

**macOS:**
```bash
# Xcode Command Line Tools required
xcode-select --install

# Or if you have Homebrew:
brew install dotnet
```

### Building Locally

**Windows (x64):**
```powershell
# Using the PowerShell script
.\LanMontainDesktop\scripts\package.ps1 `
    -RuntimeIdentifier win-x64 `
    -Version 1.0.0

# Or with dotnet directly
dotnet publish LanMontainDesktop/LanMontainDesktop.csproj `
    -c Release -r win-x64 -o ./publish/win-x64 `
    --self-contained -p:PublishSingleFile=true
```

**Windows (x86):**
```powershell
.\LanMontainDesktop\scripts\package.ps1 `
    -RuntimeIdentifier win-x86 `
    -Version 1.0.0
```

**Linux (x64):**
```bash
# Make build script executable
chmod +x scripts/build.sh

# Build
./scripts/build.sh --rid linux-x64 --version 1.0.0

# Output: ./publish/linux-x64/
```

**macOS (Intel x64):**
```bash
chmod +x scripts/build.sh

./scripts/build.sh --rid osx-x64 --version 1.0.0

# Output: ./publish/osx-x64/
```

**macOS (Apple Silicon arm64):**
```bash
chmod +x scripts/build.sh

./scripts/build.sh --rid osx-arm64 --version 1.0.0

# Output: ./publish/osx-arm64/
```

## Build Output

After building, you'll have a self-contained directory with:

```
publish/[rid]/
├── LanMontainDesktop.exe (Windows)
├── LanMontainDesktop (Linux/macOS - executable)
├── libvlc/ (Windows/macOS only)
├── Localization/ (i18n files)
├── Extensions/ (Component extension manifests)
└── Assets/ (Fonts, weather icons, etc.)
```

### Package Creation

**For Windows:**
```bash
# Create zip package
$rid = "win-x64"
$version = "1.0.0"
$dir = "LanMontainDesktop-$version-$rid"
Copy-Item -Path "./publish/$rid" -Destination $dir -Recurse
Compress-Archive -Path $dir -DestinationPath "$dir.zip"
```

**For Linux/macOS:**
```bash
# Create tar.gz package
rid=linux-x64
version=1.0.0
dir="LanMontainDesktop-$version-$rid"
mkdir -p $dir
cp -r ./publish/$rid/* $dir/
tar -czf "$dir.tar.gz" $dir
```

## Cross-Compilation Considerations

### ⚠️ Limitations

- **Windows builds must run on Windows** (or in WSL2 with additional setup)
  - libvlc has platform-specific native libraries
  - Windows-specific dependencies in vcpkg

- **Linux builds must run on Linux**
  - Different library paths and system dependencies

- **macOS builds must run on macOS**
  - Code signing / notarization (if needed) requires macOS

### ✅ Workaround Options

1. **GitHub Actions** (Recommended)
   - Automatically runs on the correct OS for each build
   - No local cross-compilation needed

2. **Docker** (For Linux on any platform)
   - Use container-based build environment
   - Example: `ghcr.io/classisland/philia-build-image:main`

3. **CI/CD Pipeline**
   - Let Actions handle all platform builds
   - Download artifacts locally for testing

## Platform-Specific Notes

### Windows

- Supports both x64 and x86 architectures
- Uses libvlc from `VideoLAN.LibVLC.Windows` NuGet package
- Includes MSVC runtime if needed

**Unsupported:**
- ARM64 (would need separate toolchain)

### Linux

- Tested on Ubuntu 22.04+
- Requires X11 libraries (no Wayland support yet)
- Self-contained includes .NET runtime
- Uses libvlc system libraries or bundled version

**Planned:**
- AppImage format
- Snap package
- .deb packaging

### macOS

- Supports both Intel (x64) and Apple Silicon (arm64)
- Uses libvlc from `VideoLAN.LibVLC.Mac` NuGet package
- Universal binary support (both architectures in one file) - not yet implemented

**Planned:**
- DMG packaging
- Code signing & notarization
- App Store distribution

## Troubleshooting

### Windows Build Fails

```bash
# Clean and retry
dotnet clean LanMontainDesktop/LanMontainDesktop.csproj
dotnet restore
dotnet publish LanMontainDesktop/LanMontainDesktop.csproj -c Release -r win-x64 --self-contained
```

### Linux Build Fails

```bash
# Check dependencies are installed
ldd ./publish/linux-x64/LanMontainDesktop | grep "not found"

# Install missing libraries
sudo apt-get install -y lib[missing-name]
```

### macOS Build Fails

```bash
# Ensure correct .NET version for ARM/Intel
dotnet --version

# Try specifying explicit SDK version
export DOTNET_ROOT=/usr/local/share/dotnet
./scripts/build.sh --rid osx-arm64 --version 1.0.0
```

## Performance & Size

| Platform | RID | Size | Notes |
|----------|-----|------|-------|
| Windows x64 | win-x64 | ~150-200 MB | Includes .NET + libvlc |
| Windows x86 | win-x86 | ~140-190 MB | Smaller footprint |
| Linux x64 | linux-x64 | ~120-170 MB | Minimal deps included |
| macOS Intel | osx-x64 | ~130-180 MB | Intel-optimized |
| macOS Silicon | osx-arm64 | ~120-170 MB | Apple Silicon native |

*Sizes vary based on trimming and included dependencies*

## Optimization Options

For smaller, faster builds, you can adjust publish settings:

```bash
# Aggressive trimming (may break reflection-based features)
-p:PublishTrimmed=true

# Embed debug symbols (larger, but better debugging)
-p:DebugType=embedded

# AOT compilation (Windows only, faster startup)
-p:PublishAot=true

# Single executable (already enabled in workflows)
-p:PublishSingleFile=true
```

## Distribution

After building, distribute the packages:

1. **Windows users**: Download `.zip`, extract, run executable
2. **Linux users**: Download `.tar.gz`, extract, run executable
3. **macOS users**: Download `.tar.gz`, extract, run executable

Plan for future:
- Installer generation (.exe for Windows, .deb for Linux)
- Code signing for macOS
- Auto-update mechanism

## References

- [.NET Runtime Identifiers](https://learn.microsoft.com/dotnet/core/rid-catalog)
- [dotnet publish options](https://learn.microsoft.com/dotnet/core/tools/dotnet-publish)
- [Avalonia Deployment](https://docs.avaloniaui.net/docs/deployment)
- [libvlc Bindings](https://github.com/videolan/libvlcsharp)
