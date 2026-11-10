public sealed partial class OS_Handle
{
    const string MemoryUpgrade02InstalledMessage = "chapter01_memory_upgrade_02_installed";
    const string MemoryUpgrade03InstalledMessage = "chapter02_memory_upgrade_03_installed";

    void RefreshMemoryUpgrade02InstalledMessage()
    {
        GameDataManager data = GameDataManager.Instance;
        if (!IsSingletonInstance || data == null || data.CurrentChapterIndex != 1
            || !data.IsMemoryRepair02Installed || !IsOSVisible || IsSystemInputLocked
            || OSSessionPersistence.IsRestoring || GameSessionFlow.Instance?.IsBusy == true
            || GameTestConsole.BlocksGameplayInput || Chapter01Ending?.IsPresenting == true
            || linkMessageController == null)
            return;

        OSCharacterInteractionSaveData interaction = data.OSCharacterInteractionState;
        // Derive this from the installed program and the existing message receipt,
        // so an interrupted install/load cannot lose or duplicate the response.
        if (interaction.activeSequenceId != OSCharacterInteractionIds.Chapter01MemoryRepair
            || interaction.HasConsumedEntry(MemoryUpgrade02InstalledMessage))
            return;

        // Closing Live halfway through an upgrade hint preserves its saved wait.
        // Once the install succeeded, that hidden hint is obsolete; otherwise it
        // could indefinitely block the completion message from reopening Live.
        if (!linkMessageController.IsPresenting
            && interaction.phase == OSCharacterInteractionSavePhase.Presenting
            && IsMemoryUpgrade02Hint(interaction.presentingEntryId))
            interaction.CompletePresentation(interaction.presentingEntryId);

        if (linkMessageController.IsPresenting
            || interaction.phase == OSCharacterInteractionSavePhase.Presenting
            || !string.IsNullOrEmpty(interaction.pendingEntryId)
            || !string.IsNullOrEmpty(interaction.pendingGlobalEntryId))
            return;

        // Let the short package/install hints finish their authored reading time.
        string displayedEntry = linkMessageController.DisplayedEntryId;
        OSWindowUnit messageWindow = linkMessageController.LinkWindow;
        if (messageWindow != null && messageWindow.IsOpen && !messageWindow.IsMinimized
            && messageWindow.gameObject.activeInHierarchy
            && IsMemoryUpgrade02Hint(displayedEntry))
            return;

        linkMessageController.TryPresentEntry(MemoryUpgrade02InstalledMessage);
    }

    static bool IsMemoryUpgrade02Hint(string entryId) =>
        entryId == "chapter01_memory_upgrade_02"
        || entryId == "chapter01_memory_upgrade_02_tool_opened";

    void RefreshMemoryUpgrade03InstalledMessage()
    {
        var data = GameDataManager.Instance;
        if (!IsSingletonInstance || data == null || data.CurrentChapterIndex != 2
            || !data.IsMemoryRepair03Installed || !IsOSVisible || IsSystemInputLocked
            || data.CurrentOSMemoryDataIndex >= MrTGame.Narrative.OSMemoryData.Chapter02CollectionWaitingDataIndex
            || OSSessionPersistence.IsRestoring || GameSessionFlow.Instance?.IsBusy == true
            || GameTestConsole.BlocksGameplayInput || linkMessageController == null
            || linkMessageController.GlobalMessagesSuppressed
            || Chapter01Ending?.IsChapter02LiveDisconnected == true
            || Chapter01Ending?.IsChapter02ReturnInProgress == true)
            return;

        // The installed program and existing message receipt also cover a save
        // made between installation and the first free dialogue slot.
        var interaction = data.OSCharacterInteractionState;
        if (interaction.activeSequenceId != Chapter02CollectionSequence
            || !interaction.HasCompletedEntry("chapter02_return_reaction")
            || interaction.HasConsumedEntry(MemoryUpgrade03InstalledMessage)
            || interaction.phase == OSCharacterInteractionSavePhase.Presenting
            || linkMessageController.IsPresenting || linkMessageController.IsReadAlongQuestionPending
            || !string.IsNullOrEmpty(interaction.presentingGlobalEntryId)
            || !string.IsNullOrEmpty(interaction.pendingEntryId)
            || !string.IsNullOrEmpty(interaction.pendingGlobalEntryId))
            return;

        var messageWindow = linkMessageController.LinkWindow;
        if (messageWindow != null && (messageWindow.IsOpen || messageWindow.IsClosing)) return;
        if (characterWindow == null || characterWindow.IsClosing) return;
        if (!characterWindow.IsOpen || characterWindow.IsMinimized)
        {
            OpenCharacterWindowForReconnect(0.15f);
            return;
        }
        if (characterWindow.IsOpening) return;

        linkMessageController.TryPresentEntry(MemoryUpgrade03InstalledMessage);
    }
}
