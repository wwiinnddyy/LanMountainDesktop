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
