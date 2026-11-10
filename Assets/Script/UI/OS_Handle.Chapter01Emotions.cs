public sealed partial class OS_Handle
{
    readonly object chapter01EmotionOwner = new object();
    GameSaveData chapter01EmotionSave;
    OSCharacterInteractionSaveData chapter01EmotionInteraction;
    string chapter01EmotionRestoreEntry;
    bool chapter01EmotionResumePending;

    // Only two first-chapter lines add an expression. Existing work, thought
    // and idle gestures remain in CharacterActivity and resume after the line.
    void RefreshChapter01StoryAnimation()
    {
        var data = GameDataManager.Instance;
        if (!IsSingletonInstance || data == null || data.CurrentChapterIndex != 1
            || characterWindowController == null)
        {
            ResetChapter01StoryAnimation();
            return;
        }

        bool changedSave = !ReferenceEquals(chapter01EmotionSave, data.SaveData)
            || !ReferenceEquals(chapter01EmotionInteraction, data.OSCharacterInteractionState);
        if (changedSave)
        {
            ResetChapter01StoryAnimation();
            chapter01EmotionSave = data.SaveData;
            chapter01EmotionInteraction = data.OSCharacterInteractionState;
            CaptureChapter01RestoredEmotionEntry();
            chapter01EmotionResumePending = true;
        }

        if (OSSessionPersistence.IsRestoring) CaptureChapter01RestoredEmotionEntry();

        if (OSSessionPersistence.IsRestoring || GameSessionFlow.Instance?.IsBusy == true
            || GameTestConsole.BlocksGameplayInput || !IsOSVisible
            || !IsCharacterActivityWindowVisible(characterWindow)
            || !characterWindowController.isActiveAndEnabled
            || characterWindowController.CurrentState != OSCharacterWindowState.Anima
            || characterWindowController.IsTransitioning)
        {
            SuspendChapter01StoryAnimation();
            return;
        }

        string entry = IsCharacterActivityMessageVisible()
            ? linkMessageController.DisplayedEntryId : null;
        var pose = ResolveChapter01StoryAnimation(entry);
        bool resumed = chapter01EmotionResumePending
            || entry != null && entry == chapter01EmotionRestoreEntry;
        chapter01EmotionResumePending = false;
        if (entry != null) chapter01EmotionRestoreEntry = null;
        if (pose == OSCharacterExtendedAnimation.None)
        {
            bool hiddenMessage = linkMessageController?.LinkWindow?.IsMinimized == true;
            characterWindowController.ReleaseStoryAnimation(chapter01EmotionOwner,
                instant: resumed || hiddenMessage);
            if (hiddenMessage) chapter01EmotionResumePending = true;
            return;
        }

        // A visible line holds through typing, option waits and reading holds.
        // Neither pending IDs nor old consumed receipts can claim the portrait.
        characterWindowController.SetStoryAnimation(chapter01EmotionOwner, pose, instant: resumed);
    }

    static OSCharacterExtendedAnimation ResolveChapter01StoryAnimation(string entryId)
    {
        switch (entryId)
        {
            case OSCharacterInteractionIds.Chapter01Entries.LiveEntryForced:
                return OSCharacterExtendedAnimation.Angry00;
            case OSCharacterInteractionIds.Chapter01Entries.StateCheckMemoryDamaged:
                return OSCharacterExtendedAnimation.Nervous;
            default:
                return OSCharacterExtendedAnimation.None;
        }
    }

    void SuspendChapter01StoryAnimation()
    {
        characterWindowController?.ReleaseStoryAnimation(chapter01EmotionOwner, instant: true);
        chapter01EmotionResumePending = true;
    }

    void CaptureChapter01RestoredEmotionEntry()
    {
        var interaction = chapter01EmotionInteraction;
        if (interaction == null) return;
        chapter01EmotionRestoreEntry = !string.IsNullOrEmpty(interaction.presentingGlobalEntryId)
            ? interaction.presentingGlobalEntryId : interaction.presentingEntryId;
    }

    void ResetChapter01StoryAnimation()
    {
        SuspendChapter01StoryAnimation();
        chapter01EmotionSave = null;
        chapter01EmotionInteraction = null;
        chapter01EmotionRestoreEntry = null;
    }
}
