#!/bin/sh
set -eu
root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
frontend=${1:-"$root/../Fanmade/frontend"}
python3 - "$root/Builds/Web" "$frontend" <<'PYTHON'
import hashlib, json, shutil, sys
from pathlib import Path
source, frontend = map(Path, sys.argv[1:])
assert (source / 'index.html').is_file(), 'Build the Unity Web player first'
files = {p.relative_to(source).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
         for p in sorted(source.rglob('*')) if p.is_file() and p.name != '.DS_Store'}
build_id = hashlib.sha256(json.dumps(files, sort_keys=True).encode()).hexdigest()[:16]
target = frontend / 'public/player' / build_id
shutil.copytree(source, target, dirs_exist_ok=True, ignore=shutil.ignore_patterns('.DS_Store'))
(frontend / 'public/player-build.json').write_text(json.dumps({
    'path': f'/player/{build_id}/index.html', 'files': files
}, indent=2) + '\n')
print(f'Installed player at {target}; commit public/player-build.json and the new directory with Git LFS')
PYTHON
