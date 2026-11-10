"""Package the verified native art and review files, preserving their paths."""
from pathlib import Path
from zipfile import ZipFile, ZIP_DEFLATED
import json

ROOT = Path(__file__).resolve().parent
PROJECT = next((parent for parent in ROOT.parents
                if (parent / 'ProjectSettings/ProjectVersion.txt').is_file()), None)
if PROJECT is None:
    raise RuntimeError('Cannot find the Unity project above the FaceRepair sources.')
RUNTIME = ROOT.parent / 'Runtime'
for name in ('verification_pixels.json', 'verification_native.json', 'verification_hands.json', 'verification_runtime.json', 'verification_unity.json', 'runtime_inventory.json'):
    assert json.loads((ROOT / name).read_text())['status'] == 'PASS'

files = [p for p in ROOT.rglob('*') if p.is_file() and p.suffix not in ('.zip', '.tmp', '.log') and '__pycache__' not in p.parts]
files += [p for p in RUNTIME.rglob('*') if p.is_file()]
files.append(Path(str(RUNTIME) + '.meta'))
target = ROOT / 'Review/RepairBust_216px.zip'
temporary = target.with_suffix('.zip.tmp')
with ZipFile(temporary, 'w', ZIP_DEFLATED) as archive:
    for path in sorted(files):
        if path.is_file():
            archive.write(path, path.relative_to(PROJECT).as_posix())
with ZipFile(temporary) as archive:
    assert archive.testzip() is None
    entries = len(archive.namelist())
temporary.replace(target)
print(f'Updated native package: {target} ({target.stat().st_size:,} bytes, {entries} files)')
