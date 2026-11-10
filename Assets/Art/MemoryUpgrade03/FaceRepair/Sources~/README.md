# 修脸与妆容动画

> 资料迁移说明（2026-10-12）：历史参考、截图和验证辅助物已归档至 [Trash](../../../../../Trash/Chapter02_2026-10-12/README.md)。部分旧目录及帧号范围保留历史写法；现行素材位置见[按动画整理的 Art 索引](../../README.md)。


已使用作者修改的四个手势，重建 10 个待机状态和 9 段单次动作，共 361 帧。手形各层原样导入，只作整数像素平移；没有重新描绘作者的修改。双肩每侧累计收窄 7 px，嘴部保留人物母版。

游戏画布为 **216×162 px**，仅使用黑色 `#000000`、暖灰 `#87827e` 和完全透明。所有帧画布与原点相同。

## 正式素材

游戏资源已搬到项目中的：

`Assets/Art/MemoryUpgrade03/FaceRepair/Runtime/`

- 根目录：人物与工作台底图、分离人物、桌面道具等静态素材。
- `Hands/`：四个作者手势及接触锚点，锚点在左上角坐标 `(25,25)`。
- `Animation/States/`：10 个累计状态补丁及眨眼帧补丁。
- `Animation/Actions/`：9 段操作帧补丁及各段结果补丁。
- `Animation/PatchParts/`：清洁、修补、左右上粉、眉毛、猫咪和爱心的局部绘画层。
- `Animation/manifest.json`：帧时长、起止状态、完成帧和手势移动轨迹。路径相对该文件所在目录。

共 398 张 PNG。导入配置为 Sprite / Single、Point、无压缩、关闭 mipmap、不缩放 NPOT、PPU 20，与现有章节 UI 素材约定一致。这里是素材入库，场景播放逻辑尚未接入。

## 当前制作源文件

- `Assets/Art/MemoryUpgrade03/FaceRepair/Sources~/ManualEdit/hands_edit.aseprite`：作者亲自修改的 116×116、4 帧、3 图层母版，原文件完整保留。
- `Assets/Art/MemoryUpgrade03/FaceRepair/Sources~/ManualEdit/character_states_edit.aseprite`：交给作者调整的 10 个人物静态状态。
- `Assets/Art/MemoryUpgrade03/FaceRepair/Sources~/Native216/repair_bust.aseprite`：当前人物分层母版。
- `Assets/Art/MemoryUpgrade03/FaceRepair/Sources~/Native216/Hands/floating_hands.aseprite` 与 `Layers/`：导入后的完整画布手势及各层缓存。
- `Native216/Animation/States/*/idle.aseprite`：10 份可编辑待机。
- `Native216/Animation/Actions/*/action.aseprite`：9 份可编辑操作。

`Native216/Animation/` 内的完整 PNG 帧和原尺寸 GIF 用于逐帧对照、复核与源文件预览；游戏使用的补丁在相邻 `Runtime/` 中，不在两处重复保留。这里的 `manifest.json` 为制作清单，配合 `verify_art.py` 自动从正式素材目录读取补丁。游戏应读取 `Runtime/Animation/manifest.json`。

## 查看与播放

`Assets/Art/MemoryUpgrade03/FaceRepair/Sources~/Review/animation_review.html` 是自包含的最新预览，图片直接取自正式素材目录。支持正常播放、逐帧、取消动作和切换状态。`Assets/Art/MemoryUpgrade03/FaceRepair/Sources~/Review/single_hand_contact_sheet.png` 展示四个实际接触帧；`Assets/Art/MemoryUpgrade03/FaceRepair/Sources~/Review/user_edited_hands_2x.png` 展示作者修改的手势。

播放时依次叠加底图、当前 `state_patch.png` 和待机帧补丁。操作开始时眼睛回到睁眼帧，播放操作 `Deltas`。完整结束的同一帧替换目标状态补丁、清除操作层，并开始目标状态待机。取消只清除操作层，不提交结果。猫咪和爱心是从 `07_neutral` 分出的互斥结果。

| 动作 | 起始状态 | 完成状态 |
|---|---|---|
| `01_wipe` | 原始 | 擦拭后 |
| `02_fill` | 擦拭后 | 填补后 |
| `03_powder_left` | 填补后 | 左侧上粉后 |
| `04_powder_right` | 左侧上粉后 | 两侧上粉后 |
| `05_brows` | 两侧上粉后 | 画眉后 |
| `06_tilt` | 画眉后 | 轻轻偏头 |
| `07_return` | 轻轻偏头 | 回正 |
| `08_cat` | 回正 | 猫咪图案 |
| `09_heart` | 回正 | 爱心图案 |

每个状态待机为 5.18 秒。每段最多一只浮空手，只保留短袖口；桌布不移动。补丁使用普通透明叠加，黑色和暖灰都可以覆盖原像素。

## 验证与重导出

- `Assets/Art/MemoryUpgrade03/FaceRepair/Sources~/verification_hands.json`：作者像素导入一致性，差异为 0。
- `Assets/Art/MemoryUpgrade03/FaceRepair/Sources~/verification_native.json`：通过 Aseprite MCP 重新打开全部动画，核对合成、时长和手势各层，差异为 0。
- `Assets/Art/MemoryUpgrade03/FaceRepair/Sources~/verification_pixels.json`：补丁合成、首尾衔接、GIF 时序、两色透明度、脸部边界和桌布位置。
- `Assets/Art/MemoryUpgrade03/FaceRepair/Sources~/runtime_inventory.json`：正式 PNG 的路径与 SHA-256。
- `Assets/Art/MemoryUpgrade03/FaceRepair/Sources~/verification_runtime.json`：搬移后的像素字节、清单路径、导入元数据和作者母版哈希。
- `Assets/Art/MemoryUpgrade03/FaceRepair/Sources~/verification_unity.json`：Unity MCP 实际加载 398 个 Sprite，全部为 216×162，导入参数验证通过。
- `Assets/Art/MemoryUpgrade03/FaceRepair/Sources~/cleanup_report.json`：本次移走的旧稿数量及原母版哈希。

继续制作时使用 `Assets/Art/MemoryUpgrade03/FaceRepair/Sources~/import_artist_hands.lua`、`Assets/Art/MemoryUpgrade03/FaceRepair/Sources~/build_animations.lua`、`Assets/Art/MemoryUpgrade03/FaceRepair/Sources~/build_review.lua`。底层绘制和导出都通过 `Assets/Art/MemoryUpgrade03/FaceRepair/Sources~/aseprite_mcp_client.py` 调用真实 Aseprite MCP，回执保存在 `Assets/Art/MemoryUpgrade03/FaceRepair/Sources~/mcp_receipts.jsonl`。当前只生成 Native216，432 和 1448 放大版本已经归档。

导出后执行 `Assets/Art/MemoryUpgrade03/FaceRepair/Sources~/verify_art.py` 和 `Assets/Art/MemoryUpgrade03/FaceRepair/Sources~/verify_reopen.lua`，再运行 `Assets/Art/MemoryUpgrade03/FaceRepair/Sources~/publish_runtime.ps1` 将正式 PNG 搬入 Assets，保留已有 `.meta` GUID。执行 `Assets/Art/MemoryUpgrade03/FaceRepair/Sources~/verify_runtime.py` 并通过 Unity MCP 检查实际导入后，最后运行 `Assets/Art/MemoryUpgrade03/FaceRepair/Sources~/build_preview.py` 和 `Assets/Art/MemoryUpgrade03/FaceRepair/Sources~/package_review.py`。Python 验证只读图片，不绘制或重采样。

## 旧稿

旧参考、试画、过时手形、高分辨率版本和旧 ZIP 已移到：

`Trash/Chapter02RepairBust/2026-10-12_ArtistHands/`

共 1,246 个文件，约 27.4 MiB，没有删除。归档内 `archive_inventory.json` 记录每个文件的原始相对路径与哈希，可按原路径还原。当前 `Assets/Art/MemoryUpgrade03/FaceRepair/Sources~/Reference/` 仅保留已确认的人物 v6 参考。
