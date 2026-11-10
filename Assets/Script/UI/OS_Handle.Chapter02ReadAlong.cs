using System;

public sealed partial class OS_Handle
{
    public const string Chapter02ReadAlongPrefix = "chapter02_readalong_";

    public static bool IsChapter02ReadAlongEntry(string entryId)
        => !string.IsNullOrEmpty(entryId)
            && entryId.StartsWith(Chapter02ReadAlongPrefix, StringComparison.Ordinal);

    static bool IsChapter02ReadAlongNode(int dataIndex)
    {
        switch (dataIndex)
        {
            case 5440: case 5550: case 5690: case 5830:
            case 5870: case 6010: case 6020:
            case 6290: case 6710: case 6940: case 7060: case 7160:
            case 7190: case 7200: return true;
            default: return false;
        }
    }

    public static int GetChapter02ReadAlongAnswer(OSMemoryProcessSaveData memory, string entryId)
    {
        if (memory == null) return 0;
        switch (entryId)
        {
            case "chapter02_readalong_5550": return memory.chapter02ReadIronFrame;
            case "chapter02_readalong_5690": return memory.chapter02ReadHeartbeat;
            case "chapter02_readalong_5830": return memory.chapter02ReadFoolReading;
            case "chapter02_readalong_6010": return memory.chapter02ReadAssistant;
            case "chapter02_readalong_7160": return memory.chapter02ReadDaughter;
            default: return 0;
        }
    }

    bool TryGetChapter02ReadAlongContext(out GameDataManager data, out OSMemoryProcessSaveData memory)
    {
        data = GameDataManager.Instance;
        memory = data?.OSProcessData?.memory;
        if (data == null || memory == null || data.CurrentChapterIndex != 2
            || data.CurrentOSMemoryDataIndex < 5410) return false;
        memory.EnsureData();
        return true;
    }

    bool HasReadChapter02ReadAlongMessage(int dataIndex, GameDataManager data, OSMemoryProcessSaveData memory)
    {
        string entryId = Chapter02ReadAlongPrefix + dataIndex;
        if (GetChapter02ReadAlongAnswer(memory, entryId) != 0) return true;
        var state = data.OSCharacterInteractionState;
        return state.HasConsumedGlobalEntry(entryId);
    }

    void QueueChapter02ReadAlongMessage(int dataIndex)
    {
        if (!IsChapter02ReadAlongNode(dataIndex)
            || !TryGetChapter02ReadAlongContext(out var data, out var memory)
            || HasReadChapter02ReadAlongMessage(dataIndex, data, memory)
            || memory.chapter02ReadAlongQueue.Contains(dataIndex)) return;
        memory.chapter02ReadAlongQueue.Add(dataIndex);
    }

    void RefreshChapter02ReadAlongMessages()
    {
        if (!IsSingletonInstance
            || !TryGetChapter02ReadAlongContext(out var data, out var memory)) return;

        var queue = memory.chapter02ReadAlongQueue;
        while (queue.Count > 0 && (!IsChapter02ReadAlongNode(queue[0])
            || HasReadChapter02ReadAlongMessage(queue[0], data, memory))) queue.RemoveAt(0);
        if (queue.Count == 0) return;

        var state = data.OSCharacterInteractionState;
        // Older messages already accepted by the shared FIFO retain priority.
        if (state.optionCountdownQueue.Count > 0) return;
        if (!IsOSVisible || OSSessionPersistence.IsRestoring
            || GameSessionFlow.Instance?.IsBusy == true || GameTestConsole.BlocksGameplayInput
            || IsSystemInputLocked || IsChapter02CollectionMessageProtected
            || linkMessageController == null || linkMessageController.GlobalMessagesSuppressed
            || Chapter01Ending?.IsChapter02LiveDisconnected == true
            || Chapter01Ending?.IsChapter02ReturnInProgress == true
            || linkMessageController.IsPresenting || linkMessageController.IsWaitingForOption
            || linkMessageController.IsWaitingForExternalCompletion
            || !string.IsNullOrEmpty(state.presentingGlobalEntryId)
            || !string.IsNullOrEmpty(state.pendingGlobalEntryId)) return;

        // Typing completion clears IsPresenting before a plain line's reading hold ends.
        // Do not cut off any existing message, especially a reply to the previous question.
        var messageWindow = linkMessageController.LinkWindow;
        if (messageWindow != null && (messageWindow.IsOpen || messageWindow.IsClosing)) return;
        if (characterWindow == null || characterWindow.IsClosing) return;
        if (!characterWindow.IsOpen || characterWindow.IsMinimized)
        {
            OpenCharacterWindowForReconnect(0.15f);
            return;
        }
        if (characterWindow.IsOpening) return;

        string suffix = queue[0].ToString();
        if (linkMessageController.NotifyGlobalExternalSignal("chapter02.readalong." + suffix))
            queue.RemoveAt(0);
    }

    bool RecordChapter02ReadAlongAnswer(string sequenceId,
        OSCharacterInteractionEntryData entry, OSCharacterInteractionOptionData option)
    {
        if (sequenceId != OSCharacterInteractionIds.GlobalSequence || !IsChapter02ReadAlongEntry(entry.EntryId)
            || (option.OptionId != "1" && option.OptionId != "2")) return false;
        var memory = GameDataManager.Instance?.OSProcessData?.memory;
        if (memory == null) return false;
        int answer = option.OptionId == "1" ? 1 : 2;
        switch (entry.EntryId)
        {
            case "chapter02_readalong_5550": memory.chapter02ReadIronFrame = answer; break;
            case "chapter02_readalong_5690": memory.chapter02ReadHeartbeat = answer; break;
            case "chapter02_readalong_5830": memory.chapter02ReadFoolReading = answer; break;
            case "chapter02_readalong_6010": memory.chapter02ReadAssistant = answer; break;
            case "chapter02_readalong_7160": memory.chapter02ReadDaughter = answer; break;
            default: return false;
        }
        return true;
    }
}
