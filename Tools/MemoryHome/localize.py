"""Assemble reviewed authored translations; no network service or Unity dependency."""
import json
from pathlib import Path
import sys

OUT=Path(__file__).parent
try:
    from opencc import OpenCC
except ImportError:
    # Reuse the user's existing utility cache; do not modify Unity packages.
    cached=Path.home()/"AppData/Local/uv/cache/archive-v0/j76uC_vkaQcspfch"
    sys.path.insert(0,str(cached))
    from opencc import OpenCC

data=json.loads((OUT/"content.json").read_text(encoding="utf-8"))
by_id={m["id"]:f"os.memory.{m['index']}.text" for m in data["messages"]}
rows={}
for path in sorted(OUT.glob("locale-*.tsv")):
    for raw in path.read_text(encoding="utf-8-sig").splitlines():
        if not raw.strip() or raw.startswith("#"): continue
        cells=raw.split("\t")
        if len(cells)!=8: raise ValueError(f"{path.name}: expected id + ja/ko/fr/de/es/pt_BR/ru: {raw[:100]}")
        key=cells[0]
        if key in rows: raise ValueError("Duplicate translation "+key)
        rows[key]=cells[1:]
cc=OpenCC("s2tw")
result={}
for entry in data["localization"]:
    key=entry["key"];old=entry["values"]
    id=next((k for k,v in by_id.items() if v==key), None)
    lookup=id if id else "@"+old[2]
    translated=rows.get(lookup)
    if translated is None: raise ValueError("Missing translation: "+lookup)
    result[key]=[old[0],cc.convert(old[0]),old[2],*translated]
(OUT/"translations.json").write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding="utf-8")
print(f"{len(result)} keys, {sum(len(v) for v in result.values())} language values")
