#!/bin/sh
# Builds Politicom for macOS, Windows and Linux, each packaged with what the GNU GPL asks a copy
# to carry: the licence, the credits (and the font's licence), and the complete source.
#
#   tools/export.sh            all three platforms
#   tools/export.sh macOS      one of: macOS, Windows, Linux
#
# Needs Godot 4.7.2 .NET (GODOT, default /Applications/Godot_mono.app) and its export templates
# for each platform (see README, "Exporting a standalone build"). The source is the committed
# state (git archive HEAD): commit first, or the packaged source won't match the build.
set -eu

GODOT="${GODOT:-/Applications/Godot_mono.app/Contents/MacOS/Godot}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
BUILD="$ROOT/build"
PLATFORMS="${1:-macOS Windows Linux}"
mkdir -p "$BUILD"

if [ -n "$(git -C "$ROOT" status --porcelain)" ]; then
    echo "warning: uncommitted changes; the packaged source is the last commit, not the working tree" >&2
fi
git -C "$ROOT" archive --format=zip --prefix=Politicom-source/ -o "$BUILD/Politicom-source.zip" HEAD

# Licence, credits, font licence and source, next to the game.
add_notices() {
    cp "$ROOT/LICENSE" "$1/LICENSE.txt"
    cp "$ROOT/Data/geo/ATTRIBUTION.md" "$1/ATTRIBUTION.md"
    cp "$ROOT/Game/fonts/OFL-CrimsonPro.txt" "$1/OFL-CrimsonPro.txt"
    cp "$BUILD/Politicom-source.zip" "$1/Politicom-source.zip"
}

for platform in $PLATFORMS; do
    case "$platform" in
        macOS)
            "$GODOT" --headless --path "$ROOT/Game" --export-release macOS "$BUILD/Politicom.zip"
            rm -rf "$BUILD/Politicom.app" "$BUILD/macos"
            unzip -q "$BUILD/Politicom.zip" -d "$BUILD"
            mkdir -p "$BUILD/macos"
            cp -R "$BUILD/Politicom.app" "$BUILD/macos/"
            add_notices "$BUILD/macos"
            (cd "$BUILD/macos" && rm -f ../Politicom-macos.zip && zip -qry ../Politicom-macos.zip .)
            ;;
        Windows)
            rm -rf "$BUILD/windows" && mkdir -p "$BUILD/windows"
            "$GODOT" --headless --path "$ROOT/Game" --export-release Windows "$BUILD/windows/Politicom.exe"
            add_notices "$BUILD/windows"
            (cd "$BUILD/windows" && rm -f ../Politicom-windows.zip && zip -qr ../Politicom-windows.zip .)
            ;;
        Linux)
            rm -rf "$BUILD/linux" && mkdir -p "$BUILD/linux"
            "$GODOT" --headless --path "$ROOT/Game" --export-release Linux "$BUILD/linux/Politicom.x86_64"
            add_notices "$BUILD/linux"
            (cd "$BUILD/linux" && rm -f ../Politicom-linux.zip && zip -qr ../Politicom-linux.zip .)
            ;;
        *) echo "unknown platform: $platform" >&2; exit 1 ;;
    esac
    echo "exported $platform"
done
ls -la "$BUILD"/Politicom-*.zip
