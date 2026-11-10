using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using MrTGame.Core;
using MrTGame.Systems;
using UnityEngine;

// Explicit Editor-only integration run. Every disk write stays in a fresh Temp
// directory. The service remains a test session afterward until Play Mode exits.
public static class GameAutosavePersistenceQA
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly List<string> checks = new List<string>();
    public static string Status { get; private set; } = "idle";
    public static string OutputDirectory { get; private set; }

    public static string Start()
    {
        if (Status == "running") return Status;
        var saves = GameAutosaveService.Instance;
        if (!Application.isPlaying || saves == null || GameDataManager.Instance == null
            || GameSessionFlow.Instance == null || GameTest.Instance == null)
            return "Play Mode and game initialization required";
        if (saves.IsBusy || saves.IsWriting || GameManager.Instance.IsTransitioning)
            return "Wait for the current transition/save, then retry";
        checks.Clear();Status = "running";
        GameDataManager.Instance.StartCoroutine(Guarded());
        return Status;
    }

    static IEnumerator Guarded()
    {
        GameDataManager data = GameDataManager.Instance;
        GameAutosaveService saves = GameAutosaveService.Instance;
        GameSaveData original = data.SaveData;
        LanguageState originalLanguage = data.CurrentLanguageState;
        bool originalBackground = Application.runInBackground;
        GameResourceSystem resources = GameResourceSystem.Instance;
        bool resourceWasEnabled = resources != null && resources.enabled;
        string originalPath = saves.SavePath;
        string[] originalFiles = { originalPath, originalPath + ".bak", originalPath + ".tmp" };
        string[] originalHashes = originalFiles.Select(Fingerprint).ToArray();
        var stack = new Stack<IEnumerator>();
        OutputDirectory = Path.GetFullPath(Path.Combine("Temp", "GameAutosavePersistenceQA", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(OutputDirectory);
        saves.EnterTestSession();
        saves.UseIsolatedStorageForTests(OutputDirectory);
        saves.EnterTestSession();
        data.ReplaceSaveData(JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(original)));
        Application.runInBackground = true;
        if (resources != null) resources.enabled = false;
        stack.Push(Run());
        try
        {
            while (stack.Count > 0)
            {
                object next;
                try
                {
                    if (!stack.Peek().MoveNext()) { (stack.Pop() as IDisposable)?.Dispose();continue; }
                    next = stack.Peek().Current;
                }
                catch (Exception error)
                {
                    Status = "FAILED: " + error;
                    Debug.LogError("[Autosave QA] " + Status);
                    yield break;
                }
                if (next is IEnumerator nested) stack.Push(nested);
                else yield return next;
            }
            Check(originalFiles.Select(Fingerprint).SequenceEqual(originalHashes), "normal Editor/player save files remain byte-for-byte unchanged");
            Status = "PASS: " + checks.Count + " persistence checks";
            Debug.Log("[Autosave QA] " + Status);
        }
        finally
        {
            while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
            saves.EnterTestSession();saves.EndSession();
            data.ReplaceSaveData(original);
            data.SetCurrentLanguage(originalLanguage);
            if (resources != null) resources.enabled = resourceWasEnabled;
            Application.runInBackground = originalBackground;
            File.WriteAllText(Path.Combine(OutputDirectory, "results.txt"), Status + "\n" + string.Join("\n", checks));
        }
    }

    static IEnumerator Run()
    {
        var data = GameDataManager.Instance;
        var saves = GameAutosaveService.Instance;
        var flow = GameSessionFlow.Instance;
        Check(GameTest.Instance.JumpToCheckpoint(GameTestCheckpoint.Chapter01BeforeInteractivePicture), "create isolated Chapter 1 fixture");
        yield return Wait(() => !GameTest.Instance.IsCheckpointJumpRunning && OS_Handle.Instance != null
            && !GameManager.Instance.IsTransitioning, "chapter fixture ready", 60f);
        Check(saves.IsTestSession, "GameTest checkpoint disables persistent writes");
        var os = OS_Handle.Instance;
        os.LinkMessageController.SetGlobalMessagesSuppressed(true);
        os.LinkMessageController.NotifyOSHidden();
        data.OSProcessData.installedToolProgramIds.Add(OSToolProgramIds.EnergyLoop);
        data.OSProcessData.installedToolProgramIds.Add(OSToolProgramIds.Gramophone);
        data.DiscoverPaperPush();Check(data.UnlockPaperPush(), "fixture unlocks Paper Push");
        data.SetCurrentLanguage(LanguageState.ja);
        data.SetGameOptionVolume(GameOptionVolumeType.Music, 31);
        data.SetOSResourceValue(ResourceType.Heart, 73);
        data.SetOSResourceValue(ResourceType.Battery, 42);
        data.SetOSResourceValue(ResourceType.Fault, 13);
        Check(data.TryBeginEnergyCharge(), "begin five-minute charge");
        data.OSEnergyLoopState.remainingChargeSeconds = 173.25f;
        data.OSEnergyLoopState.paperPushMatch = new OSPaperPushMatchSaveData
        {
            active = true, chapter = 1, phase = 1, round = 4,
            playerX = -24f, characterX = 43f, playerMove = 1, characterMove = 2,
            previousPlayerMove = 0, characterHadLead = true
        };

        OSWindowUnit gramophone = Field<OSWindowUnit>(os, "gramophoneWindow");
        OSWindowUnit paper = Field<OSWindowUnit>(os, "paperPushWindow");
        OSWindowUnit energy = Field<OSWindowUnit>(os, "energyLoopWindow");
        OSWindowUnit memory = Field<OSWindowUnit>(os, "memoryWindow");
        Check(gramophone != null && paper != null && energy != null && memory != null, "authored window references exist");
        gramophone.OpenStandalone();energy.OpenStandalone();
        yield return Wait(() => !gramophone.IsOpening && !energy.IsOpening, "fixture windows opened");
        var player = os.GetComponent<OSGramophonePlayer>();
        Check(player != null, "shared music player exists");
        // The home illustration opens asynchronously after the checkpoint jump.
        // Let its one-time Doorway cue run before choosing the record to persist.
        yield return Wait(() => data.OSGramophoneState.chapter01DoorwayMusicInitialized,
            "home entrance music initialized before manual selection");
        player.PlayTrack(OSGramophonePlayer.Secret);
        yield return Wait(() => player.CurrentTrackId == OSGramophonePlayer.Secret && player.IsPlaying,
            "manual music selection starts after home cue");
        player.Seek(12.5f);
        if (player.IsPlaying) player.TogglePlayback();
        gramophone.Minimize();
        Check(os.OpenPaperPushWindow(), "open paper window");
        yield return Wait(() => paper.IsOpen && !paper.IsOpening, "paper window ready");
        Check(player.AvailableTracks.Contains(OSGramophonePlayer.PaperPush), "opening paper collects its music");
        var ending = new OSChapter01EndingSaveData { phase = OSChapter01EndingPhase.Door, slideIndex = 2 };
        data.OSProcessData.chapter01Ending = ending;
        yield return Wait(() => os.Chapter01Ending.IsPresenting && os.Chapter01Ending.Phase == OSChapter01EndingPhase.Door,
            "ending fixture waits at door");

        saves.ResumeIsolatedWrites();saves.BeginSession(data.SaveData);
        Check(saves.FlushNow(), "write complete logical snapshot to isolated disk");
        saves.EndSession();
        GameSaveData saved = ReadSnapshot();
        Check(saved.hasStartedGame && saved.flow.phase == GamePhase.OSMain, "save identifies playable OS phase");
        Check(saved.osResources.heart == 73 && saved.osResources.energy == 42 && saved.osResources.error == 13,
            "all three resources are persisted");
        Check(saved.osProcess.chapter01Ending.phase == OSChapter01EndingPhase.Door, "door checkpoint is persisted");
        Check(saved.osResume.windows.Any(x => x.windowId == gramophone.WindowId && x.minimized)
            && saved.osResume.windows.Any(x => x.windowId == paper.WindowId && !x.minimized), "open and minimized windows are persisted");
        Check(saved.osProcess.energyLoop.paperPushMatch.active && saved.osProcess.energyLoop.paperPushMatch.round == 4,
            "active Paper Push round is persisted");
        Check(saved.osProcess.gramophone.paperPushCollected
            && saved.osProcess.gramophone.manuallyPlayedTrackIds.Contains(OSGramophonePlayer.Secret)
            && !saved.osProcess.gramophone.manuallyPlayedTrackIds.Contains(OSGramophonePlayer.PaperPush), "music collection and unplayed markers are persisted");
        Check(saved.osProcess.gramophone.selectedTrackId == OSGramophonePlayer.Secret
            && !saved.osProcess.gramophone.wantsPlayback, "manual paused selection is persisted before reload");
        float chargeAtSave = saved.osProcess.energyLoop.remainingChargeSeconds;
        string gramophoneId = gramophone.WindowId, paperId = paper.WindowId;

        // These in-memory changes are deliberately discarded: EndSession + menu
        // must use the last disk checkpoint, not overwrite it with stale UI data.
        data.SetOSResourceValue(ResourceType.Heart, 1);
        data.SetOSResourceValue(ResourceType.Battery, 1);
        data.OSEnergyLoopState.isCharging = false;
        data.OSEnergyLoopState.remainingChargeSeconds = 5;
        data.OSProcessData.chapter01Ending = new OSChapter01EndingSaveData();
        data.OSGramophoneState.manuallyPlayedTrackIds.Clear();
        Check(flow.ReturnToMainMenu(), "return to menu without overwriting ended session");
        yield return WaitMenu();
        yield return new WaitForSecondsRealtime(2f);
        Check(saves.FlushNow(), "menu flush succeeds");
        GameSaveData menu = ReadSnapshot();
        Check(menu.flow.phase == GamePhase.OSMain && menu.osResources.energy == 42
            && menu.osProcess.chapter01Ending.phase == OSChapter01EndingPhase.Door, "menu preserves saved run instead of serializing Boot/stale memory");
        Check(Mathf.Approximately(menu.osProcess.energyLoop.remainingChargeSeconds, chargeAtSave), "charge does not advance while on main menu/offline");

        // Deliberately continue in the same frame as a settings change, before
        // the periodic autosave sample can run.
        data.SetCurrentLanguage(LanguageState.de);
        data.SetGameOptionVolume(GameOptionVolumeType.Music, 17);
        Check(flow.ContinueGame(), "continue loads the real saved snapshot");
        yield return Wait(() => !flow.IsBusy && !GameManager.Instance.IsTransitioning && saves.SessionActive,
            "continue rebuilds scenes and activates session", 75f);
        os = OS_Handle.Instance;
        yield return Wait(() => os.Chapter01Ending.IsPresenting && os.Chapter01Ending.Phase == OSChapter01EndingPhase.Door,
            "door checkpoint resumes after scene reconstruction");
        Check(data.GetOSResourceValue(ResourceType.Heart) == 73 && data.GetOSResourceValue(ResourceType.Battery) == 42
            && data.GetOSResourceValue(ResourceType.Fault) == 13, "continue restores all resources");
        Check(data.CurrentLanguageState == LanguageState.de && data.GetGameOptionVolume(GameOptionVolumeType.Music) == 17,
            "immediate continue preserves newly changed menu preferences");
        Check(data.OSEnergyLoopState.isCharging && data.OSEnergyLoopState.remainingChargeSeconds <= chargeAtSave
            && data.OSEnergyLoopState.remainingChargeSeconds > chargeAtSave - 3f, "charge resumes from saved remaining time without offline catch-up");
        gramophone = os.GetComponentsInChildren<OSWindowUnit>(true).First(x => x.WindowId == gramophoneId);
        paper = os.GetComponentsInChildren<OSWindowUnit>(true).First(x => x.WindowId == paperId);
        Check(gramophone.IsOpen && gramophone.IsMinimized && paper.IsOpen && !paper.IsMinimized,
            "window open/minimized states reconstructed");
        var match = data.OSEnergyLoopState.paperPushMatch;
        var game = os.GetComponent<OSPaperPushWindowController>();
        Check(match.active && match.phase == 1 && match.round == 4 && game != null && game.RoundNumber == 4
            && Mathf.Approximately(game.PlayerPosition, -24) && Mathf.Approximately(game.CharacterPosition, 43),
            "Paper Push restores choice checkpoint and pawn positions");
        Check(data.OSGramophoneState.paperPushCollected && data.OSGramophoneState.selectedTrackId == OSGramophonePlayer.Secret
            && data.OSGramophoneState.manuallyPlayedTrackIds.Contains(OSGramophonePlayer.Secret)
            && !data.OSGramophoneState.manuallyPlayedTrackIds.Contains(OSGramophonePlayer.PaperPush),
            "music selection/collection/new markers restored");
        Check(!data.OSGramophoneState.wantsPlayback && Mathf.Abs(data.OSGramophoneState.playbackSeconds - saved.osProcess.gramophone.playbackSeconds) < .3f,
            "paused music playhead survives reconstruction");

        // Claim, flush, recreate the whole OS again, and claim a second time.
        // The flag and resource delta must be committed in the same snapshot.
        data.OSProcessData.chapter01Ending = new OSChapter01EndingSaveData();
        yield return null;
        data.OSEnergyLoopState.isCharging = false;
        data.OSEnergyLoopState.chargeReadyToClaim = true;
        data.OSEnergyLoopState.remainingChargeSeconds = 0;
        data.SetOSResourceValue(ResourceType.Battery, 50);
        int gained;
        Check(data.TryClaimEnergyCharge(out gained) && gained == 25 && data.GetOSResourceValue(ResourceType.Battery) == 75,
            "claim grants exactly 25 energy");
        Check(!data.TryClaimEnergyCharge(out gained), "second click cannot grant twice");
        Check(saves.FlushNow(), "claimed flag and resource total flush atomically");
        Check(flow.ReturnToMainMenu(), "return after claimed reward");
        yield return WaitMenu();
        Check(flow.ContinueGame(), "reload claimed reward checkpoint");
        yield return Wait(() => !flow.IsBusy && !GameManager.Instance.IsTransitioning && saves.SessionActive,
            "claimed checkpoint reconstructed", 75f);
        Check(data.GetOSResourceValue(ResourceType.Battery) == 75 && !data.OSEnergyLoopState.chargeReadyToClaim
            && !data.TryClaimEnergyCharge(out gained), "claim receipt persists across full reload without duplicate energy");
        Check(flow.ReturnToMainMenu(), "finish integration run at main menu");
        yield return WaitMenu();
    }

    static GameSaveData ReadSnapshot()
    {
        var result = new GameAutosaveStorage(OutputDirectory).Read();
        if (!result.Success) throw new InvalidOperationException("Read isolated save: " + result.Error);
        return JsonUtility.FromJson<GameSaveData>(result.Payload);
    }

    static IEnumerator WaitMenu()
    {
        yield return Wait(() => GameManager.Instance.Phase == GamePhase.Boot && !GameManager.Instance.IsTransitioning
            && !GameSessionFlow.Instance.IsBusy, "main menu settled", 60f);
    }

    static IEnumerator Wait(Func<bool> ready, string label, float seconds = 20f)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (!ready() && Time.realtimeSinceStartup < deadline) yield return null;
        Check(ready(), label);
    }

    static T Field<T>(object owner, string name)
    {
        var field = owner.GetType().GetField(name, Private);
        if (field == null) throw new MissingFieldException(owner.GetType().Name, name);
        return (T)field.GetValue(owner);
    }

    static void Check(bool success, string label)
    {
        if (!success) throw new InvalidOperationException(label);
        checks.Add(label);
    }

    static string Fingerprint(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "missing";
        using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(path)));
    }
}
