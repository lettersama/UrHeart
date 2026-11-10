# 第二章修脸与妆容素材

> 资料迁移说明（2026-10-12）：历史参考、截图和验证辅助物已归档至 [Trash](../../../../../Trash/Chapter02_2026-10-12/README.md)。部分旧目录及帧号范围保留历史写法；现行素材位置见[按动画整理的 Art 索引](../../README.md)。


216×162 原尺寸，黑色与暖灰两色，透明像素只用 0/255。398 张 PNG 已设置 Sprite / Single、Point、无压缩、无 mipmap、PPU 20。

`Assets/Art/MemoryUpgrade03/FaceRepair/Runtime/Animation/manifest.json` 为游戏播放清单，所有路径相对 Animation 目录。依次合成底图、累计状态补丁和当前帧补丁；完成动作时原子替换目标状态补丁并清除操作层。取消动作不提交结果。猫咪和爱心为互斥分支。

`Assets/Art/MemoryUpgrade03/FaceRepair/Runtime/Hands/manifest.json` 包含四个作者手势及锚点。`Assets/Art/MemoryUpgrade03/FaceRepair/Runtime/Animation/PatchParts` 为独立局部变化层。所有 PNG 保留完整画布，不要自动裁切、拉伸或逐帧重新居中。

可编辑 Aseprite、源帧、预览、重导出脚本和校验报告在相邻的 [`Sources~/`](../Sources~/README.md)。本次完成素材入库，尚未接入场景逻辑。
