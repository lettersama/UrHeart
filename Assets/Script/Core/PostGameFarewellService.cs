using System;
using System.Collections.Generic;
using System.Diagnostics;
using MrTGame.Core;
using UnityEngine;

/// <summary>Retains completed playthroughs and delivers their gift across New Game and restarts.</summary>
[DisallowMultipleComponent]
public sealed class PostGameFarewellService : MonoBehaviour
{
    public static PostGameFarewellService Current { get; private set; }
    public string RunId { get; private set; }
    GameDataManager data;
    PostGameFarewellStateStore store;
    PostGameFarewellState state;
    string receiptSaveId;
    bool storageUnavailable, openedThisRun, recoveredThisRun;
    readonly HashSet<string> deliveryCheckedThisRun = new HashSet<string>(StringComparer.Ordinal);
    readonly HashSet<string> reportedErrors = new HashSet<string>(StringComparer.Ordinal);
    int retainedAchievementMilestone;
    float nextPoll;

    // A committed old receipt remains proof even when the playable slot is new,
    // missing, or Steam was offline when the letter window originally opened.
    public int CommittedAchievementMilestone => data == null ? 0 : Math.Max(retainedAchievementMilestone,
        storageUnavailable ? 0 : StoryAchievementProgress.GetFarewellReached(state));

    public void Initialize(GameDataManager manager)
    {
        if (data != null) return;
        Current = this; data = manager; RunId = Guid.NewGuid().ToString("N");
    }

    bool Allowed
    {
        get
        {
            var saves = GameAutosaveService.Instance;
            return data != null && data.IsInitialized && saves != null
                && PostGameFarewellState.CanRun(Application.isEditor, GameSetup.IsDemo,
                    GameSetup.IsTestMode, saves.IsTestSession, saves.IsIsolatedStorage);
        }
    }

    void Update()
    {
        if (!Allowed || Time.unscaledTime < nextPoll) return;
        nextPoll = Time.unscaledTime + 1f;
        bool atMenu = GameManager.Instance?.Phase == GamePhase.Boot && !GameManager.Instance.IsTransitioning
            && GameSessionFlow.Instance?.IsBusy != true && MainMenuHandle.Instance?.IsMainMenuVisible == true
            && !MainMenuHandle.Instance.IsOptionPanelVisible;
        if (!atMenu && recoveredThisRun) return;

        var saves = GameAutosaveService.Instance;
        if (atMenu && saves.StoredFinalEndingCompleted && ReadReceipt(saves.StoredFarewellSaveId))
            ArmCommittedCompletion();

        // Recover once at startup even if the player immediately clicks Continue;
        // later scans run only at the idle menu. Old requests keep their original
        // save ID, parent process, exit clock and deduplication.
        var receipts = ReadRetainedReceipts(PostGameFarewellDelivery.StateDirectory);
        if (!PostGameFarewellContent.IsReady()) return;
        recoveredThisRun = true;
        foreach (var receipt in receipts)
        {
            SelectReceipt(receipt.SaveId, receipt.Store, receipt.State);
            EnsureDelivery();
            if (atMenu && !storageUnavailable && !openedThisRun && PostGameFarewellWindow.Current == null
                && state.ShouldShow(RunId, state.OwnerIsAlive()))
                ShowCommittedReceipt();
        }
    }

    // Called synchronously after Staff's successful final save/menu return.
    // Persist eligibility before the player can start a new game or quit, even
    // if localized presentation content is temporarily unavailable.
    public void NotifyCompletedReturnToMenu()
    {
        var saves = GameAutosaveService.Instance;
        if (!Allowed || !saves.StoredFinalEndingCompleted || !ReadReceipt(saves.StoredFarewellSaveId)) return;
        ArmCommittedCompletion();
        if (!storageUnavailable && PostGameFarewellContent.IsReady()) EnsureDelivery();
    }

    List<PostGameFarewellStateStore.CompletedReceipt> ReadRetainedReceipts(string root)
    {
        var receipts = PostGameFarewellStateStore.ReadCompleted(root, ReportReceiptError);
        foreach (var receipt in receipts)
            retainedAchievementMilestone = Math.Max(retainedAchievementMilestone,
                StoryAchievementProgress.GetFarewellReached(receipt.State));
        return receipts;
    }

    void SelectReceipt(string saveId, PostGameFarewellStateStore selectedStore, PostGameFarewellState selectedState)
    {
        receiptSaveId = saveId ?? string.Empty;
        store = selectedStore; state = selectedState; storageUnavailable = false;
    }

    bool ReadReceipt(string saveId)
    {
        try
        {
            var selectedStore = new PostGameFarewellStateStore(PostGameFarewellDelivery.GetSaveStateDirectory(saveId));
            SelectReceipt(saveId, selectedStore, null);
            if (store.TryRead(out state, out string error)) return true;
            DisableReceipt(error);
        }
        catch (Exception error) { DisableReceipt(error.Message); }
        return false;
    }

    void ArmCommittedCompletion()
    {
        var saves = GameAutosaveService.Instance;
        if (state.completed || !saves.StoredFinalEndingCompleted
            || !string.Equals(receiptSaveId, saves.StoredFarewellSaveId ?? string.Empty, StringComparison.Ordinal)) return;
        using (var process = Process.GetCurrentProcess())
        {
            state.completed = true;
            state.completedRunId = RunId;
            state.completedProcessId = process.Id;
            state.completedProcessStartTimeUtcTicks = process.StartTime.ToUniversalTime().Ticks;
        }
        Persist();
    }

    void EnsureDelivery()
    {
        if (!state.completed || deliveryCheckedThisRun.Contains(receiptSaveId)
            || state.completedRunId != RunId && state.OwnerIsAlive()) return;
        state.deliveryAttempted = true;
        if (!Persist()) return;
        // Check each retained playthrough once per launch. An interrupted worker
        // resumes its existing request; receipt files prevent a second delivery.
        deliveryCheckedThisRun.Add(receiptSaveId);
        state.deliveryStarted = PostGameFarewellDelivery.TrySchedule(receiptSaveId,
            data.CurrentLanguageState.ToString(), PostGameFarewellContent.FileName, PostGameFarewellContent.Letter,
            PostGameFarewellContent.WindowTitle, PostGameFarewellContent.Text("notification.body"), out string detail);
        state.lastDeliveryError = state.deliveryStarted ? null : detail;
        if (!Persist()) return;
        if (!state.deliveryStarted)
            UnityEngine.Debug.LogWarning("[Farewell] Desktop delivery unavailable; the in-game letter remains available. " + detail);
    }

    void ShowCommittedReceipt()
    {
        // Capture the source ledger, not the replaceable autosave or whichever
        // ledger is selected by the next poll while this window is still open.
        var openedStore = store;
        var openedState = state;
        var window = PostGameFarewellWindow.Show(MainMenuHandle.Instance.transform, () => {
            openedState.windowDismissed = true;
            PersistReceipt(openedStore, openedState, out _);
        });
        openedThisRun = window != null;
        if (openedThisRun)
        {
            state.windowShown = true;
            Persist();
        }
    }

    bool PersistReceipt(PostGameFarewellStateStore target, PostGameFarewellState proposed, out PostGameFarewellState saved)
    {
        if (target.TrySave(proposed, out saved, out string error))
        {
            retainedAchievementMilestone = Math.Max(retainedAchievementMilestone,
                StoryAchievementProgress.GetFarewellReached(saved));
            return true;
        }
        ReportReceiptError(error);
        return false;
    }

    bool Persist()
    {
        if (PersistReceipt(store, state, out var saved)) { state = saved; return true; }
        storageUnavailable = true; return false;
    }

    void DisableReceipt(string error)
    {
        storageUnavailable = true;
        ReportReceiptError(error);
    }

    void ReportReceiptError(string error)
    {
        if (reportedErrors.Add(error ?? string.Empty))
            UnityEngine.Debug.LogWarning("[Farewell] Local receipt unavailable; normal menus and saves remain usable. " + error);
    }

    void OnDestroy() { if (Current == this) Current = null; }
}
