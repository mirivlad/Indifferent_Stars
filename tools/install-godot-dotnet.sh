#!/usr/bin/env bash
# Install the complete official Linux x86_64 .NET editor without sudo.
set -euo pipefail

install_prefix="$HOME/.local"
if [[ "${1:-}" == "--prefix" && $# == 2 ]]; then
    install_prefix="$2"
elif [[ $# != 0 ]]; then
    echo "Usage: $0 [--prefix directory]" >&2
    exit 1
fi

if [[ "$(uname -s)" != Linux || "$(uname -m)" != x86_64 ]]; then
    echo "This installer supports Linux x86_64." >&2
    exit 1
fi
for dependency in curl unzip sha256sum; do
    if ! command -v "$dependency" >/dev/null 2>&1; then
        echo "Missing dependency: $dependency. On Debian: sudo apt install curl unzip" >&2
        exit 1
    fi
done

version=4.7.2
archive="Godot_v${version}-stable_mono_linux_x86_64.zip"
# SHA-256 published in the official godotengine/godot-builds release asset metadata.
archive_sha256=129f82db7bafd54ae14bb5bb284041c73860e8c7a009a3a026ca5e946cbff247
url="https://github.com/godotengine/godot-builds/releases/download/${version}-stable/$archive"
mkdir -p "$install_prefix"
install_prefix="$(cd "$install_prefix" && pwd)"
install_dir="$install_prefix/opt/godot-dotnet/$version"
executable="$install_dir/Godot_v${version}-stable_mono_linux.x86_64"
launcher="$install_prefix/bin/godot-dotnet"

if [[ -e "$launcher" && ! -L "$launcher" ]]; then
    echo "Refusing to replace an existing file or directory: $launcher" >&2
    exit 1
fi
if [[ -e "$install_dir" ]]; then
    if [[ ! -x "$executable" || ! -d "$install_dir/GodotSharp" ]]; then
        echo "Existing installation is incomplete: $install_dir. Choose another --prefix." >&2
        exit 1
    fi
else
    mkdir -p "$(dirname "$install_dir")"
    staging_dir="$(mktemp -d "$(dirname "$install_dir")/.install-XXXXXX")"
    trap 'rm -rf -- "$staging_dir"' EXIT
    echo "Downloading Godot $version .NET..."
    curl --fail --location --retry 3 --connect-timeout 15 --progress-bar --output "$staging_dir/$archive" "$url"
    if ! printf '%s  %s\n' "$archive_sha256" "$staging_dir/$archive" | sha256sum --check --status; then
        echo "Archive checksum verification failed; installation aborted." >&2
        exit 1
    fi
    unzip -q "$staging_dir/$archive" -d "$staging_dir/unpacked"
    extracted_dir="$staging_dir/unpacked/Godot_v${version}-stable_mono_linux_x86_64"
    chmod +x "$extracted_dir/Godot_v${version}-stable_mono_linux.x86_64"
    "$extracted_dir/Godot_v${version}-stable_mono_linux.x86_64" --headless --version
    mv -- "$extracted_dir" "$install_dir"
fi

installed_version="$("$executable" --headless --version)"
if [[ "$installed_version" != "$version".*.mono.* ]]; then
    echo "Unexpected Godot build: $installed_version" >&2
    exit 1
fi
mkdir -p "$install_prefix/bin"
ln -sfn -- "$executable" "$launcher"
printf '\nInstalled: %s\nVersion: %s\n' "$launcher" "$installed_version"
printf 'Run from the repository:\nGODOT_BIN=%q tools/run-local.sh\n' "$launcher"
case ":$PATH:" in
    *":$install_prefix/bin:"*) ;;
    *) printf 'To add the command to this shell:\nexport PATH=%q:%s\n' "$install_prefix/bin" "\"\$PATH\"" ;;
esac
