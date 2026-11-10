# 第三升级 · 美术与动画

第二章从早餐、出门到回家的像素素材，统一收在这里。每个动作一个目录；做完但没用上的动作也有自己的位置。

- **`Runtime/`**：Unity 使用的帧、动画和控制器。移动已有资源时一并保留 `.meta`，避免切断引用。
- **`Sources~/`**：Aseprite 母版、原尺寸预览、时序和必要制作脚本。末尾的 `~` 让 Unity 跳过导入，源文件仍可正常编辑和纳入 Git。
- **文档**：旧讨论和设计记录仍留在项目的 `Docs/`、`Tools/`。旧截图、备份和一次性验证产物在项目根目录 `Trash/`，可按清单找回。

| 目录 | 内容 | 当前状态 |
| --- | --- | --- |
| [BreakfastOffer](BreakfastOffer/README.md) | 端面包待机；另留早期单次递盘 | 待机已使用，单次动作留档 |
| [CupSupport](CupSupport/README.md) | 托稳咖啡杯 | 完成素材，当前剧情未播放 |
| [HandRepair](HandRepair/README.md) | 手部维修 | 完成素材，当前剧情未播放 |
| [MapPlan](MapPlan/README.md) | 指地图、路线显现、到达反馈 | 已使用 |
| [WarehouseDoor](WarehouseDoor/README.md) | 仓库开门 | 已使用 |
| [AutoPacking](AutoPacking/README.md) | 自动装箱 | 游戏使用 V3；V4 留作后续选择 |
| [CartPause](CartPause/README.md) | 推车停顿、眨眼与水面 | 已使用 |
| [CartTravel](CartTravel/README.md) | 完整推车街道循环与静态母版 | 完成备选，当前改用停顿画面 |
| [FaceRepair](FaceRepair/README.md) | 修脸、画妆与各阶段待机 | 美术和运行帧已整理，待剧情接入 |
| [CabinetSearch](CabinetSearch/README.md) | 柜门翻找 | 完成备选，未采用 |
| [HomeStills](HomeStills/README.md) | 回家章节静态候选图 | 保留候选，尚未接入 |
| [UpgradeIcon](UpgradeIcon/README.md) | 第三升级图标 | 已使用 |

以后新增动作沿用这个结构。不要把审查大图、临时导入代码和重复压缩包塞回正式素材目录；确实需要复用的制作脚本，和它的输入一起留在 `Sources~/`。

本次整理：2026-10-12。只搬移与归档，不重画像素、不改动画时序、不改剧情绑定。
