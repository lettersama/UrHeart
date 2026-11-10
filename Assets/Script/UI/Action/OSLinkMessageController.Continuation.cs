using UnityEngine;

public sealed partial class OSLinkMessageController
{
    // Clicking the chapter card (or its scripted hover timeout) advances this
    // guidance. The superseded hover line has no auto-close, so queuing its
    // successor behind it would otherwise require an unrelated click on X.
    bool TryAdvanceChapter01RecordGuidance(string sequenceId,
        OSCharacterInteractionEntryData incoming, OSCharacterInteractionSaveData state)
    {
        if (!CanAdvanceChapter01RecordGuidance(sequenceId, incoming.EntryId, state))
            return false;

        state.optionCountdownQueue.RemoveAll(item => item != null && item.sequenceId == sequenceId
            && (IsChapter01RecordGuidanceHint(item.entryId) || item.entryId == incoming.EntryId));
        if (IsChapter01RecordGuidanceHint(state.pendingEntryId))
        {
            KillPendingSequenceTween();
            state.SetPendingEntry(null);
        }
        if (state.optionCountdownReleaseKey == OptionCountdownKey(sequenceId,
                OSCharacterInteractionIds.Chapter01Entries.StateCheckMemoryDamaged)
            || state.optionCountdownReleaseKey == OptionCountdownKey(sequenceId,
                OSCharacterInteractionIds.Chapter01Entries.RecordHover)
            || state.optionCountdownReleaseKey == OptionCountdownKey(sequenceId,
                OSCharacterInteractionIds.Chapter01Entries.RecordHoverTimedOut))
            state.optionCountdownReleaseKey = null;
        return true;
    }

    bool CanAdvanceChapter01RecordGuidance(string sequenceId, string entryId,
        OSCharacterInteractionSaveData state)
        => sequenceId == OSCharacterInteractionIds.Chapter01LiveReconnect
            && state.activeSequenceId == sequenceId
            && (entryId == OSCharacterInteractionIds.Chapter01Entries.RecordStartPrompt
                || entryId == OSCharacterInteractionIds.Chapter01Entries.RecordHoverTimedOut)
            && (!hasDisplayedMessage || (!displayedEntryIsGlobal && displayedSequenceId == sequenceId
                && IsChapter01RecordGuidanceHint(displayedEntryId)));

    static bool IsChapter01RecordGuidanceHint(string entryId)
        => entryId == OSCharacterInteractionIds.Chapter01Entries.StateCheckMemoryDamaged
            || entryId == OSCharacterInteractionIds.Chapter01Entries.RecordHover
            || entryId == OSCharacterInteractionIds.Chapter01Entries.RecordHoverTimedOut;

    // These sequence choices drive progression through option callbacks as well
    // as NextEntryId. Optional memory commentary keeps its existing dismissal.
    public bool IsProgressionChoicePending
    {
        get
        {
            if (!TryGetActiveSequence(out _, out var state, out var sequence)
                || !sequence.TryGetEntry(state.presentingEntryId, out var entry)
                || entry.PartType != OSCharacterInteractionPartType.TextWithOptions
                || entry.AllowUnansweredTimeout || entry.SelectTimeoutOptionOnInterrupt)
                return false;

            return sequence.SequenceId == OSCharacterInteractionIds.PrologueEvent01HeartHeal
                || sequence.SequenceId == OSCharacterInteractionIds.PrologueEvent01HeartHealEnding
                || sequence.SequenceId == OSCharacterInteractionIds.Chapter01LiveReconnect
                || sequence.SequenceId == "chapter02.recovery"
                || (sequence.SequenceId == OSCharacterInteractionIds.Chapter01MemoryRepair
                    && entry.EntryId == OSCharacterInteractionIds.Chapter01Tool.FilesFound);
        }
    }

    bool TryDismissOptionalMemoryQuestion()
    {
        if (!TryGetActiveSequence(out _, out var state, out var sequence)
            || sequence.SequenceId != OSCharacterInteractionIds.Chapter01MemoryRepair
            || !sequence.TryGetEntry(state.presentingEntryId, out var entry)
            || entry.PartType != OSCharacterInteractionPartType.TextWithOptions
            || IsProgressionChoicePending || entry.SelectTimeoutOptionOnInterrupt)
            return false;

        // These optional recollections may award resources only on an answer.
        // Closing them gives no reward, but must release both the saved question
        // and any later guide waiting in the legacy single pending slot.
        PreservePendingSequenceMessage(sequence, entry, state);
        ClearOptionCountdown(sequence.SequenceId, entry.EntryId);
        state.CancelPresentation();
        return true;
    }

    static bool HasTimedContinuation(OSCharacterInteractionEntryData entry)
        => entry != null && entry.PartType == OSCharacterInteractionPartType.Text
            && !string.IsNullOrWhiteSpace(entry.NextEntryId);

    void ArmNextEntryCountdown(OSCharacterInteractionSequenceData sequence,
        OSCharacterInteractionEntryData entry)
    {
        var state = GetSaveState();
        if (state == null || sequence == null || !HasTimedContinuation(entry)
            || state.activeSequenceId != sequence.SequenceId || state.presentingEntryId != entry.EntryId)
            return;
        string key = OptionCountdownKey(sequence.SequenceId, entry.EntryId);
        if (state.nextEntryCountdownKey == key) return;
        state.nextEntryCountdownKey = key;
        state.nextEntryCountdownRemaining = entry.NextEntryDelay;
    }

    bool PreserveNextEntryCountdown()
    {
        if (!TryGetActiveSequence(out _, out var state, out var sequence)
            || !sequence.TryGetEntry(state.presentingEntryId, out var entry)
            || !HasTimedContinuation(entry)) return false;
        // Dismissing during the typewriter starts the same authored wait. If
        // it already started, neither closing nor reopening resets its clock.
        ArmNextEntryCountdown(sequence, entry);
        return true;
    }

    // Called by the persistent OS owner, including while Link/Live is inactive.
    internal void RefreshNextEntryCountdown()
    {
        var state = GetSaveState();
        if (state == null || string.IsNullOrEmpty(state.nextEntryCountdownKey)) return;
        if (!TryGetActiveSequence(out var data, out _, out var sequence)
            || !sequence.TryGetEntry(state.presentingEntryId, out var entry)
            || !HasTimedContinuation(entry)
            || state.nextEntryCountdownKey != OptionCountdownKey(sequence.SequenceId, entry.EntryId))
        {
            state.nextEntryCountdownKey = null;
            state.nextEntryCountdownRemaining = 0f;
            return;
        }
        if (!isOSVisible || OSSessionPersistence.IsRestoring || GameSessionFlow.Instance?.IsBusy == true
            || GameTestConsole.BlocksGameplayInput || IsOptionCountdownSettingsOpen()
            || IsSuspendedByMinimize() || (!Application.isBatchMode && !Application.isFocused)) return;

        state.nextEntryCountdownRemaining = Mathf.Max(0f,
            state.nextEntryCountdownRemaining - Time.unscaledDeltaTime);
        if (state.nextEntryCountdownRemaining > 0f
            || (linkWindow != null && linkWindow.IsClosing)
            || (ownerCharacterWindow != null && ownerCharacterWindow.IsClosing)) return;
        if (!sequence.TryGetEntry(entry.NextEntryId, out var nextEntry)) return;

        // Reserve before notifying completion listeners, which may themselves
        // enqueue an ambient message. This also survives the next TriggerDelay.
        string continuationKey = OptionCountdownKey(sequence.SequenceId, nextEntry.EntryId);
        ReleasingCountdownMessageKey = continuationKey;
        state.nextEntryCountdownKey = null;
        StopDisplayedTrailingDotsLoop(true);
        if (activePresentationEntryId == entry.EntryId && !activePresentationIsGlobal)
            CompleteActivePresentation(entry.EntryId, presentationVersion, false);
        else
        {
            state.CompletePresentation(entry.EntryId, !entry.Repeatable);
            state.SetPendingEntry(null);
            EntryPresentationCompleted?.Invoke(sequence.SequenceId, entry);
            SyncEvent01TutorialSuppression();
            UpdateSequenceCompletion(sequence, state);
        }
        // A completion listener may legitimately move to a different sequence.
        if (state != GetSaveState() || state.activeSequenceId != sequence.SequenceId
            || !CanPresentSequenceEntry(nextEntry, data, state))
        {
            if (state.optionCountdownReleaseKey == continuationKey) state.optionCountdownReleaseKey = null;
            return;
        }
        ScheduleOrPresentSequenceEntry(sequence, nextEntry);
        if (!HasMessageContent(nextEntry))
        {
            StopVisualPresentation(true);
            CloseLinkWindowWithoutCallback();
        }
    }
}
