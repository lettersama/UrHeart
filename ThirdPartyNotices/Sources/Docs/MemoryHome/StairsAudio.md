# Memory Home 上楼音效

> 以下为首版制作与验证记录。2026-09-18 全套音效调整后，上楼已缩为 **1.45 秒 / −30.04 LUFS**；当前参数、来源哈希和试听以 [音效清单](AudioReview.md) 与 `Tools/MemoryHome/audio-manifest.json` 为准。

- 使用位置：楼下走廊 `hall` 的 `upstairs` 导航，初次上楼（2730）及修窗后上楼（3740）共用 `stairs_up` cue。
- 游戏资源：`Assets/Audio/SFX/MemoryHome_StairsUp.wav`。
- 原始文件：`freesound_community-walking-up-wood-stairs-in-shoes-83439.mp3`。
- 来源：[walking up wood stairs in shoes](https://pixabay.com/sound-effects/film-special-effects-walking-up-wood-stairs-in-shoes-83439/)。页面标注作者 Bastard Fish (Freesound)，发布账号 freesound_community。
- 许可：[Pixabay Content License](https://pixabay.com/service/license-summary/)。
- 获取日期：2026-09-18，通过浏览器下载到本地 Downloads。

## 剪辑与响度

原音频约 12.456 秒、48 kHz、单声道。取 1.90–4.65 秒的四步，成品 2.75 秒；15 ms 淡入和末尾 120 ms 淡出，避免切点爆音。导出双单声道立体声，便于与现有 2D 立体声音效按相同声道布局比较。施加固定 +5.8 dB 增益，不作动态压缩或变速。

| 音效 | 综合响度 | 真峰值 |
| --- | ---: | ---: |
| MemoryHome_DoorOpen | −29.00 LUFS | −12.67 dBTP |
| OpenLock / double-door-lock | −29.35 LUFS | −13.26 dBTP |
| ShowPaper / Paper_Game_01_Switch | −25.05 LUFS | −4.17 dBTP |
| MemoryHome_StairsUp | −28.98 LUFS | −8.84 dBTP |

脚步综合响度与开门接近，保留了较突出的脚步瞬态，峰值留有 8.84 dB 余量。使用原有图片交互 AudioSource 和 SFX 路由，保留现有转场时长与交互规则。Unity 导入为 PCM / DecompressOnLoad，预加载启用，48 kHz 立体声。

## 复现

```powershell
ffmpeg -i freesound_community-walking-up-wood-stairs-in-shoes-83439.mp3 -af "atrim=start=1.9:end=4.65,asetpts=PTS-STARTPTS,afade=t=in:d=0.015,afade=t=out:st=2.63:d=0.12,pan=stereo|c0=c0|c1=c0" -ar 48000 -c:a pcm_s16le trim.wav
ffmpeg -i trim.wav -af "volume=5.8dB" -ar 48000 -c:a pcm_s16le MemoryHome_StairsUp.wav
ffmpeg -i MemoryHome_StairsUp.wav -af "loudnorm=I=-23:TP=-1:LRA=11:print_format=json" -f null -
```

读取最后一条命令的 `input_i` / `input_tp`，该测量不改写音频。

- 原始 SHA-256：`52cdadd39c5ffdc34cbf5fd5a3e8a60cfd2445a44675fcd8f81a708a1a5ae267`
- 成品 SHA-256：`71c2e98012f883fe37ed0ce290581fe6ca65d1dacd4c25b4387912c75e0b5b02`

`Tools/MemoryHome/build_content.py` 和 `content.json` 同步声明 cue 与两条结果。现有 Builder 保留已绑定 AudioClip，重建剧情不会清掉引用。

## 验证

2026-09-18：Play Mode 使用克隆 SaveData 从 2680 进门。首次上楼通过 UI PointerClick 事件验证箭头位于射线最前方，跳到 2730、进入 upstairs、AudioSource 正在播放；修窗后分支验证跳到 3740 并播放。两次均验证 SFXMixerGroup 路由和重复提交拦截。AudioSource 音量 1、pitch 1、spatialBlend 0；下楼不触发上楼 cue。结束后恢复原 SaveData 引用并退出 Play Mode，控制台无错误。运行记录见 `stairs-audio-verification.json`。
