"""Extract the Carl Mod server ZIP into .runtime/server-original (skips mono.msi and .bak snapshots).

usage: python tools/extract-server.py [--zip <server.zip>] [<destination>]

Without --zip, the script uses the server ZIP in .refereces/ (004vser_*.zip). The archive's single top-level
folder is stripped, so <destination> receives "Carl Mod.exe" and "Carl Mod_Data" directly.
"""
import argparse
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
parser = argparse.ArgumentParser(description='Extract the Carl Mod server ZIP.')
parser.add_argument('--zip', type=Path, help='server ZIP (default: .refereces/004vser_*.zip)')
parser.add_argument('destination', type=Path, nargs='?', default=ROOT / '.runtime' / 'server-original')
args = parser.parse_args()
SRC = args.zip or next((ROOT / '.refereces').glob('004vser_*.zip'), None)
if SRC is None or not SRC.is_file():
    parser.error('server ZIP not found; pass --zip <path to the Carl Mod server ZIP>')
DST = args.destination

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
