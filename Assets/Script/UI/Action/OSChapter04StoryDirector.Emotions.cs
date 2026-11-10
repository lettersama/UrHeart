using UnityEngine;

/// <summary>
/// Live's present-day emotion follows the saved conversation, independently of
/// the TV's recollection. Only the entry gestures are transient; loading a line
/// goes straight to its stable expression without replaying its emotional rise.
/// </summary>
public sealed partial class OSChapter04StoryDirector
{
    enum LiveEmotionCue { Sad, Teary, Cry }

    OSCharacterWindowController liveEmotionCharacter;
    string liveEmotionEntryId;
    int liveEmotionVisit = -1;
    bool liveEmotionRestorePending = true;
    bool liveEmotionCryReached;

    bool OwnsChapter04LiveEmotion => isActiveAndEnabled && !GameSetup.IsDemo
        && GameDataManager.Instance?.CurrentChapterIndex == 4;

    // Chapter-four installation begins before IsActive. Keep the settled grief
    // during that entrance; chapter-three cleanup cannot release this owner.
    void ApplyEntryLiveEmotion()
    {
        if (!OwnsChapter04LiveEmotion)
        { ReleaseLiveEmotion(); return; }
        if (OSSessionPersistence.IsRestoring) return;
        SetLiveEmotion(OSCharacterExtendedAnimation.Breakdown02);
    }

    void ApplyLiveEmotionForCurrentLine()
    {
        if (!OwnsChapter04LiveEmotion || State == null)
        { ReleaseLiveEmotion(); return; }
        if (OSSessionPersistence.IsRestoring || !ReferenceEquals(observed, State)) return;

        bool instant = liveEmotionRestorePending;
        // The same owner spans the warning, treatment and epilogue. Clearing
        // grief screen effects never implicitly clears the character's tears.
        switch (FinalTreatmentStage)
        {
            case OSChapter04FinalTreatmentStage.Completed:
                ReleaseLiveEmotionCore(instant || liveEmotionCharacter == null);
                return;
            case OSChapter04FinalTreatmentStage.Ending:
                SetLiveEmotion(OSCharacterExtendedAnimation.Breakdown02, instant);
                liveEmotionRestorePending = false;
                return;
            case OSChapter04FinalTreatmentStage.WaitingForWarning:
            case OSChapter04FinalTreatmentStage.FailureWarning:
            case OSChapter04FinalTreatmentStage.Pending:
            case OSChapter04FinalTreatmentStage.Opened:
            case OSChapter04FinalTreatmentStage.FinalHeal:
                SetLiveEmotion(OSCharacterExtendedAnimation.Breakdown01, instant);
                liveEmotionRestorePending = false;
                return;
        }

        string entryId = State.currentEntryId;
        LiveEmotionCue cue = ResolveLiveEmotionCue(entryId);
        if (entryId != liveEmotionEntryId || State.currentVisit != liveEmotionVisit)
        {
            bool restoring = liveEmotionRestorePending || State.currentTextCompleted
                || HasLiveEmotionHistory(entryId, State.currentVisit);
            instant |= restoring;
            liveEmotionCryReached = restoring;
            liveEmotionEntryId = entryId;
            liveEmotionVisit = State.currentVisit;
            liveEmotionRestorePending = false;
        }

        OSCharacterExtendedAnimation animation;
        switch (cue)
        {
            case LiveEmotionCue.Teary:
                animation = OSCharacterExtendedAnimation.Breakdown00;
                break;
            case LiveEmotionCue.Cry:
                // Grave 10-16 has already built up through the preceding tears.
                // The three earlier peaks rise during their own actual text,
                // so pauses, skipped typing and localization share one clock.
                if (!IsGradualCryEntry(entryId) || State.currentTextCompleted || ReachedLiveCryText(entryId))
                    liveEmotionCryReached = true;
                animation = liveEmotionCryReached ? OSCharacterExtendedAnimation.Breakdown01
                    : OSCharacterExtendedAnimation.Breakdown00;
                break;
            default:
                animation = OSCharacterExtendedAnimation.Breakdown02;
                break;
        }
        SetLiveEmotion(animation, instant);
    }

    bool HasLiveEmotionHistory(string entryId, int visit)
    {
        if (State.history == null) return false;
        foreach (var record in State.history)
            if (record != null && record.entryId == entryId && record.visit == visit) return true;
        return false;
    }

    bool ReachedLiveCryText(string entryId)
    {
        if (messages == null || messages.DisplayedEntryId != entryId || !messages.IsPresenting) return false;
        var label = messages.Chapter04PlaybackLabel;
        if (label == null || label.textInfo == null || label.textInfo.characterCount <= 0) return false;
        float threshold = entryId == EntryPrefix + "31" ? .45f : .55f;
        return label.maxVisibleCharacters >= Mathf.CeilToInt(label.textInfo.characterCount * threshold);
    }

    static bool IsGradualCryEntry(string entryId) => entryId == EntryPrefix + "08"
        || entryId == EntryPrefix + "25a" || entryId == EntryPrefix + "31";

    static LiveEmotionCue ResolveLiveEmotionCue(string entryId)
    {
        if (!IsOwnEntry(entryId)) return LiveEmotionCue.Sad;
        switch (entryId.Substring(EntryPrefix.Length))
        {
            case "02": case "18": case "24a": case "40": case "41":
            case "grave_06": case "grave_07": case "grave_08": case "grave_09":
                return LiveEmotionCue.Teary;
            case "08": case "25a": case "31":
            case "grave_10": case "grave_11": case "grave_12": case "grave_13":
            case "grave_14": case "grave_15": case "grave_16":
                return LiveEmotionCue.Cry;
            default:
                return LiveEmotionCue.Sad;
        }
    }

    void SetLiveEmotion(OSCharacterExtendedAnimation animation, bool instant = false)
    {
        var character = os?.CharacterWindowController;
        if (character != liveEmotionCharacter)
        {
            liveEmotionCharacter?.ReleaseStoryAnimation(this, instant: true);
            liveEmotionCharacter = character;
            // First binding/rebinding reconstructs the saved portrait. It is
            // not a new narrative beat and must not flash a transition.
            instant = true;
        }
        // Intent survives a hidden or booting Live window; the character owner
        // applies it when ready. Repeating an intent does not restart its clip.
        liveEmotionCharacter?.SetStoryAnimation(this, animation, instant: instant);
    }

    // ResetPresentation and chapter exit always clean up silently. Keeping the
    // no-argument entry also lets the isolated validation call that real path.
    void ReleaseLiveEmotion() => ReleaseLiveEmotionCore(true);

    void ReleaseLiveEmotionCore(bool instant)
    {
        liveEmotionCharacter?.ReleaseStoryAnimation(this, instant: instant);
        liveEmotionCharacter = null;
        liveEmotionEntryId = null;
        liveEmotionVisit = -1;
        liveEmotionRestorePending = true;
        liveEmotionCryReached = false;
    }
}
