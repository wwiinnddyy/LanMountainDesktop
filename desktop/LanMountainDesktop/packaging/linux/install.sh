#!/usr/bin/env sh
set -eu

SCRIPT_DIR="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
APP_BIN="$SCRIPT_DIR/LanMountainDesktop"
DESKTOP_TEMPLATE="$SCRIPT_DIR/share/applications/LanMountainDesktop.desktop"
ICON_SOURCE="$SCRIPT_DIR/share/icons/hicolor/256x256/apps/lanmountaindesktop.png"

APPLICATIONS_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
ICONS_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/icons/hicolor/256x256/apps"
DESKTOP_TARGET="$APPLICATIONS_DIR/LanMountainDesktop.desktop"
ICON_TARGET="$ICONS_DIR/lanmountaindesktop.png"

check_audio_dependencies() {
    MISSING_DEPS=""

    if command -v dpkg >/dev/null 2>&1; then
        if ! dpkg -s libportaudio2 >/dev/null 2>&1; then
            MISSING_DEPS="$MISSING_DEPS libportaudio2"
        fi
        if ! dpkg -s libasound2 >/dev/null 2>&1; then
            MISSING_DEPS="$MISSING_DEPS libasound2"
        fi
    elif command -v rpm >/dev/null 2>&1; then
        if ! rpm -q portaudio-libs >/dev/null 2>&1; then
            MISSING_DEPS="$MISSING_DEPS portaudio-libs"
        fi
        if ! rpm -q alsa-lib >/dev/null 2>&1; then
            MISSING_DEPS="$MISSING_DEPS alsa-lib"
        fi
    elif command -v pacman >/dev/null 2>&1; then
        if ! pacman -Q portaudio >/dev/null 2>&1; then
            MISSING_DEPS="$MISSING_DEPS portaudio"
        fi
        if ! pacman -Q alsa-lib >/dev/null 2>&1; then
            MISSING_DEPS="$MISSING_DEPS alsa-lib"
        fi
    elif command -v apk >/dev/null 2>&1; then
        if ! apk -e info portaudio >/dev/null 2>&1; then
            MISSING_DEPS="$MISSING_DEPS portaudio"
        fi
        if ! apk -e info alsa-lib >/dev/null 2>&1; then
            MISSING_DEPS="$MISSING_DEPS alsa-lib"
        fi
    fi

    if [ -n "$MISSING_DEPS" ]; then
        return 1
    fi
    return 0
}

install_audio_dependencies() {
    if command -v apt-get >/dev/null 2>&1; then
        sudo apt-get update
        sudo apt-get install -y libportaudio2 libasound2
    elif command -v dnf >/dev/null 2>&1; then
        sudo dnf install -y portaudio-libs alsa-lib
    elif command -v yum >/dev/null 2>&1; then
        sudo yum install -y portaudio-libs alsa-lib
    elif command -v pacman >/dev/null 2>&1; then
        sudo pacman -S --noconfirm portaudio alsa-lib
    elif command -v apk >/dev/null 2>&1; then
        sudo apk add portaudio alsa-lib
    else
        printf '%s\n' "Warning: Could not detect package manager. Please install audio dependencies manually:"
        printf '%s\n' "  - libportaudio2 (or portaudio-libs/portaudio)"
        printf '%s\n' "  - libasound2 (or alsa-lib)"
    fi
}

if ! check_audio_dependencies; then
    printf '%s\n' "Installing audio dependencies for recording features..."
    install_audio_dependencies

    if ! check_audio_dependencies; then
        printf '%s\n' "Warning: Audio dependencies may not be installed correctly."
        printf '%s\n' "Recording and study monitoring features may not work properly."
    fi
fi

mkdir -p "$APPLICATIONS_DIR" "$ICONS_DIR"

cp "$ICON_SOURCE" "$ICON_TARGET"
sed \
  -e "s|@@EXEC@@|$APP_BIN|g" \
  -e "s|@@ICON@@|lanmountaindesktop|g" \
  "$DESKTOP_TEMPLATE" > "$DESKTOP_TARGET"

chmod +x "$APP_BIN" "$DESKTOP_TARGET"

if command -v update-desktop-database >/dev/null 2>&1; then
  update-desktop-database "$APPLICATIONS_DIR" >/dev/null 2>&1 || true
fi

if command -v gtk-update-icon-cache >/dev/null 2>&1; then
  gtk-update-icon-cache "${XDG_DATA_HOME:-$HOME/.local/share}/icons/hicolor" >/dev/null 2>&1 || true
fi

printf '%s\n' "Installed desktop entry: $DESKTOP_TARGET"
printf '%s\n' "Installed icon: $ICON_TARGET"
