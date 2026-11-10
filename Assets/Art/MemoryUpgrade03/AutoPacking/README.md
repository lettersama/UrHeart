# 自动装箱

游戏当前使用 **V3 的 52 帧**，与 `Runtime/` 对应。可编辑母版为 `Sources~/Box_v3/collection-box-v3.aseprite`。

`Sources~/Box_v4/` 是尚未接入的 54 帧版本。它没有独立 Aseprite 母版，保留了 `build_v4.py`、时序和全部导出帧；重建时会读取兄弟目录 `Box_v3/` 的帧和时序，因此这两个目录一起保存。
