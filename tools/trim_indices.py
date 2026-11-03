"""Copies to a new folder only what the feed keeps of a release: the version given, its full package and its delta, with its indexes.

Usage: python tools/trim_indices.py <release folder> <upload folder> <version>
"""

import hashlib
import json
import shutil
import sys
from pathlib import Path

source, target, version = Path(sys.argv[1]), Path(sys.argv[2]), sys.argv[3]
if target.exists():
    shutil.rmtree(target)
target.mkdir()

index = json.loads((source / "releases.win.json").read_text(encoding="utf-8"))
kept = [asset for asset in index["Assets"] if asset["Version"] == version]
assert {asset["Type"] for asset in kept} == {"Full", "Delta"}, kept

for asset in kept:
    shutil.copy2(source / asset["FileName"], target / asset["FileName"])
    data = (target / asset["FileName"]).read_bytes()
    assert len(data) == asset["Size"], asset
    assert hashlib.sha256(data).hexdigest().upper() == asset["SHA256"], asset

(target / "releases.win.json").write_text(json.dumps({"Assets": kept}, separators=(",", ":")), encoding="utf-8")
lines = [line for line in (source / "RELEASES").read_text(encoding="utf-8").splitlines() if f"-{version}-full" in line]
assert len(lines) == 1, lines
(target / "RELEASES").write_text(lines[0], encoding="utf-8")

for file in sorted(target.iterdir()):
    print(file.name, file.stat().st_size)
print((target / "RELEASES").read_text(encoding="utf-8"))
