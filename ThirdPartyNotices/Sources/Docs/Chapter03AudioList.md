# 第三章音效清单

2026-10-25。新增 **44 个音频文件**：P1 主线 26 个、P2 环境循环 6 个、P3 可选物件 12 个；另复用 17 个既有键。

新文件位于 `Assets/Audio/SFX/Chapter03/`，均为 **44.1 kHz、单声道、PCM16 WAV**。38 个单次声注册到 `AudioData.SFX`，6 个环境循环注册到 `AudioData.AMBIENT`。原库中缺少的 7 个复用键已补登记，既有音频文件与原有登记保持不变。

制作方向为轻、近、略闷的居家拟音。新单次通常用 0.3–0.35，复用声按原需求 0.25–0.45；环境循环 0.1–0.12。屋内雨声仍使用原来的 0.33 增益。参数与波形检查不能替代主观试听。

## 新增 P1：主线动作

| 键名 | 秒 | 使用位置 | 来源与作者 | 授权 |
| --- | ---: | --- | --- | --- |
| `Chapter03_Step_0` | 0.200 | 屋内每走一格；随机变体，连续不重复 | [footstep_wood_000](https://kenney.nl/assets/impact-sounds) — Kenney Vleugels (Kenney.nl)<br>[impactMetal_light_000](https://kenney.nl/assets/impact-sounds) — Kenney Vleugels (Kenney.nl) | CC0 1.0 |
| `Chapter03_Step_1` | 0.210 | 屋内每走一格；随机变体，连续不重复 | [footstep_wood_001](https://kenney.nl/assets/impact-sounds) — Kenney Vleugels (Kenney.nl)<br>[impactMetal_light_001](https://kenney.nl/assets/impact-sounds) — Kenney Vleugels (Kenney.nl) | CC0 1.0 |
| `Chapter03_Step_2` | 0.190 | 屋内每走一格；随机变体，连续不重复 | [footstep_wood_002](https://kenney.nl/assets/impact-sounds) — Kenney Vleugels (Kenney.nl)<br>[impactMetal_light_002](https://kenney.nl/assets/impact-sounds) — Kenney Vleugels (Kenney.nl) | CC0 1.0 |
| `Chapter03_Step_3` | 0.220 | 屋内每走一格；随机变体，连续不重复 | [footstep_wood_003](https://kenney.nl/assets/impact-sounds) — Kenney Vleugels (Kenney.nl)<br>[impactMetal_light_003](https://kenney.nl/assets/impact-sounds) — Kenney Vleugels (Kenney.nl) | CC0 1.0 |
| `Chapter03_StepWet_0` | 0.270 | 厨房／院子积水格脚步；随机变体 | [footsteps in a water puddle](https://freesound.org/people/launemax/sounds/249911/) — launemax | CC0 1.0 |
| `Chapter03_StepWet_1` | 0.250 | 厨房／院子积水格脚步；随机变体 | [footsteps in a water puddle](https://freesound.org/people/launemax/sounds/249911/) — launemax | CC0 1.0 |
| `Chapter03_StepWet_2` | 0.280 | 厨房／院子积水格脚步；随机变体 | [footsteps in a water puddle](https://freesound.org/people/launemax/sounds/249911/) — launemax | CC0 1.0 |
| `Chapter03_StepMud_0` | 0.260 | 院子非积水格脚步；随机变体 | [footstep_grass_000](https://kenney.nl/assets/impact-sounds) — Kenney Vleugels (Kenney.nl)<br>[bottle-shake-03.flac](https://opengameart.org/content/liquid-bottle-drink-set) — qubodup | CC0 1.0 |
| `Chapter03_StepMud_1` | 0.280 | 院子非积水格脚步；随机变体 | [footstep_grass_001](https://kenney.nl/assets/impact-sounds) — Kenney Vleugels (Kenney.nl)<br>[bottle-shake-04.flac](https://opengameart.org/content/liquid-bottle-drink-set) — qubodup | CC0 1.0 |
| `Chapter03_StepMud_2` | 0.250 | 院子非积水格脚步；随机变体 | [footstep_grass_002](https://kenney.nl/assets/impact-sounds) — Kenney Vleugels (Kenney.nl)<br>[bottle-shake-05.flac](https://opengameart.org/content/liquid-bottle-drink-set) — qubodup | CC0 1.0 |
| `Chapter03_ScrewdriverTurn` | 0.750 | interaction.east.store_door.open：螺丝刀拧开门锁 | [scrape-5.wav](https://opengameart.org/content/scrapes) — AntumDeluge<br>[metal_button_press2.wav](https://opengameart.org/content/metal-interactions) — qubodup | CC0 1.0 |
| `Chapter03_KeyDrop` | 0.500 | 首次打开 store_makeup，拉链后钥匙轻弹落地 | [impactMetal_light_002](https://kenney.nl/assets/impact-sounds) — Kenney Vleugels (Kenney.nl)<br>[impactMetal_light_004](https://kenney.nl/assets/impact-sounds) — Kenney Vleugels (Kenney.nl)<br>[impactMetal_light_001](https://kenney.nl/assets/impact-sounds) — Kenney Vleugels (Kenney.nl) | CC0 1.0 |
| `Chapter03_KeyUnlock` | 1.000 | 持钥匙打开 bed_door | [metal_interaction1.wav](https://opengameart.org/content/metal-interactions) — qubodup<br>[scrape-2.wav](https://opengameart.org/content/scrapes) — AntumDeluge<br>[double-door-lock](https://pixabay.com/sound-effects/double-door-lock-101210/) — DesignDean / freesound_community | CC0 1.0<br>Pixabay Content License |
| `Chapter03_CoffeeBrew` | 4.200 | interaction.kitchen.machine.brew：咖啡机启动 | [Cafetière Italienne](https://lasonotheque.org/cafetiere-italienne-s0469.html) — Joseph SARDIN<br>[Short steam pressure release](../Tools/Chapter03Audio/build_audio.py) — MrTGame production (procedural sound design) | CC0 1.0<br>Original project-generated audio; no third-party source material |
| `Chapter03_BeansPour` | 1.000 | kitchen_coffee_jar 首次装豆 | [chips-handle-3](https://kenney.nl/assets/casino-audio) — Kenney Vleugels (Kenney.nl)<br>[chips-collide-2](https://kenney.nl/assets/casino-audio) — Kenney Vleugels (Kenney.nl) | CC0 1.0 |
| `Chapter03_KeypadPress_0` | 0.100 | 密码面板实际接受一个数字；随机变体 | [metal_button_press1.wav](https://opengameart.org/content/metal-interactions) — qubodup | CC0 1.0 |
| `Chapter03_KeypadPress_1` | 0.110 | 密码面板实际接受一个数字；随机变体 | [metal_button_press2.wav](https://opengameart.org/content/metal-interactions) — qubodup | CC0 1.0 |
| `Chapter03_KeypadPress_2` | 0.095 | 密码面板实际接受一个数字；随机变体 | [metal_button_press1.wav](https://opengameart.org/content/metal-interactions) — qubodup | CC0 1.0 |
| `Chapter03_KeypadError` | 0.300 | 提交错误四位密码 | [electric fan motor blades removed](https://pixabay.com/sound-effects/electric-fan-motor-blades-removed-13169/) — edr | Pixabay Content License |
| `Chapter03_KeypadAccept` | 0.500 | 提交正确密码，继电器和锁舌 | [metal_button_press1.wav](https://opengameart.org/content/metal-interactions) — qubodup<br>[double-door-lock](https://pixabay.com/sound-effects/double-door-lock-101210/) — DesignDean / freesound_community | CC0 1.0<br>Pixabay Content License |
| `Chapter03_LabDoorOpen` | 2.000 | memory.9300：实验室门打开 | [Door Opening](https://pixabay.com/sound-effects/door-opening-454242/) — DRAGON-STUDIO<br>[Cold-room pressure hiss](../Tools/Chapter03Audio/build_audio.py) — MrTGame production (procedural sound design) | Pixabay Content License<br>Original project-generated audio; no third-party source material |
| `Chapter03_MirrorCabinet` | 0.500 | memory.9010：镜柜打开 | [metal_button_press2.wav](https://opengameart.org/content/metal-interactions) — qubodup<br>[Door Opening](https://pixabay.com/sound-effects/door-opening-454242/) — DRAGON-STUDIO | CC0 1.0<br>Pixabay Content License |
| `Chapter03_MirrorTouch` | 0.200 | memory.9048：触碰镜面 | [Dishes](https://pixabay.com/sound-effects/film-special-effects-dishes-88148/) — Nomadyag / freesound_community | Pixabay Content License |
| `Chapter03_EnvelopeOpen` | 1.250 | 遗书首次从屏幕下方进入；全文阅读不加声音 | [cards-pack-open-2](https://kenney.nl/assets/casino-audio) — Kenney Vleugels (Kenney.nl)<br>[cards-pack-take-out-1](https://kenney.nl/assets/casino-audio) — Kenney Vleugels (Kenney.nl) | CC0 1.0 |
| `Chapter03_BoxOpen` | 0.400 | interaction.lab.box：小首饰盒打开 | [metal_button_press1.wav](https://opengameart.org/content/metal-interactions) — qubodup<br>[metal_swing1.wav](https://opengameart.org/content/metal-interactions) — qubodup | CC0 1.0 |
| `Chapter03_ItemStow` | 0.250 | 物品栏真正新增物品；恢复旧库存不播 | [Cloth Grab Foley](https://pixabay.com/sound-effects/household-cloth-grab-foley-soundque-foley-recording-445581/) — Soundque<br>[impactWood_light_001](https://kenney.nl/assets/impact-sounds) — Kenney Vleugels (Kenney.nl) | Pixabay Content License<br>CC0 1.0 |

## 新增 P2：房间环境

| 键名 | 秒 | 使用位置 | 来源与作者 | 授权 |
| --- | ---: | --- | --- | --- |
| `Chapter03_RoomTone` | 12.000 | 书房、两段走廊、大厅、卧室，循环 0.1 | [Groupe froid](https://lasonotheque.org/groupe-froid-s3597.html) — Joseph SARDIN & Axeline T.<br>[crack02.mp3.flac](https://opengameart.org/content/35-wooden-crackshitsdestructions) — Independent.nu; uploaded by qubodup<br>[crack05.mp3.flac](https://opengameart.org/content/35-wooden-crackshitsdestructions) — Independent.nu; uploaded by qubodup | CC0 1.0 |
| `Chapter03_KitchenClock` | 8.000 | 厨房，循环 0.1 | [Almost inaudible room air beneath clock ticks](../Tools/Chapter03Audio/build_audio.py) — MrTGame production (procedural sound design)<br>[metal_button_press1.wav](https://opengameart.org/content/metal-interactions) — qubodup<br>[metal_button_press2.wav](https://opengameart.org/content/metal-interactions) — qubodup | Original project-generated audio; no third-party source material<br>CC0 1.0 |
| `Chapter03_Drip` | 12.000 | 厨房循环 0.1／卫生间循环 0.12 | [Almost inaudible sink air beneath sparse drips](../Tools/Chapter03Audio/build_audio.py) — MrTGame production (procedural sound design)<br>[Water drop / Splash](https://freesound.org/people/bxyorna/sounds/410335/) — bxyorna | Original project-generated audio; no third-party source material<br>CC0 1.0 |
| `Chapter03_TubeBuzz` | 8.000 | 储物间，循环 0.1 | [Groupe froid](https://lasonotheque.org/groupe-froid-s3597.html) — Joseph SARDIN & Axeline T.<br>[Fluorescent electrical air texture](../Tools/Chapter03Audio/build_audio.py) — MrTGame production (procedural sound design)<br>[guitar jack noises raw](https://pixabay.com/sound-effects/guitar-jack-noises-raw-26374/) — Fester993 / freesound_community | CC0 1.0<br>Original project-generated audio; no third-party source material<br>Pixabay Content License |
| `Chapter03_LampBuzz` | 8.000 | 大厅，循环上限 0.1，跟随灯光亮度 | [Groupe froid](https://lasonotheque.org/groupe-froid-s3597.html) — Joseph SARDIN & Axeline T. | CC0 1.0 |
| `Chapter03_LabHum` | 12.000 | 实验室，循环 0.12 | [Groupe froid](https://lasonotheque.org/groupe-froid-s3597.html) — Joseph SARDIN & Axeline T.<br>[Low cold-room structural air vibration](../Tools/Chapter03Audio/build_audio.py) — MrTGame production (procedural sound design) | CC0 1.0<br>Original project-generated audio; no third-party source material |

## 新增 P3：点选物件

| 键名 | 秒 | 使用位置 | 来源与作者 | 授权 |
| --- | ---: | --- | --- | --- |
| `Chapter03_PhoneLift` | 0.650 | hall_phone：拿起听筒 | [metal_button_press2.wav](https://opengameart.org/content/metal-interactions) — qubodup<br>[impactWood_light_003](https://kenney.nl/assets/impact-sounds) — Kenney Vleugels (Kenney.nl)<br>[Cloth Grab Foley](https://pixabay.com/sound-effects/household-cloth-grab-foley-soundque-foley-recording-445581/) — Soundque | CC0 1.0<br>Pixabay Content License |
| `Chapter03_PhoneStatic` | 2.200 | hall_phone：拿起听筒后的短线路噪声 | [Band-limited analog telephone-line noise](../Tools/Chapter03Audio/build_audio.py) — MrTGame production (procedural sound design)<br>[guitar jack noises raw](https://pixabay.com/sound-effects/guitar-jack-noises-raw-26374/) — Fester993 / freesound_community | Original project-generated audio; no third-party source material<br>Pixabay Content License |
| `Chapter03_FridgeOpen` | 1.150 | kitchen_fridge：冰箱门 | [Cloth Grab Foley](https://pixabay.com/sound-effects/household-cloth-grab-foley-soundque-foley-recording-445581/) — Soundque<br>[Rubber-seal pressure release](../Tools/Chapter03Audio/build_audio.py) — MrTGame production (procedural sound design)<br>[Door Opening](https://pixabay.com/sound-effects/door-opening-454242/) — DRAGON-STUDIO | Pixabay Content License<br>Original project-generated audio; no third-party source material |
| `Chapter03_JarLid` | 0.550 | kitchen_sugar：陶瓷罐盖 | [Dishes](https://pixabay.com/sound-effects/film-special-effects-dishes-88148/) — Nomadyag / freesound_community<br>[bottle-open-02.flac](https://opengameart.org/content/liquid-bottle-drink-set) — qubodup | Pixabay Content License<br>CC0 1.0 |
| `Chapter03_Wardrobe` | 1.300 | bed_wardrobe：衣柜门 | [Door Opening](https://pixabay.com/sound-effects/door-opening-454242/) — DRAGON-STUDIO | Pixabay Content License |
| `Chapter03_Cardboard` | 0.950 | store_boxes／junk_boxes：纸箱挪动 | [cards-pack-open-1](https://kenney.nl/assets/casino-audio) — Kenney Vleugels (Kenney.nl)<br>[card-shove-1](https://kenney.nl/assets/casino-audio) — Kenney Vleugels (Kenney.nl) | CC0 1.0 |
| `Chapter03_PillboxRattle` | 0.600 | junk_medicine／hall_coat：空药盒轻碰 | [chips-handle-3](https://kenney.nl/assets/casino-audio) — Kenney Vleugels (Kenney.nl)<br>[cards-pack-take-out-2](https://kenney.nl/assets/casino-audio) — Kenney Vleugels (Kenney.nl) | CC0 1.0 |
| `Chapter03_Wheelchair` | 1.050 | junk_wheelchair：折叠轮椅 | [metal_swing1.wav](https://opengameart.org/content/metal-interactions) — qubodup<br>[metal_interaction2.wav](https://opengameart.org/content/metal-interactions) — qubodup | CC0 1.0 |
| `Chapter03_HingeCreak` | 1.000 | junk_door：松合页 | [scrape-6.wav](https://opengameart.org/content/scrapes) — AntumDeluge<br>[metal_interaction1.wav](https://opengameart.org/content/metal-interactions) — qubodup | CC0 1.0 |
| `Chapter03_Zipper` | 0.650 | store_makeup：拉链，首次随后接 KeyDrop | [Zipper (jacket zipper)](https://opengameart.org/content/zipper) — AntumDeluge | CC0 1.0 |
| `Chapter03_LampFlicker` | 0.220 | 书房台灯每次实际故障的起点，单次短滋声 | [guitar jack noises raw](https://pixabay.com/sound-effects/guitar-jack-noises-raw-26374/) — Fester993 / freesound_community<br>[Brief irregular electrical-contact noise](../Tools/Chapter03Audio/build_audio.py) — MrTGame production (procedural sound design) | Pixabay Content License<br>Original project-generated audio; no third-party source material |
| `Chapter03_LeavesRain` | 2.500 | yard_tree：近处雨打树叶，单次 | [Rain and storm](https://bigsoundbank.com/rain-and-storm-s0124.html) — Joseph SARDIN<br>[Cloth Grab Foley](https://pixabay.com/sound-effects/household-cloth-grab-foley-soundque-foley-recording-445581/) — Soundque<br>[Water drop / Splash](https://freesound.org/people/bxyorna/sounds/410335/) — bxyorna | CC0 1.0<br>Pixabay Content License |

## 复用音效

| 键名 | 使用位置 | 来源与作者 | 授权 |
| --- | --- | --- | --- |
| `MemoryHome_DoorOpen` | 走门换房间，0.35 | [Door Opening](https://pixabay.com/sound-effects/door-opening-454242/) — DRAGON-STUDIO | Pixabay Content License |
| `MemoryHome_DoorLocked` | 实验室锁门、卡门、大门、院子回屋受阻，0.4 | [double-door-lock](https://pixabay.com/sound-effects/double-door-lock-101210/) — DesignDean / freesound_community | Pixabay Content License |
| `MemoryHome_MetalPickup` | 拿起螺丝刀、钥匙 | [double-door-lock](https://pixabay.com/sound-effects/double-door-lock-101210/) — DesignDean / freesound_community<br>[guitar jack noises raw](https://pixabay.com/sound-effects/guitar-jack-noises-raw-26374/) — Fester993 / freesound_community | Pixabay Content License |
| `MemoryHome_Pickup` | 拿起咖啡杯 | [Cloth Grab Foley](https://pixabay.com/sound-effects/household-cloth-grab-foley-soundque-foley-recording-445581/) — Soundque<br>[Large Wood Plank Falls Down](https://pixabay.com/sound-effects/film-special-effects-large-wood-plank-falls-down-230892/) — Alex_Jauk | Pixabay Content License |
| `MemoryHome_Toolbox` | 储物间工具箱 | [Drawer OPEN](https://pixabay.com/sound-effects/drawer-open-98801/) — cMilan / freesound_community<br>[double-door-lock](https://pixabay.com/sound-effects/double-door-lock-101210/) — DesignDean / freesound_community | Pixabay Content License |
| `Chapter02_Knock` | 卧室门 first / repeat / coffee 文本首次开始，0.45；末次原钩子不改 | [Door Knocking](https://pixabay.com/sound-effects/film-special-effects-door-knocking-175163/) — SoundReality | Pixabay Content License |
| `Chapter02_PaperRustle` | 便条、日记、维修记录纸面实际打开，0.35 | [card-shove-1.ogg](https://kenney.nl/assets/casino-audio) — Kenney<br>[card-slide-3.ogg](https://kenney.nl/assets/casino-audio) — Kenney | CC0 1.0 |
| `Chapter02_Drawer` | 厨房抽屉 | [Drawer OPEN](https://pixabay.com/sound-effects/drawer-open-98801/) — cMilan / freesound_community | Pixabay Content License |
| `Chapter02_CupSet` | 床头柜、实验室床边实际放咖啡 | [Dishes](https://pixabay.com/sound-effects/film-special-effects-dishes-88148/) — Nomadyag / freesound_community<br>[impactWood_light_001.ogg](https://kenney.nl/assets/impact-sounds) — Kenney | Pixabay Content License<br>CC0 1.0 |
| `Chapter02_WaterPour` | 加水、咖啡入杯 | [1242.wav](https://bigsoundbank.com/glass-of-water-filling-1-s1242.html) — Joseph SARDIN | CC0 1.0 |
| `Chapter02_Cloth` | 外衣、沙发毯、窗帘、院子衣物和实验室揭被单，0.3 | [Cloth Grab Foley](https://pixabay.com/sound-effects/household-cloth-grab-foley-soundque-foley-recording-445581/) — Soundque | Pixabay Content License |
| `Chapter02_RobotJoint` | 储物间旧腿 | [double-door-lock](https://pixabay.com/sound-effects/double-door-lock-101210/) — DesignDean / freesound_community<br>[electric fan motor blades removed](https://pixabay.com/sound-effects/electric-fan-motor-blades-removed-13169/) — edr | Pixabay Content License |
| `Chapter02_MetalLatch` | 院门插销，0.35 | [double-door-lock](https://pixabay.com/sound-effects/double-door-lock-101210/) — DesignDean / freesound_community | Pixabay Content License |
| `Ch01End_ShotSwitch` | 院子 zoom_in / zoom_out，0.25 | [Ch01End_ShotSwitch.wav](../Assets/Audio/SFX/Chapter01Ending/Ch01End_ShotSwitch.wav) — 原始作者未留档 | 项目既有可用素材；来源未留档 |
| `eaglaxle-electronic-beep-1-453381` | memory.9060 识别本机，0.3 | [electronic beep 1](https://pixabay.com/sound-effects/electronic-beep-1-453381/) — eaglaxle | Pixabay Content License |
| `Chapter02_CursorCapture` | live_to_center 开始，0.3 | [Brief mechanical intake air](../Tools/Chapter02Audio/build_minigame_audio.py) — MrTGame production<br>[double-door-lock](https://pixabay.com/sound-effects/double-door-lock-101210/) — DesignDean / freesound_community<br>[impactSoft_medium_003.ogg](https://kenney.nl/assets/impact-sounds) — Kenney | Original project-generated audio<br>Pixabay Content License<br>CC0 1.0 |
| `Chapter02_CursorRelease` | live_to_home 开始，0.3 | [open_003.ogg](https://kenney.nl/assets/interface-sounds) — Kenney<br>[double-door-lock](https://pixabay.com/sound-effects/double-door-lock-101210/) — DesignDean / freesound_community | CC0 1.0<br>Pixabay Content License |

`Ch01End_ShotSwitch` 的原始来源未留档；作者在本次对话中确认旧库素材均可用，本轮按明确要求直接复用。这里不把它推定为 CC0，也没有改写其作者信息。原库少数条目包含项目原创生成层，逐层信息保存在复用清单。

## 播放与恢复规则

- 声音选择、音量、随机变体、暂停和清理集中在 `OSChapter03StoryDirector.Sfx.cs`；其他前端文件只留调用钩子。
- 脚步与密码按键的音高随机为 0.97–1.03；同组连续不重复。随机源独立于剧情提示。
- 已开始的文本、纸面、效果、拆信与房间切换依靠保存的 `beat.begun` 去重。重建窗口、恢复旧库存和 `ApplyWorldState` 不补播单次声；房间环境可恢复。
- 环境在进出房间时淡入淡出。设置、窗口状态、OS 可用状态会暂停本章自己的音源；重置时清理。雨声与音乐不借用这组音源。
- 院子保留清单明确要求的轻脚步和物件声，不另加段落音效。推理规则、白条独白、Live 崩溃与遗书全文阅读不加新音效；只保留遗书开始时的拆信声。回忆文字和「咚」不加声音。
- `OSChapter03StoryDirector.Music.cs` 保持原样，未制作或修改 `Ch03_*.ogg`。现有最后一次敲门与雷声钩子保持原样。

## 来源记录与复现

- [44 个新文件的逐层制作记录](../Tools/Chapter03Audio/production-manifest.json)：来源、作者、授权、裁剪／滤波／叠层、成品测量与校验值。
- [新增录音取得记录](../Tools/Chapter03Audio/acquired-sources.json)：原网页、下载链接、许可证页证据和文件 SHA-256。
- [17 个复用键的来源记录](../Tools/Chapter03Audio/reuse-manifest.json)。
- [AudioData 注册脚本](../Tools/Chapter03Audio/register_audio.py)：只追加缺失键，重复执行不会增加重复项。
- [试听页](../Tools/Chapter03Audio/audition.html)：原文件直放，未经过游戏 Mixer；最终音量请在游戏里判断。

## pancake 试听重点

1. **脚步密度**：在屋内连续走一段、跨积水、再走院子泥地；听每格一次会不会烦，以及金属底是否太硬。
2. **环境循环音量**：厨房滴水＋钟、大厅灯嗡、实验室冷库声；确认只增加房间质感，不压过雨声、对白节奏和后续音乐。
3. **密码面板**：连续输入四位、输错、再输对；确认按键是老机械件，错误嗡声不刺耳，成功声不像奖励提示。

## 首批接入验证

以下为首次接入时的验证快照；积水脚步后续替换记录见下一节。

- Unity 编译通过，最终 Console 错误为 0。44 个新增引用全部解析为 44.1 kHz 单声道 AudioClip。
- [音频资产检查](../Tools/Chapter03SfxQA/Validation/2026-10-25/assets.json)：**292 项通过，0 失败**，覆盖文件格式、时长、GUID、非静音、削波和接缝指标。
- [隔离 PlayMode 检查](../Tools/Chapter03SfxQA/Validation/2026-10-25/Runtime/results.json)：**164 项通过**，覆盖抽屉／纸面读档去重、三种地面脚步、普通与剧情换房单次门声、环境切换、密码回调、设置／OS 暂停、禁配段落，以及雨声／音乐音源归属。
- [来源核对](../Tools/Chapter03SfxQA/Validation/2026-10-25/provenance.json)：49 条第三方录音（43 条 CC0、6 条 Pixabay）与 9 个原创处理层均有逐层记录；44 个成品、44 条加工链及来源文件的哈希均匹配。
- [保留项核对](../Tools/Chapter03SfxQA/Validation/2026-10-25/preservation.json)：真实存档、三个场景、剧情数据、雨声和 Music.cs 等 23 个保护项不变，17 个复用音频不变；AudioData 只新增 51 条缺失登记，原有字节内容不变。

运行验证使用静音监听与隔离存档，结束后已退出 Play Mode。本清单不代表已完成主观音色验收；整章自然游玩和最终听感由 pancake 把关。

## 积水脚步修订（2026-10-25）

根据 pancake「像水滴／咕噜声，不像脚踩水」的反馈，替换 `Chapter03_StepWet_0..2`。原先单颗水滴加地毯脚步的组合已移除，改用 launemax 的 [footsteps in a water puddle](https://freesound.org/people/launemax/sounds/249911/)（CC0）。从同一录音的 3.115–3.385、3.865–4.115、4.590–4.870 秒各取一次独立落脚，避开开头的大水花；采用公开高质量 MP3 预览，原始无损文件未下载。

三个成品为 0.27／0.25／0.28 秒，保持录音原速，无拉伸或额外水滴层。只做单声道转换、70 Hz 高通／5 kHz 低通、短淡入淡出、瞬态压低和音量整理，成品 RMS 仍为 −33.5 dBFS；游戏调用的音量和随机音高规则保持原样。

- [本次核对](../Tools/Chapter03SfxQA/Validation/2026-10-25-StepWet/revision-verification.json)：Unity 已重新导入三条单声道 PCM 音频，GUID 与引用保持；Console 错误为 0。91 项基线中仅三条 WAV 改变，其余 41 条 WAV、44 个 meta、AudioData、Sfx.cs 和 Music.cs 均不变。
- [制作修订记录](../Tools/Chapter03Audio/stepwet-revision-20261025.json) 与逐层制作清单已更新；旧版本保存在 `Tools/Chapter03Audio/Revisions/StepWet_Before_20261025/`。
- 运行 `python Tools/Chapter03Audio/build_audio.py --only-step-wet` 可只重建这三条。试听页已给音频 URL 加入文件版本号；刷新页面后可听新版。

pancake 已试听认可新版并确认在游戏内采用。正式成品位于 `Assets/Audio/SFX/Chapter03/Chapter03_StepWet_0..2.wav`；原始录音、制作脚本和旧版备份保留在 `Tools/Chapter03Audio/`。Unity 中已核实 `Assets/Data/AudioData.asset` 的三个 SFX 键各唯一引用对应的新成品，`GameSetUp.unity` 使用这份 AudioData；原有积水格脚步调用直接使用新版。

本次没有修改播放逻辑，未重复整章 PlayMode 验证。
