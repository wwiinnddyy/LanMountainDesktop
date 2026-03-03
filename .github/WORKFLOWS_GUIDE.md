# GitHub CI/CD Workflow Setup Guide

## Overview

This document describes the CI/CD workflows configured for LanMontainDesktop. These workflows are designed to maintain code quality, automate testing, and streamline the release process.

## Workflows

### 1. Build & Test (`build.yml`)
**Trigger:** Every push/PR to main branches, or manual dispatch

**What it does:**
- Builds both LanMontainDesktop and RecommendationBackend in Debug and Release modes
- Runs unit tests (if available)
- Uploads build artifacts for inspection
- Runs on Windows (windows-latest)

**Branch Coverage:** main, master, dev, develop

### 2. Code Quality (`code-quality.yml`)
**Trigger:** Pull requests and pushes to main branches, or manual dispatch

**What it does:**
- Builds projects with analysis
- Checks code formatting using `dotnet format`
- (Optional) Can integrate with Qodana for professional code analysis

**Branch Coverage:** main, master, dev, develop

**Optional: Qodana Integration**
Uncomment the Qodana step in the workflow and add your token as a secret:
```bash
# In GitHub > Settings > Secrets > Actions
QODANA_TOKEN=your_token_here
QODANA_ENDPOINT=https://qodana.cloud
```

### 3. Release & Publish (`release.yml`)
**Trigger:** Push git tags (v1.0.0, release-1.0.0), or manual workflow dispatch

**What it does:**
- Builds for **Windows** (x64, x86) - self-contained executables
- Builds for **Linux** (x64) - tar.gz packages
- Builds for **macOS** (x64, arm64) - universal support
- Publishes optimized release builds for all platforms
- Generates GitHub Release with all platform artifacts
- Supports pre-release versions

**Supported Platforms:**
| Platform | Architectures | Output Format | Status |
|----------|---------------|---------------|--------|
| Windows | x64, x86 | .zip | ✅ Full support |
| Linux | x64 | .tar.gz | ✅ Full support |
| macOS | x64, arm64 (Apple Silicon) | .tar.gz | ✅ Full support |

**Build Scripts:**
- Windows: Uses PowerShell (`LanMontainDesktop\scripts\package.ps1`)
- Linux/macOS: Uses Bash (`scripts/build.sh`)

**Usage:**

*Create a full release for all platforms:*
```bash
git tag v1.0.0
git push origin v1.0.0
# Automatically triggers Windows + Linux + macOS builds
```

*Manual trigger with selective platforms:*
Go to GitHub > Actions > Release & Publish > Run workflow
- Specify version: `1.0.0`
- Toggle build targets as needed:
  - ✅ Build Windows (x64/x86)
  - ✅ Build Linux (x64)
  - ✅ Build macOS (x64/arm64)
- Check pre-release option if needed

### 4. Issue Management (`issue-management.yml`)
**Trigger:** Daily at 1:30 AM UTC, or manual dispatch

**What it does:**
- Automatically marks inactive issues as "stale"
- Closes old stale issues/PRs after grace period
- Issues with `need-more-info` or `waiting-for-response` labels
- Grace period: 14 days to stale, 7 days before close
- PR grace period: 21 days to stale, 14 days before close

## Repository Secrets & Configuration

To enable all workflows, configure these GitHub secrets:

### Required
None - the workflows use default GitHub token

### Optional (for enhanced features)
1. **Qodana Integration**
   - Go to GitHub Settings > Secrets > Actions
   - Add `QODANA_TOKEN` from https://qodana.cloud

## Local Development Setup

To align with CI workflows, set up your local environment:

```bash
# Install .NET 10 SDK
# https://dotnet.microsoft.com/download/dotnet/10.0

# Restore dependencies
dotnet restore

# Build (like CI does)
dotnet build LanMontainDesktop/LanMontainDesktop.csproj
dotnet build LanMontainDesktop.RecommendationBackend/LanMontainDesktop.RecommendationBackend.csproj

# Format code locally (required by CI)
dotnet format

# Run tests
dotnet test LanMontainDesktop/LanMontainDesktop.csproj
dotnet test LanMontainDesktop.RecommendationBackend/LanMontainDesktop.RecommendationBackend.csproj

# Alternative: Use local build scripts (Linux/macOS)
./scripts/build.sh --rid linux-x64 --version 1.0.0
./scripts/build.sh --rid osx-x64 --version 1.0.0

# Or on Windows with the PowerShell script
./LanMontainDesktop/scripts/package.ps1 -RuntimeIdentifier win-x64 -Version 1.0.0
```

### Cross-Platform Build Scripts

**Linux / macOS:**
```bash
# Make script executable first
chmod +x scripts/build.sh

# Build for Linux
./scripts/build.sh --rid linux-x64 --version 1.0.0

# Build for macOS x64
./scripts/build.sh --rid osx-x64 --version 1.0.0

# Build for macOS ARM64 (Apple Silicon)
./scripts/build.sh --rid osx-arm64 --version 1.0.0

# Full help
./scripts/build.sh --help
```

**Windows:**
```powershell
# Using PowerShell script
.\LanMontainDesktop\scripts\package.ps1 -RuntimeIdentifier win-x64 -Version 1.0.0

# Or use dotnet directly
dotnet publish LanMontainDesktop/LanMontainDesktop.csproj `
    -c Release -r win-x64 -o ./publish/win-x64 `
    -p:PublishSingleFile=true --self-contained
```

## Pull Request Process

1. **Create a branch** from `dev` or `develop`
   ```bash
   git checkout -b feature/your-feature
   ```

2. **Make your changes** and test locally
   ```bash
   dotnet build
   dotnet format  # Important!
   dotnet test
   ```

3. **Push and create a PR**
   - The PR template will guide you
   - Fill out all required sections
   - Link related issues

4. **Checks will run automatically:**
   - CI builds in Debug & Release
   - Code quality checks
   - Code formatting validation

5. **Review and merge**
   - Address any feedback
   - Wait for all checks to pass
   - Merge to `dev` or `main` as appropriate

## Release Process

### For Stable Releases
```bash
# On main branch
git tag v1.0.0
git push origin v1.0.0
# GitHub Actions will automatically create the release
```

### For Pre-releases
1. Go to Actions tab
2. Select "Release & Publish" workflow
3. Click "Run workflow"
4. Enter version (e.g., 1.0.0-beta)
5. Check "Mark as pre-release"
6. Click "Run workflow"

## Monitoring

### Status Badge
Add to your README.md:
```markdown
![Build Status](https://github.com/YOUR_ORG/LanMontainDesktop/workflows/Build%20&%20Test/badge.svg)
```

### Check Workflow Status
- GitHub > Actions tab
- View workflow runs and logs
- See build artifacts

## Troubleshooting

### Build Failures
1. Check the workflow logs in GitHub Actions
2. Try building locally with same .NET version
3. Ensure all submodules are initialized: `git clone --recursive`

### PR Checks Not Running
- Ensure branch is up-to-date with main
- Review branch protection rules in Settings
- Check if workflows are enabled in Actions

### Release Creation Failed
- Verify tag format (v*.* or release-*.*)
- Check that csproj files are in correct format
- Review workflow output for specific errors

## Future Enhancements

Consider adding:
- Test coverage reporting
- Performance benchmarking
- Automated versioning (CalVer/SemVer)
- Multi-platform builds (Linux, macOS)
- Installer generation (.exe, .msi)
- Automated changelog generation
- Docker images for backend

## References

- [GitHub Actions Documentation](https://docs.github.com/actions)
- [ClassIsland CI/CD Setup](https://github.com/ClassIsland/ClassIsland) (reference project)
- [.NET Build & Deploy](https://learn.microsoft.com/dotnet/devops/build-cross-platform)
- [Avalonia Desktop Deployment](https://docs.avaloniaui.net/docs/deployment)
