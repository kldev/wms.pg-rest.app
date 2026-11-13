#!/usr/bin/env bash
# Tailwind CSS with the standalone CLI (no Node): src/PgRestWms.Web/Styles/app.css -> wwwroot/app.css (committed).
# The binary is downloaded once into .tools/ (git-ignored) and checked against the release's sha256sums.txt.
#   ./scripts/tailwind.sh            build (minified) - `dotnet build` also does this once the binary is there
#   ./scripts/tailwind.sh --watch    rebuild on every change of a .razor file or app.css
set -euo pipefail

VERSION="v4.3.5"
repo="$(cd "$(dirname "$0")/.." && pwd)"
tools="$repo/.tools"
bin="$tools/tailwindcss-$VERSION"

case "$(uname -s)-$(uname -m)" in
  Linux-x86_64) asset="tailwindcss-linux-x64" ;;
  Linux-aarch64 | Linux-arm64) asset="tailwindcss-linux-arm64" ;;
  Darwin-arm64) asset="tailwindcss-macos-arm64" ;;
  Darwin-x86_64) asset="tailwindcss-macos-x64" ;;
  *) echo "Unsupported platform $(uname -s)-$(uname -m) - download $VERSION from github.com/tailwindlabs/tailwindcss/releases into $bin" >&2; exit 1 ;;
esac

if [[ ! -x "$bin" ]]; then
  mkdir -p "$tools"
  url="https://github.com/tailwindlabs/tailwindcss/releases/download/$VERSION"
  echo "Downloading Tailwind CSS $VERSION ($asset)..." >&2
  curl -fsSL "$url/$asset" -o "$bin.part"
  expected="$(curl -fsSL "$url/sha256sums.txt" | awk -v a="$asset" '$2 == a || $2 == "./"a || $2 == "*"a { print $1 }')"
  actual="$( (sha256sum "$bin.part" 2>/dev/null || shasum -a 256 "$bin.part") | awk '{ print $1 }')"
  if [[ -z "$expected" || "$expected" != "$actual" ]]; then
    rm -f "$bin.part"
    echo "Checksum mismatch for $asset (expected '$expected', got '$actual')" >&2
    exit 1
  fi
  chmod +x "$bin.part" && mv "$bin.part" "$bin"
  ln -sfn "tailwindcss-$VERSION" "$tools/tailwindcss"
fi

cd "$repo/src/PgRestWms.Web"
if [[ "${1:-}" == "--watch" ]]; then
  exec "$bin" -i Styles/app.css -o wwwroot/app.css --watch
fi
"$bin" -i Styles/app.css -o wwwroot/app.css --minify
