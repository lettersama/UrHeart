using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public sealed partial class OS_Handle
{
    const float CharacterHairBrushMinIdle = 35f;
    const float CharacterHairBrushMaxIdle = 60f;
    const string MotherDaughterRecallEntry = "chapter01_memory_mother_daughter_recall";
    const string RobotAssistantEntry = "chapter01_memory_robot_assistant";

    OSPaperPushWindowController characterActivityPaperPush;
    bool characterActivityPaperPushResolved;
    float characterIdleElapsed;
    float characterHairBrushDelay;

    void RefreshCharacterActivityAnimation()
    {
        GameDataManager data = GameDataManager.Instance;
        if (!IsSingletonInstance || data == null
            || (data.CurrentChapterIndex != 1 && data.CurrentChapterIndex != 2)
            || !IsOSVisible || OSSessionPersistence.IsRestoring
            || GameSessionFlow.Instance?.IsBusy == true || GameTestConsole.BlocksGameplayInput
            || !IsCharacterActivityWindowVisible(characterWindow)
            || characterWindowController == null || !characterWindowController.isActiveAndEnabled
            || characterWindowController.CurrentState != OSCharacterWindowState.Anima
            || characterWindowController.IsTransitioning)
        {
            ReleaseCharacterActivityAnimation();
            return;
        }

        if (data.CurrentChapterIndex == 2)
        {
            // Chapter 2's visible-line expressions now share the story owner,
            // including the two former thinking-only activity cues.
            characterWindowController.SetActivityAnimation(OSCharacterExtendedAnimation.None);
            ResetCharacterIdleGestureTimer();
            return;
        }

        OSCharacterExtendedAnimation activity = ResolveCharacterActivityAnimation(data);
        characterWindowController.SetActivityAnimation(activity);
        if (activity != OSCharacterExtendedAnimation.None || !CanPlayCharacterIdleGesture(data))
        {
            ResetCharacterIdleGestureTimer();
            return;
        }

        // Start a fresh quiet interval after opening Live, messages, work, or a
        // resource-state change. A failed request also waits before trying again.
        if (characterHairBrushDelay <= 0f)
            characterHairBrushDelay = Random.Range(CharacterHairBrushMinIdle, CharacterHairBrushMaxIdle);
        characterIdleElapsed += Time.unscaledDeltaTime;
        if (characterIdleElapsed < characterHairBrushDelay)
            return;

        characterWindowController.TryPlayHairBrush();
        ResetCharacterIdleGestureTimer();
    }

    OSCharacterExtendedAnimation ResolveCharacterActivityAnimation(GameDataManager data)
    {
        if (!characterActivityPaperPushResolved)
        {
            // The match controller lives on the OS root so minimizing the
            // PaperPush window does not discard its round state.
            characterActivityPaperPush = GetComponent<OSPaperPushWindowController>();
            characterActivityPaperPushResolved = true;
        }
        if (characterActivityPaperPush != null && characterActivityPaperPush.IsCharacterWorking)
            return OSCharacterExtendedAnimation.Expend01;

        bool toolVisible = IsCharacterActivityWindowVisible(toolWindow) && !IsSystemInputLocked;
        OSToolProgramLibrary library = toolWindowController != null ? toolWindowController.ProgramLibrary : null;
        if (toolVisible && !data.HasToolProgramsUnpacked && library != null && library.IsUnpacking)
            return OSCharacterExtendedAnimation.Expend01;

        OSCharacterInteractionSaveData interaction = data.OSCharacterInteractionState;
        bool toolSequence = interaction.activeSequenceId == OSCharacterInteractionIds.Chapter01MemoryRepair;
        bool messageVisible = IsCharacterActivityMessageVisible();
        if (toolSequence)
        {
            // Processing predates the Tool window. Its protected presentation
            // is resumed by LinkMessage after closing, minimizing, or loading.
            if (messageVisible && linkMessageController.DisplayedSequenceId == interaction.activeSequenceId)
            {
                string entryId = linkMessageController.DisplayedEntryId;
                if (entryId == OSCharacterInteractionIds.Chapter01Tool.Processing)
                    return OSCharacterExtendedAnimation.Expend01;
                if (toolVisible && entryId == OSCharacterInteractionIds.Chapter01Tool.Ready)
                    return OSCharacterExtendedAnimation.Expend02;
                if (entryId == MotherDaughterRecallEntry || entryId == RobotAssistantEntry)
                    return OSCharacterExtendedAnimation.Expend02;
            }

            // The command reminder can be hidden while its saved wait is still
            // active; derive work from that wait so reopening Tool restores it.
            if (toolVisible && toolWindowController != null && toolWindowController.IsReady
                && !data.HasToolProgramsUnpacked
                && interaction.presentingEntryId == OSCharacterInteractionIds.Chapter01Tool.FollowUp)
                return OSCharacterExtendedAnimation.Expend01;
        }

        return OSCharacterExtendedAnimation.None;
    }

    bool CanPlayCharacterIdleGesture(GameDataManager data)
    {
        if (IsSystemInputLocked || characterWindowController.IsChapter01ReconnectActive
            || characterWindowController.CurrentExtendedAnimation != OSCharacterExtendedAnimation.None
            || IsCharacterActivityMessageVisible()
            || (linkMessageController != null && (linkMessageController.IsPresenting
                || linkMessageController.IsWaitingForOption
                || linkMessageController.IsWaitingForExternalCompletion)))
            return false;

        OSResourceRuntimeState resources = data.OSResourceState;
        if (resources.IsHeartLow || resources.IsEnergyLow || resources.IsFaultHigh)
            return false;

        GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        TMP_InputField input = selected != null ? selected.GetComponentInParent<TMP_InputField>() : null;
        return input == null || !input.isFocused;
    }

    bool IsCharacterActivityMessageVisible()
    {
        return linkMessageController != null
            && IsCharacterActivityWindowVisible(linkMessageController.LinkWindow)
            && !string.IsNullOrEmpty(linkMessageController.DisplayedEntryId);
    }

    static bool IsCharacterActivityWindowVisible(OSWindowUnit window)
    {
        return window != null && window.IsOpen && !window.IsMinimized
            && !window.IsClosing && window.gameObject.activeInHierarchy;
    }

    void ReleaseCharacterActivityAnimation()
    {
        characterWindowController?.SetActivityAnimation(OSCharacterExtendedAnimation.None);
        ResetCharacterIdleGestureTimer();
    }

    void ResetCharacterIdleGestureTimer()
    {
        characterIdleElapsed = 0f;
        characterHairBrushDelay = 0f;
    }
}
