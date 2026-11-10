using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MrTGame.Core;
using UnityEngine;

// Fault-injection checks: no player save, native Steam session, or scene changes.
public static class GameShutdownQA
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    const BindingFlags StaticHidden = BindingFlags.Static | BindingFlags.NonPublic;

    public static string Run()
    {
        if (Application.isPlaying) return "Run outside Play Mode.";
        var checks = new List<string>();
        string directory = Path.GetFullPath("Temp/OrdinaryExitFix/QA/" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var root = new GameObject("Shutdown QA") { hideFlags = HideFlags.HideAndDontSave };
        root.SetActive(false);
        var data = root.AddComponent<GameDataManager>();
        var saves = root.AddComponent<GameAutosaveService>();
        var storage = new GameAutosaveStorage(directory);
        Set(saves, "data", data);
        Set(saves, "storage", storage);
        Set(saves, "initialized", true);
        Set(saves, "isolatedStorage", true);
        object gate = Get(storage, "gate");
        bool locked = false;
        try
        {
            Check(saves.FlushNow(), "normal final write succeeds", checks);
            Check(storage.Read().Success, "final write produces a valid recoverable save", checks);
            string original = File.ReadAllText(storage.MainPath);
            Check(saves.FlushNow() && !saves.IsWriting, "unchanged flush needs no worker", checks);

            // The real file writer blocks on its own storage gate, simulating
            // a hung disk operation without touching the player's filesystem.
            Monitor.Enter(gate); locked = true;
            data.GameOptions.musicVolume = 31;
            var elapsed = Stopwatch.StartNew();
            Check(!saves.FlushNow(), "blocked final write reports failure", checks);
            Check(elapsed.ElapsedMilliseconds >= 2800 && elapsed.ElapsedMilliseconds < 4500,
                "blocked final write returns within its three-second budget", checks);
            var pending = (Task<GameAutosaveWriteResult>)Get(saves, "writer");
            string pendingJson = (string)Get(saves, "writingJson");
            Check(pending != null && !pending.IsCompleted, "timeout retains the outstanding writer", checks);
            Check(File.ReadAllText(storage.MainPath) == original, "timeout preserves the last committed save", checks);

            data.GameOptions.musicVolume = 47;
            elapsed.Restart();
            Invoke(saves, "OnApplicationQuit");
            Check(elapsed.ElapsedMilliseconds < 4500, "Alt+F4 callback also returns within its save budget", checks);
            Check(ReferenceEquals(pending, Get(saves, "writer")) && pendingJson == (string)Get(saves, "writingJson"),
                "quit does not overlap or overwrite the stalled write", checks);
            elapsed.Restart();
            Invoke(saves, "OnDestroy");
            Invoke(saves, "OnApplicationFocus", false);
            Invoke(saves, "OnApplicationPause", true);
            Invoke(saves, "OnApplicationQuit");
            Check(elapsed.ElapsedMilliseconds < 250, "destruction and repeated lifecycle callbacks do not wait again", checks);

            Monitor.Exit(gate); locked = false;
            Check(pending.Wait(5000), "original writer can finish after the stall clears", checks);
            Invoke(saves, "FinishWriter", false, -1);
            Check(!saves.IsWriting && saves.LastError == null, "late success is accepted and clears the error", checks);
            Check(JsonUtility.FromJson<GameSaveData>(storage.Read().Payload).gameOptions.musicVolume == 31,
                "late completion retains its own immutable snapshot", checks);
            Set(saves, "quitting", false);
            Check(saves.FlushNow(), "a subsequent flush can commit newer state", checks);
            Check(JsonUtility.FromJson<GameSaveData>(storage.Read().Payload).gameOptions.musicVolume == 47,
                "subsequent save contains the latest state", checks);

            CheckSteamTeardown(root, checks);
            string result = "PASS: " + checks.Count + " shutdown checks\n" + string.Join("\n", checks);
            File.WriteAllText(Path.Combine(directory, "results.txt"), result);
            return result + "\n" + directory;
        }
        finally
        {
            if (locked) Monitor.Exit(gate);
            var writer = Get(saves, "writer") as Task;
            if (writer != null) writer.Wait(5000);
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    static void CheckSteamTeardown(GameObject root, List<string> checks)
    {
        Type type = typeof(SteamManager);
        var guard = type.GetField("s_ShuttingDown", StaticHidden);
        if (guard == null) return;
        var instance = type.GetField("s_instance", StaticHidden);
        var ever = type.GetField("s_EverInitialized", StaticHidden);
        object oldGuard = guard.GetValue(null), oldInstance = instance.GetValue(null), oldEver = ever.GetValue(null);
        // The inactive object never runs Awake or initializes the native API.
        var steam = root.AddComponent<SteamManager>();
        try
        {
            guard.SetValue(null, false);
            instance.SetValue(null, steam);
            Invoke(steam, "OnApplicationQuit");
            Check((bool)guard.GetValue(null) && !SteamManager.Initialized, "quit rejects new Steam API users", checks);
            Invoke(steam, "OnDestroy");
            Check(instance.GetValue(null) == null && !SteamManager.Initialized,
                "late Steam queries cannot resurrect the destroyed singleton", checks);
            Invoke(steam, "OnDestroy");
            Check(instance.GetValue(null) == null, "duplicate Steam destruction is harmless", checks);
            type.GetMethod("InitOnPlayMode", StaticHidden).Invoke(null, null);
            Check(!(bool)guard.GetValue(null), "next Play session clears the quit guard", checks);
        }
        finally
        {
            instance.SetValue(null, oldInstance);
            guard.SetValue(null, oldGuard);
            ever.SetValue(null, oldEver);
            UnityEngine.Object.DestroyImmediate(steam);
        }
    }

    static object Get(object target, string name) => target.GetType().GetField(name, Hidden).GetValue(target);
    static void Set(object target, string name, object value) => target.GetType().GetField(name, Hidden).SetValue(target, value);
    static object Invoke(object target, string name, params object[] args)
        => target.GetType().GetMethod(name, Hidden).Invoke(target, args);
    static void Check(bool value, string description, List<string> checks)
    {
        if (!value) throw new InvalidOperationException(description);
        checks.Add(description);
    }
}
