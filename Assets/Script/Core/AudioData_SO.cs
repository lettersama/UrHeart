using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class AudioData
{
    public string keyid;
    public float pitchmin = 1f;
    public float pitchmax = 1f;
    public AudioClip audioclip;
}

[Serializable]
public sealed class SfxAudioData : AudioData
{
    public SfxAudioData()
    {
        pitchmin = 0.8f;
        pitchmax = 1.2f;
    }
}

[CreateAssetMenu(menuName = "MrTGame/AudioData", fileName = "AudioData")]
public class AudioData_SO : ScriptableObject
{
    public List<AudioData> BGM = new List<AudioData>();
    public List<SfxAudioData> SFX = new List<SfxAudioData>();
    public List<AudioData> AMBIENT = new List<AudioData>();
}
