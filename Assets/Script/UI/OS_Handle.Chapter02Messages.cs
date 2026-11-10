using System.Collections.Generic;

public sealed partial class OS_Handle
{
    const string Chapter02BacktalkEntry = "chapter02_memory_breakfast_backtalk";
    const string Chapter02AutoScrollEntry = "chapter02_memory_auto_scroll";
    const string Chapter02MedicineEntry = "chapter02_memory_medicine_pocketed";

    // Global message receipts already provide once-only persistence. This small
    // session queue only prevents fast memory clicks from cutting off the hold.
    readonly Queue<int> chapter02MemoryMessages = new Queue<int>(3);
    GameSaveData chapter02MessageSave;

    static string GetChapter02MemoryMessageEntry(int dataIndex)
    {
        switch (dataIndex)
        {
            case 4530: return Chapter02BacktalkEntry;
            case 4760: return Chapter02AutoScrollEntry;
            case 5110: return Chapter02MedicineEntry;
            default: return null;
        }
    }

    bool SyncChapter02MemoryMessageContext(out GameDataManager data)
    {
        data = GameDataManager.Instance;
        GameSaveData currentSave = data != null && data.CurrentChapterIndex == 2
            ? data.SaveData : null;
        if (!ReferenceEquals(chapter02MessageSave, currentSave))
        {
            chapter02MemoryMessages.Clear();
            chapter02MessageSave = currentSave;
        }
        return currentSave != null;
    }

    void QueueChapter02MemoryMessage(int dataIndex)
    {
        if (!SyncChapter02MemoryMessageContext(out var data)) return;
        string entryId = GetChapter02MemoryMessageEntry(dataIndex);
        if (entryId == null
            || data.OSCharacterInteractionState.HasConsumedGlobalEntry(entryId)
            || chapter02MemoryMessages.Contains(dataIndex)) return;
        chapter02MemoryMessages.Enqueue(dataIndex);
    }

    void RefreshChapter02MemoryMessages()
    {
        if (!IsSingletonInstance
            || !SyncChapter02MemoryMessageContext(out var data)
            || chapter02MemoryMessages.Count == 0) return;

        var interaction = data.OSCharacterInteractionState;
        while (chapter02MemoryMessages.Count > 0
            && interaction.HasConsumedGlobalEntry(
                GetChapter02MemoryMessageEntry(chapter02MemoryMessages.Peek())))
            chapter02MemoryMessages.Dequeue();
        if (chapter02MemoryMessages.Count == 0) return;

        if (!IsOSVisible || IsChapter02CollectionMessageProtected || OSSessionPersistence.IsRestoring
            || GameSessionFlow.Instance?.IsBusy == true || GameTestConsole.BlocksGameplayInput
            || IsSystemInputLocked || linkMessageController == null
            || linkMessageController.GlobalMessagesSuppressed
            || linkMessageController.IsReadAlongQuestionPending
            || !interaction.HasCompletedEntry("chapter02_return_reaction")
            || Chapter01Ending?.IsChapter02LiveDisconnected == true
            || Chapter01Ending?.IsChapter02ReturnInProgress == true
            || linkMessageController.IsPresenting || linkMessageController.IsWaitingForOption
            || linkMessageController.IsWaitingForExternalCompletion
            || !string.IsNullOrEmpty(interaction.pendingGlobalEntryId)) return;

        // IsPresenting becomes false as soon as a plain message finishes typing.
        // Its window remains open for the authored auto-close interval.
        var messageWindow = linkMessageController.LinkWindow;
        string displayedEntry = linkMessageController.DisplayedEntryId;
        bool isChapter02Comment = displayedEntry == Chapter02BacktalkEntry
            || displayedEntry == Chapter02AutoScrollEntry
            || displayedEntry == Chapter02MedicineEntry
            || displayedEntry == MemoryUpgrade03InstalledMessage
            || IsChapter02ReadAlongEntry(displayedEntry);
        if (messageWindow != null
            && (messageWindow.IsClosing || (isChapter02Comment && messageWindow.IsOpen))) return;

        if (characterWindow == null || characterWindow.IsClosing) return;
        if (!characterWindow.IsOpen || characterWindow.IsMinimized)
        {
            OpenCharacterWindowForReconnect(0.15f);
            return;
        }
        if (characterWindow.IsOpening) return;

        int dataIndex = chapter02MemoryMessages.Peek();
        if (linkMessageController.NotifyGlobalExternalSignal("memory.text_completed." + dataIndex))
            chapter02MemoryMessages.Dequeue();
    }
}
