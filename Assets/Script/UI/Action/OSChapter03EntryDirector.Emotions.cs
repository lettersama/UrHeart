public sealed partial class OSChapter03EntryDirector
{
    const string InstallingExpressionEntryId = EntryPrefix + "installing";

    OSCharacterWindowController entryAnimationCharacter;
    bool installingExpressionPresented;
    bool entryAnimationRestorePending = true;

    void OnEntryPresentationStarted(string sequence, OSCharacterInteractionEntryData entry)
    {
        if (!IsEngaged || !ReferenceEquals(observed, State)
            || Step != EntryStep.Installing || State.chapter03EntryInstallAnswer != 0
            || entry?.EntryId != InstallingExpressionEntryId) return;

        // Queue acceptance is not a performance cue. Keep the pout through the
        // question and any temporary window closure, until its actual answer.
        installingExpressionPresented = true;
        RefreshEntryAnimation();
    }

    void RestoreEntryAnimationIntent()
    {
        if (!entryAnimationRestorePending || State == null || OSSessionPersistence.IsRestoring) return;
        entryAnimationRestorePending = false;
        var interaction = GameDataManager.Instance?.OSCharacterInteractionState;
        installingExpressionPresented = Step == EntryStep.Installing
            && State.chapter03EntryInstallAnswer == 0
            && (interaction?.presentingGlobalEntryId == InstallingExpressionEntryId
                || interaction?.HasConsumedGlobalEntry(InstallingExpressionEntryId) == true);

        // The 1.6-second thinking beat is transient. Restoring an already
        // completed installation goes directly to its stable question phase.
        // An installation still in progress retains its normal completion beat.
        if (Step == EntryStep.Installing && GameDataManager.Instance.IsMemoryRepair04Installed
            && library?.IsInstalling != true)
        {
            StopThinking();
            cueStage = 2;
        }
        RefreshEntryAnimation(instant: true);
    }

    void RefreshEntryAnimation(bool instant = false)
    {
        if (OSSessionPersistence.IsRestoring) return;
        if (!IsEngaged)
        {
            ReleaseEntryAnimation(instant: true);
            return;
        }

        var character = os?.CharacterWindowController;
        if (character != entryAnimationCharacter)
        {
            entryAnimationCharacter?.ReleaseStoryAnimation(this, instant: true);
            entryAnimationCharacter = character;
        }

        var animation = ResolveEntryAnimation(Step, State.chapter03EntryInstallAnswer != 0,
            installingExpressionPresented, thinking, library?.IsInstalling == true,
            GameDataManager.Instance.IsMemoryRepair04Installed);
        if (animation == OSCharacterExtendedAnimation.None)
            entryAnimationCharacter?.ReleaseStoryAnimation(this, instant);
        else
            entryAnimationCharacter?.SetStoryAnimation(this, animation, instant: instant);
    }

    static OSCharacterExtendedAnimation ResolveEntryAnimation(EntryStep step, bool answered,
        bool questionPresented, bool isThinking, bool installing, bool installed)
    {
        if (step == EntryStep.AutoInstall) return OSCharacterExtendedAnimation.Expend01;
        if (step != EntryStep.Installing) return OSCharacterExtendedAnimation.None;
        if (!answered && questionPresented) return OSCharacterExtendedAnimation.Angry01;
        if (!answered && isThinking) return OSCharacterExtendedAnimation.Expend02;
        return installing || !installed ? OSCharacterExtendedAnimation.Expend01
            : OSCharacterExtendedAnimation.None;
    }

    void ReleaseEntryAnimation(bool instant)
    {
        entryAnimationCharacter?.ReleaseStoryAnimation(this, instant);
        entryAnimationCharacter = null;
    }
}
