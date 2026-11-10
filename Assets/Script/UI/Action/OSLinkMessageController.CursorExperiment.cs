using System;

public sealed partial class OSLinkMessageController
{
    bool IsChapter02FinalTalkOptionGestureBlocked()
        => activeEntry != null && ((OSChapter02FinalTalkDirector.IsOwnEntry(activeEntry.EntryId)
            && OS_Handle.Instance?.Chapter02FinalTalk?.IsOptionGestureBlocked == true)
            || (OSChapter03EntryDirector.IsOwnEntry(activeEntry.EntryId)
            && OS_Handle.Instance?.Chapter03Entry?.IsOptionGestureBlocked == true)
            || (OSChapter04EntryDirector.IsOwnEntry(activeEntry.EntryId)
            && OS_Handle.Instance?.Chapter04Entry?.IsOptionGestureBlocked == true)
            || (OSChapter04StoryDirector.IsOwnEntry(activeEntry.EntryId)
            && OS_Handle.Instance?.Chapter04Story?.IsOptionGestureBlocked == true));

    bool TryGetCursorExperimentReservedEntry(out string entryId)
    {
        entryId = null;
        var data = GameDataManager.Instance;
        var chapter04 = OS_Handle.Instance?.Chapter04Entry;
        if (chapter04 != null && chapter04.IsEngaged)
        {
            entryId = chapter04.ReservedEntryId ?? string.Empty;
            return true;
        }
        var chapter03 = OS_Handle.Instance?.Chapter03Entry;
        var story04 = OS_Handle.Instance?.Chapter04Story;
        if (story04 != null && story04.IsActive)
        { entryId = story04.ReservedEntryId ?? string.Empty; return true; }
        if (chapter03 != null && chapter03.IsEngaged)
        {
            entryId = chapter03.ReservedEntryId ?? string.Empty;
            return true;
        }
        var story03 = OS_Handle.Instance?.Chapter03Story;
        if (story03 != null && story03.IsActive)
        { entryId = story03.ReservedEntryId ?? string.Empty; return true; }
        // The rematch keeps owning the seated cursor at HeartTalkHold, but its
        // empty cue queue must not replace the final conversation's reservation.
        var finalTalk = OS_Handle.Instance?.Chapter02FinalTalk;
        if (finalTalk != null && finalTalk.IsEngaged)
        {
            entryId = finalTalk.ReservedEntryId ?? string.Empty;
            return true;
        }
        var paperPush = OS_Handle.Instance?.Chapter02PaperPush;
        if (paperPush != null && paperPush.IsStoryProtected)
        {
            entryId = paperPush.ReservedEntryId ?? string.Empty;
            return true;
        }
        var finale = OS_Handle.Instance?.Chapter02Finale;
        if (finale != null && finale.IsEngaged)
        {
            entryId = finale.ReservedEntryId ?? string.Empty;
            return true;
        }
        var heartTalk = OS_Handle.Instance?.CursorHeartTalk;
        if (heartTalk != null && heartTalk.IsEngaged)
        {
            entryId = heartTalk.ReservedEntryId ?? string.Empty;
            return true;
        }
        var release = OS_Handle.Instance?.CursorRelease;
        if (release != null && release.IsEngaged)
        {
            entryId = release.ReservedEntryId ?? string.Empty;
            return true;
        }
        var p = data != null ? data.OSProcessData.memory : null;
        if (data == null || data.CurrentChapterIndex != 2 || p == null || !p.chapter02ClawCursorCaptured
            || p.chapter02CursorExperimentRound >= 4) return false;
        int step = p.chapter02CursorStoryStep;
        if (step <= 0)
        {
            // Match the director's legacy-save migration: an already-running
            // test does not rewind to its unrecorded first interrogation.
            bool alreadyTesting = p.chapter02CursorExperimentRound > 1
                || (p.chapter02CursorExperimentRound == 1
                    && p.chapter02CursorExperimentPhase != (int)OSCursorExperimentController.ExperimentPhase.Intake
                    && p.chapter02CursorExperimentPhase != (int)OSCursorExperimentController.ExperimentPhase.Inactive);
            if (alreadyTesting) return false;
            step = 1;
        }
        string suffix;
        switch (step)
        {
            case OSCursorExperimentStoryDirector.AssistanceStep: suffix = "assisted_release"; break;
            case OSCursorExperimentStoryDirector.PersistenceReactionStep: suffix = "persistence"; break;
            case 1: suffix = "caught"; break;
            case 2: suffix = "study"; break;
            case 3: suffix = "question"; break;
            case 4: suffix = p.chapter02CursorStoryAnswer == 1 ? "not_bug"
                    : p.chapter02CursorStoryAnswer == 2 ? "human" : "silent"; break;
            case 5: suffix = "tether"; break;
            case 6: suffix = "first_test"; break;
            case OSCursorExperimentStoryDirector.SafetyQuestionStep: suffix = "safety_question"; break;
            case OSCursorExperimentStoryDirector.SafetyReplyStep:
                suffix = p.chapter02CursorStorySafetyAnswer == 1 ? "safety_deny" : "safety_confirm"; break;
            case OSCursorExperimentStoryDirector.SecondTestQuestionStep: suffix = "second_question"; break;
            case OSCursorExperimentStoryDirector.SecondTestReplyStep:
                suffix = p.chapter02CursorStorySecondTestAnswer == 1 ? "second_deny" : "second_confirm"; break;
            case OSCursorExperimentStoryDirector.SecondTestRulesStep: suffix = "second_test"; break;
            case OSCursorExperimentStoryDirector.ThirdTestReturnStep: suffix = "third_return"; break;
            case OSCursorExperimentStoryDirector.ThirdTestQuestionStep: suffix = "third_question"; break;
            case OSCursorExperimentStoryDirector.ThirdTestReplyStep:
                suffix = p.chapter02CursorStoryThirdTestAnswer == 1 ? "third_deny" : "third_confirm"; break;
            case OSCursorExperimentStoryDirector.ThirdTestPromiseStep: suffix = "third_promise"; break;
            case OSCursorExperimentStoryDirector.ThirdTestRulesStep: suffix = "third_test"; break;
            default: return false;
        }
        entryId = "cursor_test_ch02_" + suffix;
        return true;
    }

    bool AllowsCursorExperimentEntry(OSCharacterInteractionEntryData entry)
        => !TryGetCursorExperimentReservedEntry(out string reserved)
            || (entry != null && string.Equals(entry.EntryId, reserved, StringComparison.Ordinal));

    /// <summary>
    /// Reserve the current mainline slot without advancing or consuming
    /// any entry. Old desktop hints cannot survive in front of the current line.
    /// </summary>
    public void MaintainCursorExperimentMessageReservation()
    {
        if (!TryGetCursorExperimentReservedEntry(out string reserved)) return;
        var state = GetSaveState();
        if (state == null) return;

        bool currentDisplayed = hasDisplayedMessage && displayedEntryIsGlobal
            && string.Equals(displayedEntryId, reserved, StringComparison.Ordinal);
        bool currentActive = IsPresenting && activePresentationIsGlobal
            && string.Equals(activePresentationEntryId, reserved, StringComparison.Ordinal);
        bool emptyChapter04Shell = OS_Handle.Instance?.Chapter04Story?.IsActive == true
            && !hasDisplayedMessage && !IsPresenting;
        bool staleVisual = (hasDisplayedMessage && !currentDisplayed) || (IsPresenting && !currentActive)
            || (linkWindow != null && linkWindow.IsOpen && !currentDisplayed && !currentActive && !emptyChapter04Shell);

        // Cancel transient desktop work, not its consumed/completed history or
        // resource receipts. The selected experiment line remains resumable.
        KillPendingSequenceTween();
        if (!string.IsNullOrEmpty(state.presentingEntryId) || !string.IsNullOrEmpty(state.pendingEntryId))
            state.CancelPresentation();
        if (!string.IsNullOrEmpty(state.pendingGlobalEntryId)
            && !string.Equals(state.pendingGlobalEntryId, reserved, StringComparison.Ordinal))
        {
            KillPendingGlobalTween();
            state.pendingGlobalEntryId = null;
        }
        if (!string.IsNullOrEmpty(state.presentingGlobalEntryId)
            && !string.Equals(state.presentingGlobalEntryId, reserved, StringComparison.Ordinal))
            state.presentingGlobalEntryId = null;
        if (reconnectEntry != null && !string.Equals(reconnectEntry.EntryId, reserved, StringComparison.Ordinal))
        {
            reconnectEntry = null;
            if (ownerCharacterWindow != null)
                ownerCharacterWindow.OpenAnimationCompleted -= HandleReconnectOpenAnimationCompleted;
        }
        KillReconnectAnimationTween();
        if (!staleVisual) return;
        // Do not invoke interruption options, Closed reactions or auto-advance.
        StopVisualPresentation(true);
        CloseLinkWindowWithoutCallback(true);
    }
}
