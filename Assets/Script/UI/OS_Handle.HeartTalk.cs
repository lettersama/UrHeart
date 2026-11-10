using MrTGame.Narrative;
using UnityEngine;

public sealed partial class OS_Handle
{
    // Called only after the return presentation acknowledges completion. It is
    // also safe on a Done snapshot, without reopening completed experiments.
    public void CompleteChapter02HeartTalkReturn()
    {
        var data = GameDataManager.Instance;
        if (data == null || data.CurrentChapterIndex != 2
            || data.OSProcessData.memory.chapter02HeartTalkStep != (int)OSCursorHeartTalkDirector.HeartTalkStep.Done)
            return;

        CursorRelease?.ReleaseHeartTalkLayout();
        cursorExperimentDiscard?.ResetPresentation();
        GetComponent<OSCursorExperimentAmbientGlitch>()?.ResetEffect();
        GetComponent<OSCursorExperimentFocusPresentation>()?.ResetPresentation();
        cursorExperiment?.ResetPresentation();
        CursorExperimentInterior?.ResetPresentation();
        CursorExperimentStory?.ResetPresentation();
        cursorExperimentWindow?.PrepareRuntimeClosed();
        cursorExperimentHandoffHold = 0f;
        RefreshCursorExperimentStoryProtection();

        // Restoring the interior's parent/layout never closes Live. A saved
        // closed/minimized window is reopened once at this authored boundary.
        if (characterWindow != null && (!characterWindow.IsOpen || characterWindow.IsMinimized))
            OpenCharacterWindowForReconnect(0f);

        if (data.CurrentOSMemoryDataIndex == OSMemoryData.Chapter02CollectionWaitingDataIndex)
        {
            if (memoryWindowController != null && memoryWindowController.CompleteChapter02Collection())
                OpenMemoryWindow();
        }
        // Do not reopen older messages: their resource and consumption receipts
        // remain intact, and normal OS messages may resume after this callback.
        linkMessageController?.NotifyOSBecameVisible();
    }
}
