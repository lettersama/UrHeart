# 留声机音频处理记录

处理日期：2026-09-23。该记录涵盖离线音频处理与测量；Unity 导入、混音路由和实际播放由留声机集成负责。

## 来源与署名

- 新曲原始文件：`Assets/Audio/Ambient/LOOP Dreamcore Ambient (by juanjo_sound).wav`，由项目作者提供。
- 原曲名称：`LOOP Dreamcore Ambient`；署名：`juanjo_sound`，依据原始文件名保留。没有从文件名推断或补写授权许可。
- 游戏副本：`Assets/Audio/Music/Gramophone/OldDream.ogg`。OGG 标签内保留原曲名、作者与原始文件名。
- 秘密序章实际使用：`OS_Handle.HeartHealEndingAmbientKey = "SecretPart"` → `Assets/Data/AudioData.asset` → GUID `769f0dd97cc084344a3c75f973396129` → `Assets/Audio/Ambient/WAV_Bats_Clock_Tower_loop.wav`。不要与同目录的 `SecretPart.wav` 混淆。
- 纸推音乐：`Assets/Audio/Music/PaperPush/TechnoChiptale.ogg`，作者 `Centurion_of_war`；原有来源与许可记录见 `Tools/PaperPush/MusicSources/README.md`。

## 实测与增益

使用 FFmpeg 7.1 的 `loudnorm` 分析整曲，读取其 **input** 指标；未把该滤镜的动态归一化输出用作成品。文件为 44,100 Hz 双声道。时长按解码采样数计算。

| 文件 | 时长（秒） | 采样帧数 | 综合响度（LUFS） | 真峰值（dBTP） | 响度范围（LU） |
| --- | ---: | ---: | ---: | ---: | ---: |
| 秘密序章 `WAV_Bats_Clock_Tower_loop.wav` | 130.678254 | 5,762,911 | -30.41 | -17.48 | 5.40 |
| 纸推 `TechnoChiptale.ogg` | 64.000000 | 2,822,400 | -30.00 | -13.99 | 2.30 |
| 原始 Dreamcore WAV | 82.153855 | 3,622,985 | -14.71 | -3.10 | 3.50 |
| 留声机 `OldDream.ogg` | 82.153855 | 3,622,985 | -30.00 | -18.32 | 3.50 |

Dreamcore 采用固定 **-15.29 dB** 增益，Vorbis quality 7。没有压缩动态、限幅、变调、变速、重采样、截取或增删采样。完整保留源文件的采样率、双声道和时长。输出大小为 1,830,813 字节。

改动前播放路径（用于确定平衡基准）：

- 秘密序章走 `AudioManager.PlayAmbient()`；交叉淡入完成后的 AudioSource 音量为 `1.0`，即混音组前约 **-30.41 LUFS**。
- 纸推走 `OSPaperPushMusic`；默认 AudioSource 音量为 `0.7`（-3.098 dB），即混音组前约 **-33.10 LUFS**。
- 两个既有路径因此相差约 2.69 LU。它们分属 Ambient / Music 混音组；默认设置都为 75，但玩家可以分别调节，所以文件级测量不能证明所有用户设置下的最终输出完全一致。

留声机库建议统一走 Music 混音组。若以现有纸推的听感强度为测量目标，三个库条目的最终 AudioSource 音量建议为：

| 留声机条目 | 建议最终 AudioSource 音量 | 混音组前预测综合响度 |
| --- | ---: | ---: |
| 秘密序章现有 WAV | 0.733834 | 约 -33.10 LUFS |
| 纸推现有 OGG | 0.700000 | 约 -33.10 LUFS |
| 新 `OldDream.ogg` | 0.700000 | 约 -33.10 LUFS |

若实现使用“统一基础音量 × 每曲增益”，等价配置为基础音量 `0.7`、秘密序章增益 `1.048335`、另两首增益 `1.0`；最终 AudioSource 音量均不超过 1。也可直接用表中的各曲音量。该建议只作用于留声机库，不要求修改既有剧情或小游戏的播放音量。若统一基础音量而不设曲目补偿，三首文件间仅差 0.41 LU，也已基本配平。

## 循环边界检查

最终集成采用统一 0.7 的默认播放音量，两首环境曲和纸推均路由 Music（含秘密环节原来的播放调用）。三首在混音组前约为 -33.51 / -33.10 / -33.10 LUFS，差值 0.41 LU；未追加单曲增益补偿。玩家调整留声机音量会统一作用于这三首，游戏设置中的 Music 音量再控制总线。三首均使用 Streaming / Vorbis quality 0.8 / 原采样率导入。

这是采样层面的检查，**未完成主观试听**，不能据此宣称循环接点不可闻。

- 原始 Dreamcore 的首尾采样并非完全连续：左右声道的最大边界跳变为 `0.04449463`，即 -27.03 dBFS。
- 降增益并编码后的 `OldDream.ogg` 边界最大跳变为 `0.00815987`，即 **-41.77 dBFS**；以 0.7 音量播放时约 -44.86 dBFS（混音组前）。成品内部相邻采样跳变的 99.9 百分位为 -43.31 dBFS，边界仅高约 1.54 dB。
- 成品首秒和末秒的 RMS 分别为 -43.91 / -41.12 dBFS。源文件首尾本身有电平变化，处理没有额外加入淡入淡出。
- 解码帧数与源 WAV 一致，为 3,622,985；没有额外添加一段静音或裁剪循环尾部。数值未显示异常大的边界跳变，但最终仍应在 Unity 循环播放中听一次接点。

## 复现

所用工具：`imageio_ffmpeg` 自带 `ffmpeg-win-x86_64-v7.1.exe`。从项目根目录执行：

```text
ffmpeg -hide_banner -nostdin -i "Assets/Audio/Ambient/LOOP Dreamcore Ambient (by juanjo_sound).wav" -map_metadata 0 -af "asetpts=N/SR/TB,volume=-15.29dB" -c:a libvorbis -q:a 7 -metadata "title=LOOP Dreamcore Ambient (Old Dream)" -metadata "artist=juanjo_sound" -metadata "comment=User-supplied source: LOOP Dreamcore Ambient (by juanjo_sound).wav. Static gain -15.29 dB; original retained." "Assets/Audio/Music/Gramophone/OldDream.ogg"
```

整曲测量：

```text
ffmpeg -hide_banner -nostdin -i "INPUT_AUDIO" -af "loudnorm=I=-30:TP=-1:LRA=11:print_format=json" -f null -
```

## SHA-256

原始 Dreamcore WAV 和其 `.meta` 在处理前后均校验一致；其他既有音频没有写入。

| 文件 | SHA-256 |
| --- | --- |
| 原始 Dreamcore WAV | `45cfa3b20d553d5a5e39258f03943409fe05f66b4292fd38bd23cc4f8adf5920` |
| 原始 Dreamcore WAV.meta | `f5dd11aa65f786a05b7f1ca7620b87bb55c96dca2e88363fd6c9b0627a7a8235` |
| OldDream.ogg | `f3ad204ef74b1647b9da05dba65c5875e6c5505668666136efa8ec9e2e186b86` |
| 秘密序章 WAV | `fb5472f99073a10f7f9ddb415df3cf8e1957355670a0be82a8c8014e571decf6` |
| 纸推 OGG | `db7b215cb0bb14ee2728bb6a85787210fb987f6cdd70d098e402ba8a6119ae2e` |

## 第一章入户插画曲 Out There（2026-10-06）

用户提供 `Assets/Audio/Ambient/LOOP Out There (by juanjo_sound).wav`。规范化副本为 `Assets/Audio/Music/Gramophone/OutThere.wav`，文件内保留曲名、作者 `juanjo_sound` 和来源记录。原始 WAV 未修改。

| 文件 | 综合响度（LUFS） | 真峰值（dBTP） | 响度范围（LU） |
| --- | ---: | ---: | ---: |
| 原始 Out There | -14.60 | -3.09 | 3.00 |
| 规范化 OutThere.wav | -30.00 | -18.49 | 3.00 |

同样用 FFmpeg 7.1 `loudnorm` 的 **input** 指标复测，成品只施加固定 **-15.40 dB** 增益，输出 24-bit PCM；没有采用 loudnorm 动态输出，没有限幅、压缩、变速、淡入淡出、重采样或裁剪。44,100 Hz 双声道，源与成品均为 **2,523,877 帧 / 57.230771 秒**，循环长度保持不变。Music 库仍使用统一 `0.7` 播放音量即可，与 OldDream 文件级响度一致。

成品首尾最大采样跳变为 -40.03 dBFS（源 -24.63 dBFS）；处理仅同比缩放原有边界，没有另做接点交叉淡化。已完成客观测量，尚未主观试听循环接点。完整数值及 SHA-256 见同目录 `out-there-audio-report.json`。

```text
ffmpeg -hide_banner -nostdin -n -i "Assets/Audio/Ambient/LOOP Out There (by juanjo_sound).wav" -map_metadata 0 -af "volume=-15.40dB" -c:a pcm_s24le -metadata "title=Out There" -metadata "artist=juanjo_sound" -metadata "comment=User-supplied source: LOOP Out There (by juanjo_sound).wav; static gain -15.40 dB; original retained; no resampling or trimming." "Assets/Audio/Music/Gramophone/OutThere.wav"
```
