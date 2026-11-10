using System;

// Append new milestones; the saved value also grants every earlier achievement.
public enum StoryAchievementMilestone
{
    None = 0,
    GravekeeperTreated = 1,
    RobotBroughtHome = 2,
    Chapter01Complete = 3,
    CursorTestsComplete = 4,
    PaperPushGearUsed = 5,
    Chapter02Complete = 6,
    MothersLetterRead = 7,
    Chapter03Complete = 8,
    GameComplete = 9,
    MyDaughter = 10,
    SecretGiftReceived = 11
}

// Local story receipts only. Demo progress is retained; platform submission has
// its own release/session checks and must never be called from this class.
public static class StoryAchievementProgress
{
    public const int Count = 11;
    const int CurrentVersion = 2;
    static readonly string[] apiNames = {
        "ACH_GRAVEKEEPER_TREATED", "ACH_ROBOT_BROUGHT_HOME", "ACH_CHAPTER_01_COMPLETE",
        "ACH_CURSOR_TESTS_COMPLETE", "ACH_PAPER_PUSH_GEAR_USED", "ACH_CHAPTER_02_COMPLETE",
        "ACH_MOTHERS_LETTER_READ", "ACH_CHAPTER_03_COMPLETE", "ACH_GAME_COMPLETE",
        "ACH_MY_DAUGHTER", "ACH_SECRET_GIFT_RECEIVED"
    };

    // Zero is a local sentinel, not a Steam achievement. Invalid inputs return null.
    public static string ApiName(int milestone) => milestone >= 1 && milestone <= Count
        ? apiNames[milestone - 1] : null;

    // Do not call EnsureData here: EnsureData itself performs the one-time migration.
    public static int GetReached(GameSaveData save) => save == null ? 0 : Clamp(save.storyAchievementMilestone);

    // The post-game window opens in Boot, whose SaveData holds preferences only.
    // Callers supply a successfully read/committed receipt from any completed
    // playthrough; starting a new game must not erase a pending Steam unlock.
    public static int GetFarewellReached(PostGameFarewellState receipt)
        => receipt != null && receipt.completed && (receipt.windowShown || receipt.windowDismissed)
            ? (int)StoryAchievementMilestone.SecretGiftReceived : 0;

    public static bool Mark(GameSaveData save, StoryAchievementMilestone milestone)
    {
        if (save == null) return false;
        int before = GetReached(save);
        MigrateLegacyProgress(save);
        save.storyAchievementMilestone = Math.Max(GetReached(save), Clamp((int)milestone));
        return save.storyAchievementMilestone > before;
    }

    public static void Record(StoryAchievementMilestone milestone)
    {
        var data = GameDataManager.Instance;
        if (data != null) Mark(data.SaveData, milestone);
    }

    public static void MigrateLegacyProgress(GameSaveData save)
    {
        if (save == null) return;
        save.storyAchievementMilestone = GetReached(save);
        if (save.storyAchievementVersion >= CurrentVersion) return;
        // Version 1 already has exact receipts for the first six milestones. Do not
        // reinterpret those using later transient story/checkpoint fields.
        if (save.storyAchievementVersion < 1)
            save.storyAchievementMilestone = Math.Max(save.storyAchievementMilestone, InferLegacy(save));
        save.storyAchievementMilestone = Math.Max(save.storyAchievementMilestone, InferLaterLegacy(save));
        save.storyAchievementVersion = CurrentVersion;
    }

    static int InferLaterLegacy(GameSaveData save)
    {
        var os = save.osProcess;
        var memory = os?.memory;
        var story = memory?.chapter04Story;

        // Ending starts only after the last deliberate mother tap succeeds.
        // Completed is written after the postcard has been collected and Staff ends.
        // Ending itself cannot prove that the postcard was collected.
        if (story?.finalTreatmentStage == OSChapter04FinalTreatmentStage.Completed)
            return (int)StoryAchievementMilestone.MyDaughter;
        if (story?.finalTreatmentStage == OSChapter04FinalTreatmentStage.Ending)
            return (int)StoryAchievementMilestone.GameComplete;

        // Darken..AwaitUnlock (1..8) happen before the chapter-card click.
        // Prepare (9) is written by the accepted click, before TV installation.
        if ((os?.currentChapterIndex ?? 0) >= 4
            || memory != null && memory.chapter04EntryStep >= (int)OSChapter04EntryDirector.EntryStep.Prepare
            || story?.started == true || story?.completed == true
            || story != null && story.finalTreatmentStage != OSChapter04FinalTreatmentStage.None)
            return (int)StoryAchievementMilestone.Chapter03Complete;

        // Older collecting saves restore directly to collected without replaying
        // their removed animation. The entry starts only after collection completes.
        if (memory?.chapter03Story?.Has("letter_collected") == true
            || memory?.chapter03Story?.Has("letter_collecting") == true
            || memory != null && memory.chapter04EntryStep > 0)
            return (int)StoryAchievementMilestone.MothersLetterRead;
        return 0;
    }

    static int InferLegacy(GameSaveData save)
    {
        var os = save.osProcess;
        var memory = os?.memory;
        var energy = os?.energyLoop;
        var ending = os?.chapter01Ending;
        int chapter = os?.currentChapterIndex ?? 0;

        // Legacy Ready had no separate receipt for its closing-window boundary.
        // Accept it once; versioned saves only advance through the completion hook.
        if (chapter >= 3 || memory != null && (memory.chapter03EntryStep > 0
            || memory.chapter02DemoEndReached || memory.chapter02FinalTalkStep >= 16))
            return (int)StoryAchievementMilestone.Chapter02Complete;

        // Active old second matches can retain a stale outcome that restore clears.
        // Neither revealing the gear nor beginning/sliding the match proves completion.
        if (energy != null && (energy.chapter02PaperPushStep >= 6
                || energy.paperPushMatch?.active != true
                    && (energy.chapter02PaperPushTiltWin == 1 || energy.chapter02PaperPushTiltWin == 2))
            || memory != null && (memory.chapter02FinalTalkInvitationPending || memory.chapter02FinalTalkStep > 0))
            return (int)StoryAchievementMilestone.PaperPushGearUsed;

        // Cursor phase 6 is Completed. Story steps 19/20/21 and assistance flags
        // still belong to the third test and deliberately provide no evidence here.
        if (memory != null && (memory.chapter02CursorExperimentRound >= 4
                || memory.chapter02CursorExperimentPhase == 6 || memory.chapter02CursorReleaseStep > 0
                || memory.chapter02RedCursorUnlocked || memory.chapter02HeartTalkStep > 0
                || memory.chapter02FinaleStep > 0)
            || energy != null && energy.chapter02PaperPushStep > 0
            || HasMemoryInRange(memory, 5410, 7460))
            return (int)StoryAchievementMilestone.CursorTestsComplete;

        // The ending enum is append-only, not chronological: DoorKnocked is 8
        // but precedes Animation. Completed legacy saves may replay on load, so
        // retain their evidence before any presentation controller resets it.
        if (chapter >= 2 || ending != null && (ending.completed
                || ending.phase == OSChapter01EndingPhase.Glitch
                || ending.phase == OSChapter01EndingPhase.Completed
                || ending.phase == OSChapter01EndingPhase.RecoveryAwaitingConfirmation
                || ending.phase == OSChapter01EndingPhase.Chapter02Started)
            || HasMemoryInRange(memory, 4000, 7460))
            return (int)StoryAchievementMilestone.Chapter01Complete;

        var picture = memory?.pictureScenario;
        if (picture != null && picture.initialized && picture.scenarioId == "chapter01_home"
            || ending != null && (ending.phase == OSChapter01EndingPhase.Slides
                || ending.phase == OSChapter01EndingPhase.Door
                || ending.phase == OSChapter01EndingPhase.DoorKnocked
                || ending.phase == OSChapter01EndingPhase.Animation)
            || HasMemoryInRange(memory, 2700, 3800))
            return (int)StoryAchievementMilestone.RobotBroughtHome;

        // Event_01 completes when treatment starts; Event_02 activates only after
        // its result is accepted. Process index zero also means never started.
        if (chapter >= 1 || HasMemoryInRange(memory, 1000, 2680))
            return (int)StoryAchievementMilestone.GravekeeperTreated;
        if (save.eventCompletionList != null)
            foreach (var entry in save.eventCompletionList)
                if (entry != null && entry.eventID == "Event_02" && (entry.isActive || entry.isCompleted))
                    return (int)StoryAchievementMilestone.GravekeeperTreated;
        return 0;
    }

    // Bounded authored ranges: Chapter 1 home descriptions 2700..3800,
    // Chapter 2 4000..7460, and post-cursor-test reading 5410..7460.
    // Unrelated high-numbered interaction nodes are never treated as later chapters.
    static bool HasMemoryInRange(OSMemoryProcessSaveData memory, int first, int last)
    {
        if (memory == null) return false;
        if (memory.currentDataIndex >= first && memory.currentDataIndex <= last) return true;
        if (memory.triggeredDataIndices != null)
            foreach (int node in memory.triggeredDataIndices)
                if (node >= first && node <= last) return true;
        return false;
    }

    static int Clamp(int milestone) => Math.Max(0, Math.Min(Count, milestone));
}
