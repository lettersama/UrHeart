"""Build short Memory Home SFX from recorded sources; requires FFmpeg on PATH or --ffmpeg."""
import argparse
import hashlib
import html
import json
import math
from pathlib import Path
import re
import shutil
import subprocess
import wave

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
SFX = ROOT / "Assets/Audio/SFX"
WORK = ROOT / "Temp/MemoryHomeAudioBatch/render"
DOCS = ROOT / "Docs/MemoryHome"
TARGET = -30.0

def source(file, title, author, url="", existing=False):
    return dict(path=str((SFX if existing else HERE / "AudioSources") / file),
                title=title, author=author, url=url)

SOURCES = {
    "door": source("dragon-studio-door-opening-454242.mp3", "Door Opening", "DRAGON-STUDIO", "https://pixabay.com/sound-effects/door-opening-454242/"),
    "stairs": source("freesound_community-walking-up-wood-stairs-in-shoes-83439.mp3", "walking up wood stairs in shoes", "Bastard Fish / freesound_community", "https://pixabay.com/sound-effects/film-special-effects-walking-up-wood-stairs-in-shoes-83439/"),
    "glass": source("glass-454859.mp3", "Broken Glass Impact", "Universfield", "https://pixabay.com/sound-effects/broken-glass-impact-454859/"),
    "hammer": source("hammer-38478.mp3", "Hitting nails with Hammer", "aidansamuel / freesound_community", "https://pixabay.com/sound-effects/film-special-effects-hitting-nails-with-hammer-38478/"),
    "knock": source("knock-175163.mp3", "Door Knocking", "SoundReality", "https://pixabay.com/sound-effects/film-special-effects-door-knocking-175163/"),
    "sweep": source("sweep-456369.mp3", "Sweep the glass with a broom", "spinopel", "https://pixabay.com/sound-effects/sweep-the-glass-with-a-broom-456369/"),
    "wood": source("wood-230892.mp3", "Large Wood Plank Falls Down", "Alex_Jauk", "https://pixabay.com/sound-effects/film-special-effects-large-wood-plank-falls-down-230892/"),
    "cloth": source("cloth-445581.mp3", "Cloth Grab Foley", "Soundque", "https://pixabay.com/sound-effects/household-cloth-grab-foley-soundque-foley-recording-445581/"),
    "drawer": source("drawer-98801.mp3", "Drawer OPEN", "cMilan / freesound_community", "https://pixabay.com/sound-effects/drawer-open-98801/"),
    "dishes": source("dishes-88148.mp3", "Dishes", "Nomadyag / freesound_community", "https://pixabay.com/sound-effects/film-special-effects-dishes-88148/"),
    "lock": source("freesound_community-double-door-lock-101210.mp3", "double-door-lock", "freesound_community", "https://pixabay.com/sound-effects/double-door-lock-101210/", True),
    "jack": source("freesound_community-guitar-jack-noises-raw-26374.mp3", "guitar jack noises raw", "freesound_community", "https://pixabay.com/sound-effects/guitar-jack-noises-raw-26374/", True),
    "motor": source("edr-electric-fan-motor-blades-removed-13169.mp3", "electric fan motor blades removed", "edr", "https://pixabay.com/sound-effects/electric-fan-motor-blades-removed-13169/", True),
    "beep": source("eaglaxle-electronic-beep-1-453381.mp3", "electronic beep 1", "eaglaxle", "https://pixabay.com/sound-effects/electronic-beep-1-453381/", True),
    "paper": source("Paper_Game_01_Switch.wav", "Paper_Game_01_Switch", "项目已有资源", existing=True),
}

def layer(src, start=0, take=None, at=0, gain=0, filters=""):
    return dict(source=src, start=start, take=take, at=at, gain=gain, filters=filters)

def clip(cue, name, label, duration, usage, *layers):
    return dict(cue=cue, file="MemoryHome_" + name + ".wav", label=label,
                duration=duration, usage=usage, layers=list(layers))

CLIPS = [
    clip("door", "DoorOpen", "开门", 1.35, "进家门、进入工作室和卧室", layer("door", take=1.65, filters="atempo=1.25")),
    clip("stairs_up", "StairsUp", "上楼", 1.45, "楼下走廊上楼；两步", layer("stairs", 1.93, 1.45)),
    clip("stairs_down", "StairsDown", "下楼", 1.45, "楼上走廊下楼；同源脚步另一段，略降音调", layer("stairs", 5.73, 1.38, filters="asetrate=45600,aresample=48000")),
    clip("walk", "Walk", "房间移动", 1.15, "厨房、客厅、女儿门前及房间返回走廊", layer("stairs", 3.35, 1.15)),
    clip("door_locked", "DoorLocked", "门把手未打开", 1.05, "尝试女儿房门把手", layer("lock"), layer("lock", take=.32, at=.38, gain=-4)),
    clip("drawer", "Drawer", "拉动抽屉", 1.20, "进入走廊抽屉特写", layer("drawer")),
    clip("paper", "Paper", "翻动信件", 1.10, "走廊及抽屉内查看信件", layer("paper"), layer("paper", at=.43, gain=-3, filters="atempo=.9")),
    clip("wood", "Wood", "木板碰放", 1.15, "拿木板、放木板、扶正木相框", layer("wood", .13, 1.0, filters="highpass=f=65")),
    clip("cloth", "Cloth", "布料整理", 1.15, "拿抹布、摆正靠垫", layer("cloth", 2.77, 1.15, filters="highpass=f=100")),
    clip("pickup", "Pickup", "拿起扫帚", 1.10, "拿扫帚：握取布料声叠轻木声", layer("cloth", .9, .75), layer("wood", .13, .65, at=.16, gain=-9)),
    clip("metal_pickup", "MetalPickup", "拿起小工具", 1.05, "拿锤子、钉子、保险丝", layer("lock"), layer("jack", .54, .22, at=.48, gain=-6)),
    clip("toolbox", "Toolbox", "打开工具箱", 1.25, "解锁工具箱特写后打开", layer("drawer", take=1.15, gain=-5), layer("lock", at=.08)),
    clip("robot_place", "RobotPlace", "放下机器人", 1.05, "工作台首次放置；主要落放声在前半秒", layer("wood", .13, .48, filters="lowpass=f=3500,atempo=1.1"), layer("lock", at=.08, gain=-9)),
    clip("nail_place", "NailPlace", "放置钉子", 1.05, "六处钉子各自第一次点击", layer("lock", .05, .28, filters="highpass=f=500"), layer("lock", .15, .22, at=.32, gain=-5)),
    clip("hammer", "Hammer", "锤钉子", 1.25, "六处钉子各自第二次点击；两下锤击", layer("hammer", .12, 1.25)),
    clip("glass", "GlassBreak", "楼下碎玻璃", 1.60, "放下机器人后原有剧情事件", layer("glass", take=1.60)),
    clip("sweep", "Sweep", "扫掉玻璃", 1.50, "满足木板加固和扫帚条件后清理", layer("sweep", .43, 1.50, filters="highpass=f=100")),
    clip("dishes", "Dishes", "收拾碗碟", 1.35, "持有抹布后收拾水槽", layer("dishes", take=1.35)),
    clip("power_off", "PowerOff", "断开电源", 1.05, "机器人接口断电", layer("jack", .02, .50, filters="lowpass=f=5000")),
    clip("connect", "Connect", "接好电线", 1.05, "正确接口配对成功", layer("jack", .29, .48), layer("lock", .1, .15, at=.4, gain=-7)),
    clip("fuse", "Fuse", "更换保险丝", 1.10, "断电后放入保险丝", layer("lock", take=.4), layer("jack", .54, .22, at=.45, gain=-4)),
    clip("power_on", "PowerOn", "通电测试成功", 1.25, "接线及保险丝就绪后通电", layer("motor", take=.9, gain=-4), layer("beep", at=.65)),
    clip("knock", "Knock", "门外敲门", 1.40, "修好机器人后的原有剧情事件", layer("knock", take=1.40)),
]

# Keep these recordings for review, but text-only interactions do not play them.
for entry in CLIPS:
    if entry["cue"] in ("paper", "door_locked"):
        entry["enabled"] = False
        entry["usage"] = "备用，未触发：此处只有文本反馈，无物件或场景变化"

def run(ffmpeg, args):
    p = subprocess.run([ffmpeg, "-hide_banner", "-nostdin", "-y"] + args,
                       capture_output=True, text=True, encoding="utf-8", errors="replace")
    if p.returncode:
        raise RuntimeError(p.stderr)
    return p.stderr

def measure(ffmpeg, path):
    log = run(ffmpeg, ["-i", str(path), "-af", "loudnorm=I=-30:TP=-1:LRA=11:print_format=json", "-f", "null", "-"])
    data = json.loads(re.findall(r'\{\s*"input_i".*?\}', log, flags=re.S)[-1])
    return {"lufs": float(data["input_i"]), "truePeak": float(data["input_tp"])}

def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()

def build(ffmpeg, entry):
    duration = entry["duration"]
    rendered = []
    for i, part in enumerate(entry["layers"]):
        src = Path(SOURCES[part["source"]]["path"])
        if not src.exists():
            raise FileNotFoundError(src)
        output = WORK / f'{entry["cue"]}-{i}.wav'
        trim = f'atrim=start={part["start"]}'
        if part["take"] is not None:
            trim += f':duration={part["take"]}'
        filters = [trim, "asetpts=PTS-STARTPTS", "aresample=48000", "aformat=channel_layouts=stereo"]
        if part["filters"]:
            filters.append(part["filters"])
        filters += [f'volume={part["gain"]}dB', "afade=t=in:d=0.005"]
        if part["at"]:
            filters.append(f'adelay={round(part["at"]*1000)}:all=1')
        filters += ["apad", f"atrim=duration={duration}"]
        run(ffmpeg, ["-i", str(src), "-af", ",".join(filters), "-ar", "48000", "-c:a", "pcm_f32le", str(output)])
        rendered.append(output)
    mixed = WORK / f'{entry["cue"]}-mixed.wav'
    args = [arg for path in rendered for arg in ("-i", str(path))]
    inputs = "".join(f"[{i}:a]" for i in range(len(rendered)))
    tail = f"afade=t=out:st={duration-.10}:d=0.10"
    run(ffmpeg, args + ["-filter_complex", inputs + f"amix=inputs={len(rendered)}:normalize=0," + tail,
                        "-ar", "48000", "-c:a", "pcm_f32le", str(mixed)])
    before = measure(ffmpeg, mixed)
    if not math.isfinite(before["lufs"]):
        raise ValueError(f'Silent clip: {entry["cue"]}')
    gain = TARGET - before["lufs"]
    if before["truePeak"] + gain > -5:
        raise ValueError(f'Excessive transient in {entry["cue"]}; revise the edit instead of clipping')
    destination = SFX / entry["file"]
    for _ in range(3):
        run(ffmpeg, ["-i", str(mixed), "-af", f"volume={gain}dB", "-ar", "48000", "-c:a", "pcm_s16le", str(destination)])
        result = measure(ffmpeg, destination)
        if abs(result["lufs"] - TARGET) <= .1:
            break
        gain += TARGET - result["lufs"]
    with wave.open(str(destination), "rb") as wav:
        result.update(duration=wav.getnframes()/wav.getframerate(), channels=wav.getnchannels(), rate=wav.getframerate())
    assert 1 <= result["duration"] <= 2 and abs(result["lufs"]-TARGET) <= .15, result
    return dict(entry, **result, gainDB=round(gain, 3), sha256=sha(destination))

def write_review(manifest):
    rows = []
    lines = ["# 交互式插画音效审听清单", "", "2026-09-18：全部按 −30 LUFS 综合响度处理，48 kHz / 16-bit 立体声。主要操作段约 1–1.5 秒，短碰击允许自然衰减后的静音尾部。所有响度均为文件测量值，实际播放还受游戏主音量与 SFX 音量控制。", "", "| # | 音效 | 使用位置 | 秒 | LUFS |", "|---:|---|---|---:|---:|"]
    for i, item in enumerate(manifest["clips"], 1):
        label = item["label"] + ("（停用）" if not item.get("enabled", True) else "")
        # This document lives in Docs/MemoryHome, two levels below the project root.
        rel = "../../Assets/Audio/SFX/" + item["file"]
        lines.append(f'| {i} | [{label}]({rel}) | {item["usage"]} | {item["duration"]:.2f} | {item["lufs"]:.2f} |')
        rows.append(f'<tr><td>{i:02}</td><td><b>{html.escape(label)}</b><small>{html.escape(item["usage"])}</small></td><td>{item["duration"]:.2f}s<br>{item["lufs"]:.2f} LUFS</td><td><audio controls preload="none" src="{rel}"></audio></td></tr>')
    lines += ["", "纯观察、条件不满足、已经做完的重复检查、错误接线、悬停和背包选中保留安静。普通房间移动有脚步，物件特写返回不额外添加走路声。", "", "## 素材来源与复现", "", "新下载素材来自 Pixabay，适用 [Pixabay Content License](https://pixabay.com/service/license-summary/)。已有项目音效只制作独立派生文件，不修改全局原音频。完整剪辑参数、来源与 SHA-256 见 [audio-manifest.json](../../Tools/MemoryHome/audio-manifest.json)，复现脚本为 [build_audio.py](../../Tools/MemoryHome/build_audio.py)。", ""]
    for key, item in manifest["sources"].items():
        link = f'[{item["title"]}]({item["url"]})' if item["url"] else item["title"]
        lines.append(f'- {link} — {item["author"]}')
    verification = DOCS / "audio-batch-verification.json"
    if verification.exists():
        check = json.loads(verification.read_text(encoding="utf-8"))
        lines += ["", "## 场景验证", "",
                  f'首版完整 Play Mode 流程：{check["playModeStatus"]}（{check["checkCount"]} 项现有检查）；'
                  f'{check["clipCount"]} 个音效引用、{check["outcomes"]} 条带音效的交互结果，制作源与 Unity 资产无差异。'
                  '存档副本测试结束后已恢复原存档引用。'
                  '详情见 [验证记录](audio-batch-verification.json)。']
    if (DOCS / "text-only-audio-verification.json").exists():
        lines += ["", "后续反馈修正：取消大厅信封、抽屉信件与女儿房门把手的声音。当前启用 21 项素材、59 条带音效的交互结果；仅有文本反馈的结果均无音效。见 [定点验证](text-only-audio-verification.json)。"]
    (DOCS / "AudioReview.md").write_text("\n".join(lines)+"\n", encoding="utf-8")
    body = '<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>回忆屋音效审听</title><style>body{max-width:1080px;margin:40px auto;padding:0 24px;background:#171717;color:#e7e5e2;font:16px/1.6 system-ui}h1{font-size:28px}p,small{color:#b6b0aa}small{display:block}table{border-collapse:collapse;width:100%}td{padding:15px 10px;border-bottom:1px solid #3c3335}td:first-child{color:#c66473}audio{width:300px}a{color:#db8e9a}@media(max-width:700px){audio{width:200px}td{padding:10px 4px}}</style><h1>回忆屋 · 音效审听</h1><p>第一版｜23 项｜约 −30 LUFS｜1–2 秒。按编号提意见即可。每次只播放一项。</p><table>' + ''.join(rows) + '</table><p>纯观察、重复检查和条件不足暂不发声。文件响度已统一，系统播放器音量会影响试听响度。</p><script>document.addEventListener("play",e=>{if(e.target.tagName==="AUDIO")document.querySelectorAll("audio").forEach(a=>{if(a!==e.target)a.pause()})},true)</script></html>'
    active = sum(item.get("enabled", True) for item in manifest["clips"])
    body = body.replace("第一版｜23 项｜", f"{len(manifest['clips'])} 项素材，{active} 项启用｜")
    (DOCS / "AudioReview.html").write_text(body, encoding="utf-8")

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--ffmpeg", default=shutil.which("ffmpeg"))
    args = parser.parse_args()
    if not args.ffmpeg:
        parser.error("Provide --ffmpeg PATH or add FFmpeg to PATH")
    WORK.mkdir(parents=True, exist_ok=True)
    DOCS.mkdir(parents=True, exist_ok=True)
    results = []
    for entry in CLIPS:
        result = build(args.ffmpeg, entry)
        results.append(result)
        print(f'{entry["cue"]}: {result["duration"]:.2f}s, {result["lufs"]:.2f} LUFS, {result["truePeak"]:.2f} dBTP', flush=True)
    sources = {key:dict(value, path=str(Path(value["path"]).relative_to(ROOT)).replace("\\", "/"), sha256=sha(value["path"])) for key,value in SOURCES.items()}
    manifest = dict(date="2026-09-18", targetLUFS=TARGET, sources=sources, clips=results)
    (HERE / "audio-manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2)+"\n", encoding="utf-8")
    write_review(manifest)

if __name__ == "__main__":
    main()
