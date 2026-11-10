using System.Collections.Generic;
using UnityEngine;

// Short fades for every hand-over the player makes on its own: opening or
// closing the paper-push window, a hidden memory window, and a chapter 2
// activity changing its track. Authored silences and story cuts keep their
// own timing; this only replaces the instant pause/stop between two songs.
public sealed partial class OSGramophonePlayer
{
    const float BorrowedPauseFade = .3f, BorrowedResumeFade = .35f;
    const float TemporaryFadeInSeconds = .4f, TemporaryFadeOutSeconds = .35f;

    AudioSource temporaryTail;
    float temporaryTailRate;
    AudioSource activityCrossfadeSource;
    readonly Dictionary<string, float> activityResumeSeconds = new Dictionary<string, float>();

    // The borrowed voice we are handing over from, while it is still the channel's voice.
    AudioSource ActivityCrossfadeSource => activityCrossfadeSource != null
        && AudioManager.Instance?.CurrentMusicSource == activityCrossfadeSource ? activityCrossfadeSource : null;

    // A hitch frame (the window opening) must not swallow the whole fade.
    static float FadeStep => Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);

    void TickTemporaryFadeIn(AudioSource source)
    {
        float target = Volume;
        float rate = Mathf.Max(target, .05f) / TemporaryFadeInSeconds;
        source.volume = Mathf.MoveTowards(source.volume, target, FadeStep * rate);
    }

    void BeginTemporaryTail(AudioSource source, bool immediate)
    {
        if (immediate || !source.isPlaying || source.volume <= 0f)
        {
            source.Stop();
            if (temporaryTail == source) temporaryTail = null;
            return;
        }
        temporaryTail = source;
        temporaryTailRate = source.volume / TemporaryFadeOutSeconds;
    }

    bool ResumeTemporaryTail(AudioSource source, AudioClip clip)
    {
        if (temporaryTail != source) return false;
        temporaryTail = null;
        return source.isPlaying && source.clip == clip;
    }

    void TickTemporaryTail()
    {
        if (temporaryTail == null) return;
        if (!temporaryTail.isPlaying) { temporaryTail = null; return; }
        temporaryTail.volume = Mathf.MoveTowards(temporaryTail.volume, 0f,
            FadeStep * temporaryTailRate);
        if (temporaryTail.volume > 0f) return;
        temporaryTail.Stop();
        temporaryTail = null;
    }

    void StopTemporaryTail()
    {
        if (temporaryTail != null) temporaryTail.Stop();
        temporaryTail = null;
    }

    // A window hidden in play fades out; a lost application focus pauses at
    // once, since the player loop may stop before any fade could finish.
    void PauseBorrowedVoice(AudioManager audio)
    {
        if (Application.isFocused) audio.PauseMusicPlayback(BorrowedPauseFade);
        else audio.PauseMusicPlayback();
    }

    // Called before a track change replaces activityClip. The current voice
    // keeps playing and the next PlayMusic crossfades from it.
    void BeginActivityCrossfade()
    {
        // A change that arrives before the previous one started keeps the same old voice.
        var current = ActivitySource ?? OwnedSource ?? ActivityCrossfadeSource;
        if (ActivitySource != null && !string.IsNullOrEmpty(activityTrackId))
            activityResumeSeconds[activityTrackId] = ActivitySource.time;
        activityCrossfadeSource = current;
    }

    // A track left for a moment (the claw machine opened over the memory)
    // continues where it was; one that starts after an authored silence starts
    // from the top, as before.
    void ApplyActivityResumePosition(AudioSource source)
    {
        if (string.IsNullOrEmpty(activityTrackId)
            || !activityResumeSeconds.TryGetValue(activityTrackId, out float seconds)) return;
        activityResumeSeconds.Remove(activityTrackId);
        // Only a voice that has just started from zero; never seek a song that kept playing.
        if (source == null || source.clip == null || source.time > .05f) return;
        source.time = Mathf.Repeat(seconds, Mathf.Max(.01f, source.clip.length));
    }

    void ClearActivityCrossfade(bool forgetPositions)
    {
        activityCrossfadeSource = null;
        if (forgetPositions) activityResumeSeconds.Clear();
    }
}
