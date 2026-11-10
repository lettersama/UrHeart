using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Chapter 2 cues use their complete AudioData keys and the shared SFX mix.</summary>
public static class Chapter02Audio
{
    static readonly Dictionary<string, float> LastPlayed =
        new Dictionary<string, float>(StringComparer.Ordinal);

    internal static bool CanPlay => Application.isPlaying && Application.isFocused
        && !OSSessionPersistence.IsRestoring;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetSession()
    {
        LastPlayed.Clear();
    }

    public static AudioSource Play(
        string cue, float gain = 1f, float pitch = 1f, float minInterval = .08f)
    {
        if (!CanPlay) return null;
        AudioManager manager = AudioManager.Instance;
        if (manager == null) return null;
        AudioClip clip = FindClip(cue);
        if (clip == null) return null;

        float now = Time.unscaledTime;
        float interval = float.IsNaN(minInterval) || float.IsInfinity(minInterval)
            ? .08f : Mathf.Max(0f, minInterval);
        if (LastPlayed.TryGetValue(cue, out float previous)
            && now >= previous && now - previous < interval)
            return null;

        AudioSource source = manager.PlaySfx(clip, gain, pitch);
        if (source != null) LastPlayed[cue] = now;
        return source;
    }

    public static AudioClip FindClip(string cue)
    {
        if (string.IsNullOrEmpty(cue)) return null;
        var clips = GameDataManager.Instance?.AudioData?.SFX;
        if (clips == null) return null;
        // Read the current data each time: early calls must not cache a missing cue.
        for (int i = 0; i < clips.Count; i++)
        {
            AudioData entry = clips[i];
            if (entry != null && string.Equals(entry.keyid, cue, StringComparison.Ordinal))
                return entry.audioclip;
        }
        return null;
    }
}

/// <summary>
/// A loop owned by one presentation, outside the shared one-shot pool.
/// Its caller stops it when hidden and disposes it when the presentation ends.
/// </summary>
public sealed class Chapter02AudioLoop : IDisposable
{
    readonly Component host;
    AudioSource source;
    bool disposed;

    public Chapter02AudioLoop(Component host)
    {
        this.host = host;
    }

    public void Set(string cue, float gain, float pitch = 1f)
    {
        if (disposed) return;
        if (!Chapter02Audio.CanPlay)
        {
            Stop();
            return;
        }
        AudioManager manager = AudioManager.Instance;
        AudioClip clip = Chapter02Audio.FindClip(cue);
        if (host == null || manager == null || manager.SFXMixerGroup == null || clip == null)
        {
            Stop();
            return;
        }

        if (source == null)
        {
            var owner = new GameObject("Chapter02AudioLoop");
            owner.transform.SetParent(host.transform, false);
            source = owner.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
        }

        source.outputAudioMixerGroup = manager.SFXMixerGroup;
        source.loop = true;
        source.volume = float.IsNaN(gain) ? 0f : Mathf.Clamp01(gain);
        source.pitch = float.IsNaN(pitch) ? 1f : Mathf.Clamp(pitch, -3f, 3f);
        if (source.clip != clip)
        {
            source.Stop();
            source.clip = clip;
        }
        if (!source.isPlaying && source.gameObject.activeInHierarchy) source.Play();
    }

    public void Stop()
    {
        if (source == null) return;
        source.Stop();
        source.clip = null;
        source.loop = false;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Stop();
        if (source == null) return;
        GameObject owner = source.gameObject;
        source = null;
        if (Application.isPlaying) UnityEngine.Object.Destroy(owner);
        else UnityEngine.Object.DestroyImmediate(owner);
    }
}
