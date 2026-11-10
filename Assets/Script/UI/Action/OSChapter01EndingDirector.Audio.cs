using UnityEngine;
using UnityEngine.Audio;

// Sound for the ending: a CCTV bed under the slides that dies away before the
// door, then a music box that tears along with the picture and sticks with it.
public sealed partial class OSChapter01EndingDirector
{
    AudioSource endingHiss, endingFan, endingMusic, endingStuck;
    float lastAudioTick = float.NaN, stuckSliceStart, musicPitchTarget = 1f;
    int glitchClipCursor;
    bool stuckAudio;

    AudioSource EndingSource(ref AudioSource source, AudioClip clip, AudioMixerGroup group, bool loop)
    {
        if (clip == null) return null;
        if (source == null)
        {
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;source.spatialBlend = 0f;
        }
        source.clip = clip;source.loop = loop;source.outputAudioMixerGroup = group;
        return source;
    }

    void StartSlideAudio(int index)
    {
        var mixer = AudioManager.Instance;
        if (configuration.slideShotSwitch != null)
            mixer?.PlaySfx(configuration.slideShotSwitch, .55f);
        foreach (var bed in new[] {
            EndingSource(ref endingHiss, configuration.slideHiss, mixer?.AmbientMixerGroup, true),
            EndingSource(ref endingFan, configuration.slideFan, mixer?.AmbientMixerGroup, true) })
        {
            if (bed == null || bed.isPlaying) continue;
            bed.volume = 0f;
            bed.time = Random.Range(0f, bed.clip.length * .9f);
            bed.Play();
        }
        UpdateSlideAudio(index, 0f);
    }

    // Workbench, turn and departure keep the room; the abandoned robot loses
    // the fan; the dark corridor keeps only tape hiss; the door is silent.
    void UpdateSlideAudio(int index, float progress)
    {
        int last = SlideCount - 1;
        float smooth = Mathf.SmoothStep(0f, 1f, progress);
        float hiss = index < last - 1 ? 1f : index == last - 1 ? .35f : Mathf.Lerp(.35f, 0f, Mathf.Clamp01(progress / .35f));
        float fan = index < last - 2 ? 1f : index == last - 2 ? Mathf.Lerp(1f, .12f, smooth)
            : index == last - 1 ? .05f : Mathf.Lerp(.05f, 0f, Mathf.Clamp01(progress / .35f));
        if (endingHiss != null) endingHiss.volume = hiss * configuration.slideHissVolume;
        if (endingFan != null) endingFan.volume = fan * configuration.slideFanVolume;
    }

    void StopSlideAudio()
    {
        if (endingHiss != null) endingHiss.Stop();
        if (endingFan != null) endingFan.Stop();
    }

    void StartFilmAudio()
    {
        StopSlideAudio();
        lastAudioTick = float.NaN;stuckAudio = false;musicPitchTarget = 1f;
        var music = EndingSource(ref endingMusic, configuration.animationMusicBox, AudioManager.Instance?.MusicMixerGroup, false);
        if (music == null) return;
        music.pitch = 1f;music.volume = configuration.animationMusicVolume;music.time = 0f;
        music.Play();
    }

    // Called every frame with the shader's own amount and tick. A crackle sounds
    // on the same ticks the picture tears; past the middle the music box skips.
    void UpdateFilmAudio(float amount, float tick)
    {
        var music = endingMusic;
        if (music != null)
        {
            if (stuckAudio && music.time > stuckSliceStart + .07f) music.time = stuckSliceStart;
            music.pitch = Mathf.MoveTowards(music.pitch, musicPitchTarget, Time.unscaledDeltaTime * 2f);
        }
        if (tick == lastAudioTick) return;
        lastAudioTick = tick;
        float tearChance = Mathf.Lerp(.35f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.1f, .65f, amount)));
        if (Hash(tick * .731f + 3.1f) >= tearChance) { musicPitchTarget = 1f - .03f * amount; return; }

        var clips = configuration.animationGlitchClips;
        if (clips != null && clips.Length > 0 && (!stuckAudio || ((int)tick & 1) == 0))
        {
            var clip = clips[(glitchClipCursor++ + (int)(Hash(tick * 2.17f) * clips.Length)) % clips.Length];
            if (clip != null)
                AudioManager.Instance?.PlaySfx(clip, Mathf.Lerp(.25f, 1f, amount) * configuration.animationGlitchVolume,
                    Mathf.Lerp(.92f, 1.12f, Hash(tick * 5.9f)));
        }
        if (music == null || stuckAudio) return;
        musicPitchTarget = 1f - .1f * amount * Hash(tick * 1.73f);
        if (amount > .45f && Hash(tick * 9.41f + 2f) < (amount - .45f) * 1.2f)
            music.time = Mathf.Max(0f, music.time - Mathf.Lerp(.05f, .16f, Hash(tick * 4.4f)));
    }

    void BeginStuckAudio()
    {
        stuckAudio = true;
        if (endingMusic != null && endingMusic.isPlaying) stuckSliceStart = endingMusic.time;
        var stuck = EndingSource(ref endingStuck, configuration.animationStuckHum, AudioManager.Instance?.SFXMixerGroup, true);
        if (stuck == null) return;
        stuck.volume = configuration.animationStuckVolume;stuck.time = 0f;
        stuck.Play();
    }

    // The picture freezes: every voice stops on the same frame.
    void StopEndingAudio()
    {
        StopSlideAudio();
        if (endingMusic != null) { endingMusic.Stop();endingMusic.pitch = 1f; }
        if (endingStuck != null) endingStuck.Stop();
        stuckAudio = false;lastAudioTick = float.NaN;
    }
}
