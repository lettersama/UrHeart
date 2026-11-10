# 修脸与妆容

美术包含 9 段动作、10 个状态待机；每段在 `Animation/Actions/` 或 `Animation/States/` 下拥有独立子目录。

- `Sources~/ManualEdit/`：作者编辑的人物与手部 Aseprite 母版。
- `Sources~/Native216/Animation/`：完整帧、Aseprite 和原尺寸 GIF。
- `Runtime/Animation/`：运行用的状态与差分帧、时序清单。
- `Sources~/Review/`：可直接打开的自包含预览页面。

制作脚本在 `Sources~/` 根目录。完整帧和运行差分帧用途不同，两套都保留；校验与打包脚本依赖的记录也随制作链保存。不要直接覆盖作者的编辑母版。

## 化妆回忆接入（2026-10-13）

`OSMemoryFaceRepairPresentation` 沿用回忆子窗口，在安装第三升级后陪伴 6960–7240 的正文。按原清单合成底图、累计状态补丁、当前动作／待机差分帧，所有坐标与毫秒时序保持不变。动作结束才替换结果状态，不修改剧情、资源或妆容选择。

| 触发节点 | 动作 |
| --- | --- |
| 6960 | 修补后的待机，等待拿起粉扑 |
| 6970 | 左侧粉扑 |
| 7000 | 右侧粉扑 |
| 7010 | 描眉 |
| 7040 | 歪头“对齐” |
| 7070 | 扶正 |
| 7190／7200 | 原剧情选项决定小猫／爱心 |
| 7210–7240 | 按实际保存的妆容保持待机；未选择时保持中性状态 |

动作在对应正文出字后开始；同一段里的连续阅读不重播动作。较快阅读时先让正在操作的手离场，再接受当前节点的新动作。退出这一段或关闭窗口会取消播放，不锁阅读、不自动翻页。失焦、设置窗口和测试控制台时暂停。

资产通过 Edit Mode 菜单 `Tools/MrTGame/OS Memory/Build Makeup Illustration` 显式生成，也可调用 `MrTGame.EditorTools.OSMemoryFaceRepairAuthoring.Build()`。生成 `Chapter02Makeup.prefab`、`Chapter02Makeup.asset`，仅给 6960 增加图窗引用并同步 CSV，保留原正文与十种语言。此入口可重复执行，不修改场景。当前接入从化妆开始，擦脸与填补两段素材仍保留备选。
