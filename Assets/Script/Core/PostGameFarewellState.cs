using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>A durable receipt for one completed playthrough, retained when New Game replaces the playable slot.</summary>
[Serializable]
public sealed class PostGameFarewellState
{
    public int version = 1;
    public bool completed;
    public string completedRunId;
    public int completedProcessId;
    public long completedProcessStartTimeUtcTicks;
    public bool windowShown;
    public bool windowDismissed;
    public bool deliveryAttempted;
    public bool deliveryStarted;
    public string lastDeliveryError;

    public bool ShouldShow(string currentRunId, bool completingProcessAlive)
        => completed && !windowDismissed && completedRunId != currentRunId && !completingProcessAlive;

    public static bool CanRun(bool editor, bool demo, bool testMode, bool testSession, bool isolated)
        => !editor && !demo && !testMode && !testSession && !isolated;

    public bool OwnerIsAlive()
    {
        if (!completed || completedProcessId <= 0) return false;
        try
        {
            using (var process = Process.GetProcessById(completedProcessId))
                return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == completedProcessStartTimeUtcTicks;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        // An inaccessible process is not evidence that the previous game exited.
        catch { return true; }
    }
}

public sealed class PostGameFarewellStateStore
{
    public sealed class CompletedReceipt
    {
        public readonly string SaveId;
        public readonly PostGameFarewellStateStore Store;
        public readonly PostGameFarewellState State;
        public CompletedReceipt(string saveId, PostGameFarewellStateStore store, PostGameFarewellState state)
        { SaveId = saveId; Store = store; State = state; }
    }

    // Recover the existing per-playthrough ledgers, including the pre-ID legacy
    // ledger. The current autosave may already belong to an unfinished new game.
    public static List<CompletedReceipt> ReadCompleted(string root, Action<string> reportError)
    {
        var result = new List<CompletedReceipt>();
        void Read(string saveId)
        {
            var candidate = new PostGameFarewellStateStore(PostGameFarewellDelivery.GetSaveStateDirectory(root, saveId));
            if (!File.Exists(candidate.FilePath)) return;
            if (!candidate.TryRead(out var receipt, out var error))
            { reportError?.Invoke(candidate.FilePath + ": " + error); return; }
            if (receipt.completed) result.Add(new CompletedReceipt(saveId, candidate, receipt));
        }
        try
        {
            Read(string.Empty);
            string saves = Path.Combine(root, "saves");
            if (!Directory.Exists(saves)) return result;
            string[] directories = Directory.GetDirectories(saves);
            Array.Sort(directories, StringComparer.Ordinal);
            foreach (string directory in directories)
            {
                string saveId = Path.GetFileName(directory);
                if (Guid.TryParseExact(saveId, "N", out _)) Read(saveId);
            }
        }
        catch (Exception error) { reportError?.Invoke(error.Message); }
        return result;
    }

    public string DirectoryPath { get; }
    public string FilePath => Path.Combine(DirectoryPath, "lifecycle.json");
    public PostGameFarewellStateStore(string directory) => DirectoryPath = Path.GetFullPath(directory);

    public bool TryRead(out PostGameFarewellState state, out string error)
    {
        state = null; error = null;
        try
        {
            if (!File.Exists(FilePath)) { state = new PostGameFarewellState(); return true; }
            state = JsonUtility.FromJson<PostGameFarewellState>(File.ReadAllText(FilePath, Encoding.UTF8));
            if (state == null || state.version != 1 || state.completed && string.IsNullOrEmpty(state.completedRunId))
                throw new InvalidDataException("Unsupported or incomplete farewell receipt.");
            return true;
        }
        catch (Exception exception) { error = exception.Message; state = null; return false; }
    }

    /// <summary>Merge monotonic receipts under a local lock; another instance cannot undo showing or dismissing the window.</summary>
    public bool TrySave(PostGameFarewellState proposed, out PostGameFarewellState saved, out string error)
    {
        saved = null; error = null;
        string temporary = null;
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            using (var gate = new FileStream(Path.Combine(DirectoryPath, "lifecycle.lock"), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None))
            {
                if (!TryRead(out var current, out error)) return false;
                var merged = JsonUtility.FromJson<PostGameFarewellState>(JsonUtility.ToJson(proposed));
                if (current.completed)
                {
                    merged.completed = true;
                    merged.completedRunId = current.completedRunId;
                    merged.completedProcessId = current.completedProcessId;
                    merged.completedProcessStartTimeUtcTicks = current.completedProcessStartTimeUtcTicks;
                }
                merged.windowShown |= current.windowShown;
                merged.windowDismissed |= current.windowDismissed;
                merged.deliveryAttempted |= current.deliveryAttempted;
                merged.deliveryStarted |= current.deliveryStarted;
                temporary = Path.Combine(DirectoryPath, "lifecycle." + Guid.NewGuid().ToString("N") + ".tmp");
                File.WriteAllText(temporary, JsonUtility.ToJson(merged, true), new UTF8Encoding(false));
                if (File.Exists(FilePath)) File.Replace(temporary, FilePath, null);
                else File.Move(temporary, FilePath);
                temporary = null; saved = merged;
                return true;
            }
        }
        catch (Exception exception) { error = exception.Message; return false; }
        finally { if (temporary != null && File.Exists(temporary)) try { File.Delete(temporary); } catch { } }
    }
}
