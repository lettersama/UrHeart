using UnityEngine;

public sealed partial class OSLinkMessageController
{
    [Header("Optional question countdown")]
    [SerializeField, Min(1f)] float optionalQuestionTimeout = 20f;
    bool startingCountdownReply;
    bool presentingOptionReply;
    string ReleasingCountdownMessageKey
    {
        get => GetSaveState()?.optionCountdownReleaseKey;
        set { var state = GetSaveState(); if (state != null) state.optionCountdownReleaseKey = value; }
    }
    OSWindowUnit optionCountdownSettings;

    // Story order is supplied by the content, not inferred from an option's reply
    // branches or the physical order of entries in the data asset.
    public static bool HasUpcomingOptionMessage(OSCharacterInteractionEntryData entry,
        OSCharacterInteractionData_SO data, OSCharacterInteractionSequenceData sequence = null)
    {
        if (entry == null || data == null || entry.FollowingMessageEntryIds == null) return false;
        int count = Mathf.Min(3, entry.FollowingMessageEntryIds.Count);
        for (int i = 0; i < count; i++)
        {
            string id = entry.FollowingMessageEntryIds[i];
            OSCharacterInteractionEntryData next = null;
            if (sequence == null || !sequence.TryGetEntry(id, out next))
                data.TryGetGlobalEntry(id, out next);
            if (next != null && next.PartType == OSCharacterInteractionPartType.TextWithOptions)
                return true;
        }
        return false;
    }

    bool CanExpireUnanswered(OSCharacterInteractionEntryData entry,
        OSCharacterInteractionSequenceData sequence)
        => entry != null && entry.PartType == OSCharacterInteractionPartType.TextWithOptions
            && entry.AllowUnansweredTimeout
            && HasUpcomingOptionMessage(entry, GameDataManager.Instance?.OSCharacterInteractionData, sequence);

    float GetOptionCountdownDuration(OSCharacterInteractionEntryData entry,
        OSCharacterInteractionSequenceData sequence)
    {
        if (entry == null || entry.PartType != OSCharacterInteractionPartType.TextWithOptions) return 0f;
        if (OSChapter03StoryDirector.IsOwnEntry(entry.EntryId) && entry.EntryId.EndsWith("_offer")
            && entry.AllowUnansweredTimeout) return Mathf.Max(1f, entry.OptionTimeoutDelay);
        // Preserve explicitly authored timeout choices (for example, declining
        // the optional chapter 1 decoration). They now share the visible timer.
        if (entry.OptionTimeoutDelay > 0f && !string.IsNullOrEmpty(entry.TimeoutOptionId))
            return entry.OptionTimeoutDelay;
        return CanExpireUnanswered(entry, sequence) ? Mathf.Max(1f, optionalQuestionTimeout) : 0f;
    }

    static string OptionCountdownKey(string sequenceId, string entryId) => sequenceId + "/" + entryId;

    bool HasCountdownReplyHold(OSCharacterInteractionSaveData state)
    {
        string key = state?.optionCountdownReplyKey;
        if (string.IsNullOrEmpty(key)) return false;
        return (key == OptionCountdownKey(displayedSequenceId, displayedEntryId)
                && linkWindow != null && linkWindow.IsOpen)
            || key == OptionCountdownKey(GlobalPresentationSequenceId, state.presentingGlobalEntryId)
            || key == OptionCountdownKey(state.activeSequenceId, state.presentingEntryId);
    }

    void PrepareCountdownMessageReservation(string sequenceId, string entryId)
    {
        var state = GetSaveState();
        if (state == null) return;
        string key = OptionCountdownKey(sequenceId, entryId);
        if (startingCountdownReply) state.optionCountdownReplyKey = key;
        else if (state.optionCountdownReplyKey != key) state.optionCountdownReplyKey = null;
        if (state.optionCountdownReleaseKey == key) state.optionCountdownReleaseKey = null;
    }

    void PrepareOptionCountdown()
    {
        var state = GetSaveState();
        if (state == null || GetOptionCountdownDuration(activeEntry, activeSequence) <= 0f) return;
        string key = OptionCountdownKey(displayedSequenceId, activeEntry.EntryId);
        if (state.optionCountdownKey == key) return;
        state.optionCountdownKey = key;
        state.optionCountdownElapsed = 0f;
    }

    void ClearOptionCountdown(string sequenceId, string entryId)
    {
        var state = GetSaveState();
        if (state == null || state.optionCountdownKey != OptionCountdownKey(sequenceId, entryId)) return;
        state.optionCountdownKey = null;
        state.optionCountdownElapsed = 0f;
    }

    void PreservePendingCountdownMessage(OSCharacterInteractionSequenceData sequence,
        OSCharacterInteractionEntryData completedEntry)
    {
        var state = GetSaveState();
        if (sequence == null || state == null || !CanExpireUnanswered(completedEntry, sequence)
            || string.IsNullOrEmpty(state.pendingEntryId)) return;
        PreservePendingSequenceMessage(sequence, completedEntry, state);
    }

    void PreservePendingSequenceMessage(OSCharacterInteractionSequenceData sequence,
        OSCharacterInteractionEntryData completedEntry, OSCharacterInteractionSaveData state)
    {
        if (sequence == null || state == null || string.IsNullOrEmpty(state.pendingEntryId)) return;
        string pendingId = state.pendingEntryId;
        if (!sequence.TryGetEntry(pendingId, out var pending) || pending.EntryId == completedEntry.EntryId) return;
        // This older single-slot message predates any arrivals in our FIFO.
        state.optionCountdownQueue.RemoveAll(item => item != null
            && item.sequenceId == sequence.SequenceId && item.entryId == pendingId);
        state.optionCountdownQueue.Insert(0, new OSCharacterDeferredMessageSaveData
        {
            sequenceId = sequence.SequenceId,
            entryId = pendingId
        });
        state.SetPendingEntry(null);
    }

    bool TryGetCountdownQuestion(out string sequenceId, out OSCharacterInteractionEntryData question)
    {
        sequenceId = null;
        question = null;
        var state = GetSaveState();
        var data = GameDataManager.Instance?.OSCharacterInteractionData;
        if (state == null || data == null) return false;
        if (data.TryGetGlobalEntry(state.presentingGlobalEntryId, out question)
            && MatchesChapter(question, GameDataManager.Instance)
            && CanExpireUnanswered(question, null))
        {
            sequenceId = GlobalPresentationSequenceId;
            return true;
        }
        if (data.TryGetSequence(state.activeSequenceId, out var sequence)
            && sequence.TryGetEntry(state.presentingEntryId, out question)
            && MatchesChapter(question, GameDataManager.Instance)
            && CanExpireUnanswered(question, sequence))
        {
            sequenceId = sequence.SequenceId;
            return true;
        }
        question = null;
        return false;
    }

    bool TryDeferForOptionCountdown(string incomingSequenceId, OSCharacterInteractionEntryData incoming)
    {
        var state = GetSaveState();
        if (incoming == null || state == null || presentingOptionReply
            || ReleasingCountdownMessageKey == OptionCountdownKey(incomingSequenceId, incoming.EntryId)) return false;
        // This line belongs to the active swatting choreography. Deferring it
        // until input unlocks would make it miss the hunt's completion signal.
        if (incomingSequenceId == "chapter02.recovery" && incoming.EntryId == "chapter02_recovery_bugs"
            && OS_Handle.Instance?.Chapter01Ending?.IsPresenting == true) return false;
        bool hasQuestion = TryGetCountdownQuestion(out var sequenceId, out var question);
        if (hasQuestion && sequenceId == incomingSequenceId && question.EntryId == incoming.EntryId) return false;
        if (!hasQuestion && TryAdvanceChapter01RecordGuidance(incomingSequenceId, incoming, state)) return false;
        // Reuse the saved FIFO for all visible timed reading holds, including
        // plain messages whose text has already completed and timed hand-offs.
        bool timedReading = IsReadingHoldActive || (hasDisplayedMessage
            && state.nextEntryCountdownKey == OptionCountdownKey(displayedSequenceId, displayedEntryId)
            && state.nextEntryCountdownRemaining > 0f);
        bool waitsForCurrent = incoming.ReplacementPolicy == OSCharacterInteractionReplacementPolicy.WaitForCurrentTextComplete;
        bool unfinishedText = waitsForCurrent && IsTextAnimating();
        bool unansweredChoice = waitsForCurrent && activeEntry != null
            && activeEntry.PartType == OSCharacterInteractionPartType.TextWithOptions;
        bool ownsPending = incomingSequenceId == GlobalPresentationSequenceId
            ? state.pendingGlobalEntryId == incoming.EntryId
            : incomingSequenceId == state.activeSequenceId && state.pendingEntryId == incoming.EntryId;
        bool pendingAhead = (!string.IsNullOrEmpty(state.pendingEntryId)
                && (incomingSequenceId != state.activeSequenceId || state.pendingEntryId != incoming.EntryId))
            || (!string.IsNullOrEmpty(state.pendingGlobalEntryId)
                && (incomingSequenceId != GlobalPresentationSequenceId || state.pendingGlobalEntryId != incoming.EntryId));
        if ((timedReading || unfinishedText || unansweredChoice)
            && incomingSequenceId == displayedSequenceId && incoming.EntryId == displayedEntryId)
            return true;
        // A scheduled head owns its TriggerDelay. Later arrivals cannot overwrite
        // its single pending slot or make its own callback rejoin the FIFO tail.
        if (ownsPending && !pendingAhead && !hasQuestion && !timedReading && !unfinishedText
            && !unansweredChoice && !HasCountdownReplyHold(state)) return false;
        if (!hasQuestion && !timedReading && !unfinishedText && !unansweredChoice && !pendingAhead
            && !HasCountdownReplyHold(state) && state.optionCountdownQueue.Count == 0
            && string.IsNullOrEmpty(ReleasingCountdownMessageKey)) return false;
        bool alreadyQueued = false;
        foreach (var queued in state.optionCountdownQueue)
            if (queued != null && queued.sequenceId == incomingSequenceId && queued.entryId == incoming.EntryId)
                alreadyQueued = true;
        if (!alreadyQueued)
            state.optionCountdownQueue.Add(new OSCharacterDeferredMessageSaveData
            {
                sequenceId = incomingSequenceId,
                entryId = incoming.EntryId
            });
        // The FIFO owns this deferred message. Do not leave a second copy in
        // the older single pending slot that could bypass the reading hold.
        if (incomingSequenceId == GlobalPresentationSequenceId && state.pendingGlobalEntryId == incoming.EntryId)
            state.pendingGlobalEntryId = null;
        else if (incomingSequenceId == state.activeSequenceId && state.pendingEntryId == incoming.EntryId)
            state.SetPendingEntry(null);
        return true;
    }

    void PresentCountdownReply(OSCharacterInteractionSequenceData sequence,
        OSCharacterInteractionEntryData reply, bool isGlobal, bool fromCountdown)
    {
        startingCountdownReply = fromCountdown;
        presentingOptionReply = true;
        try
        {
            if (isGlobal) PresentGlobalEntry(reply, false);
            else PresentSequenceEntry(sequence, reply, false);
        }
        finally { startingCountdownReply = false; presentingOptionReply = false; }
    }

    bool IsOptionCountdownSettingsOpen()
    {
        if (optionCountdownSettings == null)
            optionCountdownSettings = OS_Handle.Instance?.transform.Find("Windows/WindowUnit_Option")
                ?.GetComponent<OSWindowUnit>();
        return optionCountdownSettings != null && optionCountdownSettings.IsOpen
            && !optionCountdownSettings.IsMinimized && optionCountdownSettings.gameObject.activeInHierarchy;
    }

    void TickOptionCountdown()
    {
        if (!isWaitingForOption || activeEntry == null) return;
        float duration = GetOptionCountdownDuration(activeEntry, activeSequence);
        if (duration <= 0f) return;
        PrepareOptionCountdown();
        var state = GetSaveState();
        if (state == null) return;
        bool canTick = isOSVisible && !IsSuspendedByMinimize()
            && (Application.isBatchMode || Application.isFocused)
            && linkWindow != null && linkWindow.IsOpen && !linkWindow.IsOpening && !linkWindow.IsClosing
            && (ownerCharacterWindow == null || (ownerCharacterWindow.IsOpen && !ownerCharacterWindow.IsClosing))
            && !OSSessionPersistence.IsRestoring && GameSessionFlow.Instance?.IsBusy != true
            && !GameTestConsole.BlocksGameplayInput && OS_Handle.Instance?.IsSystemInputLocked != true
            && !globalMessagesSuppressed && !IsOptionCountdownSettingsOpen();
        if (canTick) state.optionCountdownElapsed += Time.unscaledDeltaTime;
        SetOptionCountdownProgress(1f - Mathf.Clamp01(state.optionCountdownElapsed / duration), true);
        if (!canTick || state.optionCountdownElapsed < duration) return;

        if (activeEntry.OptionTimeoutDelay > 0f && !string.IsNullOrEmpty(activeEntry.TimeoutOptionId))
        {
            TrySelectDisplayedOptionImmediately(displayedSequenceId, displayedEntryId, activeEntry.TimeoutOptionId);
            return;
        }
        // Finish with no OptionSelected event, no resource receipt and no reply.
        int version = presentationVersion;
        CompleteActivePresentation(activeEntry.EntryId, version, false);
        if (version == presentationVersion)
        {
            StopVisualPresentation(true);
            CloseLinkWindowWithoutCallback();
        }
    }

    // The OS owner calls this even when closing Link deactivates this component.
    internal void RefreshOptionCountdownQueue()
    {
        var state = GetSaveState();
        DiscardEmptySequencePlaceholders(state);
        // Older sessions may contain a dismissed optional question that still
        // owns the saved slot. A minimized question stays open and is preserved.
        if (isOSVisible && !IsPresenting && !OSSessionPersistence.IsRestoring
            && linkWindow != null && !linkWindow.IsOpen && !linkWindow.IsClosing)
            TryDismissOptionalMemoryQuestion();
        if (state == null || state.optionCountdownQueue.Count == 0 || !isOSVisible
            || IsPresenting || isWaitingForOption || IsWaitingForExternalCompletion
            || !string.IsNullOrEmpty(state.presentingGlobalEntryId)
            || !string.IsNullOrEmpty(state.presentingEntryId)
            || !string.IsNullOrEmpty(state.pendingGlobalEntryId) || !string.IsNullOrEmpty(state.pendingEntryId)
            // A completed plain line without an authored hold can yield its
            // still-open window. Requiring X here would strand typing arrivals.
            || IsReadingHoldActive || (linkWindow != null && (linkWindow.IsClosing || linkWindow.IsOpening))
            || OSSessionPersistence.IsRestoring || GameSessionFlow.Instance?.IsBusy == true
            || GameTestConsole.BlocksGameplayInput || OS_Handle.Instance?.IsSystemInputLocked == true
            || AreGlobalMessagesSuppressed || IsOptionCountdownSettingsOpen()) return;
        if (ownerCharacterWindow != null && (!ownerCharacterWindow.IsOpen || ownerCharacterWindow.IsMinimized
            || ownerCharacterWindow.IsOpening || ownerCharacterWindow.IsClosing)) return;

        var dataManager = GameDataManager.Instance;
        var data = dataManager?.OSCharacterInteractionData;
        if (data == null) return;
        while (state.optionCountdownQueue.Count > 0)
        {
            var queued = state.optionCountdownQueue[0];
            state.optionCountdownQueue.RemoveAt(0);
            if (queued == null) continue;
            if (queued.sequenceId == GlobalPresentationSequenceId)
            {
                if (!data.TryGetGlobalEntry(queued.entryId, out var entry)
                    || (!entry.Repeatable && state.HasConsumedGlobalEntry(entry.EntryId))
                    || !MatchesConditions(entry, dataManager, state)) continue;
                if (state.QueueGlobalEntry(entry.EntryId, entry.Repeatable))
                {
                    ReleasingCountdownMessageKey = OptionCountdownKey(queued.sequenceId, queued.entryId);
                    ScheduleGlobalEntry(entry);
                }
                return;
            }
            if (queued.sequenceId != state.activeSequenceId
                || !data.TryGetSequence(queued.sequenceId, out var sequence)
                || !sequence.TryGetEntry(queued.entryId, out var next)
                || !CanPresentSequenceEntry(next, dataManager, state)) continue;
            ReleasingCountdownMessageKey = OptionCountdownKey(queued.sequenceId, queued.entryId);
            ScheduleOrPresentSequenceEntry(sequence, next);
            return;
        }
    }

    void DiscardEmptySequencePlaceholders(OSCharacterInteractionSaveData state)
    {
        if (state == null || (string.IsNullOrEmpty(state.pendingEntryId)
            && string.IsNullOrEmpty(state.optionCountdownReleaseKey)
            && state.optionCountdownQueue.Count == 0)) return;
        var data = GameDataManager.Instance?.OSCharacterInteractionData;
        if (data == null
            || !data.TryGetSequence(state.activeSequenceId, out var sequence)) return;
        // Repair saves made while the old blank tool-install hint occupied a
        // pending slot or the FIFO's release reservation.
        if (sequence.TryGetEntry(state.pendingEntryId, out var pending) && IsEmptySequencePlaceholder(pending))
        {
            KillPendingSequenceTween();
            state.SetPendingEntry(null);
        }
        state.optionCountdownQueue.RemoveAll(item => item != null
            && item.sequenceId == sequence.SequenceId
            && sequence.TryGetEntry(item.entryId, out var queued) && IsEmptySequencePlaceholder(queued));
        if (!string.IsNullOrEmpty(state.optionCountdownReleaseKey))
            foreach (var entry in sequence.Entries)
                if (entry != null
                    && state.optionCountdownReleaseKey == OptionCountdownKey(sequence.SequenceId, entry.EntryId)
                    && IsEmptySequencePlaceholder(entry))
                {
                    state.optionCountdownReleaseKey = null;
                    break;
                }
    }
}
