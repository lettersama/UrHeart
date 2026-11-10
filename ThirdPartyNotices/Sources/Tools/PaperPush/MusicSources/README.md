# 当前配乐：Techno_Chiptale

- 作者：Centurion_of_war
- 页面：https://opengameart.org/content/technochiptale
- 下载：https://opengameart.org/sites/default/files/techno_chiptale_v0.6_0.ogg
- 页面许可：CC0 1.0，https://creativecommons.org/publicdomain/zero/1.0/
- 获取日期：2026-09-21

作者描述为平稳、松弛、适合带神秘感的冒险场景，90 BPM，混合式 chiptune / 复古电子风格。按作者对上一首过于欢快的反馈替换。完整保留 64 秒、24 个 4/4 小节，不变调、不变速；静态增益 −9.69 dB，成品实测 −30.00 LUFS。重新按音频采样数生成时间戳，消除原 OGG 转码时的 DTS 警告，没有增删采样。

游戏版本：`Assets/Audio/Music/PaperPush/TechnoChiptale.ogg`。通过 Unity AssetDatabase 移动旧资源保留 GUID，因此场景引用及原导入设置保持有效。游戏音量 0.7，经现有 Music 混音组；旋转音符、静音、最小化暂停和剧情音乐恢复逻辑不变。

处理命令（FFmpeg）：

```text
ffmpeg -i techno_chiptale_v0.6.ogg -af "asetpts=N/SR/TB,volume=-9.69dB" -c:a libvorbis -q:a 7 TechnoChiptale.ogg
```

原音及成品校验值与实测记录见 `source.json`。波形检查与响度测量只验证技术处理，不代表已完成主观试听。

## 旧候选：Flowerbed Fields [Loop]（已停用）

- 作者：Zane Little Music
- 页面：https://opengameart.org/content/flowerbed-fields-loop
- 下载：https://opengameart.org/sites/default/files/flowerbed_fields.ogg
- 页面许可：CC0 1.0，https://creativecommons.org/publicdomain/zero/1.0/
- 获取日期：2026-09-21

作者将其标注为 cute / cheerful / chiptune / 8-bit / NES / loop。旧试听版只用静态增益降至约 −30 LUFS，不截取、不变速、不变调。处理记录保存在 `flowerbed-fields-source.json`，原音 `flowerbed_fields.ogg` 保留作选材记录。

停用原因：作者反馈情绪过于欢快，与游戏整体气质不符。
