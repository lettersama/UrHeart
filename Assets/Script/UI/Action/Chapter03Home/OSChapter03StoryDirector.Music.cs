using UnityEngine;

/// <summary>
/// Chapter 3's own music (pancake 10-25). The yard: a search loop fading in from the last
/// knock at her door, receding while it walks back alone, then a one-shot under the white-strip
/// monologue inside the door. Live's breakdown: one long take that builds and then holds,
/// fading out over her final message. Since pancake 10-26 its opening bars are a prelude of their
/// own, waiting under the memory from the click on the bathroom mirror; the melody comes in on its
/// next beat once she says her first line. Clips live in Resources/Chapter03Story/Audio; a missing
/// clip stays silent, except the breakdown, which falls back to the old QuietRooms track.
/// </summary>
public sealed partial class OSChapter03StoryDirector
{
    const string MusicFolder = "Chapter03Story/Audio/";
    const string YardCue = "yard", YardResolveCue = "yard_resolve", BreakdownCue = "breakdown",
        BreakdownPreludeCue = "breakdown_prelude";
    const float BreakdownVolume = .31f; // pancake 10-26: a touch quieter again (about -1.5 dB)
    const float BreakdownFadeOutSeconds = 6f;
    // Ch03_BreakdownMain starts on the strings' swell; its first melody note is .4 s in.
    // The prelude keeps the original 60 BPM, so its beats fall on whole seconds.
    const float BreakdownMelodyOffset = .4f, BreakdownBeat = 1f;

    AudioSource storyMusic, fadingMusic;
    string musicCue;
    float musicBaseVolume, musicFadeIn, fadingPerSecond, musicStartAt;
    bool musicPaused;
    double scheduledMusicStart = -1, scheduledMusicRemaining = -1;
    AudioManager gramophoneAudio;
    bool gramophoneLeaseHeld;
    string pendingMusicTrack, pendingFadingTrack;
    double musicTrackStartDsp, fadingTrackStartDsp;

    // The player queries this before its own Update, including on the first
    // restored frame. Ordinary exploration remains available for manual records.
    public bool OwnsGramophoneAudio => IsActive
        && MrTGame.Core.GameManager.Instance?.Phase != MrTGame.Core.GamePhase.Boot
        && (IsCinematic || CurrentPhase == Phase.Yard || CurrentPhase == Phase.Complete
            || storyMusic != null || fadingMusic != null);

    void RefreshGramophoneAudioOwnership()
    {
        bool owns = OwnsGramophoneAudio;
        var next = AudioManager.Instance;
        if (gramophoneLeaseHeld && (!owns || gramophoneAudio != next)) ReleaseGramophoneAudio();
        if (!owns || gramophoneLeaseHeld || next == null) return;
        gramophoneAudio = next;
        gramophoneAudio.SetBackgroundAudioSuppressed(this, true, .15f);
        gramophoneLeaseHeld = true;
    }

    void ReleaseGramophoneAudio()
    {
        if (gramophoneLeaseHeld && gramophoneAudio != null)
            gramophoneAudio.SetBackgroundAudioSuppressed(this, false);
        gramophoneLeaseHeld = false;
        gramophoneAudio = null;
    }

    void PlayMusicCue(string cue)
    {
        if (cue == musicCue) return;
        AudioClip clip = null; bool loop = true; float volume = .5f, fadeIn = 1f, fadePrevious = 1.5f;
        string track = null;
        double delay = 0; musicStartAt = 0f;
        switch (cue)
        {
            // From the last knock at her door it creeps in under the reasoning, so it is there by the yard.
            case YardCue: clip = Resources.Load<AudioClip>(MusicFolder + "Ch03_YardSearch"); volume = .45f;
                track = OSGramophonePlayer.YardSearchTrack;
                fadeIn = CurrentPhase == Phase.FinalKnock ? 8f : 2.5f; break;
            case YardResolveCue: clip = Resources.Load<AudioClip>(MusicFolder + "Ch03_YardResolve");
                track = OSGramophonePlayer.YardResolveTrack; loop = false; volume = .5f; fadeIn = .2f; break;
            // The original opening bars, unchanged; eight rounds (128 s) before it would wrap,
            // enough for the note in the mirror cabinet to be read slowly.
            case BreakdownPreludeCue: clip = Resources.Load<AudioClip>(MusicFolder + "Ch03_BreakdownPrelude");
                track = OSGramophonePlayer.BreakdownTrack;
                volume = BreakdownVolume; fadeIn = 1.5f;
                Resources.Load<AudioClip>(MusicFolder + "Ch03_BreakdownMain")?.LoadAudioData(); // ready for its beat
                break;
            case BreakdownCue:
                track = OSGramophonePlayer.BreakdownTrack;
                clip = Resources.Load<AudioClip>(MusicFolder + "Ch03_BreakdownMain");
                if (clip == null)
                {
                    clip = Resources.Load<AudioClip>(MusicFolder + "Ch03_Breakdown");
                    if (clip != null) musicStartAt = 16f - BreakdownMelodyOffset; // its melody is at 16 s
                }
                volume = BreakdownVolume; fadeIn = 1.5f;
                if (clip == null)
                {
                    clip = GameDataManager.Instance?.AudioData?.BGM?.Find(x => x.keyid == "QuietRooms_05_Drifting_Loop")?.audioclip;
                    volume = .3f; track = null; // a fallback is not the authored Breakdown recording
                }
                // Straight from the prelude: the melody lands on its next beat and the piano gives way
                // underneath. Restores and a missing prelude simply fade in.
                else if (musicCue == BreakdownPreludeCue && storyMusic != null && storyMusic.isPlaying && !musicPaused)
                {
                    delay = UntilNextPreludeBeat(storyMusic) - BreakdownMelodyOffset;
                    fadeIn = 0f; fadePrevious = (float)delay + BreakdownMelodyOffset + 1f;
                }
                break;
        }
        FadeOutMusic(fadePrevious);
        musicCue = cue; // a missing clip is not looked up again every frame
        if (clip == null) return;
        RefreshGramophoneAudioOwnership();
        var musicObject = new GameObject("Chapter03Music_" + cue);
        musicObject.transform.SetParent(transform, false);
        storyMusic = musicObject.AddComponent<AudioSource>(); storyMusic.clip = clip;
        storyMusic.outputAudioMixerGroup = AudioManager.Instance?.MusicMixerGroup;
        storyMusic.playOnAwake = false; storyMusic.loop = loop; storyMusic.volume = fadeIn > 0f ? 0f : volume;
        musicBaseVolume = volume; musicFadeIn = volume / Mathf.Max(.05f, fadeIn);
        if (musicStartAt > 0f && musicStartAt < clip.length) storyMusic.time = musicStartAt;
        pendingMusicTrack = track;
        musicTrackStartDsp = AudioSettings.dspTime + delay;
        if (delay > 0)
        { scheduledMusicStart = musicTrackStartDsp; storyMusic.PlayScheduled(scheduledMusicStart); }
        else storyMusic.Play();
        if (musicPaused) storyMusic.Pause();
    }

    // Seconds until the prelude's next beat that leaves room for the main clip's lead-in.
    static double UntilNextPreludeBeat(AudioSource prelude)
    {
        double at = prelude.timeSamples / (double)Mathf.Max(1, prelude.clip.frequency);
        double beat = System.Math.Ceiling((at + BreakdownMelodyOffset + .15) / BreakdownBeat) * BreakdownBeat;
        return beat - at;
    }

    void FadeOutMusic(float seconds)
    {
        musicCue = null; scheduledMusicStart = scheduledMusicRemaining = -1;
        if (storyMusic == null) return;
        if (fadingMusic != null) Destroy(fadingMusic.gameObject);
        fadingMusic = storyMusic; fadingPerSecond = storyMusic.volume / Mathf.Max(.05f, seconds);
        pendingFadingTrack = pendingMusicTrack; fadingTrackStartDsp = musicTrackStartDsp;
        pendingMusicTrack = null;
        storyMusic = null;
    }

    void StopMusicNow()
    {
        if (storyMusic != null) Destroy(storyMusic.gameObject);
        if (fadingMusic != null) Destroy(fadingMusic.gameObject);
        storyMusic = fadingMusic = null; musicCue = null;
        pendingMusicTrack = pendingFadingTrack = null;
        scheduledMusicStart = scheduledMusicRemaining = -1;
        ReleaseGramophoneAudio();
    }

    void PauseMusic(bool paused)
    {
        if (paused == musicPaused) return;
        musicPaused = paused;
        // A melody still waiting for its beat keeps the same distance to it across the pause.
        if (paused && storyMusic != null && scheduledMusicStart > 0 && AudioSettings.dspTime < scheduledMusicStart)
        { scheduledMusicRemaining = scheduledMusicStart - AudioSettings.dspTime; storyMusic.Stop(); }
        foreach (var source in new[] { storyMusic, fadingMusic })
        {
            if (source == null || source == storyMusic && scheduledMusicRemaining > 0) continue;
            if (paused) source.Pause(); else source.UnPause();
        }
        if (!paused && storyMusic != null && scheduledMusicRemaining > 0)
        {
            if (musicStartAt > 0f && musicStartAt < storyMusic.clip.length) storyMusic.time = musicStartAt;
            scheduledMusicStart = AudioSettings.dspTime + scheduledMusicRemaining; scheduledMusicRemaining = -1;
            musicTrackStartDsp = scheduledMusicStart;
            storyMusic.PlayScheduled(scheduledMusicStart);
        }
    }

    void TickMusic(float dt)
    {
        // Restores and phase changes: the search loop belongs to the yard only.
        if ((CurrentPhase == Phase.FinalKnock || CurrentPhase == Phase.Yard) && !Has("yard_returned")) PlayMusicCue(YardCue);
        else if (musicCue == YardCue) FadeOutMusic(9f); // walking back alone, the music leaves first
        // From the click on the bathroom mirror (it opens the note) until her first line
        // (pancake 10-26). Saves restored in between start the prelude again from the top.
        if (CurrentPhase == Phase.Mirror && Has("paper_requested_note_09") && !Has("breakdown_music")
            && !Has("breakdown_quiet") && !Has("mirror_done")) PlayMusicCue(BreakdownPreludeCue);
        // A restored completed-text reading hold does not fire OnLiveStarted.
        // Recognize the last message there too, without restarting its music.
        if (CurrentPhase == Phase.Mirror && messages?.DisplayedEntryId == LiveId("live.breakdown.09"))
            State.Add("breakdown_music_fading");
        // In particular, restoring the eighth message's choice must retain the
        // music while she waits for the player's reply. The heartbeat has its
        // own earlier "breakdown_quiet" cue and remains unchanged.
        if (CurrentPhase == Phase.Mirror && Has("breakdown_music") && !Has("breakdown_music_fading")
            && !State.liveOffline && !Has("live_back_home") && !Has("mirror_done")
            && !string.IsNullOrEmpty(messages?.DisplayedEntryId)
            && messages.DisplayedEntryId.StartsWith(LiveId("live.breakdown."), System.StringComparison.Ordinal))
            PlayMusicCue(BreakdownCue);
        if ((musicCue == BreakdownCue || musicCue == BreakdownPreludeCue)
            && (Has("breakdown_music_fading") || State.liveOffline || Has("mirror_done"))) FadeOutMusic(BreakdownFadeOutSeconds);
        if (musicPaused) return;
        if (scheduledMusicStart > 0 && AudioSettings.dspTime >= scheduledMusicStart) scheduledMusicStart = -1;

        if (fadingMusic != null)
        {
            fadingMusic.volume = Mathf.MoveTowards(fadingMusic.volume, 0f, fadingPerSecond * dt);
            NotifyHeardMusic(fadingMusic, fadingTrackStartDsp, ref pendingFadingTrack);
            if (fadingMusic.volume <= 0f)
            { Destroy(fadingMusic.gameObject); fadingMusic = null; pendingFadingTrack = null; }
        }
        if (storyMusic == null) { RefreshGramophoneAudioOwnership(); return; }
        if (!storyMusic.loop && !storyMusic.isPlaying)
        {
            Destroy(storyMusic.gameObject); storyMusic = null; musicCue = null; pendingMusicTrack = null;
            RefreshGramophoneAudioOwnership(); return;
        }
        float target = musicBaseVolume;
        // The last line before she asks to be alone sits a little forward.
        if (musicCue == BreakdownCue && effectEntryId == LiveId("live.breakdown.07")) target = musicBaseVolume * 1.36f; // about +2.7 dB
        float rate = storyMusic.volume < target * .98f ? musicFadeIn : .12f;
        storyMusic.volume = Mathf.MoveTowards(storyMusic.volume, target, rate * dt);
        NotifyHeardMusic(storyMusic, musicTrackStartDsp, ref pendingMusicTrack);
        RefreshGramophoneAudioOwnership();
    }

    static void NotifyHeardMusic(AudioSource source, double startDsp, ref string track)
    {
        if (string.IsNullOrEmpty(track) || source == null || source.clip == null
            || !source.isPlaying || source.volume <= 0f || AudioSettings.dspTime < startDsp
            || OSSessionPersistence.IsRestoring || GameDataManager.Instance == null
            || MrTGame.Core.GameManager.Instance?.Phase == MrTGame.Core.GamePhase.Boot) return;
        OSGramophonePlayer.NotifyStoryTrackHeard(track);
        track = null;
    }
}
