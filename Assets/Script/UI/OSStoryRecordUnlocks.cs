// Story receipts already live in SaveData. Records reuse those receipts so old
// saves can recover a missing keepsake without replaying a match or a map scene.
public static class OSStoryRecordUnlocks
{
    public const int RobotPaperDollRecordIndex = 2;
    public const int ResearcherPaperDollRecordIndex = 3;
    public const int LowerDistrictMapRecordIndex = 4;
    public const int MotherLetterRecordIndex = 6;
    public const int MapDepartureNode = 4800;
    const int MapDepartureFollowingNode = 4810;

    public static void Refresh(GameDataManager data, bool chapter01PresentationRunning = false)
    {
        var os = OS_Handle.Instance;
        if (data == null || os == null || !os.IsOSVisible
            || OSSessionPersistence.IsRestoring || GameSessionFlow.Instance?.IsBusy == true) return;

        RefreshDiary(data);
        RefreshMotherLetter(data);
        var energy = data.OSEnergyLoopState;
        // Chapter 2 debug shortcuts also set paperPushSealed. The actual Chapter
        // 1 win receipt distinguishes a finished match from that prepared icon.
        if (!chapter01PresentationRunning && HasRobotPaperDollEvidence(energy))
            Grant(data, RobotPaperDollRecordIndex);
        if (HasResearcherPaperDollEvidence(energy))
            Grant(data, ResearcherPaperDollRecordIndex);

        if (data.IsRecordDataUnlocked(LowerDistrictMapRecordIndex)) return;
        var memory = data.OSProcessData.memory;
        var window = os.MemoryWindowController;
        bool departureTextComplete = window != null && window.CurrentData?.DataIndex == MapDepartureNode
            && memory.currentDataIndex == MapDepartureNode && window.IsCurrentTextComplete;
        // CurrentContent stays alive through the map's closing animation. It is
        // null only once that display really closed (also after a save restore).
        bool mapClosed = window == null || window.MapPresentation == null;
        if (mapClosed && HasLowerDistrictMapEvidence(memory, departureTextComplete))
            Grant(data, LowerDistrictMapRecordIndex);
    }

    public static void RefreshDiary(GameDataManager data, bool newlyCollected = false)
    {
        if (data == null || !OSChapter03DiaryRecord.HasCollectedDiary(OSChapter03DiaryRecord.GetStory(data))) return;
        bool unlocked = OSChapter03DiaryRecord.EnsureUnlocked(data);
        // Restore/periodic reconciliation only repairs a missing record. The
        // actual first-time paper pickup owns subsequent NEW marks and refreshes.
        if (newlyCollected && !unlocked)
            data.NotifyRecordDataUpdated(OSChapter03DiaryRecord.RecordIndex);
    }

    public static bool HasRobotPaperDollEvidence(OSEnergyLoopSaveData energy)
        => energy != null && energy.chapter01PaperPushWins >= 3
            && energy.paperPushSealed && !energy.paperPushSealPending;

    public static void RefreshMotherLetter(GameDataManager data, bool newlyCollected = false)
    {
        // The story's actual close receipt is also sufficient for older saves.
        // Merely reaching chapter four or opening the envelope is not evidence.
        if (data?.OSProcessData?.memory?.chapter03Story?.Has("letter_collected") != true) return;
        if (newlyCollected) Grant(data, MotherLetterRecordIndex);
        else if (!data.IsRecordDataUnlocked(MotherLetterRecordIndex)) data.UnlockRecordData(MotherLetterRecordIndex);
    }

    public static bool HasResearcherPaperDollEvidence(OSEnergyLoopSaveData energy)
        => energy != null
            && energy.chapter02PaperPushStep >= (int)OSChapter02PaperPushDirector.RematchStep.HeartTalkHold;

    public static bool HasLowerDistrictMapEvidence(OSMemoryProcessSaveData memory, bool departureTextComplete)
    {
        var history = memory?.triggeredDataIndices;
        // A later chapter/debug node alone is not proof of seeing this map.
        // 4810 is the ordinary successor: reaching it proves 4800 was finished.
        return history != null && history.Contains(OSMemoryMapPresentation.EntryNode)
            && history.Contains(MapDepartureNode)
            && (departureTextComplete || history.Contains(MapDepartureFollowingNode));
    }

    static void Grant(GameDataManager data, int recordIndex)
    {
        if (data.IsRecordDataUnlocked(recordIndex) || !data.UnlockRecordData(recordIndex)) return;
        // UnlockRecordData owns the saved set, Record NEW and the open list's
        // RecordUnlocked refresh. Only the first grant produces an acquired tip.
        if (!data.TryGetRecordData(recordIndex, out var record)) return;
        string name = data.GetLocalizedRecordName(record)?.Trim();
        if (!string.IsNullOrWhiteSpace(name)) AdditionHandle.Instance?.ShowItemAcquiredTip(name);
    }
}
