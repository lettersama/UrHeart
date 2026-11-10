using System.Collections.Generic;
using UnityEngine;

public sealed partial class OSGramophonePlayer
{
    public const string YardSearchTrack = "ch03_yard_search", YardResolveTrack = "ch03_yard_resolve",
        BreakdownTrack = "ch03_breakdown", Chapter04ThemeTrack = "ch04_theme",
        FinalHealBattleTrack = "final_heal_battle", FinalHealCalmTrack = "final_heal_calm";
    const int StoryCollectionVersion = 1;

    static string StoryMusicKey(string id)
    {
        switch (id)
        {
            case YardSearchTrack: case YardResolveTrack: case BreakdownTrack:
            case Chapter04ThemeTrack: case FinalHealBattleTrack: case FinalHealCalmTrack: return id;
            default: return null;
        }
    }

    static string StoryTrackFallback(string id)
    {
        switch (id)
        {
            case YardSearchTrack: return "雨中寻觅";
            case YardResolveTrack: return "独自归来";
            case BreakdownTrack: return "镜前";
            case Chapter04ThemeTrack: return "仍在路上";
            case FinalHealBattleTrack: return "最后的治疗";
            case FinalHealCalmTrack: return "天亮之后";
            default: return "";
        }
    }

    // Directors call this after the scheduled source has actually started.
    // Collection never changes the selected record, playhead, or NEW state.
    public static void NotifyStoryTrackHeard(string id)
    {
        var data = GameDataManager.Instance;
        if (data == null || StoryMusicKey(id) == null || OSSessionPersistence.IsRestoring
            || MrTGame.Core.GameManager.Instance?.Phase == MrTGame.Core.GamePhase.Boot) return;
        var save = data.OSGramophoneState;
        save.EnsureData();
        bool changed = !save.collectedTrackIds.Contains(id);
        if (changed) save.collectedTrackIds.Add(id);
        if (Instance == null) return;
        Instance.BindSave(data);
        changed |= Instance.CollectLibraryTrack(id);
        if (changed) Instance.Changed?.Invoke();
    }

    void RestoreCollectedStoryTracks(GameDataManager data)
    {
        foreach (string id in state.collectedTrackIds)
            if (StoryMusicKey(id) != null && !available.Contains(id)) available.Add(id);
        if (StoryMusicKey(state.selectedTrackId) != null) CollectLibraryTrack(state.selectedTrackId);
        foreach (string id in state.manuallyPlayedTrackIds)
            if (StoryMusicKey(id) != null) CollectLibraryTrack(id);
        RefreshCollectedStoryTracks(data);
    }

    bool RefreshCollectedStoryTracks(GameDataManager data)
    {
        if (state == null || data == null || state.storyCollectionVersion >= StoryCollectionVersion
            || OSSessionPersistence.IsRestoring || GameSessionFlow.Instance?.IsBusy == true
            || GameAutosaveService.Instance?.IsTestSession == true) return false;
        bool changed = false;
        foreach (string id in LegacyStoryTracks(data)) changed |= CollectLibraryTrack(id);
        state.storyCollectionVersion = StoryCollectionVersion;
        return changed;
    }

    static IEnumerable<string> LegacyStoryTracks(GameDataManager data)
    {
        var memory = data.OSProcessData.memory;
        var visits = memory.triggeredDataIndices;
        bool Visited(int node) => visits != null && visits.Contains(node);
        if (Visited(10610) || Visited(10620)) yield return YardSearchTrack;
        if (memory.chapter03Story?.Has("yard_returned") == true && Visited(10720))
            yield return YardResolveTrack;
        bool heardBreakdown = Visited(9015) || Visited(9020);
        var consumed = data.OSCharacterInteractionState.consumedGlobalEntryIds;
        for (int i = 1; !heardBreakdown && i <= 8; i++)
            heardBreakdown = consumed.Contains("chapter03_story_live_breakdown_" + i.ToString("00"));
        if (heardBreakdown) yield return BreakdownTrack;
        var chapter4 = memory.chapter04Story;
        if (chapter4 == null) yield break;
        if (chapter4.started && chapter4.history != null
            && chapter4.history.Exists(x => x != null && x.entryId == "chapter04_story_01"))
            yield return Chapter04ThemeTrack;
        var stage = chapter4.finalTreatmentStage;
        if (stage == OSChapter04FinalTreatmentStage.Ending || stage == OSChapter04FinalTreatmentStage.Completed
            || stage == OSChapter04FinalTreatmentStage.FinalHeal && chapter4.finalHealSection >= 2)
            yield return FinalHealBattleTrack;
        if (stage == OSChapter04FinalTreatmentStage.Completed) yield return FinalHealCalmTrack;
    }

    bool StoryBlocksPlayback()
    {
        if (IsHeartTalkTransition || OSSessionPersistence.IsRestoring || GameSessionFlow.Instance?.IsBusy == true
            || MrTGame.Core.GameManager.Instance?.Phase == MrTGame.Core.GamePhase.Boot
            || os?.Chapter01Ending?.IsPresenting == true) return true;
        // Query the current contract synchronously: directors update after us.
        // This also protects intentionally silent beats and paused/hidden windows.
        return AudioManager.Instance?.IsBackgroundAudioSuppressed == true
            || GetComponent<OSChapter03StoryDirector>()?.OwnsGramophoneAudio == true
            || GetComponent<OSChapter04Music>()?.OwnsGramophoneAudio == true;
    }

    void SuspendBackgroundForStory()
    {
        CapturePosition();
        ReleaseTemporary(false);
        if (OwnedSource != null) AudioManager.Instance?.StopMusic(0f);
        paperWasVisible = false;
        // After an authored stop, a record returns only on explicit player input.
        suppressAutomaticBackground = true;
        restored = false;
    }

    static float RecordGain(string id)
    {
        // Gains target the existing record library's file level (-30 LUFS).
        // New Theme is already normalized; original story assets stay untouched.
        switch (id)
        {
            case YardSearchTrack: return .1678804f;
            case YardResolveTrack: return .1188502f;
            case BreakdownTrack: return .1096478f;
            case FinalHealBattleTrack: return .1122018f;
            case FinalHealCalmTrack: return .1737801f;
            default: return 1f;
        }
    }

    void ApplyRecordPitch(AudioSource source)
    {
        if (source == null) return;
        source.pitch = 1f;
        string id = state?.selectedTrackId;
        if (id != Chapter04ThemeTrack && id != FinalHealBattleTrack && id != FinalHealCalmTrack) return;
        var mixer = AudioManager.Instance?.MusicMixerGroup?.audioMixer;
        if (mixer != null && mixer.GetFloat("MasterPlaybackPitch", out float pitch) && pitch > 0f)
            source.pitch = 1f / pitch;
    }
}
