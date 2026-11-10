using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

public class AudioManager : Singleton<AudioManager>
{
    private const string MasterVolumeParameter = "MasterVolume";
    private const string MusicVolumeParameter = "MusicVolume";
    private const string SfxVolumeParameter = "SFXVolume";
    private const string AmbientVolumeParameter = "AmbientVolume";
    private const float MaxDb = 0f;
    private const float MinDb = -80f;

    [Header("2D SFX Pool")]
    public List<AudioSource> SFXPool = new List<AudioSource>();
    public AudioSource sfxSourcePrefab;
    public GameObject sfxSourceParent;

    [Header("Music")]
    public AudioSource MusicSource_00;
    public AudioSource MusicSource_01;

    [Header("Ambient")]
    public AudioSource AmbientSource_00;
    public AudioSource AmbientSource_01;

    [Header("Mixer Control Groups")]
    public AudioMixerGroup MasterMixerGroup;
    public AudioMixerGroup SFXMixerGroup;
    public AudioMixerGroup MusicMixerGroup;
    public AudioMixerGroup AmbientMixerGroup;

    private readonly HashSet<string> reportedErrors = new HashSet<string>();
    private readonly HashSet<UnityEngine.Object> backgroundAudioSuppressors =
        new HashSet<UnityEngine.Object>();
    private readonly List<SfxClipSuppression> sfxClipSuppressions = new List<SfxClipSuppression>();
    private DualSourceChannel musicChannel;
    private DualSourceChannel ambientChannel;
    private MusicTreatmentSourceState[] musicTreatmentStates;

    // Owners of temporary source effects restore their baseline before a new
    // music operation captures or reuses the shared sources. Handlers never play/stop audio.
    public event System.Action MusicOwnershipChanging;
    public bool HasMusicTreatmentEffect => musicTreatmentStates != null;

    public bool IsBackgroundAudioSuppressed
    {
        get
        {
            // A destroyed owner cannot leave the shared channels permanently silent.
            backgroundAudioSuppressors.RemoveWhere(owner => owner == null);
            return backgroundAudioSuppressors.Count > 0;
        }
    }

    public void SetBackgroundAudioSuppressed(
        UnityEngine.Object owner, bool suppressed, float fadeDuration = 1.8f)
    {
        bool wasSuppressed = IsBackgroundAudioSuppressed;
        if (!suppressed)
        {
            backgroundAudioSuppressors.Remove(owner);
            // Releasing a lease never resumes a stale song. Its next owner decides.
            return;
        }
        if (owner == null || !backgroundAudioSuppressors.Add(owner) || wasSuppressed)
            return;

        MusicOwnershipChanging?.Invoke();
        EndMusicTreatmentEffect();
        // Bypass the guarded public stop methods once. Later owners and refreshes
        // cannot restart the fade or cut it short with an immediate stop/pause.
        StopChannel(musicChannel, fadeDuration);
        StopChannel(ambientChannel, fadeDuration);
    }

    // Replaces this owner's clip set, or releases it when suppressed is false.
    // Only future PlaySfx calls are gated; existing tails remain with their owner.
    public void SetSfxClipsSuppressed(UnityEngine.Object owner, bool suppressed, params AudioClip[] clips)
    {
        for (int i = sfxClipSuppressions.Count - 1; i >= 0; i--)
            if (sfxClipSuppressions[i].Owner == null || sfxClipSuppressions[i].Owner == owner)
                sfxClipSuppressions.RemoveAt(i);
        if (!suppressed || owner == null || clips == null) return;

        var suppression = new SfxClipSuppression(owner);
        foreach (var clip in clips)
            if (clip != null) suppression.Clips.Add(clip);
        if (suppression.Clips.Count > 0) sfxClipSuppressions.Add(suppression);
    }

    private bool IsSfxClipSuppressed(AudioClip clip)
    {
        bool suppressed = false;
        for (int i = sfxClipSuppressions.Count - 1; i >= 0; i--)
        {
            var suppression = sfxClipSuppressions[i];
            if (suppression.Owner == null) sfxClipSuppressions.RemoveAt(i);
            else if (suppression.Clips.Contains(clip)) suppressed = true;
        }
        return suppressed;
    }


    public int SfxPoolCount
    {
        get
        {
            RebuildSfxPoolCache();
            return SFXPool.Count;
        }
    }

    protected override void Awake()
    {
        base.Awake();
        if (!IsSingletonInstance)
            return;

        ResolveSceneReferences();
        musicChannel = CreateChannel("music", MusicSource_00, MusicSource_01, MusicMixerGroup);
        ambientChannel = CreateChannel("ambient", AmbientSource_00, AmbientSource_01, AmbientMixerGroup);
        InitializeSfxPool();
    }

    protected override void OnDestroy()
    {
        EndMusicTreatmentEffect();
        CancelTransition(musicChannel);
        CancelTransition(ambientChannel);
        backgroundAudioSuppressors.Clear();
        sfxClipSuppressions.Clear();
        base.OnDestroy();
    }

    /// <summary>
    /// Converts a normalized linear amplitude to the AudioMixer's decibel range.
    /// </summary>
    public static float AmplitudeToDb(float amplitude)
    {
        if (float.IsNaN(amplitude) || amplitude <= 0f)
            return MinDb;

        if (float.IsPositiveInfinity(amplitude))
            return MaxDb;

        return Mathf.Clamp(20f * Mathf.Log10(amplitude), MinDb, MaxDb);
    }

    public void SetMasterVolume(float value)
    {
        SetMixerVolume(MasterMixerGroup, MasterVolumeParameter, value);
    }

    public void SetMusicVolume(float value)
    {
        SetMixerVolume(MusicMixerGroup, MusicVolumeParameter, value);
    }

    public void SetSfxVolume(float value)
    {
        SetMixerVolume(SFXMixerGroup, SfxVolumeParameter, value);
    }

    public void SetAmbientVolume(float value)
    {
        SetMixerVolume(AmbientMixerGroup, AmbientVolumeParameter, value);
    }

    public void PlayMusic(AudioClip clip, float fadeDuration = 1f, bool loop = true, float playbackGain = 1f)
    {
        if (IsBackgroundAudioSuppressed) return;
        MusicOwnershipChanging?.Invoke();
        // New music owners must not inherit the gramophone's volume or mute.
        if (musicChannel != null) musicChannel.Gain = Clamp01Finite(playbackGain);
        PlayChannel(musicChannel, clip, fadeDuration, loop);
        // Shared voices must not retain a previous record's authored-pitch correction.
        if (musicChannel?.ActiveSource != null) musicChannel.ActiveSource.pitch = 1f;
    }

    public void StopMusic(float fadeDuration = 1f)
    {
        if (IsBackgroundAudioSuppressed) return;
        MusicOwnershipChanging?.Invoke();
        StopChannel(musicChannel, fadeDuration);
    }

    public AudioSource CurrentMusicSource => musicChannel?.ActiveSource;

    // A per-player gain, independent of the user's Music mixer setting.
    public void SetMusicPlaybackGain(float gain)
    {
        if (musicChannel == null) return;
        musicChannel.Gain = Clamp01Finite(gain);
        if (musicChannel.Transition == null && musicChannel.ActiveSource != null)
            musicChannel.ActiveSource.volume = musicChannel.Gain;
    }

    public void PauseMusicPlayback()
    {
        if (IsBackgroundAudioSuppressed || musicChannel == null) return;
        CancelTransition(musicChannel);
        var active = musicChannel.ActiveSource;
        if (active == null) return;
        active.volume = musicChannel.Gain;
        active.Pause();
        var other = active == musicChannel.Source00 ? musicChannel.Source01 : musicChannel.Source00;
        StopAndClearSource(other);
    }

    public void ResumeMusicPlayback()
    {
        if (IsBackgroundAudioSuppressed) return;
        // An instant resume also cancels a fade that was about to pause this voice.
        if (musicChannel?.PausingSource != null && musicChannel.PausingSource == musicChannel.ActiveSource)
        {
            CancelTransition(musicChannel);
            musicChannel.ActiveSource.volume = musicChannel.Gain;
        }
        musicChannel?.ActiveSource?.UnPause();
    }

    // Fades the active voice out, then pauses it at its playhead. Repeated
    // requests while the same fade runs leave it alone, so per-frame owners
    // can call this every frame. The paused voice keeps the channel gain, as
    // an instant pause does, so an instant resume is never left silent.
    public void PauseMusicPlayback(float fadeDuration)
    {
        if (IsBackgroundAudioSuppressed || musicChannel == null) return;
        var active = musicChannel.ActiveSource;
        if (!IsPositiveFinite(fadeDuration) || active == null || !active.isPlaying)
        {
            PauseMusicPlayback();
            return;
        }
        if (musicChannel.Transition != null && musicChannel.PausingSource == active) return;
        CancelTransition(musicChannel);
        var other = active == musicChannel.Source00 ? musicChannel.Source01 : musicChannel.Source00;
        musicChannel.PausingSource = active;
        musicChannel.Transition = StartCoroutine(FadeOutAndPause(musicChannel, active, other, fadeDuration));
    }

    // Resumes a paused voice from silence, or turns a pausing fade back up.
    public void ResumeMusicPlayback(float fadeDuration)
    {
        if (IsBackgroundAudioSuppressed || musicChannel == null) return;
        var active = musicChannel.ActiveSource;
        if (active == null) return;
        if (!IsPositiveFinite(fadeDuration))
        {
            ResumeMusicPlayback();
            return;
        }
        bool pausing = musicChannel.Transition != null && musicChannel.PausingSource == active;
        if (!pausing && active.isPlaying) return;
        CancelTransition(musicChannel);
        if (!active.isPlaying)
        {
            active.volume = 0f;
            active.UnPause();
        }
        musicChannel.Transition = StartCoroutine(CrossfadeChannel(musicChannel, active, null, fadeDuration));
    }

    public void BeginMusicTreatmentEffect(float cutoff, float distortion)
    {
        MusicOwnershipChanging?.Invoke();
        EndMusicTreatmentEffect();

        musicTreatmentStates = new[]
        {
            CaptureMusicTreatmentState(MusicSource_00),
            CaptureMusicTreatmentState(MusicSource_01)
        };
        SetMusicTreatmentEffect(cutoff, distortion);
    }

    public void SetMusicTreatmentEffect(float cutoff, float distortion)
    {
        if (musicTreatmentStates == null)
            return;

        float safeCutoff = Mathf.Clamp(cutoff, 10f, 22000f);
        float safeDistortion = Mathf.Clamp01(distortion);
        for (int i = 0; i < musicTreatmentStates.Length; i++)
        {
            MusicTreatmentSourceState state = musicTreatmentStates[i];
            if (state?.LowPass != null)
            {
                state.LowPass.enabled = true;
                state.LowPass.cutoffFrequency = safeCutoff;
            }

            if (state?.Distortion != null)
            {
                state.Distortion.enabled = true;
                state.Distortion.distortionLevel = safeDistortion;
            }
        }
    }

    public void EndMusicTreatmentEffect()
    {
        if (musicTreatmentStates == null)
            return;

        for (int i = 0; i < musicTreatmentStates.Length; i++)
            RestoreMusicTreatmentState(musicTreatmentStates[i]);

        musicTreatmentStates = null;
    }


    public void PlayAmbient(AudioClip clip, float fadeDuration = 1f, bool loop = true)
    {
        if (IsBackgroundAudioSuppressed) return;
        PlayChannel(ambientChannel, clip, fadeDuration, loop);
    }

    public void StopAmbient(float fadeDuration = 1f)
    {
        if (IsBackgroundAudioSuppressed) return;
        StopChannel(ambientChannel, fadeDuration);
    }

    // A previous scene can release its ambience after a new owner has already
    // started a crossfade. Keep that incoming ambience running in that case.
    public void StopAmbientClip(AudioClip clip, float fadeDuration = 1f)
    {
        if (IsBackgroundAudioSuppressed) return;
        if (clip == null || !HasChannelSources(ambientChannel, false))
            return;

        AudioSource matchingSource = GetSourcePlayingClip(ambientChannel, clip);
        if (matchingSource == null)
            return;

        AudioSource activeSource = ambientChannel.ActiveSource;
        if (activeSource == null || activeSource.clip == clip)
        {
            StopChannel(ambientChannel, fadeDuration);
            return;
        }

        CancelTransition(ambientChannel);
        if (!IsPositiveFinite(fadeDuration))
        {
            CompleteChannelTransition(ambientChannel, activeSource, matchingSource);
            return;
        }

        ambientChannel.Transition = StartCoroutine(CrossfadeChannel(
            ambientChannel, activeSource, matchingSource, fadeDuration));
    }

    public AudioSource PlaySfx(AudioClip clip, float volumeScale = 1f, float pitch = 1f)
    {
        if (clip == null || IsSfxClipSuppressed(clip))
            return null;

        AudioSource source = GetAvailableSfxSource();
        if (source == null)
        {
            ReportErrorOnce(
                "SfxPoolConfiguration",
                "AudioManager needs an SFX source prefab and pool parent when all pooled sources are busy.");
            return null;
        }

        ResetSfxSource(source);
        source.clip = clip;
        source.volume = Clamp01Finite(volumeScale);
        source.pitch = ClampPitch(pitch);
        source.Play();
        return source;
    }

    public AudioSource PlaySfx(string keyId, float volumeScale = 1f)
    {
        if (string.IsNullOrEmpty(keyId))
            return null;

        AudioData sfx = GameDataManager.Instance?.AudioData?.SFX?.Find(
            item => item != null && item.keyid == keyId);
        return PlaySfx(sfx, volumeScale);
    }

    public AudioSource PlaySfx(
        string keyId,
        float volumeScale,
        float pitch)
    {
        if (string.IsNullOrEmpty(keyId))
            return null;

        AudioData sfx = GameDataManager.Instance?.AudioData?.SFX?.Find(
            item => item != null && item.keyid == keyId);
        return PlaySfx(sfx?.audioclip, volumeScale, pitch);
    }

    public AudioSource PlaySfx(AudioData sfx, float volumeScale = 1f)
    {
        if (sfx?.audioclip == null)
            return null;

        float minPitch = ClampPitch(Mathf.Min(sfx.pitchmin, sfx.pitchmax));
        float maxPitch = ClampPitch(Mathf.Max(sfx.pitchmin, sfx.pitchmax));
        float pitch = Mathf.Approximately(minPitch, maxPitch)
            ? minPitch
            : Random.Range(minPitch, maxPitch);

        return PlaySfx(sfx.audioclip, volumeScale, pitch);
    }

    private void SetMixerVolume(AudioMixerGroup group, string parameterName, float amplitude)
    {
        AudioMixer mixer = group != null ? group.audioMixer : null;
        if (mixer == null)
        {
            ReportErrorOnce(
                parameterName,
                $"AudioManager is missing the Mixer Group for '{parameterName}'.");
            return;
        }

        float db = AmplitudeToDb(Clamp01Finite(amplitude));
        if (!mixer.SetFloat(parameterName, db))
        {
            ReportErrorOnce(
                parameterName,
                $"AudioMixer parameter '{parameterName}' is not exposed on '{mixer.name}'.");
        }
    }

    private static MusicTreatmentSourceState CaptureMusicTreatmentState(
        AudioSource source)
    {
        if (source == null)
            return null;

        AudioLowPassFilter lowPass = source.GetComponent<AudioLowPassFilter>();
        bool addedLowPass = lowPass == null;
        if (addedLowPass)
            lowPass = source.gameObject.AddComponent<AudioLowPassFilter>();

        AudioDistortionFilter distortion =
            source.GetComponent<AudioDistortionFilter>();
        bool addedDistortion = distortion == null;
        if (addedDistortion)
            distortion = source.gameObject.AddComponent<AudioDistortionFilter>();

        return new MusicTreatmentSourceState(
            lowPass,
            addedLowPass,
            distortion,
            addedDistortion);
    }

    private static void RestoreMusicTreatmentState(
        MusicTreatmentSourceState state)
    {
        if (state == null)
            return;

        if (state.LowPass != null)
        {
            if (state.AddedLowPass)
            {
                Destroy(state.LowPass);
            }
            else
            {
                state.LowPass.cutoffFrequency = state.Cutoff;
                state.LowPass.lowpassResonanceQ = state.Resonance;
                state.LowPass.enabled = state.LowPassEnabled;
            }
        }

        if (state.Distortion != null)
        {
            if (state.AddedDistortion)
            {
                Destroy(state.Distortion);
            }
            else
            {
                state.Distortion.distortionLevel = state.DistortionLevel;
                state.Distortion.enabled = state.DistortionEnabled;
            }
        }
    }


    private void ResolveSceneReferences()
    {
        MusicSource_00 = ResolveChildAudioSource(MusicSource_00, "MusicSource00");
        MusicSource_01 = ResolveChildAudioSource(MusicSource_01, "MusicSource01");
        AmbientSource_00 = ResolveChildAudioSource(AmbientSource_00, "AmbientSource00");
        AmbientSource_01 = ResolveChildAudioSource(AmbientSource_01, "AmbientSource01");

        if (sfxSourceParent == null)
        {
            Transform pool = transform.Find("SFXPool");
            if (pool == null)
                pool = transform.Find("SFXPOOL");

            if (pool != null)
                sfxSourceParent = pool.gameObject;
        }
    }

    private AudioSource ResolveChildAudioSource(AudioSource current, string childName)
    {
        if (current != null)
            return current;

        Transform child = transform.Find(childName);
        return child != null ? child.GetComponent<AudioSource>() : null;
    }

    private DualSourceChannel CreateChannel(
        string name,
        AudioSource source00,
        AudioSource source01,
        AudioMixerGroup outputGroup)
    {
        ConfigureChannelSource(source00, outputGroup);
        ConfigureChannelSource(source01, outputGroup);
        return new DualSourceChannel(name, source00, source01);
    }

    private static void ConfigureChannelSource(AudioSource source, AudioMixerGroup outputGroup)
    {
        if (source == null)
            return;

        source.Stop();
        source.playOnAwake = false;
        source.loop = false;
        source.volume = 0f;
        source.spatialBlend = 0f;

        if (outputGroup != null)
            source.outputAudioMixerGroup = outputGroup;
    }

    private void PlayChannel(DualSourceChannel channel, AudioClip clip, float fadeDuration, bool loop)
    {
        if (clip == null)
        {
            StopChannel(channel, fadeDuration);
            return;
        }

        if (!HasChannelSources(channel))
            return;

        AudioSource requestedSource = GetSourcePlayingClip(channel, clip);
        if (requestedSource != null
            && requestedSource == channel.ActiveSource
            && channel.Transition == null)
        {
            requestedSource.loop = loop;
            requestedSource.volume = channel.Gain;
            return;
        }

        CancelTransition(channel);

        AudioSource incoming = requestedSource != null
            ? requestedSource
            : GetQuieterSource(channel);
        AudioSource outgoing = incoming == channel.Source00
            ? channel.Source01
            : channel.Source00;

        if (incoming.clip != clip || !incoming.isPlaying)
        {
            incoming.Stop();
            incoming.clip = clip;
            incoming.time = 0f;
            incoming.volume = 0f;
        }

        incoming.loop = loop;
        if (!incoming.isPlaying)
            incoming.Play();

        channel.ActiveSource = incoming;

        if (!IsPositiveFinite(fadeDuration))
        {
            CompleteChannelTransition(channel, incoming, outgoing);
            return;
        }

        channel.Transition = StartCoroutine(
            CrossfadeChannel(channel, incoming, outgoing, fadeDuration));
    }

    private void StopChannel(DualSourceChannel channel, float fadeDuration)
    {
        if (!HasChannelSources(channel, false))
            return;

        CancelTransition(channel);

        if (!IsPositiveFinite(fadeDuration))
        {
            StopAndClearSource(channel.Source00);
            StopAndClearSource(channel.Source01);
            channel.ActiveSource = null;
            return;
        }

        channel.Transition = StartCoroutine(FadeOutChannel(channel, fadeDuration));
    }

    private bool HasChannelSources(DualSourceChannel channel, bool reportError = true)
    {
        bool hasSources = channel != null
            && channel.Source00 != null
            && channel.Source01 != null;

        if (!hasSources && reportError)
        {
            string channelName = channel != null ? channel.Name : "uninitialized";
            ReportErrorOnce(
                $"{channelName}Sources",
                $"AudioManager requires two AudioSources for the {channelName} channel.");
        }

        return hasSources;
    }

    private static AudioSource GetSourcePlayingClip(DualSourceChannel channel, AudioClip clip)
    {
        if (channel.Source00.clip == clip && channel.Source00.isPlaying)
            return channel.Source00;

        if (channel.Source01.clip == clip && channel.Source01.isPlaying)
            return channel.Source01;

        return null;
    }

    private static AudioSource GetQuieterSource(DualSourceChannel channel)
    {
        if (!channel.Source00.isPlaying)
            return channel.Source00;

        if (!channel.Source01.isPlaying)
            return channel.Source01;

        return channel.Source00.volume <= channel.Source01.volume
            ? channel.Source00
            : channel.Source01;
    }

    private void CancelTransition(DualSourceChannel channel)
    {
        if (channel != null)
            channel.PausingSource = null;
        if (channel == null || channel.Transition == null)
            return;

        StopCoroutine(channel.Transition);
        channel.Transition = null;
    }

    private IEnumerator CrossfadeChannel(
        DualSourceChannel channel,
        AudioSource incoming,
        AudioSource outgoing,
        float duration)
    {
        float incomingStartVolume = incoming.volume;
        float outgoingStartVolume = outgoing != null ? outgoing.volume : 0f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            incoming.volume = Mathf.Lerp(incomingStartVolume, channel.Gain, progress);

            if (outgoing != null && outgoing != incoming)
                outgoing.volume = Mathf.Lerp(outgoingStartVolume, 0f, progress);

            yield return null;
        }

        CompleteChannelTransition(channel, incoming, outgoing);
        channel.Transition = null;
    }

    private static void CompleteChannelTransition(
        DualSourceChannel channel,
        AudioSource incoming,
        AudioSource outgoing)
    {
        incoming.volume = channel.Gain;
        if (outgoing != null && outgoing != incoming)
            StopAndClearSource(outgoing);

        channel.ActiveSource = incoming;
    }

    const float MaxFadeStep = 1f / 30f;

    private IEnumerator FadeOutAndPause(
        DualSourceChannel channel,
        AudioSource source,
        AudioSource other,
        float duration)
    {
        float startVolume = source.volume;
        float otherStartVolume = other != null ? other.volume : 0f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            // A hitch frame (a window opening) must not swallow the whole fade.
            elapsed += Mathf.Min(Time.unscaledDeltaTime, MaxFadeStep);
            float progress = Mathf.Clamp01(elapsed / duration);
            source.volume = Mathf.Lerp(startVolume, 0f, progress);
            if (other != null && other != source && other.isPlaying)
                other.volume = Mathf.Lerp(otherStartVolume, 0f, progress);
            yield return null;
        }

        source.Pause();
        source.volume = channel.Gain;
        if (other != null && other != source)
            StopAndClearSource(other);
        channel.PausingSource = null;
        channel.Transition = null;
    }

    private IEnumerator FadeOutChannel(DualSourceChannel channel, float duration)
    {
        float source00StartVolume = channel.Source00.volume;
        float source01StartVolume = channel.Source01.volume;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            channel.Source00.volume = Mathf.Lerp(source00StartVolume, 0f, progress);
            channel.Source01.volume = Mathf.Lerp(source01StartVolume, 0f, progress);
            yield return null;
        }

        StopAndClearSource(channel.Source00);
        StopAndClearSource(channel.Source01);
        channel.ActiveSource = null;
        channel.Transition = null;
    }

    private static void StopAndClearSource(AudioSource source)
    {
        if (source == null)
            return;

        source.Stop();
        source.clip = null;
        source.volume = 0f;
    }

    private void InitializeSfxPool()
    {
        if (SFXPool == null)
            SFXPool = new List<AudioSource>();

        var sources = new List<AudioSource>();
        var seen = new HashSet<AudioSource>();

        for (int i = 0; i < SFXPool.Count; i++)
            AddSfxSourceIfValid(SFXPool[i], sources, seen);

        if (sfxSourceParent != null)
        {
            AudioSource[] childSources =
                sfxSourceParent.GetComponentsInChildren<AudioSource>(true);

            for (int i = 0; i < childSources.Length; i++)
                AddSfxSourceIfValid(childSources[i], sources, seen);
        }

        SFXPool.Clear();
        SFXPool.AddRange(sources);

        for (int i = 0; i < SFXPool.Count; i++)
            ResetSfxSource(SFXPool[i]);
    }

    private static void AddSfxSourceIfValid(
        AudioSource source,
        List<AudioSource> sources,
        HashSet<AudioSource> seen)
    {
        if (source != null && seen.Add(source))
            sources.Add(source);
    }

    private void RebuildSfxPoolCache()
    {
        if (SFXPool == null)
            SFXPool = new List<AudioSource>();

        for (int i = SFXPool.Count - 1; i >= 0; i--)
        {
            if (SFXPool[i] == null)
                SFXPool.RemoveAt(i);
        }

        if (SFXPool.Count > 0 || sfxSourceParent == null)
            return;

        AudioSource[] childSources =
            sfxSourceParent.GetComponentsInChildren<AudioSource>(true);

        for (int i = 0; i < childSources.Length; i++)
        {
            AudioSource source = childSources[i];
            if (source != null && !SFXPool.Contains(source))
            {
                ResetSfxSource(source);
                SFXPool.Add(source);
            }
        }
    }

    private AudioSource GetAvailableSfxSource()
    {
        RebuildSfxPoolCache();

        for (int i = 0; i < SFXPool.Count; i++)
        {
            AudioSource source = SFXPool[i];
            if (source != null && !source.isPlaying)
                return source;
        }

        return CreateSfxSource();
    }

    private AudioSource CreateSfxSource()
    {
        if (sfxSourcePrefab == null || sfxSourceParent == null)
            return null;

        AudioSource source = Instantiate(sfxSourcePrefab, sfxSourceParent.transform);
        source.name = $"sfxSource ({SFXPool.Count})";
        ResetSfxSource(source);
        source.gameObject.SetActive(true);
        SFXPool.Add(source);
        return source;
    }

    private void ResetSfxSource(AudioSource source)
    {
        if (source == null)
            return;

        source.Stop();
        source.clip = null;
        source.playOnAwake = false;
        source.loop = false;
        source.volume = 1f;
        source.pitch = 1f;
        source.spatialBlend = 0f;

        AudioMixerGroup outputGroup = SFXMixerGroup;
        if (outputGroup == null && sfxSourcePrefab != null)
            outputGroup = sfxSourcePrefab.outputAudioMixerGroup;

        if (outputGroup != null)
            source.outputAudioMixerGroup = outputGroup;

        if (sfxSourceParent != null)
        {
            source.transform.SetParent(sfxSourceParent.transform, false);
            source.transform.localPosition = Vector3.zero;
            source.transform.localRotation = Quaternion.identity;
            source.transform.localScale = Vector3.one;
        }

        source.gameObject.SetActive(true);
    }

    private void ReportErrorOnce(string key, string message)
    {
        if (reportedErrors.Add(key))
            Debug.LogError(message, this);
    }

    private static float Clamp01Finite(float value)
    {
        if (float.IsNaN(value) || value <= 0f)
            return 0f;

        if (value >= 1f)
            return 1f;

        return value;
    }

    private static float ClampPitch(float value)
    {
        if (float.IsNaN(value))
            return 1f;

        return Mathf.Clamp(value, -3f, 3f);
    }

    private static bool IsPositiveFinite(float value)
    {
        return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private sealed class SfxClipSuppression
    {
        public SfxClipSuppression(UnityEngine.Object owner) => Owner = owner;
        public UnityEngine.Object Owner { get; }
        public HashSet<AudioClip> Clips { get; } = new HashSet<AudioClip>();
    }

    private sealed class MusicTreatmentSourceState
    {
        public MusicTreatmentSourceState(
            AudioLowPassFilter lowPass,
            bool addedLowPass,
            AudioDistortionFilter distortion,
            bool addedDistortion)
        {
            LowPass = lowPass;
            AddedLowPass = addedLowPass;
            LowPassEnabled = lowPass != null && lowPass.enabled;
            Cutoff = lowPass != null ? lowPass.cutoffFrequency : 22000f;
            Resonance = lowPass != null ? lowPass.lowpassResonanceQ : 1f;
            Distortion = distortion;
            AddedDistortion = addedDistortion;
            DistortionEnabled = distortion != null && distortion.enabled;
            DistortionLevel = distortion != null
                ? distortion.distortionLevel
                : 0f;
        }

        public AudioLowPassFilter LowPass { get; }
        public bool AddedLowPass { get; }
        public bool LowPassEnabled { get; }
        public float Cutoff { get; }
        public float Resonance { get; }
        public AudioDistortionFilter Distortion { get; }
        public bool AddedDistortion { get; }
        public bool DistortionEnabled { get; }
        public float DistortionLevel { get; }
    }

    private sealed class DualSourceChannel
    {
        public DualSourceChannel(string name, AudioSource source00, AudioSource source01)
        {
            Name = name;
            Source00 = source00;
            Source01 = source01;
        }

        public string Name { get; }
        public AudioSource Source00 { get; }
        public AudioSource Source01 { get; }
        public AudioSource ActiveSource { get; set; }
        public Coroutine Transition { get; set; }
        public AudioSource PausingSource { get; set; }
        public float Gain { get; set; } = 1f;
    }
}
