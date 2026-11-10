# Memory Home 开门音效

> 以下为首版制作记录。2026-09-18 全套音效调整后，开门已缩为 **1.35 秒 / −30.01 LUFS**；当前参数、来源哈希和试听以 [音效清单](AudioReview.md) 与 `Tools/MemoryHome/audio-manifest.json` 为准。

- 使用位置：家门前首次 `enter` → `hall`，现有 `door` cue。
- 资源：`Assets/Audio/SFX/MemoryHome_DoorOpen.wav`。
- 作者：DRAGON-STUDIO；原始文件 `dragon-studio-door-opening-454242.mp3`。
- 来源：https://pixabay.com/sound-effects/door-opening-454242/
- 许可：https://pixabay.com/service/license-summary/ （Pixabay Content License）。
- 获取日期：2026-09-18。自动下载遇到 Cloudflare 验证，用户通过浏览器下载后提供本地文件。

## 响度处理

原始 1.8 秒、48 kHz、立体声 MP3；FFmpeg 测量约 −15.40 LUFS，真峰值 +0.94 dBTP。
施加固定 −13.6 dB 增益，导出 48 kHz/16-bit 立体声 PCM WAV。无动态压缩、无裁切。
处理后实测 −29.00 LUFS，真峰值 −12.67 dBTP。

参照项目现有交互音效：

| 音效 | 综合响度 | 真峰值 |
| --- | ---: | ---: |
| OpenLock / double-door-lock | −29.35 LUFS | −13.26 dBTP |
| ShowPaper / Paper_Game_01_Switch | −25.05 LUFS | −4.17 dBTP |
| 新开门音效 | −29.00 LUFS | −12.67 dBTP |

以场景动作最接近的开锁音效为主要基准。Memory 图片交互 AudioSource 音量保持 1，输出接入 `MasterMix/SFXHandle`，遵循游戏主音量和音效音量设置。
Unity 采用 PCM / DecompressOnLoad，避免短音效首次播放时的流式加载延迟。
重建剧情内容时保留已绑定的音频引用。

## 可复现性

处理命令：`ffmpeg -i dragon-studio-door-opening-454242.mp3 -af volume=-13.6dB -ar 48000 -c:a pcm_s16le MemoryHome_DoorOpen.wav`

测量：`ffmpeg -i <file> -af loudnorm=I=-23:TP=-1:LRA=11:print_format=json -f null -`，读取 input_i / input_tp（测量没有更改资源）。

原始 SHA-256：`e9a297f166050ed7f98bb93aee0215235d67483ec9e43ae86ac372aea6ea63eb`

处理后 SHA-256：`9becb1cf7c796ae019a3b46f745818d223b5f9e2db93d9f276aaa8da7bf3313e`

## 验证

2026-09-18：从 GameSetUp 干净启动，以隔离存档运行入口检查。79 项检查通过，包括实际鼠标事件命中、进门音频播放及 SFX 路由、同帧重复点击拦截、左右灰色箭头提示、39 个打字进度采样不泄漏富文本标签、红字显示。控制台无错误，测试存档恢复成功。结果见 `entry-feedback/verification.json`。
