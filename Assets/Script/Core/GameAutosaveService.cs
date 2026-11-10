using System;
using System.IO;
using System.Threading.Tasks;
using MrTGame.Core;
using UnityEngine;

// Serialize on the main thread after gameplay updates; only the immutable JSON
// crosses to the file writer. One logical slot, with an internal recovery copy.
[DisallowMultipleComponent]
public sealed class GameAutosaveService : MonoBehaviour
{
    public const int PayloadVersion = 1;
    const int FlushTimeoutMilliseconds = 3000;
    public static GameAutosaveService Instance { get; private set; }
    public bool HasSave { get; private set; }
    public bool SessionActive { get; private set; }
    public bool IsBusy => GameSessionFlow.Instance != null && GameSessionFlow.Instance.IsBusy;
    public bool IsTestSession => testSession;
    public bool IsIsolatedStorage => isolatedStorage;
    public string StoredFarewellSaveId => HasSave ? storedSnapshot?.farewellSaveId : null;
    public bool StoredFinalEndingCompleted => HasSave && storedSnapshot?.osProcess?.memory?.chapter04Story
        ?.finalTreatmentStage == OSChapter04FinalTreatmentStage.Completed;
    public bool HasEverCompletedGame { get; private set; }
    public string LastError { get; private set; }
    public string SavePath => storage?.MainPath;
    public bool IsWriting => writer != null && !writer.IsCompleted;
    public int StoredStoryAchievementMilestone => HasSave
        ? StoryAchievementProgress.GetReached(storedSnapshot) : 0;
    public event Action StatusChanged;

    GameDataManager data;
    GameAutosaveStorage storage;
    GameSaveData storedSnapshot;
    Task<GameAutosaveWriteResult> writer;
    string writingJson, lastWrittenJson, lastSettingsJson;
    float nextSample;
    int suspensionCount;
    bool initialized, testSession, blockedRead, isolatedStorage, futureSave;
    bool quitting;
    bool warnedCompletionRecord;
    string CompletionRecordPath => Path.Combine(Path.GetDirectoryName(storage.MainPath), "game-completed");

    public void Initialize(GameDataManager manager)
    {
        if (initialized) return;
        Instance = this;data = manager;initialized = true;
        string directory = Path.Combine(Application.persistentDataPath, "Saves");
#if UNITY_EDITOR
        // Editor Play and shipped builds never share the player's real progress.
        directory = Path.Combine(directory, "Editor");
#endif
        storage = new GameAutosaveStorage(directory);
        ReadCompletionRecord();
        ReadStoredSnapshot();
        if (storedSnapshot != null)
        {
            data.SaveData.gameOptions = Clone(storedSnapshot.gameOptions);
            data.SetCurrentLanguage(storedSnapshot.flow.language);
        }
        lastSettingsJson = SettingsJson();
        nextSample = Time.unscaledTime + .5f;
    }

    void LateUpdate()
    {
        if (!initialized || quitting) return;
        FinishWriter(false);
        if (testSession || suspensionCount > 0 || IsBusy || blockedRead || Time.unscaledTime < nextSample) return;
        nextSample = Time.unscaledTime + .5f;
        if (writer != null || GameManager.Instance?.IsTransitioning == true) return;
        string json = BuildSnapshotJson();
        if (json != null && json != lastWrittenJson) QueueWrite(json);
    }

    string BuildSnapshotJson()
    {
        if (data == null) return null;
        if (SessionActive && GameManager.Instance?.Phase != GamePhase.Boot)
        {
            data.SaveData.EnsureData();
            OSSessionPersistence.Capture(data.SaveData);
            GameSessionFlow.Capture(data.SaveData);
            data.SaveData.hasStartedGame = true;
            data.SaveData.saveVersion = PayloadVersion;
            return JsonUtility.ToJson(data.SaveData);
        }
        // Menu changes update preferences in the stored run, never replace its
        // last playable phase with Boot or a newly constructed scene snapshot.
        string settings = SettingsJson();
        if (settings == lastSettingsJson) return null;
        var menuSnapshot = storedSnapshot != null ? Clone(storedSnapshot) : new GameSaveData();
        menuSnapshot.EnsureData();
        menuSnapshot.gameOptions = Clone(data.GameOptions);
        menuSnapshot.flow.language = data.CurrentLanguageState;
        return JsonUtility.ToJson(menuSnapshot);
    }

    [Serializable] sealed class SettingsSnapshot
    { public GameOptionSaveData options; public LanguageState language; }
    string SettingsJson() => JsonUtility.ToJson(new SettingsSnapshot
    { options = data.GameOptions, language = data.CurrentLanguageState });
    static T Clone<T>(T value) => JsonUtility.FromJson<T>(JsonUtility.ToJson(value));

    void QueueWrite(string json)
    {
        writingJson = json;
        var target = storage;
        writer = Task.Run(() => target.Write(json));
    }
    bool FinishWriter(bool wait, int timeoutMilliseconds = -1)
    {
        if (writer == null || (!wait && !writer.IsCompleted)) return true;
        GameAutosaveWriteResult result;
        try
        {
            if (wait && !writer.IsCompleted && !writer.Wait(timeoutMilliseconds))
            {
                // Retain the task and its JSON. A timeout is not cancellation:
                // never start another writer against the same temporary file.
                SetError("Autosave write timed out; the pending write is still running.");
                return false;
            }
            result = writer.GetAwaiter().GetResult();
        }
        catch (Exception error)
        { result = new GameAutosaveWriteResult { Success = false, Error = error.Message }; }
        string json = writingJson;
        writer = null;writingJson = null;
        return AcceptWrite(result, json);
    }
    bool AcceptWrite(GameAutosaveWriteResult result, string json)
    {
        if (!result.Success)
        {
            SetError(result.Error ?? "Could not write autosave.");
            if (result.IsFutureVersion) blockedRead = true;
            nextSample = Time.unscaledTime + 5f;
            return false;
        }
        bool changed = !HasSave || !string.IsNullOrEmpty(LastError);
        bool wasCompleted = HasEverCompletedGame;
        lastWrittenJson = json;
        storedSnapshot = JsonUtility.FromJson<GameSaveData>(json);
        storedSnapshot.EnsureData();
        HasSave = storedSnapshot.hasStartedGame;
        RememberCompletedGame();
        lastSettingsJson = JsonUtility.ToJson(new SettingsSnapshot
        { options = storedSnapshot.gameOptions, language = storedSnapshot.flow.language });
        LastError = null;
        if (changed || wasCompleted != HasEverCompletedGame) StatusChanged?.Invoke();
        return true;
    }

    public bool TryLoad(out GameSaveData snapshot)
    {
        snapshot = null;
        if (!initialized || IsBusy) return false;
        if (!FlushNow()) return false;
        if (!ReadStoredSnapshot() || storedSnapshot?.hasStartedGame != true) return false;
        snapshot = Clone(storedSnapshot);
        snapshot.EnsureData();
        return true;
    }

    bool ReadStoredSnapshot()
    {
        var result = storage.Read();
        storedSnapshot = null;HasSave = false;blockedRead = false;futureSave = false;
        if (!result.Success)
        {
            blockedRead = result.FoundFiles;
            futureSave = result.IsFutureVersion;
            if (result.FoundFiles) SetError(result.Error ?? "Autosave is unavailable.");
            return false;
        }
        try
        {
            var snapshot = JsonUtility.FromJson<GameSaveData>(result.Payload);
            futureSave = snapshot != null && snapshot.saveVersion > PayloadVersion;
            if (snapshot == null || futureSave || snapshot.saveVersion < 1)
                throw new InvalidDataException("Unsupported autosave payload version.");
            snapshot.EnsureData();
            storedSnapshot = snapshot;HasSave = snapshot.hasStartedGame;
            RememberCompletedGame();
            lastWrittenJson = result.Payload;
            LastError = null;
            if (result.RecoveredFromBackup || result.RecoveredFromTemporary)
                Debug.LogWarning("[Autosave] Recovered " + result.Source + ": " + result.RecoveryReason, this);
            return true;
        }
        catch (Exception error)
        { blockedRead = true;SetError(error.Message);return false; }
    }

    void ReadCompletionRecord()
    {
        // This belongs to the local player, not the replaceable New Game slot.
        // Keeping it beside the save also isolates Editor and QA storage.
        HasEverCompletedGame = File.Exists(CompletionRecordPath);
        warnedCompletionRecord = false;
    }

    void RememberCompletedGame()
    {
        // Only a successfully loaded/written ending may unlock the background.
        // Debug jumps and Demo/test runs must not create a permanent receipt.
        if (testSession || GameSetup.IsDemo || GameSetup.IsTestMode || !StoredFinalEndingCompleted)
            return;
        HasEverCompletedGame = true;
        try
        {
            if (File.Exists(CompletionRecordPath)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(CompletionRecordPath));
            // Existence is the receipt. CreateNew never overwrites another run's receipt.
            using (File.Open(CompletionRecordPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { }
        }
        catch (Exception error)
        {
            if (File.Exists(CompletionRecordPath) || warnedCompletionRecord) return;
            warnedCompletionRecord = true;
            Debug.LogWarning("[Autosave] Could not retain the completed-game background: " + error.Message, this);
        }
    }

    public void BeginSession(GameSaveData snapshot)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
        if (futureSave || storage.IsFutureVersion)
            throw new InvalidOperationException("This save was created by a newer game version.");
        snapshot.EnsureData();snapshot.hasStartedGame = true;snapshot.saveVersion = PayloadVersion;
        // An explicit new game may replace corrupt data, but never a newer format.
        blockedRead = storage.IsFutureVersion;
        SessionActive = true;nextSample = 0f;
    }
    public void EndSession() { SessionActive = false;StatusChanged?.Invoke(); }

    public bool FlushNow()
    {
        if (!initialized || testSession) return true;
        if (suspensionCount > 0 || IsBusy) return false;
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        bool previousSucceeded = FinishWriter(true, FlushTimeoutMilliseconds);
        if (writer != null) return false;
        if (blockedRead) return false;
        string json = BuildSnapshotJson();
        if (json == null || json == lastWrittenJson) return previousSucceeded;
        // The final write must also run on the worker. A synchronous disk write
        // here would defeat the timeout when Alt+F4 reaches OnApplicationQuit.
        QueueWrite(json);
        int remaining = Math.Max(0, FlushTimeoutMilliseconds - (int)elapsed.ElapsedMilliseconds);
        return FinishWriter(true, remaining);
    }

    // Use a scope for a temporary in-memory fixture; GameTest jumps instead enter
    // a test session for the remainder of Play Mode, including quit callbacks.
    public IDisposable SuspendWrites()
    {
        FinishWriter(true);suspensionCount++;
        return new Suspension(this);
    }
    sealed class Suspension : IDisposable
    {
        GameAutosaveService owner;
        public Suspension(GameAutosaveService owner) { this.owner = owner; }
        public void Dispose()
        {
            if (owner == null) return;
            owner.suspensionCount = Mathf.Max(0, owner.suspensionCount - 1);
            owner.nextSample = 0f;owner = null;
        }
    }
    public void EnterTestSession()
    {
        FinishWriter(true);testSession = true;
        StatusChanged?.Invoke();
    }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public void UseIsolatedStorageForTests(string directory)
    {
        if (IsBusy) throw new InvalidOperationException("Wait for scene loading before changing the test store.");
        FinishWriter(true);
        storage = new GameAutosaveStorage(directory);isolatedStorage = true;
        testSession = false;SessionActive = false;lastWrittenJson = null;
        ReadCompletionRecord();
        ReadStoredSnapshot();lastSettingsJson = SettingsJson();nextSample = 0f;
    }
    public void ResumeIsolatedWrites()
    {
        if (!isolatedStorage) throw new InvalidOperationException("Only an isolated test store may resume after GameTest jumps.");
        testSession = false;nextSample = 0f;
    }
#endif
    void OnApplicationPause(bool paused) { if (paused && !quitting) FlushNow(); }
    void OnApplicationFocus(bool focused) { if (!focused && !quitting) FlushNow(); }
    void OnApplicationQuit()
    {
        if (quitting) return;
        quitting = true;
        Debug.Log("[Shutdown] Autosave flush started.", this);
        bool saved = FlushNow();
        Debug.Log("[Shutdown] Autosave flush finished: " + (saved ? "saved" : "incomplete") + ".", this);
    }
    void OnDestroy()
    {
        // Quit already spent the final save budget. Do not block a second time
        // during destruction, even if the OS has stalled the worker's disk I/O.
        FinishWriter(!quitting, FlushTimeoutMilliseconds);
        if (quitting) Debug.Log("[Shutdown] Autosave service destroyed.", this);
        if (Instance == this) Instance = null;
    }
    void SetError(string error)
    {
        if (LastError == error) return;
        LastError = error;Debug.LogWarning("[Autosave] " + error, this);
        StatusChanged?.Invoke();
    }
}
