// using UnityEngine;
// using MrTGame.Core;
// using MrTGame.Heartbeat;

// namespace MrTGame.Systems
// {
//     /// <summary>
//     /// 音频钩子:常驻 MrT_Systems。当前用程序生成的占位音(衰减正弦短音),
//     /// 正式音频资产到位后换成采样表即可,播放路径不变。
//     /// 心跳反馈:Perfect 高音 / Good 低音 / Miss 闷音(设计文档 §6:心跳=核心交互音效)。
//     /// </summary>
//     [RequireComponent(typeof(AudioSource))]
//     public class AudioManager : Singleton<AudioManager>
//     {
//         [Range(0f, 1f)][SerializeField] float volume = 0.35f;

//         AudioSource _source;
//         AudioClip _beatHigh, _beatLow, _beatMiss;

//         protected override void Awake()
//         {
//             base.Awake();
//             if (!IsSingletonInstance)
//                 return;

//             _source = GetComponent<AudioSource>();
//             _source.playOnAwake = false;
//             _beatHigh = MakeBlip("beat_perfect", 660f, 0.09f);
//             _beatLow = MakeBlip("beat_good", 440f, 0.09f);
//             _beatMiss = MakeBlip("beat_miss", 150f, 0.18f);
//         }

//         void OnEnable() => GameEvents.Beat += OnBeat;
//         void OnDisable() => GameEvents.Beat -= OnBeat;

//         void OnBeat(BeatResult result)
//         {
//             switch (result.Judgement)
//             {
//                 case BeatJudgement.Perfect: Play(_beatHigh); break;
//                 case BeatJudgement.Good: Play(_beatLow); break;
//                 default: Play(_beatMiss); break;
//             }
//         }

//         public void Play(AudioClip clip)
//         {
//             if (clip == null)
//                 return;

//             global::AudioManager sharedAudio = global::AudioManager.Instance;
//             if (sharedAudio != null && sharedAudio.PlaySfx(clip, volume) != null)
//                 return;

//             if (_source != null && sharedAudio != null && sharedAudio.SFXMixerGroup != null)
//                 _source.outputAudioMixerGroup = sharedAudio.SFXMixerGroup;

//             if (_source != null)
//                 _source.PlayOneShot(clip, volume);
//         }

//         protected override void OnDestroy()
//         {
//             if (_beatHigh != null) Destroy(_beatHigh);
//             if (_beatLow != null) Destroy(_beatLow);
//             if (_beatMiss != null) Destroy(_beatMiss);
//             base.OnDestroy();
//         }

//         /// <summary>占位音:衰减正弦。正式采样到位后删除。</summary>
//         static AudioClip MakeBlip(string name, float frequency, float seconds)
//         {
//             const int rate = 44100;
//             int count = Mathf.CeilToInt(rate * seconds);
//             var data = new float[count];
//             for (int i = 0; i < count; i++)
//             {
//                 float t = (float)i / rate;
//                 float envelope = 1f - (float)i / count;
//                 data[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * envelope * envelope;
//             }
//             var clip = AudioClip.Create(name, count, 1, rate, false);
//             clip.SetData(data, 0);
//             return clip;
//         }
//     }
// }
