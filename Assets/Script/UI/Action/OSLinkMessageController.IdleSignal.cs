using UnityEngine;

public sealed partial class OSLinkMessageController
{
    // Ambient resource feedback has lower priority than every story message,
    // including completed text still held for reading and delayed callbacks.
    internal bool TryPresentIdleGlobalSignal(string signal)
    {
        var state = GetSaveState();
        if (state == null || !isOSVisible || IsPresenting || hasDisplayedMessage
            || isWaitingForOption || IsWaitingForExternalCompletion
            || !string.IsNullOrEmpty(state.presentingGlobalEntryId)
            || !string.IsNullOrEmpty(state.presentingEntryId)
            || !string.IsNullOrEmpty(state.pendingGlobalEntryId)
            || !string.IsNullOrEmpty(state.pendingEntryId) || state.optionCountdownQueue.Count > 0
            || pendingSequenceTween != null || pendingGlobalTween != null
            || reconnectAnimationTween != null || !string.IsNullOrEmpty(state.nextEntryCountdownKey)
            || linkWindow == null || linkWindow.IsOpen || linkWindow.IsClosing
            || ownerCharacterWindow == null || !ownerCharacterWindow.IsOpen
            || ownerCharacterWindow.IsMinimized || ownerCharacterWindow.IsOpening
            || ownerCharacterWindow.IsClosing || ownerCharacterWindow.IsPointerManipulating
            || !ownerCharacterWindow.gameObject.activeInHierarchy
            || OSSessionPersistence.IsRestoring || GameSessionFlow.Instance?.IsBusy == true
            || GameTestConsole.BlocksGameplayInput || OS_Handle.Instance?.IsSystemInputLocked == true
            || AreGlobalMessagesSuppressed || IsOptionCountdownSettingsOpen()) return false;
        return NotifyGlobalExternalSignal(signal);
    }
}
