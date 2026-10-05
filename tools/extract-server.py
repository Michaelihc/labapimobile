"""Extract the reference server ZIP into .runtime/server-original (skips mono.msi and .bak snapshots)."""
import os
import sys
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SRC = next((ROOT / '.refereces').glob('004vser_*.zip'))
DST = Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT / '.runtime' / 'server-original'

DST.mkdir(parents=True, exist_ok=True)
count = 0
with zipfile.ZipFile(SRC) as archive:
    for info in archive.infolist():
        name = info.filename
        if not info.flag_bits & 0x800:
            try:
                name = name.encode('cp437').decode('gbk')
            except UnicodeError:
                pass
        name = name.replace(chr(92), '/')
        if name.endswith('mono.msi') or name.endswith('.bak'):
            continue
        # Strip the archive's top-level 004vser/ folder.
        name = name.split('/', 1)[1] if '/' in name else name
        if not name:
            continue
        out = DST / name
        if name.endswith('/'):
            out.mkdir(parents=True, exist_ok=True)
            continue
        out.parent.mkdir(parents=True, exist_ok=True)
        with archive.open(info) as source, open(out, 'wb') as target:
            while chunk := source.read(1 << 20):
                target.write(chunk)
        count += 1
print(f'extracted {count} files to {DST}')
