public sealed partial class OSLinkMessageController
{
    // Unlike chapter 1's resource confirmation, these choices have no resource
    // cost/reward. Their unanswered state still owns the message slot.
    public bool IsReadAlongQuestionPending => TryGetReadAlongProtectedQuestion(out _);

    bool TryGetReadAlongProtectedQuestion(out OSCharacterInteractionEntryData entry)
    {
        entry = null;
        var data = GameDataManager.Instance;
        var state = data?.OSCharacterInteractionState;
        if (data == null || data.CurrentChapterIndex != 2 || data.CurrentOSMemoryDataIndex < 5410
            || state == null || !OS_Handle.IsChapter02ReadAlongEntry(state.presentingGlobalEntryId)
            || data.OSCharacterInteractionData == null
            || !data.OSCharacterInteractionData.TryGetGlobalEntry(state.presentingGlobalEntryId, out entry))
            return false;
        return entry.PartType == OSCharacterInteractionPartType.TextWithOptions
            && OS_Handle.GetChapter02ReadAlongAnswer(data.OSProcessData.memory, entry.EntryId) == 0;
    }

    bool ReadAlongQuestionAllows(OSCharacterInteractionEntryData incoming)
        => !TryGetReadAlongProtectedQuestion(out var question)
            || (incoming != null && incoming.EntryId == question.EntryId);
}
