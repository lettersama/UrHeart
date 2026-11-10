using System;
using MrTGame.Core;
using UnityEngine;

public partial class GameDataManager
{
    public const int EnergyChargeReward = 25;
    public const float EnergyChargeDurationSeconds = 300f;
    public const float EnergyChargeCooldownSeconds = 300f;
    public bool IsEnergyChargeCoolingDown => OSEnergyLoopState.remainingCooldownSeconds > 0f;
    public bool IsPaperPushUnlocked => OSEnergyLoopState.minigameUnlocked;
    public bool IsPaperPushPlayable => IsPaperPushUnlocked
        && (CurrentChapterIndex != 1 || (!OSEnergyLoopState.paperPushSealPending && !OSEnergyLoopState.paperPushSealed))
        && (CurrentChapterIndex != 2 || OSEnergyLoopState.chapter02PaperPushStep >= 2
            || OS_Handle.Instance?.Chapter02PaperPush?.UnsealIconReady == true);
    public event Action EnergyChargeCompleted;
    public event Action PaperPushUnlocked;

    public bool TryBeginEnergyCharge(float durationSeconds = EnergyChargeDurationSeconds)
    {
        var state = OSEnergyLoopState;
        if (!IsEnergyLoopInstalled || state.isCharging || state.chargeReadyToClaim
            || state.remainingCooldownSeconds > 0f) return false;
        state.chargeDurationSeconds = float.IsNaN(durationSeconds) || float.IsInfinity(durationSeconds)
            ? EnergyChargeDurationSeconds : Mathf.Max(1f, durationSeconds);
        state.remainingChargeSeconds = state.chargeDurationSeconds;
        state.isCharging = true;
        return true;
    }

    // Both timers belong to the persistent data manager and keep ticking with the charger window closed.
    public void TickEnergyCharge(float deltaSeconds)
    {
        if (deltaSeconds <= 0f || float.IsNaN(deltaSeconds)
            || float.IsInfinity(deltaSeconds)) return;
        var state = OSEnergyLoopState;
        state.remainingCooldownSeconds = Mathf.Max(0f, state.remainingCooldownSeconds - deltaSeconds);
        if (!state.isCharging) return;

        float elapsedAfterCompletion = Mathf.Max(0f, deltaSeconds - state.remainingChargeSeconds);
        state.remainingChargeSeconds = Mathf.Max(0f, state.remainingChargeSeconds - deltaSeconds);
        if (state.remainingChargeSeconds > 0f) return;

        state.isCharging = false;
        state.chargeReadyToClaim = true;
        // Cooling starts at completion, including any time left in this tick; claiming never resets it.
        state.remainingCooldownSeconds = Mathf.Max(0f, EnergyChargeCooldownSeconds - elapsedAfterCompletion);
        state.completedChargeCount++;
        EnergyChargeCompleted?.Invoke();
    }

    public bool TryClaimEnergyCharge(out int gained)
    {
        gained = 0;
        var state = OSEnergyLoopState;
        if (!state.chargeReadyToClaim) return false;
        // Settle before notifying resource listeners, so a second click cannot re-enter.
        state.chargeReadyToClaim = false;
        int before = GetOSResourceValue(ResourceType.Battery);
        int after;
        if (OS_Handle.Instance != null) after = OS_Handle.Instance.ChangeEnergy(EnergyChargeReward);
        else
        {
            after = SetOSResourceValue(ResourceType.Battery, before + EnergyChargeReward);
            GameEvents.RaiseResourceChanged(ResourceType.Battery, after, OSResourceSaveData.MaxValue);
        }
        gained = state.lastEnergyGained = after - before;
        return true;
    }

    public void DiscoverPaperPush() => OSEnergyLoopState.minigameDiscovered = true;

    public bool UnlockPaperPush()
    {
        var state = OSEnergyLoopState;
        if (!state.minigameDiscovered || state.minigameUnlocked) return false;
        state.minigameUnlocked = true;
        SetDesktopProcessIsLocked(OSDesktopProcessKeys.PaperPush, false);
        SetDesktopProcessIsNew(OSDesktopProcessKeys.PaperPush, true);
        PaperPushUnlocked?.Invoke();
        return true;
    }

    public void RecordPaperPushResult(bool playerWon)
    {
        var state = OSEnergyLoopState;
        int chapter = state.paperPushMatch.active ? state.paperPushMatch.chapter : CurrentChapterIndex;
        state.paperPushMatch.active = false;
        if (playerWon)
        {
            state.paperPushWins++;
            if (chapter == 1 && !state.paperPushSealed)
            {
                state.chapter01PaperPushWins = Mathf.Min(3, state.chapter01PaperPushWins + 1);
                if (state.chapter01PaperPushWins == 3) state.paperPushSealPending = true;
            }
        }
        else state.paperPushLosses++;
    }
}
