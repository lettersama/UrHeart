using MrTGame.Core;
using UnityEngine;

// Chapter 2's Live return shares the existing recovery sequence and save receipts.
public sealed partial class OSChapter01EndingDirector
{
    const int Chapter02UpgradeGate = 4310;
    const string ReturnArrivedEntry = "chapter02_return_arrived";
    const string ReturnWorkingEntry = "chapter02_return_working";
    const string ReturnReadyEntry = "chapter02_return_ready";
    const string ReturnReactionEntry = "chapter02_return_reaction";
    bool returnSignalPending;
    float returnWorkDeadline = -1f;
    float returnReactionDeadline = -1f;
    OSCharacterExtendedAnimation returnOwnedAnimation;
    string returnPresentedEntry;

    public bool IsChapter02ReturnInProgress
    {
        get
        {
            var data = GameDataManager.Instance;
            if (data?.CurrentChapterIndex != 2) return false;
            var interaction = data.OSCharacterInteractionState;
            return interaction.activeSequenceId == RecoverySequence
                && (returnSignalPending || (interaction.HasConsumedEntry(ReturnArrivedEntry)
                    && !interaction.HasCompletedEntry(ReturnReadyEntry))
                    || (interaction.HasConsumedEntry(ReturnReactionEntry)
                        && !interaction.HasCompletedEntry(ReturnReactionEntry)));
        }
    }

    void UpdateChapter02Return()
    {
        var data = GameDataManager.Instance;
        if (data.CurrentChapterIndex != 2 || os == null || !os.IsOSVisible
            || OSSessionPersistence.IsRestoring || GameSessionFlow.Instance?.IsBusy == true
            || os.IsSystemInputLocked || recoveryMessages == null) return;
        var interaction = data.OSCharacterInteractionState;
        if (interaction.activeSequenceId != RecoverySequence
            || !interaction.HasCompletedEntry(DepartureEntry)) return;
        var character = os.CharacterWindowController;
        if (character == null) return;

        // Immediate text can resume before this component binds or observes a new save.
        // Adopt the existing presentation once instead of relying solely on callbacks.
        string displayed = recoveryMessages.DisplayedEntryId;
        if (recoveryMessages.IsPresenting && recoveryMessages.DisplayedSequenceId == RecoverySequence
            && (displayed == ReturnArrivedEntry || displayed == ReturnWorkingEntry
                || displayed == ReturnReadyEntry || displayed == ReturnReactionEntry)
            && data.OSCharacterInteractionData.TryGetSequence(RecoverySequence, out var sequence)
            && sequence.TryGetEntry(displayed, out var displayedEntry))
        {
            if (returnPresentedEntry != displayed) OnReturnEntryStarted(RecoverySequence, displayedEntry);
            if (((displayed == ReturnWorkingEntry && returnWorkDeadline < 0f)
                    || (displayed == ReturnReactionEntry && returnReactionDeadline < 0f))
                && recoveryMessages.IsDisplayedTextComplete)
                OnReturnTextCompleted(RecoverySequence, displayedEntry);
        }

        if (!interaction.HasConsumedEntry(ReturnArrivedEntry))
        {
            var memory = os.MemoryWindowController;
            if (memory?.CurrentData?.DataIndex != Chapter02UpgradeGate
                || !memory.IsCurrentTextComplete || !memory.IsWaitingForToolRepair
                || character.IsSignalOutActive) return;
            if (returnSignalPending && character.IsSignalInActive) return;

            // Earlier saves ended this sequence on departure. Resume at the new beat.
            if (interaction.isCompleted)
            {
                interaction.isCompleted = false;
                interaction.phase = OSCharacterInteractionSavePhase.WaitingForTrigger;
            }
            returnSignalPending = true;
            os.OpenCharacterWindowForReconnect(0f);
            character.PlaySignalIn(() =>
            {
                if (!returnSignalPending || GameDataManager.Instance?.CurrentChapterIndex != 2
                    || !ReferenceEquals(interaction, GameDataManager.Instance.OSCharacterInteractionState))
                    return;
                recoveryMessages?.TryPresentEntry(ReturnArrivedEntry);
                returnSignalPending = false;
            });
            return;
        }

        if (returnReactionDeadline >= 0f && Time.unscaledTime >= returnReactionDeadline)
        {
            if (recoveryMessages.TryCompleteAwaitedEntry(RecoverySequence, ReturnReactionEntry))
                returnReactionDeadline = -1f;
            return;
        }

        if (returnWorkDeadline >= 0f && Time.unscaledTime >= returnWorkDeadline)
        {
            if (recoveryMessages.TryCompleteAwaitedEntry(RecoverySequence, ReturnWorkingEntry))
            {
                returnWorkDeadline = -1f;
                recoveryMessages.TryPresentEntry(ReturnReadyEntry);
            }
            return;
        }

        // Recover between two messages as well as from a saved open message.
        if (!recoveryMessages.IsPresenting && string.IsNullOrEmpty(interaction.presentingEntryId))
        {
            if (interaction.HasCompletedEntry(ReturnWorkingEntry)
                && !interaction.HasConsumedEntry(ReturnReadyEntry))
                recoveryMessages.TryPresentEntry(ReturnReadyEntry);
            else if (interaction.HasCompletedEntry(ReturnArrivedEntry)
                && !interaction.HasConsumedEntry(ReturnWorkingEntry))
                recoveryMessages.TryPresentEntry(ReturnWorkingEntry);
        }
    }

    void OnReturnEntryStarted(string sequenceId, OSCharacterInteractionEntryData entry)
    {
        if (sequenceId != RecoverySequence || entry == null) return;
        if (entry.EntryId != ReturnArrivedEntry && entry.EntryId != ReturnWorkingEntry
            && entry.EntryId != ReturnReadyEntry && entry.EntryId != ReturnReactionEntry) return;
        returnPresentedEntry = entry.EntryId;
        var character = os?.CharacterWindowController;
        if (character == null) return;
        // The saved camera can be No Signal when a protected message resumes.
        if (character.CurrentState != OSCharacterWindowState.Anima)
            character.ApplyStateImmediate(OSCharacterWindowState.Anima);
        if (entry.EntryId == ReturnArrivedEntry || entry.EntryId == ReturnWorkingEntry)
        {
            var animation = entry.EntryId == ReturnArrivedEntry
                ? OSCharacterExtendedAnimation.Expend02 : OSCharacterExtendedAnimation.Expend01;
            if (character.SetExtendedAnimation(animation)) returnOwnedAnimation = animation;
        }
        else if (entry.EntryId == ReturnReadyEntry)
            ReleaseReturnAnimation();
    }

    void OnReturnTextCompleted(string sequenceId, OSCharacterInteractionEntryData entry)
    {
        if (sequenceId != RecoverySequence || entry == null) return;
        if (entry.EntryId == ReturnReactionEntry)
        {
            if (returnReactionDeadline < 0f) returnReactionDeadline = Time.unscaledTime + 3f;
            return;
        }
        if (entry.EntryId != ReturnWorkingEntry) return;
        recoveryMessages.StartDisplayedTrailingDotsLoop(sequenceId, entry.EntryId, .35f);
        // Localization refreshes rebuild the text without extending the work period.
        if (returnWorkDeadline < 0f) returnWorkDeadline = Time.unscaledTime + 4f;
    }

    void OnReturnEntryCompleted(string sequenceId, OSCharacterInteractionEntryData entry)
    {
        if (sequenceId != RecoverySequence || entry == null) return;
        if (entry.EntryId == ReturnReactionEntry)
        {
            returnReactionDeadline = -1f;
            return;
        }
        if (entry.EntryId != ReturnWorkingEntry) return;
        returnWorkDeadline = -1f;
        ReleaseReturnAnimation();
        var tool = os?.ToolWindowController?.GetComponent<OSWindowUnit>();
        if (tool != null && tool.IsOpen) tool.Close();
        os?.SetDesktopProcessNew(OSDesktopProcessKeys.Tool, true);
    }

    void ReleaseReturnAnimation()
    {
        var character = os?.CharacterWindowController;
        if (returnOwnedAnimation != OSCharacterExtendedAnimation.None && character != null
            && character.CurrentExtendedAnimation == returnOwnedAnimation)
            character.StopExtendedAnimation();
        returnOwnedAnimation = OSCharacterExtendedAnimation.None;
    }

    void ResetChapter02ReturnPresentation()
    {
        if (returnSignalPending && os?.CharacterWindowController?.IsSignalInActive == true)
            os.CharacterWindowController.ApplyStateImmediate(OSCharacterWindowState.VoidMessage);
        returnSignalPending = false;
        returnWorkDeadline = -1f;
        returnReactionDeadline = -1f;
        returnPresentedEntry = null;
        ReleaseReturnAnimation();
    }
}
