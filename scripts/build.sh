#!/bin/bash
# Build script for LanMountainDesktop
# Cross-platform build support: Linux, macOS

set -e

Script_Dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
Root_Dir="$(dirname "$Script_Dir")"

# Detect OS
OS=""
ARCH=""
RID=""

detect_os() {
    if [[ "$OSTYPE" == "linux-gnu"* ]]; then
        OS="linux"
        RID="linux-x64"
    elif [[ "$OSTYPE" == "darwin"* ]]; then
        OS="macos"
        ARCH=$(uname -m)
        if [[ "$ARCH" == "arm64" ]]; then
            RID="osx-arm64"
        else
            RID="osx-x64"
        fi
    else
        echo "Unsupported OS: $OSTYPE"
        exit 1
    fi
}

print_usage() {
    cat << EOF
LanMountainDesktop Build Script

Usage: $0 [command] [options]

Commands:
  build       Build the project
  publish     Publish as self-contained
  clean       Clean build output
  
Options:
  --config CONFIG     Build configuration (Debug/Release, default: Release)
  --rid RID          Runtime identifier (e.g., linux-x64, osx-arm64)
  --os OS            Operating system (linux, macos)

Examples:
  $0 build
  $0 publish --config Release --rid linux-x64
  $0 clean

EOF
}

build_project() {
    local config="$1"
    echo "Building $OS ($RID) - $config configuration..."
    
    cd "$Root_Dir"
    
    dotnet restore
    dotnet build -c "$config" --no-restore -v minimal
    
    echo "✅ Build completed"
}

publish_project() {
    local config="$1"
    local output_dir="$Root_Dir/publish/$RID"
    
    echo "Publishing $OS ($RID)..."
    
    cd "$Root_Dir"
    
    dotnet restore
    dotnet publish LanMountainDesktop/LanMountainDesktop.csproj \
        -c "$config" \
        -o "$output_dir" \
        --self-contained \
        -r "$RID" \
        -p:PublishSingleFile=true \
        -p:DebugType=none \
        -v minimal
    
    echo "✅ Published to: $output_dir"
    echo "   Size: $(du -sh "$output_dir" | cut -f1)"
}

clean_project() {
    echo "Cleaning build output..."
    cd "$Root_Dir"
    
    rm -rf ./publish
    rm -rf ./bin
    rm -rf ./obj
    find . -name "bin" -type d -exec rm -rf {} + 2>/dev/null || true
    find . -name "obj" -type d -exec rm -rf {} + 2>/dev/null || true
    
    echo "✅ Clean completed"
}

# Main
detect_os

config="Release"
command="build"

while [[ $# -gt 0 ]]; do
    case $1 in
        build|publish|clean)
            command="$1"
            shift
            ;;
        --config)
            config="$2"
            shift 2
            ;;
        --rid)
            RID="$2"
            shift 2
            ;;
        --os)
            OS="$2"
            if [[ "$OS" == "linux" ]]; then
                RID="linux-x64"
            elif [[ "$OS" == "macos" ]]; then
                RID="osx-x64"
            fi
            shift 2
            ;;
        -h|--help)
            print_usage
            exit 0
            ;;
        *)
            echo "Unknown option: $1"
            print_usage
            exit 1
            ;;
    esac
done

case $command in
    build)
        build_project "$config"
        ;;
    publish)
        publish_project "$config"
        ;;
    clean)
        clean_project
        ;;
    *)
        print_usage
        exit 1
        ;;
esac
