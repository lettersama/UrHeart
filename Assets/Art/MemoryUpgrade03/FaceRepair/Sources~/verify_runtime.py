"""Verify published bytes, manifest paths, Unity metadata and author source hashes."""
from pathlib import Path
import hashlib
import json
import re
from PIL import Image

ROOT = Path(__file__).resolve().parent
PROJECT = next((parent for parent in ROOT.parents
                if (parent / 'ProjectSettings/ProjectVersion.txt').is_file()), None)
if PROJECT is None:
    raise RuntimeError('Cannot find the Unity project above the FaceRepair sources.')
ART = ROOT.parent / 'Runtime'
inventory = json.loads((ROOT / 'runtime_inventory.json').read_text(encoding='utf-8-sig'))
manifest = json.loads((ART / 'Animation/manifest.json').read_text(encoding='utf-8-sig'))
hands = json.loads((ART / 'Hands/manifest.json').read_text(encoding='utf-8-sig'))
guids = set()

for entry in inventory['files']:
    path = ART / entry['relative']
    assert path.is_file(), path
    assert hashlib.sha256(path.read_bytes()).hexdigest() == entry['sha256'], path
    with Image.open(path) as image:
        assert image.size == (216, 162), (path, image.size)
    meta = Path(str(path) + '.meta').read_text(encoding='utf-8-sig')
    assert meta.endswith('\n'), (path, 'Unity .meta requires a trailing newline')
    for key, value in [('textureType', 8), ('spriteMode', 1), ('filterMode', 0),
                       ('enableMipMap', 0), ('textureCompression', 0), ('nPOTScale', 0),
                       ('spritePixelsToUnits', 20), ('spriteMeshType', 1)]:
        assert re.search(rf'^\s*{key}: {value}\s*$', meta, re.M), (path, key)
    guid = re.search(r'^guid: ([0-9a-f]{32})$', meta, re.M).group(1)
    assert guid not in guids, path
    guids.add(guid)
assert len(list(ART.rglob('*.png'))) == len(inventory['files'])

def require_file(base, relative):
    path = (base / relative).resolve()
    assert path.is_relative_to(ART.resolve()) and path.is_file(), path

animation = ART / 'Animation'
require_file(animation, manifest['base'])
for state in manifest['states']:
    assert 'aseprite' not in state and 'gif' not in state and 'open' not in state
    require_file(animation, state['patch'])
    for frame in state['frames']:
        assert 'png' not in frame
        require_file(animation, frame['delta'])
for action in manifest['actions']:
    assert 'aseprite' not in action and 'gif' not in action
    require_file(animation, action['resultPatch'])
    for frame in action['frames']:
        assert 'png' not in frame
        require_file(animation, frame['delta'])
for pose in hands['poses']:
    require_file(ART / 'Hands', pose['png'])
    assert pose['anchor'] == {'x': 25, 'y': 25}
artist_sha = hashlib.sha256((ROOT / 'ManualEdit/hands_edit.aseprite').read_bytes()).hexdigest()
assert artist_sha == hands['sourceSha256'] == manifest['handRendering']['sourceSha256']
cleanup = json.loads((ROOT / 'cleanup_report.json').read_text(encoding='utf-8-sig'))
for name, sha in cleanup['protectedSources'].items():
    assert hashlib.sha256((ROOT / 'ManualEdit' / name).read_bytes()).hexdigest() == sha.lower()

# Read the editor's own import log; do not modify the editor or force a reload.
log_path = Path.home() / 'AppData/Local/Unity/Editor/Editor.log'
prefix = ART.relative_to(PROJECT).as_posix() + '/'
imported = set()
if log_path.is_file():
    text = log_path.read_text(encoding='utf-8', errors='replace')
    imported = set(re.findall(r'Start importing (' + re.escape(prefix) + r'.*?\.png) using Guid', text))
expected = {prefix + entry['relative'].replace('\\', '/') for entry in inventory['files']}
report = {'status': 'PASS', 'pngCount': len(inventory['files']), 'canvas': [216, 162],
          'pngBytesUnchanged': True, 'manifestPathsExist': True,
          'spriteImportMetadata': 'Single / Point / uncompressed / no mipmap / PPU 20',
          'artistSourceSha256': artist_sha, 'protectedAuthorSourcesUnchanged': True,
          'unityEditorLogImportedPngCount': len(expected & imported)}
(ROOT / 'verification_runtime.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
print(json.dumps(report, indent=2))
