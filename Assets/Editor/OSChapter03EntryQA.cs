using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using DG.Tweening;
using MrTGame.Core;
using MrTGame.Systems;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Step = OSChapter03EntryDirector.EntryStep;

// Opt-in integration checks of the release handoff. All saved fixtures and
// any disk writes are isolated beneath Temp; never exercise a player's save.
public static class OSChapter03EntryQA
{
    sealed class HoverPointerOwner : ScriptableObject { }

    const string Prefix = "chapter03_entry_";
    const string Package = "memory-repair-4";
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly List<string> checks = new List<string>();
    static readonly Dictionary<string, string> snapshots = new Dictionary<string, string>();
    static readonly List<object> observations = new List<object>();
    static OS_Handle os;
    static OSChapter03EntryDirector entry;
    static OSChapter02FinalTalkDirector talk;
    static OSChapter02NightfallPresentation night;
    static OSLinkMessageController link;
    static OSWindowUnit live, settings, stateWindow, memoryWindow, toolWindow;
    static DesktopProcessUnit stateIcon, memoryIcon;
    static GameSetup setup;
    static Mouse testMouse;
    static Keyboard testKeyboard;
    static string context;
    static int routes;
    public static string Status { get; private set; } = "idle";
    public static string OutputDirectory { get; private set; }

    public static string Start()
    {
        if (Status == "running") return Status;
        var saves = GameAutosaveService.Instance;
        if (!Application.isPlaying || saves == null || GameDataManager.Instance == null
            || GameManager.Instance == null || GameSessionFlow.Instance == null
            || SceneLoadManager.Instance == null || GameTest.Instance == null)
            return "Play Mode initialization required";
        if (saves.IsBusy || saves.IsWriting || GameManager.Instance.IsTransitioning
            || SceneLoadManager.Instance.IsBusy || GameTest.Instance.IsCheckpointJumpRunning)
            return "Wait for active save/scene operation";
        setup = typeof(GameSetup).GetField("activeSetup", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as GameSetup;
        if (setup == null) return "Active GameSetup is required";
        checks.Clear(); snapshots.Clear(); observations.Clear(); routes = 0;
        context = "initialization";
        OutputDirectory = Path.GetFullPath(Path.Combine("Temp", "OSChapter03EntryQA", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(OutputDirectory);
        Status = "running";
        GameDataManager.Instance.StartCoroutine(Guarded());
        return Status;
    }

    static IEnumerator Guarded()
    {
        var data = GameDataManager.Instance;
        var saves = GameAutosaveService.Instance;
        var original = data.SaveData;
        var language = data.CurrentLanguageState;
        var resources = GameResourceSystem.Instance;
        bool resourcesEnabled = resources != null && resources.enabled;
        bool background = Application.runInBackground;
        bool demo = (bool)Get(setup, "isDemo");
        var previousMouse = Mouse.current;
        var previousKeyboard = Keyboard.current;
        string[] paths = { saves.SavePath, saves.SavePath + ".bak", saves.SavePath + ".tmp" };
        string[] hashes = null;
        var work = new Stack<IEnumerator>();
        try
        {
            saves.EnterTestSession(); hashes = paths.Select(Hash).ToArray();
            saves.UseIsolatedStorageForTests(OutputDirectory); saves.EnterTestSession();
            data.ReplaceSaveData(Clone(original));
            Application.runInBackground = true;
            Set(setup, "isDemo", false);
            if (resources != null) resources.enabled = false;
            testMouse = InputSystem.AddDevice<Mouse>("Chapter03EntryQAMouse");
            testKeyboard = InputSystem.AddDevice<Keyboard>("Chapter03EntryQAKeyboard");
            MouseReleased();
            work.Push(Run());
            while (work.Count > 0)
            {
                object next;
                try
                {
                    if (!work.Peek().MoveNext()) { (work.Pop() as IDisposable)?.Dispose(); continue; }
                    next = work.Peek().Current;
                }
                catch (Exception error)
                {
                    Status = "FAILED at " + context + "\n" + Describe() + "\n" + error;
                    Debug.LogError("[Chapter03 Entry QA] " + Status);
                    yield break;
                }
                if (next is IEnumerator nested) work.Push(nested); else yield return next;
            }
            Check(paths.Select(Hash).SequenceEqual(hashes), "original save files unchanged");
            Status = "PASS: " + checks.Count + " checks, " + routes + " answer routes";
        }
        finally
        {
            while (work.Count > 0) (work.Pop() as IDisposable)?.Dispose();
            try
            {
                InvokeOptional(entry, "ResetPresentation");
                talk?.ResetPresentation(); night?.ResetPresentation();
                GameTest.Instance?.GetComponent<GameTestConsole>()?.SetOpen(false);
                saves.EnterTestSession(); saves.EndSession();
                GameManager.Instance?.OpenMainMenuWithoutSave();
                data.ReplaceSaveData(original); data.SetCurrentLanguage(language);
                if (setup != null) Set(setup, "isDemo", demo);
                if (resources != null) resources.enabled = resourcesEnabled;
                Application.runInBackground = background;
                if (testMouse != null && testMouse.added) InputSystem.RemoveDevice(testMouse);
                if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
                if (testKeyboard != null && testKeyboard.added) InputSystem.RemoveDevice(testKeyboard);
                if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
                if (!ReferenceEquals(data.SaveData, original) || data.CurrentLanguageState != language || GameSetup.IsDemo != demo)
                    throw new InvalidOperationException("Original memory/language/demo mode was not restored");
                if (hashes != null && !paths.Select(Hash).SequenceEqual(hashes)) throw new IOException("Original save files changed");
                checks.Add("cleanup restored memory, language, mode, resources, input devices, and original save hashes");
            }
            catch (Exception cleanup) { Status = "FAILED during cleanup: " + cleanup + "\n" + Status; }
            if (Status == "running") Status = "FAILED: interrupted at " + context;
            File.WriteAllText(Path.Combine(OutputDirectory, "results.json"), Newtonsoft.Json.JsonConvert.SerializeObject(new {
                status = Status, context, checks, routes, snapshots = snapshots.Keys.ToArray(), observations,
                originalFiles = paths, originalHashes = hashes, finalHashes = paths.Select(Hash).ToArray(),
                testSession = saves.IsTestSession,
                note = "Screen captures are queued at real visible handoff boundaries. Exit Play Mode after this isolated run."
            }, Newtonsoft.Json.Formatting.Indented));
            File.WriteAllText(Path.Combine(OutputDirectory, "results.txt"), Status + "\n" + string.Join("\n", checks));
            Debug.Log("[Chapter03 Entry QA] " + Status + " | " + OutputDirectory);
            os = null; entry = null; talk = null; night = null; link = null; setup = null; testMouse = null; testKeyboard = null;
            live = settings = stateWindow = memoryWindow = toolWindow = null; stateIcon = memoryIcon = null;
        }
    }

    static IEnumerator Run()
    {
        context = "checkpoint";
        Check(GameTest.Instance.JumpToCheckpoint(GameTestCheckpoint.Chapter02FinalTalk), "final-talk checkpoint accepted");
        yield return Wait(() => !GameTest.Instance.IsCheckpointJumpRunning && !GameManager.Instance.IsTransitioning
            && !SceneLoadManager.Instance.IsBusy && OS_Handle.Instance != null, "checkpoint ready", 80f);
        Check(GameTest.Instance.JumpSucceeded && GameAutosaveService.Instance.IsTestSession,
            "checkpoint succeeds with formal-save writes disabled");
        Bind();
        GameDataManager.Instance.SetCurrentLanguage(LanguageState.zh_CN);
        ValidateContent();
        var boundary = Capture();
        OSChapter02FinalTalkDirector.PrepareDebugCheckpoint(boundary.osProcess.memory, boundary.osProcess.energyLoop, 1);
        boundary.osProcess.memory.chapter02FinalTalkStep = (int)OSChapter02FinalTalkDirector.FinalTalkStep.Chapter03Ready;
        boundary.osProcess.memory.chapter03EntryStep = (int)Step.Inactive;
        boundary.osProcess.memory.chapter03EntryInstallAnswer = 0;
        boundary.osProcess.currentChapterIndex = 2;
        boundary.osProcess.characterInteraction = new OSCharacterInteractionSaveData();
        boundary.osProcess.installedToolProgramIds.RemoveAll(x => x == Package);
        boundary.osResources.energy = 0;
        boundary.osResume.captured = false;
        Install(boundary);
        yield return null;
        MouseReleased(); Focus(); talk.DebugStep(); entry.DebugStep();
        yield return null;
        Focus(); talk.DebugStep(); entry.DebugStep();
        Check(entry.Step == Step.Inactive && !GameSetup.IsDemo, "release fixture waits for the player's single click");
        Check(os.OpenMemoryWindow(), "prior Memory can be open before the release handoff");
        Settle();
        string resourcesBefore = ResourceState();
        int oldNode = GameDataManager.Instance.CurrentOSMemoryDataIndex;

        context = "single click and thunder";
        int endEvents = 0;
        Action ended = () => endEvents++;
        night.EndReached += ended;
        try
        {
            DesktopClick(stateIcon);
            Check(entry.Step == Step.Thunder && night.IsThundering, "one desktop click begins release thunder");
            Check(!stateWindow.IsOpen, "StateCheck waits until thunder finishes");
            Check(os.IsSystemInputLocked, "thunder prevents conflicting OS input");
            DesktopClick(stateIcon);
            yield return Wait(() => entry.Step == Step.Important && stateWindow.IsOpen,
                "release thunder opens StateCheck and important message", 15f);
            yield return Wait(() => !stateWindow.IsOpening && IsMessage("important"), "important message is visible");
            Check(endEvents == 1 && night.HasEnded && !os.Chapter02DemoEnding.IsPresenting,
                "one thunder completion hands off without the Demo card");
            Check(!GameDataManager.Instance.OSProcessData.memory.chapter02DemoEndReached,
                "release entry does not write Demo completion");
            Check(GameDataManager.Instance.CurrentChapterIndex == 3
                && GameDataManager.Instance.CurrentOSMemoryDataIndex == oldNode,
                "chapter-three unlock is offered before its memory begins");
            Check((float)Get(night, "lightScale") > .99f && night.IsNight,
                "release handoff preserves window and cursor illumination");
            var rain = Get(night, "rain") as AudioSource;
            Check(rain != null && rain.loop && rain.volume > 0f,
                "quiet rain continues beneath release handoff");
            var sameRain = rain;
            night.RestoreEntryNight(); night.RestoreEntryNight();
            Check(ReferenceEquals(Get(night, "rain"), sameRain) && endEvents == 1,
                "restoring entry night reuses its rain and does not replay thunder");
        }
        finally { if (night != null) night.EndReached -= ended; }

        context = "important message holds for five seconds";
        ChapterClick();
        Check(entry.Step == Step.Important && !entry.CanUnlock,
            "early chapter click cannot skip the important message");
        FinishMessage("important", false);
        float completedAt = Time.realtimeSinceStartup;
        yield return Hold(4.7f);
        Check(entry.Step == Step.Important && link.LinkWindow.IsOpen && !entry.CanUnlock,
            "important message remains for at least 4.7 seconds after full text");
        yield return Wait(() => entry.Step == Step.AwaitUnlock, "important closes after its authored reading hold", 3f);
        Check(Time.realtimeSinceStartup - completedAt >= 4.9f && entry.CanUnlock,
            "unlock becomes available after the full five-second text hold");
        yield return Wait(() => !os.CursorRelease.ScriptActorIsMoving, "red cursor settles beside chapter option");
        CheckCursorOutsideChapter();
        Remember("await-unlock");
        yield return Screenshot("01-state-check-await-unlock");

        context = "unlock closes an already open memory";
        // Reproduce a user who left Memory open before visiting StateCheck.
        Check(memoryWindow.IsOpen, "prior Memory is visibly open for the close transition");
        ChapterClick();
        Check(entry.Step == Step.UpgradeOffer && entry.BlocksMemory,
            "player's chapter click commits only the upgrade offer");
        Check(memoryWindow.IsClosing || !memoryWindow.IsOpen, "unlock naturally closes an existing Memory window");
        bool wasClosing = memoryWindow.IsClosing;
        int closingNode = GameDataManager.Instance.CurrentOSMemoryDataIndex;
        memoryWindow.RestoreAndBringToFront();
        Check(wasClosing ? memoryWindow.IsClosing : !memoryWindow.IsOpen,
            "taskbar restore cannot cancel Memory's protected closing transition");
        Check(!(bool)Invoke(os.MemoryWindowController, "CanReceiveInput"),
            "closing and blocked Memory rejects direct keyboard input");
        Check(GameDataManager.Instance.CurrentOSMemoryDataIndex == closingNode,
            "blocked taskbar restore cannot advance the memory node");
        Remember("upgrade-closing-memory");
        yield return Wait(() => !memoryWindow.IsOpen && IsMessage("upgrade"), "upgrade offer replaces closed Memory");
        Check(!os.OpenMemoryWindow() && !memoryWindow.IsOpen,
            "public Memory opening is blocked before the upgrade");
        yield return CheckBlockedHover();
        Remember("upgrade-offer");
        yield return Screenshot("02-upgrade-offer");
        Check(ResourceState() == resourcesBefore, "unlock and upgrade offer do not change resources");

        // Both real option selections run the same visible installation path.
        foreach (string answer in new[] { "assumed", "fine" })
        {
            if (routes > 0) yield return Restore(snapshots["upgrade-offer"]);
            if (routes > 0) GameDataManager.Instance.OSProcessData.toolDecorationState = OSToolDecorationState.Pending;
            context = "auto-install / " + answer;
            FinishMessage("upgrade", true);
            yield return null;
            Choose("upgrade", "thanks");
            yield return Wait(() => CursorHandle.Instance?.IsScriptCaptured == true,
                answer + " captures player simulated pointer for Live's operation", 10f);
            Check(entry.Step == Step.AutoInstall && CursorHandle.Instance.IsPointerInputSuppressed,
                answer + " blocks player click and drag while Live operates");
            if (routes == 0) Remember("auto-install");
            yield return ObserveAutoInstall(answer, routes > 0);
            yield return Wait(() => CursorHandle.Instance?.IsScriptControlled != true
                && CursorHandle.Instance?.IsPointerInputSuppressed != true,
                answer + " releases player input as installation begins", 3f);
            Check(!GameDataManager.Instance.IsToolProgramInstalled(Package),
                answer + " installation has a visible duration before its receipt");
            Check(entry.BlocksMemory && !os.OpenMemoryWindow(),
                answer + " keeps Memory blocked during installation");
            Check(toolWindow.IsOpen && night.IsNight && (float)Get(night, "lightScale") > .99f,
                answer + " tool and installation remain visible in the night");
            yield return Wait(() => IsMessage("installing"), answer + " installing message is visible");
            if (routes == 0) Remember("installing-unanswered");
            FinishMessage("installing", true);
            yield return Wait(() => !os.CursorRelease.ScriptActorIsMoving,
                answer + " red cursor clears the installation dialogue");
            CheckCursorOutside(link.LinkWindow.WindowRect,
                answer + " red cursor stays outside installation text and choices");
            if (routes == 0) yield return Screenshot("03-installing");
            yield return null;
            Choose("installing", answer);
            int expectedAnswer = answer == "assumed" ? 1 : 2;
            Check(GameDataManager.Instance.OSProcessData.memory.chapter03EntryInstallAnswer == expectedAnswer,
                answer + " is recorded through the real option event");
            if (!GameDataManager.Instance.IsToolProgramInstalled(Package))
            {
                Check(entry.Step == Step.Installing && !IsMessage("ready"),
                    answer + " cannot announce success before installation finishes");
                if (routes == 0) Remember("installing-answered");
            }
            yield return Wait(() => entry.Step == Step.Ready && IsMessage("ready"),
                answer + " reaches the shared success message after installation", 15f);
            Check(GameDataManager.Instance.IsToolProgramInstalled(Package)
                && GameDataManager.Instance.OSProcessData.installedToolProgramIds.Count(x => x == Package) == 1,
                answer + " installs the upgrade exactly once");
            Check(ResourceState() == resourcesBefore && GameDataManager.Instance.GetOSResourceValue(ResourceType.Battery) == 0,
                answer + " installs successfully at zero energy without charging resources");
            Check(entry.BlocksMemory && !os.OpenMemoryWindow(),
                answer + " waits for the complete ready line before enabling Memory");
            FinishMessage("ready", false);
            yield return Wait(() => entry.Step == Step.AwaitMemory && !entry.BlocksMemory,
                answer + " full ready text enables the player launch");
            Check(link.LinkWindow.IsOpen, answer + " player can launch before the ready message auto-closes");
            yield return Wait(() => !os.CursorRelease.ScriptActorIsMoving, answer + " red cursor reaches Memory");
            CheckCursorOutsideMemory();
            if (routes == 0) { Remember("await-memory"); yield return Screenshot("04-await-memory"); }
            Check(os.OpenMemoryWindow(), answer + " player launches the upgraded Memory");
            yield return Wait(() => entry.Step == Step.Complete && memoryWindow.IsOpen,
                answer + " launch completes the handoff");
            Check(GameDataManager.Instance.TryGetOSMemoryChapterProgressRange(3, out var range)
                && GameDataManager.Instance.CurrentOSMemoryDataIndex == range.FirstDataIndex,
                answer + " actual launch starts Chapter3 at its authored first memory");
            Check(CursorHandle.Instance?.IsScriptControlled != true && !entry.BlocksMemory,
                answer + " completed handoff leaves player control available");
            if (routes == 0)
            {
                yield return Wait(() => !OSChapter03EntryDirector.IsOwnEntry(link.DisplayedEntryId),
                    "completed handoff lets the remaining ready message close", 8f);
                Remember("complete");
            }
            routes++;
        }

        context = "restore completed handoff";
        yield return RestoreComplete();

        context = "restore while prior Memory is closing";
        yield return Restore(snapshots["upgrade-closing-memory"]);
        Check(entry.Step == Step.UpgradeOffer && !memoryWindow.IsOpen && !os.OpenMemoryWindow(),
            "restoration cannot reopen Memory captured halfway through its close");
        Check(!CursorHandle.Instance.IsScriptControlled && night.IsNight && night.DebugDark >= 15.9f,
            "upgrade restoration keeps the dark desktop and free player input");
        yield return Wait(() => IsMessage("upgrade"), "upgrade choice reconstructs after restoration");

        foreach (string name in new[] { "auto-install", "installing-unanswered", "installing-answered" })
        {
            if (!snapshots.ContainsKey(name)) continue;
            context = "restore " + name;
            yield return Restore(snapshots[name]);
            Check(entry.BlocksMemory && !os.OpenMemoryWindow(), name + " restoration keeps Memory blocked");
            Check(night.IsNight && night.DebugDark >= 15.9f, name + " restoration never exposes a bright desktop");
            yield return Wait(() => GameDataManager.Instance.IsToolProgramInstalled(Package),
                name + " resumes or restarts the unfinished installation", 40f);
            Check(GameDataManager.Instance.OSProcessData.installedToolProgramIds.Count(x => x == Package) == 1
                && GameDataManager.Instance.GetOSResourceValue(ResourceType.Battery) == 0,
                name + " restoration creates one free installation receipt");
            Check(!CursorHandle.Instance.IsScriptControlled, name + " restoration releases input after install starts");
            if (name == "installing-answered")
            {
                yield return Wait(() => IsMessage("ready") || entry.Step == Step.AwaitMemory,
                    "answered installation does not demand the same answer after loading");
                Check(GameDataManager.Instance.OSProcessData.memory.chapter03EntryInstallAnswer == 1,
                    "answered installation retains the selected response");
            }
            else
            {
                yield return Wait(() => IsMessage("installing"), name + " retains its unanswered message");
                Check(entry.Step == Step.Installing, name + " installation alone cannot skip its question");
            }
        }

        context = "restore wait for Memory";
        yield return Restore(snapshots["await-memory"]);
        Check(entry.Step == Step.AwaitMemory && !entry.BlocksMemory
            && GameDataManager.Instance.IsToolProgramInstalled(Package),
            "loaded waiting state preserves installation and waits for player launch");
        Check(!CursorHandle.Instance.IsScriptControlled && !os.ToolWindowController.ProgramLibrary.Installer.IsInstalling,
            "loaded waiting state neither recaptures the player nor reinstalls");
        int beforeLaunch = GameDataManager.Instance.CurrentOSMemoryDataIndex;
        yield return Hold(.3f);
        Check(GameDataManager.Instance.CurrentOSMemoryDataIndex == beforeLaunch,
            "waiting after load does not enter Chapter3 by itself");
        yield return DiskRoundtripAwaitMemory();
        Check(os.OpenMemoryWindow(), "player can finish the restored handoff");
        yield return Wait(() => entry.Step == Step.Complete && !memoryWindow.IsOpening,
            "restored handoff completes on player launch");
        yield return Screenshot("05-chapter03-opened");
        // Keep the final scene visible until the queued Game view readback has
        // completed, before Guarded's cleanup returns to the menu.
        yield return Hold(.25f);
    }

    static IEnumerator ObserveAutoInstall(string answer, bool exerciseInputAndDecoration)
    {
        var terminal = os.ToolWindowController;
        string lastAutoPhase = null;
        bool testedInput = false, sawDecoration = false;
        float until = Time.realtimeSinceStartup + 40f;
        while (Time.realtimeSinceStartup < until
            && !(entry.Step == Step.Installing && terminal.ProgramLibrary.Installer.IsInstalling))
        {
            Focus();
            int phase = entry.DebugAutoPhase;
            sawDecoration |= terminal.Decoration != null && terminal.Decoration.IsAnimating;
            if (phase.ToString() != lastAutoPhase)
            {
                observations.Add(new { route = answer, step = entry.Step.ToString(), phase,
                    captured = CursorHandle.Instance?.IsScriptCaptured, time = Time.realtimeSinceStartup,
                    decoration = terminal.Decoration?.IsAnimating, command = terminal.CommandInput.text });
                lastAutoPhase = phase.ToString();
            }
            if (exerciseInputAndDecoration && !testedInput && phase == 4)
            {
                string expected = "tool install " + Package;
                Check(terminal.CommandInput.text == expected,
                    "Live reaches the installation button with the correct upgrade command");
                var history = Get(terminal, "commandHistory") as List<string>;
                Check(history != null, "terminal exposes a history fixture for keyboard conflict testing");
                history.Add("help");
                Set(terminal, "historyIndex", history.Count);
                terminal.RequestInputFocus();
                testKeyboard.MakeCurrent();
                InputState.Change(testKeyboard, new KeyboardState(Key.UpArrow), InputState.currentUpdateType);
                Check(testKeyboard.upArrowKey.wasPressedThisFrame, "isolated history-navigation key is pressed");
                // Exercise the real keyboard polling path; suspending the UI module alone
                // does not stop a component that directly reads Keyboard.current.
                Invoke(terminal, "LateUpdate");
                Check(terminal.CommandInput.text == expected,
                    "captured player keyboard cannot replace Live's command with terminal history");
                InputState.Change(testKeyboard, new KeyboardState(), InputState.currentUpdateType);
                terminal.CommandInput.SetTextWithoutNotify("help");
                observations.Add(new { route = answer, injectedCommand = "help", phase });
                testedInput = true;
            }
            yield return null;
        }
        Check(entry.Step == Step.Installing && terminal.ProgramLibrary.Installer.IsInstalling,
            answer + " Live starts the real installer");
        if (exerciseInputAndDecoration)
        {
            Check(sawDecoration && GameDataManager.Instance.IsToolDecorated,
                "Live waits for pending tool decoration before filling the install command");
            Check(testedInput, "Live repairs a changed command before submitting the upgrade");
        }
    }

    static IEnumerator RestoreComplete()
    {
        int thunderEvents = 0, entryMessages = 0;
        Action thunder = () => thunderEvents++;
        Action<string, OSCharacterInteractionEntryData> started = (sequence, message) => {
            if (OSChapter03EntryDirector.IsOwnEntry(message?.EntryId)) entryMessages++;
        };
        var observedNight = night;
        var observedLink = link;
        observedNight.EndReached += thunder;
        observedLink.EntryPresentationStarted += started;
        try
        {
            yield return Restore(snapshots["complete"]);
            int restoredNode = GameDataManager.Instance.CurrentOSMemoryDataIndex;
            yield return Hold(.35f);
            Check(entry.Step == Step.Complete && !entry.IsEngaged && !entry.BlocksMemory,
                "completed JSON restore keeps the handoff complete");
            Check(night.IsNight && night.HasEnded && night.DebugDark >= 15.9f
                && (float)Get(night, "lightScale") > .99f,
                "completed JSON restore keeps settled night and readable lights");
            Check(thunderEvents == 0 && entryMessages == 0
                && !OSChapter03EntryDirector.IsOwnEntry(link.DisplayedEntryId),
                "completed JSON restore does not replay thunder or entry dialogue");
            Check(CursorHandle.Instance?.IsScriptControlled != true
                && !os.CursorRelease.OwnsScriptActor(entry)
                && !os.ToolWindowController.ProgramLibrary.Installer.IsInstalling,
                "completed JSON restore does not capture either cursor or restart installation");
            Check(GameDataManager.Instance.CurrentOSMemoryDataIndex == restoredNode
                && GameDataManager.Instance.IsMemoryRepair04Installed,
                "completed JSON restore preserves chapter memory and installed upgrade");
        }
        finally
        {
            if (observedNight != null) observedNight.EndReached -= thunder;
            if (observedLink != null) observedLink.EntryPresentationStarted -= started;
        }
    }

    static IEnumerator DiskRoundtripAwaitMemory()
    {
        context = "isolated disk return and continue";
        var data = GameDataManager.Instance;
        var saves = GameAutosaveService.Instance;
        var flow = GameSessionFlow.Instance;
        var oldOS = os;
        int oldNode = data.CurrentOSMemoryDataIndex;
        saves.ResumeIsolatedWrites(); saves.BeginSession(data.SaveData);
        Check(flow.ReturnToMainMenu(), "real return-to-menu accepts the isolated handoff save");
        yield return Wait(() => GameManager.Instance.Phase == GamePhase.Boot && !GameManager.Instance.IsTransitioning
            && !flow.IsBusy, "real return transaction writes only the isolated save", 65f);
        var stored = new GameAutosaveStorage(OutputDirectory).Read();
        Check(stored.Success, "await-Memory disk save is readable");
        var disk = JsonUtility.FromJson<GameSaveData>(stored.Payload);
        Check(disk.osProcess.memory.chapter03EntryStep == (int)Step.AwaitMemory
            && disk.osProcess.memory.currentDataIndex == oldNode
            && disk.osProcess.installedToolProgramIds.Count(x => x == Package) == 1,
            "disk stores the wait boundary, unchanged memory node, and one installation receipt");
        Check(flow.ContinueGame(), "Continue accepts the isolated Chapter3 entry save");
        yield return Wait(() => !flow.IsBusy && !GameManager.Instance.IsTransitioning
            && OS_Handle.Instance != null && OS_Handle.Instance.IsOSVisible,
            "Continue reconstructs the OS from disk", 85f);
        saves.EnterTestSession(); Bind();
        yield return Wait(() => entry.Step == Step.AwaitMemory && night.IsNight,
            "continued scene restores the Chapter3 entry wait");
        Check(os != oldOS && data.CurrentOSMemoryDataIndex == oldNode
            && !entry.BlocksMemory && !CursorHandle.Instance.IsScriptControlled
            && !os.ToolWindowController.ProgramLibrary.Installer.IsInstalling,
            "fresh OS reconstruction does not replay installation or capture the player");
        observations.Add(new { boundary = "disk-continue-first-visible", dark = night.DebugDark,
            lightScale = Get(night, "lightScale"), night.IsThundering, night.HasEnded,
            resumePending = Get(entry, "resumePending") });
        Check(night.DebugDark >= 15.9f && (float)Get(night, "lightScale") > .99f,
            "disk Continue restores settled darkness with readable window and cursor lights");
    }

    static void ValidateContent()
    {
        var data = GameDataManager.Instance;
        Check(data.OSToolProgramData.TryGetProgram(Package, out var program) && program.BatteryCost == 0,
            "Chapter3 has a separate zero-cost upgrade package");
        foreach (string suffix in new[] { "important", "upgrade", "installing", "ready" })
        {
            Check(data.OSCharacterInteractionData.TryGetGlobalEntry(Prefix + suffix, out var message),
                suffix + " global message exists");
            if (suffix == "important" || suffix == "ready")
                Check(Mathf.Approximately(message.AutoCloseDelay, 5f), suffix + " uses the authored five-second hold");
        }
    }

    static IEnumerator CheckBlockedHover()
    {
        bool restoreStateWindow = stateWindow.IsOpen && !stateWindow.IsMinimized;
        if (restoreStateWindow)
        {
            stateWindow.Minimize();
            Check(stateWindow.IsMinimized, "StateCheck can move aside for a visible blocked-launcher hover");
            yield return null;
        }
        Check(memoryIcon.IsTemporarilyUnavailable, "Memory launcher is transiently unavailable");
        var normalIcon = memoryIcon.ProcessIcon.color;
        var normalName = memoryIcon.ProcessName.color;
        memoryIcon.OnPointerEnter(ClickData());
        Check(memoryIcon.IsUnavailableHovered, "blocked launcher still receives hover");
        Check(memoryIcon.ProcessIcon.color.grayscale < normalIcon.grayscale
            && memoryIcon.ProcessName.color.grayscale < normalName.grayscale,
            "blocked hover dims both icon and label");
        var mark = memoryIcon.UnavailableMark;
        var pixels = mark != null ? mark.GetComponentsInChildren<UnityEngine.UI.Graphic>() : Array.Empty<UnityEngine.UI.Graphic>();
        Check(mark != null && mark.gameObject.activeInHierarchy && pixels.Length > 0 && pixels.All(x => !x.raycastTarget),
            "blocked hover shows a non-intercepting pixel X");
        Check(pixels.All(x => x.color.a == 1f && x.color == pixels[0].color),
            "pixel X uses one solid ink color");
        Check(mark.rect.width < memoryIcon.ProcessIcon.rectTransform.rect.width
            && mark.rect.height < memoryIcon.ProcessIcon.rectTransform.rect.height,
            "pixel X is smaller than the launcher icon");
        DesktopClick(memoryIcon);
        Check(!memoryWindow.IsOpen && entry.Step == Step.UpgradeOffer,
            "clicking the blocked launcher does not progress or open Memory");
        // Functional input is checked before temporary visual ownership. The
        // real OS pointer and the player's UI input must remain untouched.
        var hoverOwner = ScriptableObject.CreateInstance<HoverPointerOwner>();
        var playerCursor = CursorHandle.Instance;
        try
        {
            Check(playerCursor != null && playerCursor.AcquireScriptControlWithoutWarp(hoverOwner, false),
                "hover evidence can position only the displayed pointer without taking player input");
            var iconRect = memoryIcon.ProcessIcon.rectTransform;
            Rect bounds = ScreenRect(iconRect);
            playerCursor.SetScriptScreenPosition(hoverOwner, new Vector2(bounds.center.x, bounds.yMin));
            // Nightfall's flashlight reads DisplayedScreenPosition, so one frame
            // places the light over the icon. The lower edge leaves its X clear.
            yield return null;
            memoryIcon.OnPointerEnter(ClickData());
            yield return Screenshot("02a-memory-unavailable-hover");
        }
        finally
        {
            if (playerCursor != null) playerCursor.ReleaseScriptControlWithoutWarp(hoverOwner);
            UnityEngine.Object.Destroy(hoverOwner);
        }
        memoryIcon.OnPointerExit(ClickData());
        Check(!memoryIcon.IsUnavailableHovered && !mark.gameObject.activeSelf
            && memoryIcon.ProcessIcon.color == normalIcon && memoryIcon.ProcessName.color == normalName,
            "leaving blocked launcher restores its normal visual state");
        if (restoreStateWindow)
        {
            stateWindow.RestoreAndBringToFront();
            Settle(); Focus(); entry.DebugStep();
            Check(stateWindow.IsOpen && !stateWindow.IsMinimized,
                "StateCheck is restored before Live's later close-button action");
            yield return Wait(() => !os.CursorRelease.ScriptActorIsMoving,
                "red cursor returns to the chapter after the visible hover check");
        }
    }

    static void CheckCursorOutsideChapter()
    {
        Check(os.StateCheckWindowController.TryGetChapterRecordTarget(3, out var target),
            "Chapter3 exposes its real record rectangle");
        CheckCursorOutside(target, "red cursor stays outside the Chapter3 option");
        var images = Get(os.StateCheckWindowController, "chapterUnitImages") as UnityEngine.UI.Image[];
        var names = Get(os.StateCheckWindowController, "chapterNameTexts") as TMPro.TMP_Text[];
        Check(images != null && names != null && images.Length == names.Length && images.Length > 3,
            "all chapter artwork and separate chapter-name rectangles resolve");
        for (int i = 0; i < images.Length; i++)
        {
            if (images[i] != null && images[i].gameObject.activeInHierarchy)
                CheckCursorOutside(images[i].rectTransform, "red cursor stays outside chapter " + i + " artwork");
            if (names[i] != null && names[i].gameObject.activeInHierarchy)
                CheckCursorOutside(names[i].rectTransform, "red cursor stays outside chapter " + i + " label");
        }
    }
    static void CheckCursorOutsideMemory()
    {
        CheckCursorOutside(memoryIcon.ProcessIcon.rectTransform, "red cursor stays outside the Memory icon");
        CheckCursorOutside(memoryIcon.ProcessName.rectTransform, "red cursor stays outside the Memory label");
    }
    static void CheckCursorOutside(RectTransform target, string label)
    {
        var image = Get(os.CursorRelease, "cursorImage") as UnityEngine.UI.Image;
        Check(image != null && image.gameObject.activeInHierarchy, label + " is visible");
        Check(!ScreenRect(image.rectTransform).Overlaps(ScreenRect(target)), label);
    }
    static Rect ScreenRect(RectTransform rect)
    {
        var corners = new Vector3[4]; rect.GetWorldCorners(corners);
        var canvas = rect.GetComponentInParent<Canvas>();
        var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        var points = corners.Select(x => RectTransformUtility.WorldToScreenPoint(camera, x)).ToArray();
        return Rect.MinMaxRect(points.Min(x => x.x), points.Min(x => x.y), points.Max(x => x.x), points.Max(x => x.y));
    }

    static void Bind()
    {
        os = OS_Handle.Instance; entry = os?.Chapter03Entry; talk = os?.Chapter02FinalTalk;
        night = talk?.Nightfall; link = os?.LinkMessageController;
        live = os?.CharacterWindowController?.GetComponent<OSWindowUnit>();
        settings = os?.transform.Find("Windows/WindowUnit_Option")?.GetComponent<OSWindowUnit>();
        stateIcon = os?.DesktopProcessUnits.FirstOrDefault(x => x != null && x.ProcessKey == OSDesktopProcessKeys.StateCheck);
        memoryIcon = os?.DesktopProcessUnits.FirstOrDefault(x => x != null && x.ProcessKey == OSDesktopProcessKeys.Memory);
        stateWindow = stateIcon != null ? Invoke(os, "GetDesktopProcessWindow", stateIcon) as OSWindowUnit : null;
        memoryWindow = memoryIcon != null ? Invoke(os, "GetDesktopProcessWindow", memoryIcon) as OSWindowUnit : null;
        toolWindow = os?.ToolWindowController?.GetComponent<OSWindowUnit>();
        Check(os != null && entry != null && talk != null && night != null && link != null && live != null
            && settings != null && stateIcon != null && memoryIcon != null && stateWindow != null && memoryWindow != null
            && toolWindow != null, "entry runtime references resolve");
        GameTest.Instance.GetComponent<GameTestConsole>()?.SetOpen(false);
        Focus();
    }
    static void Install(GameSaveData snapshot)
    {
        InvokeOptional(entry, "ResetPresentation"); talk.ResetPresentation(); link.NotifyOSHidden();
        GameDataManager.Instance.ReplaceSaveData(Clone(snapshot));
        night.ResetPresentation();
        settings.CloseFromScript(); stateWindow.CloseFromScript(); memoryWindow.CloseFromScript(); toolWindow.CloseFromScript();
        os.OpenCharacterWindowForReconnect(0f); Settle();
        link.SetGlobalMessagesSuppressed(false); link.NotifyOSBecameVisible();
        Focus(); Pump();
    }
    static IEnumerator Restore(string json)
    {
        InvokeOptional(entry, "ResetPresentation"); talk.ResetPresentation(); link.NotifyOSHidden();
        var snapshot = JsonUtility.FromJson<GameSaveData>(json);
        GameDataManager.Instance.ReplaceSaveData(snapshot);
        yield return OSSessionPersistence.Restore(snapshot);
        Bind(); link.SetGlobalMessagesSuppressed(false); link.NotifyOSBecameVisible(); Pump();
        yield return null;
        Focus(); entry.DebugStep(); Settle();
    }
    static void Focus()
    {
        testMouse?.MakeCurrent();
        testKeyboard?.MakeCurrent();
        // A real disk Continue destroys the old OS before the replacement is bound.
        if (os == null) return;
        if (entry != null) entry.DebugSetFocus(true);
        if (talk != null) talk.DebugSetFocus(true);
        SetIfPresent(night, "hasFocus", true); SetIfPresent(os.Chapter02PaperPush, "hasFocus", true);
        SetIfPresent(os.CursorRelease, "hasFocus", true);
    }
    static void MouseReleased()
    {
        if (testMouse == null) return;
        InputState.Change(testMouse, new MouseState { position = new Vector2(-10000f, -10000f) }, InputState.currentUpdateType);
        testMouse.MakeCurrent();
    }
    static void Pump()
    {
        for (int i = 0; i < 5; i++) { Focus(); talk.DebugStep(); entry.DebugStep(); Settle(); }
    }
    static void Settle()
    {
        foreach (var window in new[] { live, settings, stateWindow, memoryWindow, toolWindow, link?.LinkWindow })
        { Complete(window, "openSequence"); Complete(window, "closeSequence"); }
        Complete(link, "reopenTween"); Complete(link?.WindowLayout, "heightTween");
        Canvas.ForceUpdateCanvases();
    }
    static bool IsMessage(string suffix) => link != null && link.DisplayedEntryId == Prefix + suffix
        && link.LinkWindow != null && link.LinkWindow.IsOpen && !link.LinkWindow.IsOpening;
    static void FinishMessage(string suffix, bool options)
    {
        Settle();
        Check(IsMessage(suffix), suffix + " message is displayed before completing its text");
        if (options) link.TryCompleteDisplayedOptionTextImmediately(Prefix + suffix);
        else link.TryCompleteDisplayedTextImmediately(Prefix + suffix);
        Focus(); entry.DebugStep(); Canvas.ForceUpdateCanvases();
    }
    static void Choose(string suffix, string answer)
    {
        string sequence = link.DisplayedSequenceId;
        Check(link.IsWaitingForOption && link.TryGetActiveOptionTarget(answer, out var target),
            suffix + " exposes the authored " + answer + " choice");
        Check(!link.TrySelectDisplayedOptionImmediately(sequence, Prefix + suffix, "invalid-option"),
            suffix + " rejects an unknown option");
        Check(link.TrySelectDisplayedOptionImmediately(sequence, Prefix + suffix, answer),
            suffix + " selects " + answer + " through the shared message controller");
        Check(!link.TrySelectDisplayedOptionImmediately(sequence, Prefix + suffix, answer),
            suffix + " duplicate selection is inert");
    }
    static PointerEventData ClickData() => new PointerEventData(EventSystem.current)
        { button = PointerEventData.InputButton.Left, pointerId = -1, clickCount = 1 };
    static void DesktopClick(DesktopProcessUnit icon) => icon.OnPointerClick(ClickData());
    static void ChapterClick()
    {
        Check(os.StateCheckWindowController.TryGetChapterRecordTarget(3, out var target), "Chapter3 click target resolves");
        var receiver = target.GetComponent<OSStateCheckChapterRecordRaycast>();
        Check(receiver != null, "Chapter3 uses its production pointer receiver");
        receiver.OnPointerClick(ClickData());
    }
    static GameSaveData Capture()
    {
        var save = GameDataManager.Instance.SaveData;
        OSSessionPersistence.Capture(save); GameSessionFlow.Capture(save);
        return Clone(save);
    }
    static void Remember(string name)
    {
        string json = JsonUtility.ToJson(Capture(), true);
        snapshots[name] = json; File.WriteAllText(Path.Combine(OutputDirectory, "snapshot-" + name + ".json"), json);
    }
    static GameSaveData Clone(GameSaveData value) => JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(value));
    static string ResourceState() => JsonUtility.ToJson(GameDataManager.Instance.SaveData.osResources);
    static IEnumerator Screenshot(string name)
    {
        ScreenCapture.CaptureScreenshot(Path.Combine(OutputDirectory, name + ".png"));
        // Capture is queued for frame end. Do not mutate the next story boundary
        // until the requested visible state has had a chance to render.
        yield return null;
    }
    static IEnumerator Hold(float seconds)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < until) { Focus(); yield return null; }
    }
    static IEnumerator Wait(Func<bool> predicate, string label, float seconds = 20f)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (!predicate() && Time.realtimeSinceStartup < until) { Focus(); yield return null; }
        Check(predicate(), label);
    }
    static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        if (!checks.Contains(label)) checks.Add(label);
    }
    static string Describe() => "entry=" + entry?.Step + ", auto=" + Get(entry, "DebugAutoPhase")
        + ", message=" + link?.DisplayedEntryId + ", chapter=" + GameDataManager.Instance?.CurrentChapterIndex
        + ", memory=" + GameDataManager.Instance?.CurrentOSMemoryDataIndex
        + ", captured=" + CursorHandle.Instance?.IsScriptCaptured
        + ", installer=" + os?.ToolWindowController?.ProgramLibrary?.Installer?.IsInstalling
        + ", dark=" + night?.DebugDark + ", lightScale=" + Get(night, "lightScale")
        + ", thundering=" + night?.IsThundering + ", nightEnded=" + night?.HasEnded
        + ", resumePending=" + Get(entry, "resumePending");
    static object Get(object target, string name)
    {
        if (target == null) return null;
        var type = target.GetType();
        return type.GetField(name, Fields)?.GetValue(target) ?? type.GetProperty(name, Fields)?.GetValue(target);
    }
    static void Set(object target, string name, object value)
    {
        var field = target?.GetType().GetField(name, Fields);
        if (field == null) throw new MissingFieldException(target?.GetType().Name, name);
        field.SetValue(target, value);
    }
    static void SetIfPresent(object target, string name, object value) => target?.GetType().GetField(name, Fields)?.SetValue(target, value);
    static object Invoke(object target, string name, params object[] args)
    {
        var method = target?.GetType().GetMethod(name, Fields);
        if (method == null) throw new MissingMethodException(target?.GetType().Name, name);
        return method.Invoke(target, args);
    }
    static void InvokeOptional(object target, string name) => target?.GetType().GetMethod(name, Fields)?.Invoke(target, null);
    static void Complete(object target, string field)
    { var tween = Get(target, field) as Tween; if (tween != null && tween.IsActive()) tween.Complete(true); }
    static string Hash(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "absent";
        using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(path)));
    }
}
