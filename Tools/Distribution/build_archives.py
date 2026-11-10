"""Build UrHeart release archives from tracked files and the local font directory."""
from pathlib import Path
import argparse
import hashlib
import json
import re
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parents[2]
EXCLUDED = (
    'Assets/Plugins/Demigiant', 'Assets/Plugins/Sirenix',
    'Assets/ThirdParty/VolFx', 'Assets/VolFx', 'Assets/_Recovery',
)
COMMERCIAL_IDS = {'32416', '89041', '300643'}

def excluded(name):
    return any(name == p or name == p + '.meta' or name.startswith(p + '/') for p in EXCLUDED)

def digest(path):
    h = hashlib.sha256()
    with path.open('rb') as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b''):
            h.update(chunk)
    return h.hexdigest()

def record(name):
    path = ROOT / name
    if not path.is_file() or path.is_symlink():
        raise RuntimeError(f'Missing file or symlink: {name}')
    return {'path': name, 'bytes': path.stat().st_size, 'sha256': digest(path)}

def write_archive(path, files, prefix=''):
    with zipfile.ZipFile(path, 'x', zipfile.ZIP_DEFLATED, compresslevel=6, allowZip64=True) as archive:
        for name in files:
            archive.write(ROOT / name, prefix + name)
    with zipfile.ZipFile(path) as archive:
        bad = archive.testzip()
        if bad:
            raise RuntimeError(f'ZIP integrity failed: {bad}')

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--version', required=True, help='Release label, e.g. 2026-11-10')
    parser.add_argument('--output', type=Path, required=True, help='Output folder outside the project')
    args = parser.parse_args()
    if not re.fullmatch(r'[A-Za-z0-9._-]+', args.version):
        raise RuntimeError('Version must contain only letters, numbers, dots, dashes and underscores')
    output = args.output.resolve()
    if output == ROOT or ROOT in output.parents:
        raise RuntimeError('Use an output folder outside the project')
    tracked = list(filter(None, subprocess.check_output(['git', '-C', str(ROOT), 'ls-files', '-z'], encoding='utf-8').split('\0')))
    unexpected = [p for p in tracked if excluded(p)]
    if unexpected:
        raise RuntimeError(f'Commercial or recovery files are tracked: {unexpected[:5]}')
    fonts = sorted(p.relative_to(ROOT).as_posix() for p in (ROOT / 'Assets/Font').rglob('*') if p.is_file())
    if not fonts or not (ROOT / 'Assets/Font.meta').is_file():
        raise RuntimeError('Install the font supplement before packaging')
    fonts.append('Assets/Font.meta')
    project = sorted(set(tracked) | set(fonts))
    for name in project:
        if name.endswith('.meta'):
            match = re.search(r'productId: (\d+)', (ROOT / name).read_text(encoding='utf-8-sig', errors='replace'))
            if match and match[1] in COMMERCIAL_IDS:
                raise RuntimeError(f'Commercial asset metadata found outside excluded directories: {name}')
    records = [record(name) for name in project]
    output.mkdir(parents=True, exist_ok=True)
    project_zip = output / f'UrHeart-UnityProject-{args.version}.zip'
    font_zip = output / f'UrHeart-FontAssets-{args.version}.zip'
    for path in [project_zip, font_zip, output / 'SHA256SUMS.txt', output / 'project-files-manifest.json', output / 'font-assets-manifest.json']:
        if path.exists():
            raise RuntimeError(f'Output exists; choose a new empty folder: {path}')
    write_archive(project_zip, project, 'UrHeart/')
    write_archive(font_zip, sorted(fonts))
    font_names = set(fonts)
    for filename, selected in [('project-files-manifest.json', records), ('font-assets-manifest.json', [r for r in records if r['path'] in font_names])]:
        (output / filename).write_text(json.dumps({'version': args.version, 'files': selected}, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    # Check that source files did not change while archiving.
    for item in records:
        if digest(ROOT / item['path']) != item['sha256']:
            raise RuntimeError(f'Source changed while packaging: {item["path"]}')
    artifacts = [project_zip, font_zip, output / 'project-files-manifest.json', output / 'font-assets-manifest.json']
    (output / 'SHA256SUMS.txt').write_text(''.join(f'{digest(path)}  {path.name}\n' for path in artifacts), encoding='utf-8')
    print(json.dumps({'status': 'PASS', 'files': len(records), 'artifacts': [{'name': p.name, 'bytes': p.stat().st_size} for p in artifacts]}, indent=2))

if __name__ == '__main__':
    main()
