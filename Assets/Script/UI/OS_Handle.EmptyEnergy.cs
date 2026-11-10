using MrTGame.Core;
using UnityEngine;

public sealed partial class OS_Handle
{
    const float EmptyEnergyReminderCooldown = 8f;
    GameSaveData emptyEnergyReminderSave;
    bool emptyEnergyReminderPending;
    int emptyEnergyReminderFrame;
    float nextEmptyEnergyReminderAt;

    void OnEmptyEnergyUsed()
    {
        var data = GameDataManager.Instance;
        if (!IsOSVisible || data == null || data.CurrentChapterIndex < 1
            || GameManager.Instance?.Phase != GamePhase.OSMain
            || OSSessionPersistence.IsRestoring || GameSessionFlow.Instance?.IsBusy == true)
            return;
        if (!ReferenceEquals(emptyEnergyReminderSave, data.SaveData))
        {
            ResetEmptyEnergyReminder();
            emptyEnergyReminderSave = data.SaveData;
        }
        if (emptyEnergyReminderPending || Time.unscaledTime < nextEmptyEnergyReminderAt) return;
        emptyEnergyReminderPending = true;
        emptyEnergyReminderFrame = Time.frameCount;
    }

    void TickEmptyEnergyReminder()
    {
        var data = GameDataManager.Instance;
        if (data == null || !ReferenceEquals(emptyEnergyReminderSave, data.SaveData))
        {
            ResetEmptyEnergyReminder();
            return;
        }
        if (data.GetOSResourceValue(ResourceType.Battery) > 0)
        {
            emptyEnergyReminderPending = false;
            return;
        }
        // Let the action finish and its story messages claim the channel first.
        // Only one transient reminder waits; it is never written into the save queue.
        if (!emptyEnergyReminderPending || Time.frameCount <= emptyEnergyReminderFrame
            || !IsOSVisible || IsSystemInputLocked || GameTestConsole.BlocksGameplayInput
            || !Application.isFocused || GameManager.Instance?.Phase != GamePhase.OSMain
            || characterWindowController == null
            || characterWindowController.CurrentState != OSCharacterWindowState.Anima
            || linkMessageController == null)
            return;
        if (!linkMessageController.TryPresentIdleGlobalSignal("energy.empty." + Random.Range(0, 3))) return;
        emptyEnergyReminderPending = false;
        nextEmptyEnergyReminderAt = Time.unscaledTime + EmptyEnergyReminderCooldown;
        characterWindowController.PlayEmptyEnergyShake();
    }

    void ResetEmptyEnergyReminder()
    {
        emptyEnergyReminderSave = null;
        emptyEnergyReminderPending = false;
        emptyEnergyReminderFrame = 0;
        nextEmptyEnergyReminderAt = 0f;
    }
}
