using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class OSPaperPushAudio
{
    public AudioClip hop, choice, guard, windup, light, heavy, parry, fall, getUp, win, lose;
    struct Voice { public AudioSource source;public AudioClip clip; }
    readonly List<Voice> voices = new List<Voice>(6);
    AudioSource windupVoice;

    public void Play(AudioClip clip, float volume = 1f)
    {
        if (clip == null || AudioManager.Instance == null) return;
        voices.RemoveAll(v => v.source == null || !v.source.isPlaying || v.source.clip != v.clip);
        AudioSource source = AudioManager.Instance.PlaySfx(clip, volume);
        if (source != null) voices.Add(new Voice { source = source, clip = clip });
    }

    public void BeginWindup()
    {
        StopWindup();
        if (windup == null || AudioManager.Instance == null) return;
        windupVoice = AudioManager.Instance.PlaySfx(windup, .65f);
        if (windupVoice != null) voices.Add(new Voice { source = windupVoice, clip = windup });
    }

    public void StopWindup()
    {
        if (windupVoice != null && windupVoice.clip == windup) windupVoice.Stop();
        windupVoice = null;
    }

    public void StopAll()
    {
        // Return pooled voices immediately on close/minimize. Do not leave paused sources
        // in the shared pool, where other OS sounds could legitimately reuse them.
        foreach (Voice voice in voices)
            if (voice.source != null && voice.source.clip == voice.clip) voice.source.Stop();
        voices.Clear();windupVoice = null;
    }
}
