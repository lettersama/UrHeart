public sealed partial class OS_Handle
{
    readonly object chapter02EmotionOwner = new object();
    GameSaveData chapter02EmotionSave;
    OSCharacterInteractionSaveData chapter02EmotionInteraction;
    string chapter02EmotionEntry;
    string chapter02EmotionRestoreEntry;
    OSCharacterExtendedAnimation chapter02EmotionPose;
    bool chapter02EmotionResumePending;

    // The visible line owns its expression through typing, choices and the reading hold.
    // Queued messages and old consumed receipts must never change Live's face.
    void RefreshChapter02StoryAnimation()
    {
        var data = GameDataManager.Instance;
        if (!IsSingletonInstance || data == null || data.CurrentChapterIndex != 2
            || characterWindowController == null)
        {
            ResetChapter02StoryAnimation();
            return;
        }

        bool changedSave = !ReferenceEquals(chapter02EmotionSave, data.SaveData)
            || !ReferenceEquals(chapter02EmotionInteraction, data.OSCharacterInteractionState);
        if (changedSave)
        {
            ResetChapter02StoryAnimation();
            chapter02EmotionSave = data.SaveData;
            chapter02EmotionInteraction = data.OSCharacterInteractionState;
            CaptureChapter02RestoredEmotionEntry();
            chapter02EmotionResumePending = true;
        }

        if (OSSessionPersistence.IsRestoring) CaptureChapter02RestoredEmotionEntry();

        if (OSSessionPersistence.IsRestoring || GameSessionFlow.Instance?.IsBusy == true
            || GameTestConsole.BlocksGameplayInput || !IsOSVisible
            || !IsCharacterActivityWindowVisible(characterWindow)
            || !characterWindowController.isActiveAndEnabled
            || characterWindowController.CurrentState != OSCharacterWindowState.Anima
            || characterWindowController.IsTransitioning)
        {
            SuspendChapter02StoryAnimation();
            return;
        }

        string entry = IsCharacterActivityMessageVisible()
            ? linkMessageController.DisplayedEntryId : null;
        OSCharacterExtendedAnimation pose = ResolveChapter02StoryAnimation(entry);
        // Directors close a bubble before presenting their next line. Keep the
        // last actually shown pose across that gap (including the quiet answers),
        // without pre-playing the next line's expression from a queued ID.
        if (entry == null && HasChapter02EmotionContinuation()
            && linkMessageController?.LinkWindow?.IsMinimized != true
            && chapter02EmotionEntry != null)
        {
            entry = chapter02EmotionEntry;
            pose = chapter02EmotionPose;
        }
        bool restoredEntry = entry != null && entry == chapter02EmotionRestoreEntry;
        bool resumed = chapter02EmotionResumePending || restoredEntry;
        chapter02EmotionResumePending = false;
        if (entry != null) chapter02EmotionRestoreEntry = null;
        if (pose == OSCharacterExtendedAnimation.None)
        {
            // A minimized message may return to the same line. Keep its cue identity
            // so the magnifier does not get taken out again when it is restored.
            bool hiddenMessage = linkMessageController?.LinkWindow?.IsMinimized == true;
            characterWindowController.ReleaseStoryAnimation(chapter02EmotionOwner,
                instant: resumed || hiddenMessage);
            if (hiddenMessage) chapter02EmotionResumePending = true;
            else { chapter02EmotionEntry = null; chapter02EmotionPose = OSCharacterExtendedAnimation.None; }
            return;
        }

        bool sameCue = chapter02EmotionEntry == entry && chapter02EmotionPose == pose;
        // A restored line is already in progress; resume the magnifier's idle loop.
        OSCharacterExtendedAnimation applied = pose == OSCharacterExtendedAnimation.MagnifyingGlass00
            && (changedSave || restoredEntry || (resumed && sameCue))
                ? OSCharacterExtendedAnimation.MagnifyingGlass01 : pose;
        // Keep using 01 after a resume. Re-requesting 00 would restart its introduction.
        if (chapter02EmotionPose == pose && pose == OSCharacterExtendedAnimation.MagnifyingGlass00
            && characterWindowController.CurrentExtendedAnimation == OSCharacterExtendedAnimation.MagnifyingGlass01)
            applied = OSCharacterExtendedAnimation.MagnifyingGlass01;
        characterWindowController.SetStoryAnimation(chapter02EmotionOwner, applied, instant: resumed);
        chapter02EmotionEntry = entry;
        chapter02EmotionPose = pose;
    }

    void SuspendChapter02StoryAnimation()
    {
        characterWindowController?.ReleaseStoryAnimation(chapter02EmotionOwner, instant: true);
        chapter02EmotionResumePending = true;
    }

    void CaptureChapter02RestoredEmotionEntry()
    {
        var interaction = chapter02EmotionInteraction;
        if (interaction == null) return;
        chapter02EmotionRestoreEntry = !string.IsNullOrEmpty(interaction.presentingGlobalEntryId)
            ? interaction.presentingGlobalEntryId : interaction.presentingEntryId;
    }

    bool HasChapter02EmotionContinuation()
    {
        string id = chapter02EmotionEntry;
        if (string.IsNullOrEmpty(id)) return false;
        if (id.StartsWith("chapter02_final_talk_", System.StringComparison.Ordinal))
            return !string.IsNullOrEmpty(Chapter02FinalTalk?.ReservedEntryId);
        if (id.StartsWith("cursor_test_ch02_", System.StringComparison.Ordinal))
            return CursorExperimentStory?.IsStoryProtected == true;
        if (id.StartsWith("cursor_release_ch02_", System.StringComparison.Ordinal))
            return !string.IsNullOrEmpty(CursorRelease?.ReservedEntryId);
        if (id.StartsWith("heart_talk_ch02_", System.StringComparison.Ordinal))
            return !string.IsNullOrEmpty(CursorHeartTalk?.ReservedEntryId);
        if (id.StartsWith("finale_ch02_", System.StringComparison.Ordinal))
            return !string.IsNullOrEmpty(Chapter02Finale?.ReservedEntryId);
        if (id.StartsWith("paper_push_ch02_", System.StringComparison.Ordinal))
            return !string.IsNullOrEmpty(Chapter02PaperPush?.ReservedEntryId);
        return false;
    }

    void ResetChapter02StoryAnimation()
    {
        SuspendChapter02StoryAnimation();
        chapter02EmotionSave = null;
        chapter02EmotionInteraction = null;
        chapter02EmotionEntry = null;
        chapter02EmotionRestoreEntry = null;
        chapter02EmotionPose = OSCharacterExtendedAnimation.None;
    }

}
