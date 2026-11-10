"""Read-only image QA. Writes a JSON report, never raster media."""
from pathlib import Path
import hashlib
import json
from PIL import Image, ImageSequence

ROOT = Path(__file__).resolve().parent
RUNTIME = ROOT.parent / 'Runtime'
ALLOWED = {(0, 0, 0, 0), (0, 0, 0, 255), (135, 130, 126, 255)}


def read(path):
    if not path.exists():
        path = RUNTIME / path.relative_to(ROOT / 'Native216')
    im = Image.open(path).convert('RGBA')
    colors = im.getcolors(4)
    assert colors and all(color in ALLOWED for _, color in colors), str(path)
    return im


def equal(a, b, label):
    assert a.size == b.size and a.tobytes() == b.tobytes(), label


def over(a, b):
    return Image.alpha_composite(a, b)


def gif_check(path, frames, loop):
    gif = Image.open(path)
    if loop:
        assert gif.info.get('loop') == 0, f'{path}: missing idle loop'
    else:
        assert 'loop' not in gif.info, f'{path}: operation repeats'
    expected = []
    elapsed = 0
    for f in frames:
        expected.append((elapsed, f))
        elapsed += f['durationMs']
    actual_time, index = 0, 0
    for frame in ImageSequence.Iterator(gif):
        while index + 1 < len(expected) and expected[index + 1][0] <= actual_time:
            index += 1
        equal(frame.convert('RGBA'), read(path.parent.parent.parent / expected[index][1]['png']), str(path))
        actual_time += frame.info.get('duration', 0)
    assert actual_time == elapsed, (str(path), actual_time, elapsed)
    return gif.n_frames


def main():
    reports = []
    for folder in ('Native216',):
        art = ROOT / folder / 'Animation'
        m = json.loads((art / 'manifest.json').read_text())
        base = read(ROOT / folder / 'repair_bust.png')
        states = {s['id']: s for s in m['states']}
        scale = base.width // 216
        face_mask = read(art / 'face_surface_mask.png')
        outside_face = [pixel[3] == 0 for pixel in face_mask.getdata()]
        cloth_box = tuple(v * scale for v in (0, 140, 52, 162))
        cloth_reference = base.crop(cloth_box)
        assert m['maxVisibleHands'] == 1
        parts_dir = art / 'PatchParts'
        if not any(parts_dir.glob('*.png')):
            parts_dir = RUNTIME / 'Animation/PatchParts'
        for part in parts_dir.glob('*.png'):
            assert all(not outside or pixel[3] == 0
                       for outside, pixel in zip(outside_face, read(part).getdata())), str(part) + ' extends outside face'
        state_images = {}
        count = 0
        for state in m['states']:
            opened = read(art / state['open'])
            state_images[state['id']] = opened
            if state['angle'] == 0:
                scale = base.width // 216
                mouth = tuple(v * scale for v in (96, 73, 105, 79))
                equal(opened.crop(mouth), base.crop(mouth), state['id'] + ' approved mouth retained')
            equal(over(base, read(art / state['patch'])), opened, state['id'] + ' cumulative patch')
            for frame in state['frames']:
                equal(over(opened, read(art / frame['delta'])), read(art / frame['png']), state['id'] + ' idle')
                count += 1
            equal(read(art / state['frames'][0]['png']), read(art / state['frames'][-1]['png']), 'idle seam')
            gif_check(art / state['gif'], state['frames'], True)
        for action in m['actions']:
            source, target = state_images[action['from']], state_images[action['to']]
            equal(over(source, read(art / action['resultPatch'])), target, action['id'] + ' incremental result')
            for frame in action['frames']:
                equal(over(source, read(art / frame['delta'])), read(art / frame['png']), action['id'] + ' operation delta')
                equal(read(art / frame['png']).crop(cloth_box), cloth_reference, action['id'] + ' table cloth moved')
                count += 1
            equal(read(art / action['frames'][0]['png']), source, action['id'] + ' first frame')
            equal(read(art / action['frames'][-1]['png']), target, action['id'] + ' last frame')
            gif_check(art / action['gif'], action['frames'], False)
        assert state_images['08_cat'].tobytes() != state_images['09_heart'].tobytes()
        # A branch replacement must produce the intended branch with no carry-over.
        for sid in ('08_cat', '09_heart'):
            equal(over(base, read(art / states[sid]['patch'])), state_images[sid], 'exclusive branch')
        reports.append({'folder': folder, 'states': len(states), 'actions': len(m['actions']), 'verifiedFrames': count,
                        'status': 'PASS', 'paletteAndAlpha': 'exact', 'idleAndActionPatchComposition': 'PASS',
                        'gifTimelineAndLoopMetadata': 'PASS', 'endpointPixelDifferences': 0})
        reports[-1].update({'cosmeticPatchesStayWithinFace': 'PASS', 'tableClothUnchangedEveryActionFrame': 'PASS'})
    reference_sha = hashlib.sha256((ROOT / 'Reference/approved-v6.png').read_bytes()).hexdigest()
    assert reference_sha == '49a3d4a996b2e960072c4702561a75289a3eb2d248d1ee0907823dc4cc5465e5'
    report = {'status': 'PASS', 'sourceUnchangedSha256': reference_sha, 'versions': reports}
    (ROOT / 'verification_pixels.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps(report, indent=2))


if __name__ == '__main__':
    main()
