#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
EXTERNAL="$ROOT/.external"
mkdir -p "$EXTERNAL"

fetch() {
  local name="$1" url="$2" ref="${3:-master}"
  local dir="$EXTERNAL/$name"
  if [ -d "$dir/.git" ]; then
    echo "Updating $name..."
    git -C "$dir" fetch --depth 1 origin "$ref"
    git -C "$dir" checkout --detach FETCH_HEAD
  else
    echo "Cloning $name..."
    git clone --depth 1 --branch "$ref" "$url" "$dir"
  fi
  echo "$name $(git -C "$dir" rev-parse HEAD)"
}

fetch Crystal.Database https://github.com/Suprcode/Crystal.Database.git master
fetch Crystal.MapEditor https://github.com/Suprcode/Crystal.MapEditor.git master
