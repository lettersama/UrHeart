using System;
using System.Collections.Generic;
using MrTGame.Core;
using UnityEditor;
using UnityEngine;

// Explicit, synchronous Edit Mode regression check. The inactive fixture is never
// initialized, opens no save store, and is destroyed before returning to the editor.
public static class OSEnergyChargeCooldownQA
{
    static readonly List<string> checks = new List<string>();
    public static string Status { get; private set; } = "idle";

    [MenuItem("Tools/QA/Verify Energy Charge Cooldown")]
    public static void RunFromMenu() => Debug.Log(Start());

    public static string Start()
    {
        if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            return "Edit Mode required: this check uses a disposable data fixture";
        if (OS_Handle.Instance != null)
            return "Exit the active OS session before running data-only QA";
        var saves = GameAutosaveService.Instance;
        if (saves != null && (saves.IsBusy || saves.IsWriting))
            return "Wait for the current save/scene operation";

        var originalManager = GameDataManager.Instance;
        var originalSave = originalManager != null ? originalManager.SaveData : null;
        string originalJson = originalSave != null ? JsonUtility.ToJson(originalSave) : null;
        GameObject fixture = null;
        IDisposable writeScope = null;
        checks.Clear();
        Status = "running";
        try
        {
            writeScope = saves?.SuspendWrites();
            fixture = new GameObject("Energy Charge QA") { hideFlags = HideFlags.HideAndDontSave };
            fixture.SetActive(false);
            var data = fixture.AddComponent<GameDataManager>();
            Check(!data.IsInitialized && GameDataManager.Instance == originalManager,
                "fixture does not initialize services or replace the live singleton");
            Run(data);
            Check(originalManager == null || (ReferenceEquals(originalManager.SaveData, originalSave)
                && JsonUtility.ToJson(originalSave) == originalJson), "existing in-memory save remains unchanged");
            Status = "PASS: " + checks.Count + " energy charge/cooldown checks\n" + string.Join("\n", checks);
        }
        catch (Exception error)
        {
            Status = "FAILED: " + error + "\nCompleted: " + string.Join("\n", checks);
        }
        finally
        {
            if (fixture != null) UnityEngine.Object.DestroyImmediate(fixture);
            writeScope?.Dispose();
        }
        return Status;
    }

    static void Run(GameDataManager data)
    {
        data.OSProcessData.installedToolProgramIds.Add(OSToolProgramIds.EnergyLoop);
        data.SetOSResourceValue(ResourceType.Battery, 17);
        var state = data.OSEnergyLoopState;
        int completions = 0;
        data.EnergyChargeCompleted += () => completions++;
        Check(state.remainingCooldownSeconds == 0 && data.TryBeginEnergyCharge()
            && state.remainingChargeSeconds == 300, "first charge starts immediately at five minutes");
        Check(!data.TryBeginEnergyCharge(), "active charging cannot restart");
        foreach (float invalid in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            data.TickEnergyCharge(invalid);
        Check(state.isCharging && state.remainingChargeSeconds == 300 && completions == 0,
            "invalid time deltas do not advance charging");
        data.TickEnergyCharge(299);
        Check(state.isCharging && !state.chargeReadyToClaim && state.remainingCooldownSeconds == 0,
            "cooldown does not begin before completion");
        data.TickEnergyCharge(1);
        Check(!state.isCharging && state.chargeReadyToClaim && state.remainingCooldownSeconds == 300
            && state.completedChargeCount == 1 && completions == 1, "completion starts five-minute cooldown before claiming");
        Check(data.GetOSResourceValue(ResourceType.Battery) == 17 && !data.TryBeginEnergyCharge(),
            "completion waits for manual claim and cannot start another charge");

        // No window/controller exists: the data clock must remain independently usable.
        data.TickEnergyCharge(40);
        Check(state.remainingCooldownSeconds == 260 && state.chargeReadyToClaim,
            "cooldown advances with no charging window while the reward waits");
        int gained;
        Check(data.TryClaimEnergyCharge(out gained) && gained == 25
            && data.GetOSResourceValue(ResourceType.Battery) == 42 && state.remainingCooldownSeconds == 260,
            "claim during cooldown grants 25 and does not restart the timer");
        Check(!data.TryClaimEnergyCharge(out gained) && gained == 0
            && data.GetOSResourceValue(ResourceType.Battery) == 42 && !data.TryBeginEnergyCharge(),
            "claimed reward cannot be paid twice and cooldown blocks restart");
        foreach (float invalid in new[] { -1f, float.NaN, float.PositiveInfinity })
            data.TickEnergyCharge(invalid);
        Check(state.remainingCooldownSeconds == 260, "invalid time deltas do not advance cooldown");

        string snapshot = JsonUtility.ToJson(data.SaveData);
        var resumed = JsonUtility.FromJson<GameSaveData>(snapshot);
        resumed.EnsureData();
        data.SaveData.osProcess = resumed.osProcess;
        data.SaveData.osResources = resumed.osResources;
        state = data.OSEnergyLoopState;
        Check(state.remainingCooldownSeconds == 260 && !state.chargeReadyToClaim
            && data.GetOSResourceValue(ResourceType.Battery) == 42 && !data.TryBeginEnergyCharge(),
            "JSON restore preserves cooldown and claim receipt without offline catch-up");
        data.TickEnergyCharge(259);
        Check(state.remainingCooldownSeconds == 1 && !data.TryBeginEnergyCharge(),
            "next cycle remains blocked for the final cooldown second");
        data.TickEnergyCharge(1);
        Check(state.remainingCooldownSeconds == 0 && data.TryBeginEnergyCharge(),
            "next cycle starts as soon as the five-minute cooldown expires");
        data.TickEnergyCharge(600);
        Check(state.remainingCooldownSeconds == 0 && state.chargeReadyToClaim
            && state.completedChargeCount == 2 && completions == 2,
            "one large delta crosses completion and cooldown using elapsed remainder");
        Check(!data.TryBeginEnergyCharge(), "unclaimed reward still blocks restart after cooldown expires");
        data.TickEnergyCharge(1000);
        Check(state.completedChargeCount == 2 && completions == 2,
            "waiting after completion never completes or emits the event twice");
        Check(data.TryClaimEnergyCharge(out gained) && gained == 25
            && data.GetOSResourceValue(ResourceType.Battery) == 67 && data.TryBeginEnergyCharge(),
            "two manual rewards raise the chapter budget from 17 to 67 and allow the next cycle");
        data.TickEnergyCharge(315);
        Check(state.remainingCooldownSeconds == 285 && state.completedChargeCount == 3 && completions == 3,
            "crossing completion by fifteen seconds leaves 285 seconds of cooldown");

        var legacy = JsonUtility.FromJson<OSEnergyLoopSaveData>(
            "{\"isCharging\":true,\"chargeDurationSeconds\":90,\"remainingChargeSeconds\":45}");
        legacy.EnsureData();
        Check(legacy.remainingCooldownSeconds == 0 && legacy.chargeDurationSeconds == 300
            && legacy.remainingChargeSeconds == 150, "legacy save without cooldown preserves charge progress and starts with zero cooldown");
        legacy.EnsureData();
        Check(legacy.remainingChargeSeconds == 150 && legacy.remainingCooldownSeconds == 0,
            "legacy migration is idempotent");
        var pending = JsonUtility.FromJson<OSEnergyLoopSaveData>("{\"chargeReadyToClaim\":true,\"completedChargeCount\":1}");
        pending.EnsureData();
        Check(pending.chargeReadyToClaim && pending.remainingCooldownSeconds == 0,
            "legacy pending reward remains claimable without inventing a cooldown");
        foreach (float invalid in new[] { -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            pending.remainingCooldownSeconds = invalid;
            pending.EnsureData();
            Check(pending.remainingCooldownSeconds == 0, "invalid saved cooldown is repaired: " + invalid);
        }
        pending.remainingCooldownSeconds = 1000;
        pending.EnsureData();
        Check(pending.remainingCooldownSeconds == 300, "saved cooldown is bounded to five minutes");
    }

    static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        checks.Add(label);
    }
}
