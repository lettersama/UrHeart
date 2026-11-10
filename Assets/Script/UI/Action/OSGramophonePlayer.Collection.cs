using System.Collections.Generic;

public sealed partial class OSGramophonePlayer
{
    OSMemoryProcessSaveData collectionMemory;
    List<int> collectionHistory;
    int collectionHistoryCount = -1, collectionCurrentNode = -1, collectionEvidence = -1;

    static string ActivityMusicKey(string id)
    {
        switch (id)
        {
            case OSChapter02ActivityMusic.DailyTrackId: return OSChapter02ActivityMusic.DailyMusicKey;
            case OSChapter02ActivityMusic.WarehouseTrackId: return OSChapter02ActivityMusic.WarehouseMusicKey;
            case OSChapter02ActivityMusic.ReturnHomeTrackId: return OSChapter02ActivityMusic.ReturnHomeMusicKey;
            case OSChapter02ActivityMusic.MakeupTrackId: return OSChapter02ActivityMusic.MakeupMusicKey;
            case OSChapter02ActivityMusic.ClawTrackId: return OSChapter02ActivityMusic.ClawMusicKey;
            case OSChapter02ActivityMusic.CursorTrackId: return OSChapter02ActivityMusic.CursorMusicKey;
            default: return null;
        }
    }

    void RestoreCollectedActivityTracks(GameDataManager data)
    {
        collectionMemory = null;
        collectionHistory = null;
        collectionHistoryCount = collectionCurrentNode = collectionEvidence = -1;
        foreach (string id in state.collectedTrackIds)
            if (ActivityMusicKey(id) != null && !available.Contains(id)) available.Add(id);
        // A previously selected/played record is also direct collection evidence.
        // Recover it before BindSave validates the saved selection, without playing it.
        CollectLibraryTrack(state.selectedTrackId);
        foreach (string id in state.manuallyPlayedTrackIds) CollectLibraryTrack(id);
        RefreshCollectedActivityTracks(data);
    }

    bool RefreshCollectedActivityTracks(GameDataManager data)
    {
        if (state == null || data == null || data.CurrentChapterIndex < 2
            || OSSessionPersistence.IsRestoring || GameSessionFlow.Instance?.IsBusy == true) return false;
        var memory = data.OSProcessData.memory;
        if (memory == null) return false;
        bool changed = false;
        int evidence = ActivityCollectionEvidence(memory);
        var history = memory.triggeredDataIndices;
        int count = history?.Count ?? 0;
        if (!ReferenceEquals(collectionMemory, memory) || !ReferenceEquals(collectionHistory, history)
            || collectionHistoryCount != count || collectionCurrentNode != memory.currentDataIndex
            || collectionEvidence != evidence)
        {
            collectionMemory = memory;
            collectionHistory = history;
            collectionHistoryCount = count;
            collectionCurrentNode = memory.currentDataIndex;
            collectionEvidence = evidence;
            // Check actual visited segments, never infer earlier songs from a later
            // node number: debug checkpoints can legitimately skip whole scenes.
            if (history != null)
                foreach (int node in history) changed |= CollectMemoryTrack(node);
            changed |= CollectMemoryTrack(memory.currentDataIndex);
            if ((evidence & 1) != 0) changed |= CollectLibraryTrack(OSChapter02ActivityMusic.ClawTrackId);
            if ((evidence & 2) != 0) changed |= CollectLibraryTrack(OSChapter02ActivityMusic.CursorTrackId);
        }
        // This also records an early visit before the activity has a story receipt.
        // A hidden/restoring window or a silent transition is not a new visit.
        if (activityOwner != null && activityClip != null && !activitySuspended && !activitySilence)
            changed |= CollectLibraryTrack(activityTrackId);
        return changed;
    }

    bool CollectMemoryTrack(int node)
    {
        if (node < 4000 || node > 7460) return false;
        var cue = OSChapter02ActivityMusic.ResolveMemoryMusic(node);
        // Silent ranges deliberately retain the preceding TrackId for fading.
        return !cue.Silent && CollectLibraryTrack(cue.TrackId);
    }

    bool CollectLibraryTrack(string id)
    {
        if (ActivityMusicKey(id) == null && StoryMusicKey(id) == null) return false;
        bool changed = false;
        if (!state.collectedTrackIds.Contains(id))
        {
            state.collectedTrackIds.Add(id);
            changed = true;
        }
        if (!available.Contains(id))
        {
            available.Add(id);
            changed = true;
        }
        return changed;
    }

    static int ActivityCollectionEvidence(OSMemoryProcessSaveData memory)
    {
        // Discovery/desktop placement only unlocks the icon, not the music.
        bool claw = memory.chapter02ClawIntroShown || memory.chapter02ClawThanksShown
            || memory.chapter02ClawTargetRevealed || memory.chapter02ClawCapturedMask != 0
            || memory.chapter02ClawOutletPrize > 0 || memory.chapter02ClawFirstPrizeTaken
            || memory.chapter02ClawNextRequestShown || memory.chapter02ClawSecondTargetRevealed;
        bool cursor = memory.chapter02ClawCursorCaptured || memory.chapter02CursorExperimentRound > 0
            || memory.chapter02CursorStoryStep > 0 || memory.chapter02CursorReleaseStep > 0
            || memory.chapter02HeartTalkStep > 0;
        // Cursor checkpoints can bypass the claw activity, so keep these independent.
        return (claw ? 1 : 0) | (cursor ? 2 : 0);
    }
}
