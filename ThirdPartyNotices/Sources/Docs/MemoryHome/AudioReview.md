# 交互式插画音效审听清单

2026-09-18：全部按 −30 LUFS 综合响度处理，48 kHz / 16-bit 立体声。主要操作段约 1–1.5 秒，短碰击允许自然衰减后的静音尾部。所有响度均为文件测量值，实际播放还受游戏主音量与 SFX 音量控制。

| # | 音效 | 使用位置 | 秒 | LUFS |
|---:|---|---|---:|---:|
| 1 | [开门](../../Assets/Audio/SFX/MemoryHome_DoorOpen.wav) | 进家门、进入工作室和卧室 | 1.35 | -30.01 |
| 2 | [上楼](../../Assets/Audio/SFX/MemoryHome_StairsUp.wav) | 楼下走廊上楼；两步 | 1.45 | -30.04 |
| 3 | [下楼](../../Assets/Audio/SFX/MemoryHome_StairsDown.wav) | 楼上走廊下楼；同源脚步另一段，略降音调 | 1.45 | -30.03 |
| 4 | [房间移动](../../Assets/Audio/SFX/MemoryHome_Walk.wav) | 厨房、客厅、女儿门前及房间返回走廊 | 1.15 | -30.03 |
| 5 | [门把手未打开（停用）](../../Assets/Audio/SFX/MemoryHome_DoorLocked.wav) | 备用，未触发：此处只有文本反馈，无物件或场景变化 | 1.05 | -30.03 |
| 6 | [拉动抽屉](../../Assets/Audio/SFX/MemoryHome_Drawer.wav) | 进入走廊抽屉特写 | 1.20 | -30.00 |
| 7 | [翻动信件（停用）](../../Assets/Audio/SFX/MemoryHome_Paper.wav) | 备用，未触发：此处只有文本反馈，无物件或场景变化 | 1.10 | -29.98 |
| 8 | [木板碰放](../../Assets/Audio/SFX/MemoryHome_Wood.wav) | 拿木板、放木板、扶正木相框 | 1.15 | -29.96 |
| 9 | [布料整理](../../Assets/Audio/SFX/MemoryHome_Cloth.wav) | 拿抹布、摆正靠垫 | 1.15 | -29.98 |
| 10 | [拿起扫帚](../../Assets/Audio/SFX/MemoryHome_Pickup.wav) | 拿扫帚：握取布料声叠轻木声 | 1.10 | -30.03 |
| 11 | [拿起小工具](../../Assets/Audio/SFX/MemoryHome_MetalPickup.wav) | 拿锤子、钉子、保险丝 | 1.05 | -30.04 |
| 12 | [打开工具箱](../../Assets/Audio/SFX/MemoryHome_Toolbox.wav) | 解锁工具箱特写后打开 | 1.25 | -29.99 |
| 13 | [放下机器人](../../Assets/Audio/SFX/MemoryHome_RobotPlace.wav) | 工作台首次放置；主要落放声在前半秒 | 1.05 | -30.00 |
| 14 | [放置钉子](../../Assets/Audio/SFX/MemoryHome_NailPlace.wav) | 六处钉子各自第一次点击 | 1.05 | -30.00 |
| 15 | [锤钉子](../../Assets/Audio/SFX/MemoryHome_Hammer.wav) | 六处钉子各自第二次点击；两下锤击 | 1.25 | -29.98 |
| 16 | [楼下碎玻璃](../../Assets/Audio/SFX/MemoryHome_GlassBreak.wav) | 放下机器人后原有剧情事件 | 1.60 | -29.98 |
| 17 | [扫掉玻璃](../../Assets/Audio/SFX/MemoryHome_Sweep.wav) | 满足木板加固和扫帚条件后清理 | 1.50 | -29.97 |
| 18 | [收拾碗碟](../../Assets/Audio/SFX/MemoryHome_Dishes.wav) | 持有抹布后收拾水槽 | 1.35 | -30.00 |
| 19 | [断开电源](../../Assets/Audio/SFX/MemoryHome_PowerOff.wav) | 机器人接口断电 | 1.05 | -30.02 |
| 20 | [接好电线](../../Assets/Audio/SFX/MemoryHome_Connect.wav) | 正确接口配对成功 | 1.05 | -29.95 |
| 21 | [更换保险丝](../../Assets/Audio/SFX/MemoryHome_Fuse.wav) | 断电后放入保险丝 | 1.10 | -29.99 |
| 22 | [通电测试成功](../../Assets/Audio/SFX/MemoryHome_PowerOn.wav) | 接线及保险丝就绪后通电 | 1.25 | -29.99 |
| 23 | [门外敲门](../../Assets/Audio/SFX/MemoryHome_Knock.wav) | 修好机器人后的原有剧情事件 | 1.40 | -30.02 |

纯观察、条件不满足、已经做完的重复检查、错误接线、悬停和背包选中保留安静。普通房间移动有脚步，物件特写返回不额外添加走路声。

## 素材来源与复现

新下载素材来自 Pixabay，适用 [Pixabay Content License](https://pixabay.com/service/license-summary/)。已有项目音效只制作独立派生文件，不修改全局原音频。完整剪辑参数、来源与 SHA-256 见 [audio-manifest.json](../../Tools/MemoryHome/audio-manifest.json)，复现脚本为 [build_audio.py](../../Tools/MemoryHome/build_audio.py)。

- [Door Opening](https://pixabay.com/sound-effects/door-opening-454242/) — DRAGON-STUDIO
- [walking up wood stairs in shoes](https://pixabay.com/sound-effects/film-special-effects-walking-up-wood-stairs-in-shoes-83439/) — Bastard Fish / freesound_community
- [Broken Glass Impact](https://pixabay.com/sound-effects/broken-glass-impact-454859/) — Universfield
- [Hitting nails with Hammer](https://pixabay.com/sound-effects/film-special-effects-hitting-nails-with-hammer-38478/) — aidansamuel / freesound_community
- [Door Knocking](https://pixabay.com/sound-effects/film-special-effects-door-knocking-175163/) — SoundReality
- [Sweep the glass with a broom](https://pixabay.com/sound-effects/sweep-the-glass-with-a-broom-456369/) — spinopel
- [Large Wood Plank Falls Down](https://pixabay.com/sound-effects/film-special-effects-large-wood-plank-falls-down-230892/) — Alex_Jauk
- [Cloth Grab Foley](https://pixabay.com/sound-effects/household-cloth-grab-foley-soundque-foley-recording-445581/) — Soundque
- [Drawer OPEN](https://pixabay.com/sound-effects/drawer-open-98801/) — cMilan / freesound_community
- [Dishes](https://pixabay.com/sound-effects/film-special-effects-dishes-88148/) — Nomadyag / freesound_community
- [double-door-lock](https://pixabay.com/sound-effects/double-door-lock-101210/) — freesound_community
- [guitar jack noises raw](https://pixabay.com/sound-effects/guitar-jack-noises-raw-26374/) — freesound_community
- [electric fan motor blades removed](https://pixabay.com/sound-effects/electric-fan-motor-blades-removed-13169/) — edr
- [electronic beep 1](https://pixabay.com/sound-effects/electronic-beep-1-453381/) — eaglaxle
- Paper_Game_01_Switch — 项目已有资源

## 场景验证

首版完整 Play Mode 流程：passed（3733 项现有检查）；23 个音效引用、62 条带音效的交互结果，制作源与 Unity 资产无差异。存档副本测试结束后已恢复原存档引用。详情见 [验证记录](audio-batch-verification.json)。

后续反馈修正：取消大厅信封、抽屉信件与女儿房门把手的声音。当前启用 21 项素材、59 条带音效的交互结果；仅有文本反馈的结果均无音效。见 [定点验证](text-only-audio-verification.json)。
