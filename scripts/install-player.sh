#!/bin/sh
set -eu
root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
frontend=${1:-"$root/../Fanmade/frontend"}
test -s "$root/Builds/Web/index.html"
mkdir -p "$frontend/public/player"
rsync -a --delete "$root/Builds/Web/" "$frontend/public/player/"
printf '%s\n' "Installed player into $frontend/public/player"
