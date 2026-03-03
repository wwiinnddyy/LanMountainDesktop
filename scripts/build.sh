#!/bin/bash
# LanMontainDesktop Build Script for Linux/macOS
# Usage: ./build.sh [options]
# Example: ./build.sh --project LanMontainDesktop.csproj --rid linux-x64 --version 1.0.0

set -e

# Default values
PROJECT="LanMontainDesktop/LanMontainDesktop.csproj"
CONFIGURATION="Release"
RID=""
VERSION=""
PUBLISH_DIR=""
SKIP_RESTORE=false
VERBOSE=false

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Functions
print_error() {
    echo -e "${RED}❌ Error: $1${NC}" >&2
}

print_success() {
    echo -e "${GREEN}✅ $1${NC}"
}

print_info() {
    echo -e "${BLUE}ℹ️  $1${NC}"
}

print_warning() {
    echo -e "${YELLOW}⚠️  $1${NC}"
}

show_help() {
    cat << EOF
LanMontainDesktop Build Script for Linux/macOS

Usage: $0 [options]

Options:
    -p, --project PATH          Project file path (default: LanMontainDesktop/LanMontainDesktop.csproj)
    -c, --config CONFIG         Configuration: Release/Debug (default: Release)
    -r, --rid RID              Runtime Identifier: linux-x64, osx-x64, osx-arm64 (required)
    -v, --version VERSION      Version number (default: read from csproj)
    -o, --output DIR           Output directory for publish
    --skip-restore             Skip dotnet restore
    --verbose                  Verbose output
    -h, --help                 Show this help message

Examples:
    ./build.sh --rid linux-x64 --version 1.0.0
    ./build.sh --rid osx-x64 --output ./publish
    ./build.sh --project LanMontainDesktop/LanMontainDesktop.csproj --rid osx-arm64

EOF
}

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        -p|--project)
            PROJECT="$2"
            shift 2
            ;;
        -c|--config)
            CONFIGURATION="$2"
            shift 2
            ;;
        -r|--rid)
            RID="$2"
            shift 2
            ;;
        -v|--version)
            VERSION="$2"
            shift 2
            ;;
        -o|--output)
            PUBLISH_DIR="$2"
            shift 2
            ;;
        --skip-restore)
            SKIP_RESTORE=true
            shift
            ;;
        --verbose)
            VERBOSE=true
            shift
            ;;
        -h|--help)
            show_help
            exit 0
            ;;
        *)
            print_error "Unknown option: $1"
            show_help
            exit 1
            ;;
    esac
done

# Validation
if [ -z "$RID" ]; then
    print_error "Runtime Identifier (--rid) is required"
    show_help
    exit 1
fi

if [ ! -f "$PROJECT" ]; then
    print_error "Project file not found: $PROJECT"
    exit 1
fi

# Detect OS
if [[ "$OSTYPE" == "linux-gnu"* ]]; then
    OS="linux"
    DETECTED_RID="linux-x64"
elif [[ "$OSTYPE" == "darwin"* ]]; then
    OS="macos"
    # Try to detect architecture
    if [ "$(uname -m)" == "arm64" ]; then
        DETECTED_RID="osx-arm64"
    else
        DETECTED_RID="osx-x64"
    fi
else
    print_error "Unsupported OS: $OSTYPE"
    exit 1
fi

print_info "Detected OS: $OS ($DETECTED_RID)"
print_info "Target RID: $RID"

# Read version from csproj if not provided
if [ -z "$VERSION" ]; then
    VERSION=$(grep -oP '<Version>\K[^<]*' "$PROJECT" | head -1)
    if [ -z "$VERSION" ]; then
        VERSION="1.0.0"
        print_warning "No version found in csproj, using default: $VERSION"
    fi
fi

print_info "Version: $VERSION"
print_info "Configuration: $CONFIGURATION"

# Set output directory
if [ -z "$PUBLISH_DIR" ]; then
    PUBLISH_DIR="./publish/$RID"
fi

print_info "Output directory: $PUBLISH_DIR"

# Restore dependencies
if [ "$SKIP_RESTORE" = false ]; then
    print_info "Restoring dependencies..."
    if [ "$VERBOSE" = true ]; then
        dotnet restore --verbosity detailed
    else
        dotnet restore
    fi
    print_success "Dependencies restored"
fi

# Build
print_info "Building..."
if [ "$VERBOSE" = true ]; then
    dotnet build "$PROJECT" \
        -c "$CONFIGURATION" \
        --no-restore \
        --verbosity detailed
else
    dotnet build "$PROJECT" -c "$CONFIGURATION" --no-restore
fi
print_success "Build completed"

# Publish
print_info "Publishing..."
PUBLISH_ARGS=(
    "$PROJECT"
    "-c" "$CONFIGURATION"
    "-o" "$PUBLISH_DIR"
    "-r" "$RID"
    "--self-contained"
)

# Add platform-specific publish options
if [ "$VERBOSE" = true ]; then
    PUBLISH_ARGS+=("--verbosity" "detailed")
fi

dotnet publish "${PUBLISH_ARGS[@]}" \
    -p:PublishSingleFile=true \
    -p:PublishTrimmed=false \
    -p:DebugType=embedded \
    -p:DebugSymbols=false

print_success "Published to: $PUBLISH_DIR"

# Show result
if [ -d "$PUBLISH_DIR" ]; then
    SIZE=$(du -sh "$PUBLISH_DIR" | cut -f1)
    FILE_COUNT=$(find "$PUBLISH_DIR" -type f | wc -l)
    print_success "Build complete! Output size: $SIZE ($FILE_COUNT files)"
    
    if [ "$VERBOSE" = true ]; then
        print_info "Output contents:"
        ls -lh "$PUBLISH_DIR"
    fi
else
    print_error "Publish directory not found"
    exit 1
fi

print_success "Done!"
