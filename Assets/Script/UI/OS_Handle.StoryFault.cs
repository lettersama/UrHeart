// The fault meter creeps up at the moments the OS itself is visibly shaken in
// chapters one and two, so chapter three's breakdown has a history. Each beat
// is applied once per save; a save that is already past a beat catches up.
// Worst case after chapter two: 10 + 8 + 5 + 5 + 4 + 10 = 42, well under the
// high-fault warning (GameDataManager.HighFaultThreshold = 80).
public sealed partial class OS_Handle
{
    const string StoryFaultReceipt = "story.fault";

    struct StoryFaultBeat
    {
        public string id;
        public int delta;
        public System.Func<GameDataManager, bool> reached;
    }

    static readonly StoryFaultBeat[] StoryFaultBeats =
    {
        // Chapter 1 ending: the film breaks into the glitch.
        new StoryFaultBeat { id = "ch01_ending_glitch", delta = 8, reached = data =>
            data.CurrentChapterIndex >= 2 || IsChapter01EndingPastGlitch(data.OSProcessData.chapter01Ending.phase) },
        // Chapter 2 warehouse quake leaking into the OS, then the collapse.
        new StoryFaultBeat { id = "ch02_quake", delta = 5, reached = data => Chapter02StoryStage(data) >= 1 },
        new StoryFaultBeat { id = "ch02_collapse", delta = 5, reached = data => Chapter02StoryStage(data) >= 2 },
        // The hidden memory breaks through.
        new StoryFaultBeat { id = "ch02_hidden_memory", delta = 4, reached = data => Chapter02StoryStage(data) >= 3 },
        // Nightfall during the heart-to-heart.
        new StoryFaultBeat { id = "ch02_nightfall", delta = 10, reached = data => Chapter02StoryStage(data) >= 4 },
    };

    // 0 before the quake, 1 quake, 2 collapse, 3 hidden memory, 4 nightfall.
    // Later markers imply earlier ones, so a save that skips a marker still catches up.
    static int Chapter02StoryStage(GameDataManager data)
    {
        if (data.CurrentChapterIndex > 2)
            return 4;
        if (data.CurrentChapterIndex != 2)
            return 0;
        OSMemoryProcessSaveData memory = data.OSProcessData.memory;
        if (memory.chapter02FinalTalkStep >= (int)OSChapter02FinalTalkDirector.FinalTalkStep.Night)
            return 4;
        if (memory.chapter02FinalTalkStep > 0
            || memory.chapter02FinaleStep >= (int)OSChapter02FinaleDirector.FinaleStep.ReturnPause)
            return 3;
        int node = data.CurrentOSMemoryDataIndex;
        if (memory.chapter02FinaleStep > 0 || node >= 5610)
            return 2;
        return node >= 5490 ? 1 : 0;
    }

    static bool IsChapter01EndingPastGlitch(OSChapter01EndingPhase phase) =>
        phase == OSChapter01EndingPhase.Glitch || phase == OSChapter01EndingPhase.Completed
        || phase == OSChapter01EndingPhase.Chapter02Started || phase == OSChapter01EndingPhase.RecoveryAwaitingConfirmation;

    void RefreshStoryFault()
    {
        GameDataManager data = GameDataManager.Instance;
        if (data == null || OSSessionPersistence.IsRestoring || GameSessionFlow.Instance?.IsBusy == true
            || data.OSProcessData == null || data.OSProcessData.memory == null || data.OSProcessData.chapter01Ending == null)
            return;
        OSCharacterInteractionSaveData receipts = data.OSCharacterInteractionState;
        if (receipts == null)
            return;

        for (int i = 0; i < StoryFaultBeats.Length; i++)
        {
            StoryFaultBeat beat = StoryFaultBeats[i];
            if (!beat.reached(data))
                continue;
            // The option-resource receipt list survives sequence changes and saves.
            if (receipts.TryMarkOptionResourcesApplied(StoryFaultReceipt, beat.id, "applied"))
                ChangeError(beat.delta);
        }
    }
}
