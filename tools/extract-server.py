"""Extract a Carl Mod server ZIP into .runtime/ (skips mono.msi and .bak snapshots).

usage: python tools/extract-server.py [--build 0.0.4|0.0.5] [--zip <server.zip>] [<destination>]

--build picks the default ZIP in .refereces/ and the default destination:
  0.0.4 (default)  004vser_*.zip           -> .runtime/server-original      (the 0.0.4 build with the deathmatch module)
  0.0.5            CarlMod_0.0.5_*.zip     -> .runtime/server-original-005
--zip and <destination> override them. The archive's single top-level folder ("004vser", "Carl Mod Server - 0.0.5", ...)
is stripped, so <destination> receives "Carl Mod.exe" and "Carl Mod_Data" directly. Entry names may use backslashes.
"""
import argparse
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
BUILDS = {
    '0.0.4': ('004vser_*.zip', 'server-original'),
    '0.0.5': ('CarlMod_0.0.5_*.zip', 'server-original-005'),
}
parser = argparse.ArgumentParser(description='Extract a Carl Mod server ZIP.')
parser.add_argument('--build', choices=sorted(BUILDS), default='0.0.4', help='which reference server (default: 0.0.4)')
parser.add_argument('--zip', type=Path, help='server ZIP (default: the --build ZIP in .refereces/)')
parser.add_argument('destination', type=Path, nargs='?', help='output folder (default: the --build folder in .runtime/)')
args = parser.parse_args()
pattern, folder = BUILDS[args.build]
SRC = args.zip or next((ROOT / '.refereces').glob(pattern), None)
if SRC is None or not SRC.is_file():
    parser.error('server ZIP not found; pass --zip <path to the Carl Mod server ZIP>')
DST = args.destination or ROOT / '.runtime' / folder

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
        # Strip the archive's top-level folder.
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
