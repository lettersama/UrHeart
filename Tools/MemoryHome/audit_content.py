"""Read-only semantic regression audit against the user's committed baseline.

Run: uv run --no-project --with pyyaml python Tools/MemoryHome/audit_content.py
"""
import json
from pathlib import Path
import re
import subprocess
import yaml

ROOT=Path(__file__).resolve().parents[2]
def asset(path,baseline=False):
    raw=(subprocess.check_output(["git","show","HEAD:"+path],cwd=ROOT).decode("utf-8-sig")
         if baseline else (ROOT/path).read_text(encoding="utf-8-sig"))
    raw=re.sub(r"^%.*\n", "",raw,flags=re.M)
    raw=re.sub(r"^--- !u!\d+ &\d+.*$","---",raw,flags=re.M)
    return yaml.safe_load(raw)["MonoBehaviour"]

content=json.loads((ROOT/"Tools/MemoryHome/content.json").read_text(encoding="utf-8"))
locpath="Assets/Data/LocalizationData.asset"
previous={e["key"]:e for e in asset(locpath,True)["entries"]}
current={e["key"]:e for e in asset(locpath)["entries"]}
assert all(current.get(k)==v for k,v in previous.items()),"Existing localization changed"
for entry in content["localization"]:
    actual=current[entry["key"]]["values"]
    assert len(actual)==10 and {v["language"] for v in actual}==set(range(10))
    assert [v["text"] for v in sorted(actual,key=lambda x:x["language"])]==entry["values"]
    assert all(v["text"].strip() for v in actual)
    highlights=entry["values"][0].count("<color=red>")
    for value in actual:
        assert value["text"].count("<color=red>")==value["text"].count("</color>")==highlights, \
            f'Localized red hint mismatch: {entry["key"]}/{value["language"]}'

mempath="Assets/Data/OSMemoryData.asset"
previous_nodes={e["dataIndex"]:e for e in asset(mempath,True)["dataList"]}
current_nodes={e["dataIndex"]:e for e in asset(mempath)["dataList"]}
for index,node in previous_nodes.items():
    actual=current_nodes[index]
    if index==content["entryMemoryIndex"]:
        # Only the interactive entry's scenario reference is new.
        node=json.loads(json.dumps(node));actual=json.loads(json.dumps(actual))
        for group in (node,actual):
            for action in group["onEnterActions"]:
                if action["actionId"]=="memory.enter_interactive_scene":action["asset"]=None
    assert actual==node,f"Existing Memory changed: {index}"
for row in content["messages"]:
    actual=current_nodes[row["index"]]
    assert actual["sourceText"]==row["zh"] and actual["nextDataIndex"]==-1

scenes={s["sceneId"]:s for s in content["scenes"]}
indices={m["index"] for m in content["messages"]}
assert len(indices)==len(content["messages"])
for scene in scenes.values():
    navigation=scene['navigation']
    assert set(navigation)=={'up','down','left','right'}
    definitions={h['hotspotId']:h for h in scene['hotspots']}
    for ref in filter(None,navigation.values()):
        assert ref in definitions and definitions[ref]['kind']==0, f'Invalid navigation: {scene["sceneId"]}/{ref}'
        assert any(o['targetScene'] for o in definitions[ref]['outcomes']), f'Navigation without destination: {ref}'
    for hotspot in scene["hotspots"]:
        assert hotspot["labelKey"] in current
        for outcome in hotspot["outcomes"]:
            assert outcome["memoryIndex"] in indices
            assert not outcome["targetScene"] or outcome["targetScene"] in scenes
            assert not outcome.get("tooltipLabelKey") or outcome["tooltipLabelKey"] in current
report={"existingLocalizationPreserved":len(previous),"existingMemoryPreserved":len(previous_nodes),
        "newMemory":len(indices),"newLocalizationKeys":len(content["localization"]),"localeValues":len(content["localization"])*10,
        "scenes":len(scenes),"hotspots":sum(len(s["hotspots"]) for s in scenes.values()),"status":"passed"}
(ROOT/"Tools/MemoryHome/content-audit.json").write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding="utf-8")
print(json.dumps(report,ensure_ascii=False))
