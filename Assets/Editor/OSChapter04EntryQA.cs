using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using MrTGame.Core;
using MrTGame.Narrative;
using MrTGame.OS;
using MrTGame.Systems;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

// Opt-in isolated regression. Long chapter-3 prerequisites are supplied by a
// final-letter fixture; Close, chapter selection, choices, installation and TV
// launch use production callbacks. The configured installation duration is never advanced.
public static class OSChapter04EntryQA
{
    const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    const string Prefix = "chapter04_entry_";
    static readonly List<string> checks = new List<string>(), screenshots = new List<string>();
    static readonly List<object> observations = new List<object>();
    static readonly Dictionary<string, string> snapshots = new Dictionary<string, string>();
    static readonly Dictionary<MonoBehaviour, bool> focusStates = new Dictionary<MonoBehaviour, bool>();
    static GameDataManager data;
    static GameSetup setup;
    static OS_Handle os;
    static OSChapter04EntryDirector entry;
    static OSChapter03StoryDirector story;
    static OSChapter03HomeController home;
    static OSMemoryWindowController memory;
    static OSWindowUnit memoryWindow, settings, stateWindow;
    static OSChapter02NightfallPresentation nightfall;
    static Mouse mouse;
    static Keyboard keyboard;
    static string context;
    static bool cancelled, monitorProgress, achievementHooksOnly;
    static float expectedProgress;
    static int observedStep = -1;
    static OSLinkMessageController Link => os.LinkMessageController;
    static OSToolInstallWindowController Installer => os.ToolWindowController.ProgramLibrary.Installer;
    static OSMemoryProcessSaveData M => data.OSProcessData.memory;
    static OSChapter03StoryState S => M.chapter03Story;
    static OSChapter03StoryPresentation P => story.Presentation;
    static int Step => (int)entry.Step;
    static float Elapsed => M.chapter04EntryElapsed;
    static bool ModalReady => P != null && P.IsLetterUnfolded && P.IsModalOpen && (bool)Get(P, "CanInteract");
    public static string Status { get; private set; } = "idle";
    public static string OutputDirectory { get; private set; }
    public static void Cancel() => cancelled = true;

    public static string Start() => Start(false);

    public static string StartAchievementHooks() => Start(true);

    static string Start(bool achievementsOnly)
    {
        if (Status == "running") return Status;
        var saves = GameAutosaveService.Instance;
        if (!Application.isPlaying || saves == null || GameDataManager.Instance == null || GameManager.Instance == null
            || SceneLoadManager.Instance == null || GameSessionFlow.Instance == null || GameTest.Instance == null)
            return "Play Mode initialization required";
        if (saves.IsBusy || saves.IsWriting || GameManager.Instance.IsTransitioning || SceneLoadManager.Instance.IsBusy
            || GameSessionFlow.Instance.IsBusy || GameTest.Instance.IsCheckpointJumpRunning) return "Wait for active save/scene operation";
        if (saves.IsTestSession) return "Fresh Play Mode required to protect the original Editor save path";
        setup = typeof(GameSetup).GetField("activeSetup", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as GameSetup;
        if (setup == null) return "Active GameSetup is required";
        checks.Clear(); screenshots.Clear(); observations.Clear(); snapshots.Clear(); focusStates.Clear();
        data = GameDataManager.Instance; context = "initialization"; cancelled = monitorProgress = false; observedStep = -1;
        achievementHooksOnly = achievementsOnly;
        OutputDirectory = Path.GetFullPath(Path.Combine("Temp", "OSChapter04EntryQA", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(OutputDirectory); Status = "running"; data.StartCoroutine(Guarded()); return Status;
    }

    static IEnumerator Guarded()
    {
        var saves = GameAutosaveService.Instance; var original = data.SaveData;
        var language = data.CurrentLanguageState; var resources = GameResourceSystem.Instance;
        bool resourceEnabled = resources != null && resources.enabled, background = Application.runInBackground;
        bool demo = (bool)Get(setup, "isDemo"); float volume = AudioListener.volume;
        var random = UnityEngine.Random.state; var previousMouse = Mouse.current; var previousKeyboard = Keyboard.current;
        string[] files = new[] { saves.SavePath, saves.SavePath + ".bak", saves.SavePath + ".tmp" }
            .Concat(Directory.GetFiles("Assets/Scenes", "*.unity", SearchOption.AllDirectories)).ToArray();
        string[] hashes = null; Exception failure = null; var work = new Stack<IEnumerator>();
        try
        {
            saves.EnterTestSession(); hashes = files.Select(Hash).ToArray();
            saves.UseIsolatedStorageForTests(OutputDirectory); saves.EnterTestSession();
            data.ReplaceSaveData(Clone(original)); data.SetCurrentLanguage(LanguageState.zh_CN); Set(setup, "isDemo", false);
            if (resources != null) resources.enabled = false;
            Application.runInBackground = true; AudioListener.volume = 0f;
            mouse = InputSystem.AddDevice<Mouse>("Chapter04EntryQAMouse");
            keyboard = InputSystem.AddDevice<Keyboard>("Chapter04EntryQAKeyboard"); ReleaseInput();
            work.Push(Run());
            while (work.Count > 0)
            {
                object next = null;
                try
                {
                    Focus(); Sample();
                    if (cancelled) throw new OperationCanceledException("Chapter 4 entry QA cancelled");
                    if (!work.Peek().MoveNext()) { (work.Pop() as IDisposable)?.Dispose(); continue; }
                    next = work.Peek().Current;
                }
                catch (Exception error) { failure = error; }
                if (failure != null)
                {
                    Status = "FAILED at " + context + "\n" + Describe() + "\n" + failure;
                    string path = Path.Combine(OutputDirectory, "failure.png"); screenshots.Add(path); ScreenCapture.CaptureScreenshot(path);
                    yield return null; yield return new WaitForSecondsRealtime(.25f);
                    Debug.LogError("[Chapter04 Entry QA] " + Status); break;
                }
                if (next is IEnumerator nested) work.Push(nested); else yield return next;
            }
            if (failure == null) { Check(files.Select(Hash).SequenceEqual(hashes), "formal save and all scene hashes unchanged"); Status = "PASS"; }
        }
        finally
        {
            while (work.Count > 0) (work.Pop() as IDisposable)?.Dispose();
            monitorProgress = false;
            try
            {
                foreach (var pair in focusStates) if (pair.Key != null) Set(pair.Key, "hasFocus", pair.Value);
                bool focusRestored = focusStates.All(pair => pair.Key == null || Equals(Get(pair.Key, "hasFocus"), pair.Value));
                if (GameTest.Instance?.IsCheckpointJumpRunning == true) GameTest.Instance.StopAllCoroutines();
                entry?.ResetPresentation(); story?.ResetPresentation(); home?.Suspend();
                os?.LinkMessageController?.NotifyOSHidden(); os?.Chapter03Entry?.ResetPresentation(); os?.Chapter02FinalTalk?.ResetPresentation();
                GameTest.Instance?.GetComponent<GameTestConsole>()?.SetOpen(false);
                saves.EnterTestSession(); saves.EndSession(); GameManager.Instance?.OpenMainMenuWithoutSave();
                data.ReplaceSaveData(original); data.SetCurrentLanguage(language); Set(setup, "isDemo", demo);
                if (resources != null) resources.enabled = resourceEnabled;
                Application.runInBackground = background; AudioListener.volume = volume;
                if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
                if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
                UnityEngine.Random.state = random;
                if (!ReferenceEquals(data.SaveData, original) || data.CurrentLanguageState != language || GameSetup.IsDemo != demo
                    || Application.runInBackground != background || AudioListener.volume != volume || !focusRestored
                    || resources != null && resources.enabled != resourceEnabled
                    || previousMouse != null && previousMouse.added && Mouse.current != previousMouse
                    || previousKeyboard != null && previousKeyboard.added && Keyboard.current != previousKeyboard
                    || !UnityEngine.Random.state.Equals(random)) throw new InvalidOperationException("Original session settings were not restored");
                if (hashes != null && !files.Select(Hash).SequenceEqual(hashes)) throw new IOException("Formal save or scene changed");
                checks.Add("cleanup restores original save, language, Demo, resources, audio, background, input, focus and random state");
            }
            catch (Exception error) { Status = "FAILED during cleanup: " + error + "\n" + Status; }
            if (Status == "running") Status = "FAILED: interrupted at " + context;
            if (Status == "PASS") Status = "PASS: " + checks.Count + " checks";
            File.WriteAllText(Path.Combine(OutputDirectory, "results.json"), JsonConvert.SerializeObject(new {
                status = Status, context, achievementHooksOnly, checks, observations, snapshots = snapshots.Keys.ToArray(), screenshots,
                originalFiles = files, originalHashes = hashes, finalHashes = files.Select(Hash).ToArray(),
                note = "Isolated muted Play Mode; final-letter prerequisites supplied by fixture. Real letter Close, chapter-card/option pointer receivers, TV single/double click, shared close guard and Escape. Text typing is completed immediately; transition clocks and the configured installation duration are never accelerated. Actual session restores at saved boundaries. No authored fourth-chapter opening dialogue or curtain opening is invented."
            }, Formatting.Indented));
            File.WriteAllText(Path.Combine(OutputDirectory, "results.txt"), Status + "\n" + string.Join("\n", checks));
            Debug.Log("[Chapter04 Entry QA] " + Status + " | " + OutputDirectory);
            os = null; entry = null; story = null; home = null; memory = null; nightfall = null; setup = null; data = null; mouse = null; keyboard = null;
        }
    }

    static IEnumerator Run()
    {
        Check(GameTest.Instance.JumpToCheckpoint(GameTestCheckpoint.Chapter03MemoryStart), "Chapter 3 checkpoint accepted");
        yield return Wait(() => !GameTest.Instance.IsCheckpointJumpRunning && !GameManager.Instance.IsTransitioning
            && !SceneLoadManager.Instance.IsBusy && OS_Handle.Instance != null, "checkpoint ready", 80f);
        Bind(); Check(GameTest.Instance.JumpSucceeded && GameAutosaveService.Instance.IsTestSession, "checkpoint uses isolated storage");
        if (achievementHooksOnly)
        {
            yield return PrepareFinalLetter();
            yield return AchievementHooks();
            yield break;
        }
        ValidateLocales();
        context = "TV program catalog";
        Check(data.OSToolProgramData != null
            && data.OSToolProgramData.TryGetProgram(OSToolProgramIds.TV, out var tvProgram)
            && tvProgram != null && tvProgram.Id == OSToolProgramIds.TV
            && !string.IsNullOrWhiteSpace(tvProgram.DisplayName),
            "TV lookup returns the authored nonempty ID and display name after Editor reload");
        yield return PrepareFinalLetter();
        yield return NaturalRoute();
        yield return BoundaryRestores();
        yield return LegacyRestore();
        Check(Mathf.Approximately(data.CurrentStateCheckProgress, expectedProgress), "global State Check progress stayed unchanged throughout every sampled frame and restore");
        Check(screenshots.All(path => File.Exists(path) && new FileInfo(path).Length > 100), "all requested natural screenshots were written");
    }

    static IEnumerator PrepareFinalLetter()
    {
        context = "real final-letter close"; Link.NotifyOSHidden(); entry.ResetPresentation(); story.ResetPresentation();
        foreach (var window in os.GetComponentsInChildren<OSWindowUnit>(true)) if (window.IsOpen) window.CloseSilently();
        home.ResetRuntime(); memory.PrepareRuntime(); var save = Clone(data.SaveData);
        save.osProcess.currentChapterIndex = 3;
        save.osProcess.installedToolProgramIds.Remove("tv");
        save.osProcess.memory.chapter04EntryStep = 0; save.osProcess.memory.chapter04EntryElapsed = 0f;
        save.osProcess.memory.chapter04TVPinned = true;
        save.unlockedRecordDataIndices.RemoveAll(index => index == OSStoryRecordUnlocks.MotherLetterRecordIndex);
        save.osProcess.memory.currentDataIndex = 11090;
        save.osProcess.memory.triggeredDataIndices = new List<int> { 8110, 11090 };
        save.osProcess.memory.chapter03HomeInitialized = true;
        save.osProcess.memory.chapter03HomeView = new OSHomeView.State { room = "lab", me = new Vector2Int(7, 5), dong = new Vector2Int(8, 5) };
        var state = new OSChapter03StoryState { version = 1, interactionVisualVersion = 1,
            phase = (int)OSChapter03StoryDirector.Phase.Letter, homeOpened = true };
        foreach (string flag in new[] { "home_revealed", "first_floor", "opening_8020", "opening_8070", "opening_8080", "opening_8110",
            "paper_note_01", "bed_open", "lab_open", "mirror_done", "dong_absorbed", "bed_first", "bed_second_done", "box_open",
            "reunion_started", "live_returned", "hairclip_seen", "letter_started", "letter_unfolded" }) state.Add(flag);
        foreach (string room in new[] { "study", "west", "hall", "kitchen", "east", "store", "bed", "bath", "junk", "lab", "yard" }) state.Add("room_" + room);
        state.papers.AddRange(new[] { "note_01", "diary_13" });
        state.memoryQueue.Add(new OSChapter03StoryBeat("letter"));
        state.memoryQueue.Add(new OSChapter03StoryBeat("delay", delay: 3f));
        state.memoryQueue.Add(new OSChapter03StoryBeat("phase", index: (int)OSChapter03StoryDirector.Phase.Complete));
        save.osProcess.memory.chapter03Story = state;
        save.osProcess.characterInteraction = new OSCharacterInteractionSaveData(); save.osResume = new OSResumeSaveData();
        if (achievementHooksOnly)
        {
            StoryAchievementProgress.MigrateLegacyProgress(save);
            save.storyAchievementMilestone = (int)StoryAchievementMilestone.Chapter02Complete;
        }
        data.ReplaceSaveData(save); Link.SetGlobalMessagesSuppressed(false); Link.NotifyOSBecameVisible();
        os.OpenCharacterWindowForReconnect(0f); Check(os.OpenMemoryWindow(), "Memory opens the final-letter fixture");
        yield return Wait(() => story.IsAvailable && ModalReady && Ready(home.HomeWindow), "real unfolded letter awaits its Close button");
        foreach (var unit in memory.TextUnits) unit?.CompleteImmediately();
        Check(S.Has("letter_unfolded") && !S.Has("letter_collected") && Step == 0, "fixture has not collected the letter or begun transition");
        OSStoryRecordUnlocks.RefreshMotherLetter(data);
        Check(!data.IsRecordDataUnlocked(OSStoryRecordUnlocks.MotherLetterRecordIndex),
            "opening and unfolding the letter does not unlock its record before collection");
        if (achievementHooksOnly)
            Check(Reached == (int)StoryAchievementMilestone.Chapter02Complete,
                "opening and unfolding the letter leaves the achievement receipt at 6");
        data.SetDesktopProcessIsNew(OSDesktopProcessKeys.Record, false);
        // The unfolded letter uses a screen-corner close button on letterLayer,
        // outside the ordinary paper modal's frame and its legacy Close button.
        var close = Get(P, "letterScreenClose") as Button;
        Check(close != null && close.IsInteractable() && close.gameObject.activeInHierarchy, "real final-letter X is available");
        float started = Time.realtimeSinceStartup;
        int unlocks = 0, tipsBefore = MotherLetterTipCount();
        Action<int> countUnlock = index => { if (index == OSStoryRecordUnlocks.MotherLetterRecordIndex) unlocks++; };
        data.RecordUnlocked += countUnlock;
        try
        {
            if (achievementHooksOnly) ClickThroughRaycast(close.gameObject, close.transform as RectTransform, "final-letter X");
            else close.onClick.Invoke();
            Check(data.IsRecordDataUnlocked(OSStoryRecordUnlocks.MotherLetterRecordIndex)
                && data.TryGetDesktopProcessIsNew(OSDesktopProcessKeys.Record, out bool recordIsNew) && recordIsNew,
                "the real letter Close unlocks Mother's Letter and marks Record NEW");
            Check(unlocks == 1 && MotherLetterTipCount() == tipsBefore + 1,
                "the first collection emits exactly one record-unlock event and one acquisition tip");
            data.SetDesktopProcessIsNew(OSDesktopProcessKeys.Record, false);
            for (int i = 0; i < 3; i++)
            {
                OSStoryRecordUnlocks.RefreshMotherLetter(data);
                OSStoryRecordUnlocks.RefreshMotherLetter(data, true);
            }
            Check(unlocks == 1 && data.UnlockedRecordDataIndices.Count(index => index == OSStoryRecordUnlocks.MotherLetterRecordIndex) == 1
                && MotherLetterTipCount() == tipsBefore + 1,
                "repeated receipt reconciliation cannot duplicate the record, unlock event or acquisition tip");
            Check(data.TryGetDesktopProcessIsNew(OSDesktopProcessKeys.Record, out recordIsNew) && !recordIsNew,
                "reconciliation does not restore an already-cleared Record NEW marker");
            if (achievementHooksOnly)
            {
                Check(Reached == (int)StoryAchievementMilestone.MothersLetterRead,
                    "the real letter Close callback records MothersLetterRead exactly at collection");
                close.onClick.Invoke();
                Check(Reached == (int)StoryAchievementMilestone.MothersLetterRead,
                    "repeating the letter Close cannot advance beyond receipt 7");
            }
        }
        finally { data.RecordUnlocked -= countUnlock; }
        Check(S.Has("letter_collected") && !P.IsModalOpen, "real Close callback collects and hides letter immediately");
        expectedProgress = data.CurrentStateCheckProgress; monitorProgress = true;
        yield return Wait(() => Step == 1, "original three-second delay starts new transition", 12f);
        float duration = Time.realtimeSinceStartup - started;
        Check(duration >= 2.9f, "original post-letter three seconds are not skipped");
        observations.Add(new { context, type = "post-letter delay", seconds = duration });
    }

    static int Reached => StoryAchievementProgress.GetReached(data.SaveData);

    static IEnumerator AchievementHooks()
    {
        context = "letter and chapter achievement boundaries";
        Check(Reached == (int)StoryAchievementMilestone.MothersLetterRead && !entry.TryUnlockChapter(),
            "the automatic transition retains receipt 7 and rejects an early chapter unlock");
        Remember("achievement-letter-collected");
        yield return Restore(snapshots["achievement-letter-collected"], "collected letter achievement");
        Check(Reached == (int)StoryAchievementMilestone.MothersLetterRead && !P.IsModalOpen,
            "restoring collection keeps receipt 7 without replaying the letter callback");

        yield return Wait(() => IsMessage("end"), "the automatic transition reaches L1", 45f);
        Check(Reached == (int)StoryAchievementMilestone.MothersLetterRead,
            "blackout, Home closure and automatic chapter outro do not grant Chapter03Complete");
        FinishMessage("end", false);
        yield return Wait(() => IsMessage("later"), "L2 follows L1 for the achievement route");
        FinishMessage("later", true); yield return null;
        yield return SelectOption("later");
        yield return Wait(() => Step == (int)OSChapter04EntryDirector.EntryStep.AwaitUnlock && entry.CanUnlock,
            "the chapter card waits for a real player click", 25f);
        Check(Reached == (int)StoryAchievementMilestone.MothersLetterRead && data.CurrentChapterIndex == 3,
            "reaching the fourth-chapter card still leaves receipt 7");
        Remember("achievement-await-click");
        yield return Restore(snapshots["achievement-await-click"], "pending chapter-card achievement");
        yield return Wait(() => entry.CanUnlock, "restored chapter card accepts its real click");
        Check(Reached == (int)StoryAchievementMilestone.MothersLetterRead,
            "loading the pending card cannot infer receipt 8 for a versioned save");
        Check(os.StateCheckWindowController.TryGetChapterRecordTarget(4, out var card),
            "the fourth-chapter card exposes the production click target");
        var receiver = card.GetComponent<OSStateCheckChapterRecordRaycast>();
        Check(receiver != null, "the fourth-chapter card has its production pointer receiver");
        ClickThroughRaycast(receiver.gameObject, card, "fourth chapter achievement card");
        Check(Step == (int)OSChapter04EntryDirector.EntryStep.Prepare
            && Reached == (int)StoryAchievementMilestone.Chapter03Complete,
            "the accepted real card click records Chapter03Complete before installation");
        Check(!entry.TryUnlockChapter() && Reached == (int)StoryAchievementMilestone.Chapter03Complete,
            "a repeated unlock attempt is rejected and keeps receipt 8");
        Remember("achievement-chapter-clicked");
        yield return Restore(snapshots["achievement-chapter-clicked"], "accepted chapter-card achievement");
        Check(Reached == (int)StoryAchievementMilestone.Chapter03Complete && !data.IsTVInstalled,
            "restoring the accepted click preserves receipt 8 without granting later achievements");
        Check(!entry.TryUnlockChapter() && Reached == (int)StoryAchievementMilestone.Chapter03Complete,
            "a repeated unlock after restore cannot advance the achievement receipt");
        Check(Mathf.Approximately(data.CurrentStateCheckProgress, expectedProgress),
            "achievement hooks leave global State Check progress unchanged");
    }

    static IEnumerator NaturalRoute()
    {
        context = "natural outro";
        yield return Wait(() => Step == 1 && Elapsed >= .35f, "darkening advances naturally"); Remember("darken");
        yield return Wait(() => Step == 2, "darkening reaches black before Home closure");
        Check(!home.View.AllLightsOn && home.View.PlayerLightScale <= .01f && home.View.PresentationBlackout >= .999f,
            "room, player light and self-lit glyphs reach complete blackout");
        Check(nightfall.IsNight && entry.DaylightProgress == 0f, "the OS remains at night until Memory begins moving");
        yield return Screenshot("01-home-black.png");
        yield return Wait(() => Step == 3 && Elapsed >= .12f, "Memory moves after Home closure"); Remember("centre-memory");
        Check(entry.DaylightProgress > 0f && entry.DaylightProgress < 1f, "OS daylight starts gradually during Memory movement");
        Check(os.IsWindowDismissalBlocked(memoryWindow), "Memory dismissal remains guarded during its authored movement");
        settings.OpenStandalone(); yield return Wait(() => Ready(settings), "settings open during gradual daylight");
        float pausedClock = Elapsed, pausedDaylight = entry.DaylightProgress;
        yield return Hold(.65f);
        Check(Step == 3 && Mathf.Abs(Elapsed - pausedClock) < .02f && Mathf.Abs(entry.DaylightProgress - pausedDaylight) < .001f,
            "settings pause both Memory movement and global daylight");
        settings.CloseFromScript(); yield return null; yield return null;
        int earlyDarkPixels = DarkPixels();
        Check(earlyDarkPixels > 0 && NightOverlayVisible(), "the partial OS night veil resumes after closing settings");
        yield return Screenshot("01b-os-daylight-start.png");
        yield return Wait(() => Step == 4, "Memory reaches its final silent position");
        yield return Wait(() => memoryWindow.AllowsMove && memoryWindow.PresentationOffset.sqrMagnitude < .001f,
            "centred Memory releases its movement and presentation offset");
        Check(!home.HomeWindow.IsOpen, "Home and its inventory stay closed");
        AssertFrozenMemory();
        var bounds = ScreenRect(memoryWindow.WindowRect);
        Check(Mathf.Abs(bounds.center.x - Screen.width * .5f) < 12f, "Memory is horizontally centred on screen");
        AssertLiveProtected("silent hold"); Remember("silence");
        yield return Screenshot("02-memory-centred-silent.png");
        string finalText = MemoryText(); int finalIndex = data.CurrentOSMemoryDataIndex;
        Check(!os.IsWindowDismissalBlocked(memoryWindow), "centred Memory is no longer dismissal protected");
        var minimizeMemory = WindowButton(memoryWindow, OSWindowButton.WindowAction.Minimize);
        Check(minimizeMemory != null && minimizeMemory.IsInteractable, "Memory minimize button is restored after centering");
        minimizeMemory.OnPointerClick(Pointer(minimizeMemory.transform as RectTransform)); yield return null;
        Check(memoryWindow.IsOpen && memoryWindow.IsMinimized, "Memory minimizes through its production button"); Remember("silence-minimized");
        Check(os.OpenMemoryWindow(), "Memory launcher restores its minimized window");
        yield return Wait(() => Ready(memoryWindow), "Memory is restored from minimize");
        yield return DragMemory();
        var closeMemory = WindowButton(memoryWindow, OSWindowButton.WindowAction.Close);
        Check(closeMemory != null && closeMemory.IsInteractable, "Memory close button is restored after centering");
        closeMemory.OnPointerClick(Pointer(closeMemory.transform as RectTransform)); yield return null;
        Check(!memoryWindow.IsOpen && !home.HomeWindow.IsOpen && !P.IsModalOpen,
            "Memory closes normally without opening Home or the collected letter"); Remember("silence-closed");
        yield return Wait(() => Step == 4 && Elapsed >= 1f, "global daylight continues during the silent hold with Memory closed");
        yield return null;
        Check(DarkPixels() < earlyDarkPixels, "the actual full-screen veil loses opaque pixels as the OS brightens");
        yield return Wait(() => entry.DaylightProgress >= 1f, "the whole OS reaches full daylight naturally");
        yield return null; AssertFullDaylight();
        yield return Screenshot("02b-os-full-daylight-memory-closed.png");
        yield return Wait(() => IsMessage("end"), "L1 appears after the silent hold");
        Check(Link.DisplayedEntryId == Prefix + "end", "the authored L1 is shown");
        Check(!memoryWindow.IsOpen, "Live dialogue continues while Memory stays closed");
        Check(os.OpenMemoryWindow(), "player may reopen Memory while Live continues");
        yield return Wait(() => Ready(memoryWindow), "reopened Memory finishes its normal opening");
        Check(data.CurrentOSMemoryDataIndex == finalIndex && MemoryText() == finalText
            && !home.HomeWindow.IsOpen && !P.IsModalOpen && S.Has("letter_collected"),
            "reopening preserves the final text and does not replay Home or the letter");
        AssertFrozenMemory();
        FinishMessage("end", false); float l1Finished = Time.realtimeSinceStartup;
        yield return Wait(() => IsMessage("later"), "L2 follows L1");
        Check(Time.realtimeSinceStartup - l1Finished >= 1.4f, "L1 keeps its approximately 1.5-second gap");
        FinishMessage("later", true); yield return null; AssertSingleOption("later"); Remember("later");
        yield return SelectOption("later");
        yield return Wait(() => Step == 8 && entry.CanUnlock, "red cursor opens State Check and waits for the player", 25f);
        Check(data.CurrentChapterIndex == 3 && !data.IsTVInstalled, "chapter card has not started chapter 4 or installed TV automatically");
        Check(os.StateCheckWindowController.TryGetChapterRecordTarget(4, out var card), "fourth chapter exposes its actual click target");
        var receiver = card.GetComponent<OSStateCheckChapterRecordRaycast>();
        Check(receiver != null, "fourth chapter uses the production pointer receiver");
        ClickThroughRaycast(receiver.gameObject, card, "fourth chapter card");
        yield return Wait(() => IsMessage("prepare"), "player's fourth-chapter click opens L3");
        FinishMessage("prepare", true); yield return null; AssertSingleOption("ready");
        yield return Screenshot("03-prepare-L3.png"); yield return SelectOption("ready");
        yield return Wait(() => Step == 10 && Installer.IsChapter04Installation && Ready(Installer.GetComponent<OSWindowUnit>()), "confirmation starts the special installer");
        yield return Installation();
        context = "TV player launch";
        Check(!Installer.IsChapter04Installation, "installer leaves its special installation mode after completion");
        Check(data.IsTVInstalled, "TV installation is committed before desktop launch");
        Check(data.OSProcessData.installedToolProgramIds.All(id => !string.IsNullOrWhiteSpace(id)),
            "installation records contain no empty program IDs");
        Check(os.TVDesktopIcon != null && os.TVDesktopIcon.gameObject.activeInHierarchy, "TV appears on the desktop");
        Remember("await-tv"); AssertFrozenMemory(); AssertLiveProtected("waiting for TV"); AssertFullDaylight();
        yield return Wait(() => !os.CursorRelease.ScriptActorIsMoving, "red cursor reaches the TV waiting position");
        Vector2 parked = os.CursorRelease.CompanionScreenPosition; float largestProbe = 0f;
        float probeUntil = Time.realtimeSinceStartup + 7.5f;
        while (Time.realtimeSinceStartup < probeUntil)
        {
            largestProbe = Mathf.Max(largestProbe, Vector2.Distance(parked, os.CursorRelease.CompanionScreenPosition));
            yield return null;
        }
        Check(largestProbe > 2f && !os.CursorRelease.ScriptActorIsMoving, "red cursor probes toward TV and comes back without clicking");
        Check(Step == 11 && !os.TVWindow.IsOpen, "waiting leaves TV unopened for the player");
        var icon = os.TVDesktopIcon;
        ClickThroughRaycast(icon.gameObject, icon.ProcessIcon.rectTransform, "TV single click", 1); yield return null;
        Check(Step == 11 && !os.TVWindow.IsOpen, "single click does not open TV");
        ClickThroughRaycast(icon.gameObject, icon.ProcessIcon.rectTransform, "TV double click", 2);
        yield return Wait(() => Step == 12 && Ready(os.TVWindow) && !memoryWindow.IsOpen, "double click opens TV and closes Memory");
        Check(data.CurrentChapterIndex == 4 && os.TVController.AreCurtainsClosed && !os.TVController.IsCurtainOpen,
            "chapter 4 begins with the TV curtains closed");
        yield return Wait(() => Link.LinkWindow.IsOpen && !Link.WindowLayout.IsOnRight, "Live chat extends to the left");
        yield return TVWindowControls();
        yield return MotherLetterRecord();
        Check(!os.OpenMemoryWindow(), "chapter 4 no longer reopens Memory"); Remember("complete");
        yield return Screenshot("06-tv-curtains-and-left-chat.png");
        yield return Hold(.5f); Check(Step == 12 && !Installer.IsChapter04Installation, "completed entry does not repeat installation");
    }

    static IEnumerator Installation()
    {
        context = $"natural {OSChapter04EntryDirector.InstallationSeconds:0}-second installation";
        var window = Installer.GetComponent<OSWindowUnit>();
        AssertLiveProtected("installation"); AssertFullDaylight();
        var canvasGroup = window.GetComponent<CanvasGroup>();
        Check(window.AllowsMove && canvasGroup != null && canvasGroup.interactable && canvasGroup.blocksRaycasts,
            "installer accepts title movement and pointer raycasts through its root CanvasGroup");
        Check(window.GetComponentsInChildren<OSWindowButton>(true).All(button => !button.gameObject.activeInHierarchy),
            "installer exposes no close, minimize or pin buttons");
        Check(window.transform.GetSiblingIndex() < memoryWindow.transform.GetSiblingIndex(),
            "the installer is initially placed behind the existing Memory window");
        Check(!os.IsSystemInputLocked && !CursorHandle.Instance.IsScriptControlled, "installation leaves other windows and player cursor available");
        Check(os.IsWindowDismissalBlocked(window), "shared player-dismissal guard protects installer");
        var close = window.GetComponentsInChildren<OSWindowButton>(true).FirstOrDefault(button => button.Action == OSWindowButton.WindowAction.Close);
        if (close != null) close.OnPointerClick(Pointer(close.transform as RectTransform));
        Check(window.IsOpen && Installer.IsChapter04Installation, "installer X cannot cancel installation");
        window.Close(); window.Minimize();
        Check(window.IsOpen && !window.IsMinimized && Installer.IsChapter04Installation, "shared taskbar close/minimize actions are blocked");
        InputState.Change(keyboard, new KeyboardState(Key.Escape), InputState.currentUpdateType); yield return null; ReleaseInput(); yield return null;
        Check(window.IsOpen && Installer.IsChapter04Installation, "Escape leaves installer running");
        if (settings.IsOpen) settings.CloseFromScript();
        os.OpenToolWindow(); yield return Wait(() => Ready(os.ToolWindowController.GetComponent<OSWindowUnit>()), "another window opens during installation");
        Check(Installer.IsChapter04Installation && !os.IsSystemInputLocked, "opening another window does not stop installation or lock the desktop");
        var tool = os.ToolWindowController.GetComponent<OSWindowUnit>();
        Check(window.transform.GetSiblingIndex() < tool.transform.GetSiblingIndex(), "installer remains below the opened tool window");
        var dimming = os.GetComponent<OSWindowActivityDimming>();
        bool memoryPinned = memoryWindow.IsAlwaysOnTop;
        memoryWindow.SetAlwaysOnTop(false); tool.BringToFront(); dimming.RefreshPresentation();
        Check(!dimming.IsDimmed(window) && !dimming.IsDimmed(tool) && dimming.IsDimmed(memoryWindow),
            "background installer stays undimmed while an ordinary background Memory window still dims");
        foreach (var installedWindow in os.GetComponentsInChildren<OSToolInstallWindowController>(true))
        {
            var unit = installedWindow.GetComponent<OSWindowUnit>();
            if (unit.IsOpen && !unit.IsMinimized && unit.gameObject.activeInHierarchy)
                Check(!dimming.IsDimmed(unit), "every visible installer component is exempt from activity dimming");
        }
        memoryWindow.SetAlwaysOnTop(memoryPinned);
        tool.Close(); Check(!tool.IsOpen, "another window can be closed normally");
        Check(Installer.IsChapter04Installation && window.IsOpen && window.gameObject.activeInHierarchy,
            "closing Tool leaves the detached chapter-four installation running without recreating it");
        float beforePause = Elapsed; settings.OpenStandalone();
        yield return Wait(() => Ready(settings), "settings open during installation"); beforePause = Elapsed;
        yield return Hold(.65f);
        Check(Mathf.Abs(Elapsed - beforePause) < .02f, "settings pause the installation clock"); settings.CloseFromScript();
        yield return DragInstaller(window);
        yield return Wait(() => Step == 10 && Elapsed >= 8f, "installation naturally reaches eight seconds", 30f);
        float savedElapsed = Elapsed; Remember("installing-eight-seconds");
        yield return Screenshot("04-installing-eight-seconds.png");
        var dots = new HashSet<string>();
        float until = Time.realtimeSinceStartup + 1.8f;
        while (Time.realtimeSinceStartup < until && Step == 10)
        {
            foreach (var text in window.GetComponentsInChildren<TMPro.TMP_Text>(true))
                if (text.text.StartsWith("少女安装中", StringComparison.Ordinal)) dots.Add(text.text);
            yield return null;
        }
        Check(dots.Count >= 3 && dots.All(text => text.Substring("少女安装中".Length).All(ch => ch == '.')),
            "少女安装中 displays cycling trailing dots");
        yield return Restore(snapshots["installing-eight-seconds"], "installing-eight-seconds");
        yield return Wait(() => Step == 10 && Installer.IsChapter04Installation, "saved installation resumes");
        AssertLiveProtected("restored installation"); AssertFullDaylight();
        Check(Elapsed >= savedElapsed - .01f && Elapsed < savedElapsed + 1.5f, "restored installation resumes from saved elapsed time");
        yield return Wait(() => Step == 10 && Elapsed >= OSChapter04EntryDirector.InstallationSeconds * .64f,
            "installation advances beyond its former 25-second save limit", OSChapter04EntryDirector.InstallationSeconds);
        float lateSavedElapsed = Elapsed; Remember("installing-late");
        yield return Restore(snapshots["installing-late"], "installing-late");
        yield return Wait(() => Step == 10 && Installer.IsChapter04Installation, "late saved installation resumes");
        Check(Elapsed >= lateSavedElapsed - .01f && Elapsed < lateSavedElapsed + 1.5f,
            "late installation elapsed time survives save normalization and restore");
        float resumedAt = Time.realtimeSinceStartup; float resumedElapsed = Elapsed;
        yield return Wait(() => Step == 11, "installation finishes naturally after its remaining time",
            OSChapter04EntryDirector.InstallationSeconds + 15f);
        float remainingTime = Time.realtimeSinceStartup - resumedAt;
        Check(remainingTime >= OSChapter04EntryDirector.InstallationSeconds - resumedElapsed - .25f,
            "remaining installation duration is not skipped or accelerated");
        observations.Add(new { context, savedElapsed, lateSavedElapsed, resumedElapsed, remainingTime,
            expectedTotalSeconds = OSChapter04EntryDirector.InstallationSeconds, titleVariants = dots.ToArray() });
        yield return Screenshot("05-tv-installed.png");
    }

    static IEnumerator BoundaryRestores()
    {
        foreach (string name in new[] { "darken", "centre-memory", "silence", "silence-minimized", "silence-closed", "later", "await-tv", "complete" })
        {
            context = "saved boundary: " + name; yield return Restore(snapshots[name], name);
            int expected = name == "darken" ? 1 : name == "centre-memory" ? 3 : name.StartsWith("silence", StringComparison.Ordinal) ? 4
                : name == "later" ? 6 : name == "await-tv" ? 11 : 12;
            yield return Wait(() => Step >= expected && Step <= (expected < 4 ? expected + 1 : expected), "saved step resumes without resetting to the beginning");
            if (expected < 12) AssertFrozenMemory();
            if (expected >= 3) Check(!home.HomeWindow.IsOpen, "Home stays closed after this restored boundary");
            if (expected == 3 || expected == 4)
            {
                Check(entry.DaylightProgress > 0f && entry.DaylightProgress < 1f
                    && Mathf.Abs(nightfall.DebugDaylightProgress - entry.DaylightProgress) < .001f && NightOverlayVisible(),
                    "restored partial daylight is applied to the OS veil without restarting from black");
            }
            if (expected == 4)
            {
                Check(!os.IsWindowDismissalBlocked(memoryWindow) && memoryWindow.AllowsMove,
                    "restored centred Memory keeps normal dismissal and movement controls");
                AssertLiveProtected("restored silent hold");
                if (name == "silence-closed") Check(!memoryWindow.IsOpen, "a closed Memory stays closed after restoring the silent hold");
                if (name == "silence-minimized") Check(memoryWindow.IsOpen && memoryWindow.IsMinimized,
                    "a minimized Memory stays minimized after restoring the silent hold");
                Check(!P.IsModalOpen, "restoring the silent hold never replays the collected letter");
            }
            if (expected >= 6) AssertFullDaylight();
            if (expected == 11) AssertLiveProtected("restored TV wait");
            if (expected == 6)
            {
                yield return Wait(() => IsMessage("later"), "pending L2 choice is restored"); FinishMessage("later", true); yield return null;
                AssertSingleOption("later"); Check(!IsMessage("end"), "restoring L2 does not replay L1");
            }
            if (expected >= 11) Check(data.IsTVInstalled && !Installer.IsChapter04Installation, "installed TV is preserved without a new installer");
            if (expected == 12)
            {
                yield return Wait(() => os.TVWindow.IsOpen && !memoryWindow.IsOpen, "completed save restores TV and keeps Memory closed");
                Check(os.TVController.AreCurtainsClosed && !Link.WindowLayout.IsOnRight, "completed save keeps closed curtains and left-side chat");
                Check(M.chapter04TVPinned && os.TVWindow.IsAlwaysOnTop, "completed save restores the selected default TV pin state");
            }
        }
    }

    static IEnumerator LegacyRestore()
    {
        context = "legacy completed Chapter 3 save";
        var legacy = JObject.Parse(snapshots["later"]); var mem = (JObject)legacy["osProcess"]["memory"];
        mem.Property("chapter04EntryStep")?.Remove(); mem.Property("chapter04EntryElapsed")?.Remove();
        var storyJson = (JObject)mem["chapter03Story"];
        storyJson["phase"] = (int)OSChapter03StoryDirector.Phase.Complete;
        storyJson["memoryQueue"] = new JArray(); storyJson["liveQueue"] = new JArray();
        legacy["unlockedRecordDataIndices"] = new JArray(((JArray)legacy["unlockedRecordDataIndices"])
            .Where(value => (int)value != OSStoryRecordUnlocks.MotherLetterRecordIndex));
        string json = legacy.ToString(Formatting.Indented); File.WriteAllText(Path.Combine(OutputDirectory, "legacy-input.json"), json);
        yield return Restore(json, "legacy-complete");
        Check(S.Has("letter_collected") && data.IsRecordDataUnlocked(OSStoryRecordUnlocks.MotherLetterRecordIndex),
            "an older collected-letter save repairs its missing Mother's Letter record");
        Check(data.UnlockedRecordDataIndices.Count(index => index == OSStoryRecordUnlocks.MotherLetterRecordIndex) == 1
            && MotherLetterTipCount() == 0, "legacy record repair grants one copy without replaying the collection tip");
        yield return Wait(() => Step == 1, "old completed save with no new fields begins the transition once");
        Remember("legacy-migrated"); yield return Restore(snapshots["legacy-migrated"], "legacy-migrated");
        Check(Step == 1 || Step == 2, "saved migrated boundary resumes instead of reinitializing");
        Check(!data.IsTVInstalled && !Installer.IsChapter04Installation, "legacy migration does not install TV before the player agrees");
    }

    static void ValidateLocales()
    {
        context = "ten-language runtime readback";
        var locales = JObject.Parse(File.ReadAllText("Tools/Chapter04Entry/locales.json"));
        string[] languages = { "zh_CN", "zh_TW", "en", "ja", "ko", "fr", "de", "es", "pt_BR", "ru" };
        for (int i = 0; i < languages.Length; i++)
        {
            data.SetCurrentLanguage((LanguageState)i);
            foreach (var key in locales.Properties())
                Check(data.GetLocalizedText(key.Name, "#missing#") == (string)key.Value[languages[i]], languages[i] + " actual text: " + key.Name);
        }
        data.SetCurrentLanguage(LanguageState.zh_CN);
    }

    static IEnumerator DragInstaller(OSWindowUnit window)
    {
        Check(window.AllowsMove && Get(window, "presentationBackgroundOwner") == null,
            "the installer has no continuing presentation lock on its title");
        Canvas.ForceUpdateCanvases();
        var title = Get(window, "dragArea") as RectTransform;
        Check(title != null, "the installer uses the ordinary window title drag area");
        var pointer = Pointer(title); var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
        Check(hits.Count > 0 && window.IsDragAreaTarget(hits[0].gameObject)
            && ExecuteEvents.GetEventHandler<IPointerDownHandler>(hits[0].gameObject) == window.gameObject,
            "the visible installer title is reached by the topmost real pointer raycast");
        pointer.pointerCurrentRaycast = pointer.pointerPressRaycast = hits[0];
        Vector2 before = window.WindowRect.anchoredPosition;
        ExecuteEvents.Execute(window.gameObject, pointer, ExecuteEvents.pointerDownHandler);
        ExecuteEvents.Execute(window.gameObject, pointer, ExecuteEvents.beginDragHandler);
        pointer.delta = new Vector2(48f, -18f); pointer.position += pointer.delta;
        ExecuteEvents.Execute(window.gameObject, pointer, ExecuteEvents.dragHandler);
        ExecuteEvents.Execute(window.gameObject, pointer, ExecuteEvents.endDragHandler);
        ExecuteEvents.Execute(window.gameObject, pointer, ExecuteEvents.pointerUpHandler);
        Vector2 moved = window.WindowRect.anchoredPosition;
        int sibling = window.transform.GetSiblingIndex();
        var initialSiblings = window.transform.parent.Cast<Transform>().Select(child => child.name).ToArray();
        Check(Vector2.Distance(before, moved) > 2f, "dragging the installer title changes its window position");
        yield return Hold(.25f);
        observations.Add(new { context, type = "installer drag retention", before = new[] { before.x, before.y }, moved = new[] { moved.x, moved.y },
            after = new[] { window.WindowRect.anchoredPosition.x, window.WindowRect.anchoredPosition.y }, sibling, afterSibling = window.transform.GetSiblingIndex(),
            initialSiblings, finalSiblings = window.transform.parent.Cast<Transform>().Select(child => child.name).ToArray() });
        Check(Vector2.Distance(moved, window.WindowRect.anchoredPosition) < .1f,
            "installation updates preserve the dragged position");
        // The permanent red pointer is a sibling which moves above all windows.
        // Its movement can change a numeric index without changing window order.
        var dimming = os.GetComponent<OSWindowActivityDimming>();
        dimming.RefreshPresentation();
        Check(dimming.FrontmostWindow == window,
            "installation updates keep the dragged installer ahead of ordinary windows");
        Check(Installer.IsChapter04Installation && window.IsOpen && !window.IsMinimized,
            "dragging keeps the protected installation running");
        var tool = os.ToolWindowController.GetComponent<OSWindowUnit>();
        os.OpenToolWindow(); yield return Wait(() => Ready(tool), "Tool reopens after the installer is moved");
        tool.Close(); yield return Hold(.2f);
        Check(Installer.IsChapter04Installation && window.IsOpen
            && Vector2.Distance(moved, window.WindowRect.anchoredPosition) < .1f,
            "opening and closing Tool retains the moved installer and its position");
        yield return Screenshot("04a-installer-dragged.png");
    }

    static IEnumerator TVWindowControls()
    {
        context = "TV shared window controls and pin preference";
        var tv = os.TVWindow;
        var template = Get(os, "gramophoneWindow") as OSWindowUnit;
        var dimming = os.GetComponent<OSWindowActivityDimming>();
        Check(new OSMemoryProcessSaveData().chapter04TVPinned && M.chapter04TVPinned && tv.IsAlwaysOnTop,
            "fresh TV preferences and the first opened window default to pinned");
        Check(template != null && dimming != null, "Gramophone chrome and shared activity dimming are available");
        var buttons = tv.GetComponentsInChildren<OSWindowButton>(true);
        Check(buttons.Length == 3, "TV has exactly the three shared window buttons");
        foreach (var action in new[] { OSWindowButton.WindowAction.Close, OSWindowButton.WindowAction.Minimize, OSWindowButton.WindowAction.ToggleAlwaysOnTop })
        {
            var button = buttons.SingleOrDefault(candidate => candidate.Action == action);
            var source = WindowButton(template, action);
            Check(button != null && source != null && button.LinkedWindow == tv && button.gameObject.activeInHierarchy && button.IsInteractable,
                "TV " + action + " button targets TV through the standard window handler");
            Check(Equals(Get(button, "inactiveSprite"), Get(source, "inactiveSprite"))
                && Equals(Get(button, "activeSprite"), Get(source, "activeSprite"))
                && button.GetComponent<Image>().material == source.GetComponent<Image>().material,
                "TV " + action + " retains the Gramophone button sprites and material");
            var background = Get(button, "activeBackground") as Image;
            var sourceBackground = Get(source, "activeBackground") as Image;
            Check(background != null && sourceBackground != null && background.transform.IsChildOf(button.transform)
                && background.sprite == sourceBackground.sprite,
                "TV " + action + " owns its cloned hover background instead of referencing the template");
        }
        var pin = WindowButton(tv, OSWindowButton.WindowAction.ToggleAlwaysOnTop);
        tv.BringToFront();
        ClickThroughRaycast(pin.gameObject, pin.transform as RectTransform, "TV pin toggle off"); yield return null;
        Check(!tv.IsAlwaysOnTop && !M.chapter04TVPinned, "the user's pin toggle updates the saved preference");
        tv.SetAlwaysOnTop(true);
        Check(!M.chapter04TVPinned, "programmatic pin changes do not overwrite the user's saved preference");
        tv.SetAlwaysOnTop(false);
        var minimize = WindowButton(tv, OSWindowButton.WindowAction.Minimize);
        ClickThroughRaycast(minimize.gameObject, minimize.transform as RectTransform, "TV minimize"); yield return null;
        Check(tv.IsMinimized && !M.chapter04TVPinned, "the shared TV minimize button preserves an unpinned preference");
        os.OpenTVWindow(); yield return Wait(() => Ready(tv), "TV restores through its desktop launcher");
        // Minimize restoration is immediate; let newly enabled Graphics render
        // before simulating the next separate player click.
        yield return null;
        Check(!tv.IsAlwaysOnTop && !M.chapter04TVPinned, "restoring a minimized TV respects the saved unpinned choice");
        var close = WindowButton(tv, OSWindowButton.WindowAction.Close);
        tv.BringToFront(); ClickThroughRaycast(close.gameObject, close.transform as RectTransform, "TV close"); yield return null;
        Check(!tv.IsOpen && !M.chapter04TVPinned, "the shared TV close button preserves the unpinned preference");
        Remember("tv-unpinned-closed");
        yield return Restore(snapshots["tv-unpinned-closed"], "tv-unpinned-closed");
        Check(!os.TVWindow.IsOpen && !M.chapter04TVPinned, "a saved closed, unpinned TV remains closed and unpinned after load");
        os.OpenTVWindow(); yield return Wait(() => Ready(os.TVWindow), "the loaded closed TV reopens normally"); tv = os.TVWindow;
        Check(!tv.IsAlwaysOnTop && !M.chapter04TVPinned, "TV reopening after load retains the user's unpinned choice");
        var tool = os.ToolWindowController.GetComponent<OSWindowUnit>();
        os.OpenToolWindow(); yield return Wait(() => Ready(tool), "an ordinary window opens above the unpinned TV");
        tool.BringToFront(); dimming.RefreshPresentation();
        Check(dimming.IsDimmed(tv) && !dimming.IsDimmed(tool), "an unpinned background TV follows ordinary window dimming");
        tool.Close(); tv.BringToFront(); pin = WindowButton(tv, OSWindowButton.WindowAction.ToggleAlwaysOnTop);
        ClickThroughRaycast(pin.gameObject, pin.transform as RectTransform, "TV pin toggle on"); yield return null;
        Check(tv.IsAlwaysOnTop && M.chapter04TVPinned, "the shared pin button restores and saves the pinned state");
        os.OpenToolWindow(); yield return Wait(() => Ready(tool), "an ordinary window opens beside the pinned TV");
        tool.BringToFront(); dimming.RefreshPresentation();
        Check(!dimming.IsDimmed(tv), "a pinned TV remains undimmed with another ordinary window active"); tool.Close();
        close = WindowButton(tv, OSWindowButton.WindowAction.Close); tv.BringToFront();
        ClickThroughRaycast(close.gameObject, close.transform as RectTransform, "pinned TV close"); yield return null;
        Check(!tv.IsOpen && M.chapter04TVPinned, "closing a pinned TV does not replace its saved preference with the close-time reset");
        os.OpenTVWindow(); yield return Wait(() => Ready(tv), "the pinned TV reopens normally");
        Check(tv.IsAlwaysOnTop && M.chapter04TVPinned, "ordinary TV reopen reapplies the pinned preference");
        yield return Screenshot("06a-tv-shared-controls-pinned.png");
    }

    static IEnumerator MotherLetterRecord()
    {
        context = "Mother's Letter record attachment";
        var addition = AdditionHandle.Instance;
        var records = os.RecordWindowController;
        var recordWindow = records != null ? records.GetComponent<OSWindowUnit>() : null;
        Check(addition != null && recordWindow != null, "the existing Record window and attachment host resolve");
        Check(data.TryGetRecordData(OSStoryRecordUnlocks.MotherLetterRecordIndex, out var record)
            && record.AttachmentType == RecordAttachmentType.Letter && record.RecordIcon != null,
            "Mother's Letter is an authored record with its icon and letter attachment");
        var recordLocales = JObject.Parse(File.ReadAllText("Tools/Chapter04Entry/mother-letter-locales.json"));
        Check(record.RecordDescription == (string)recordLocales["os.record.6.description"]["zh_CN"]
            && data.GetLocalizedRecordDescription(record) == record.RecordDescription,
            "the record displays its authored description");
        var controls = PlayerCtrlManager.Instance;
        bool canMove = controls != null && controls.CanMove, canInteract = controls != null && controls.CanInteractive;
        string storyBefore = null, bodyBefore = null;
        var mainPresentation = P;
        bool tvPinned = M.chapter04TVPinned;
        var live = os.CharacterWindowController?.GetComponent<OSWindowUnit>();
        bool restoreLive = live != null && live.IsOpen && !live.IsMinimized;
        try
        {
            if (tvPinned)
            {
                var pin = WindowButton(os.TVWindow, OSWindowButton.WindowAction.ToggleAlwaysOnTop);
                os.TVWindow.BringToFront();
                ClickThroughRaycast(pin.gameObject, pin.transform as RectTransform, "TV unpin before reading Record");
                yield return null;
            }
            Check(!M.chapter04TVPinned && !os.TVWindow.IsAlwaysOnTop,
                "the saved TV pin preference allows Record to remain in front while reading");
            // The entry fixture has no body lines, but can still own Live's
            // foreground conversation shell. Use its real Close control before
            // reading Record, just as a player must, and capture state afterward.
            if (restoreLive)
            {
                yield return Wait(() => Ready(live), "Live is ready for its normal close control");
                live.BringToFront();
                yield return null;
                var closeLive = WindowButton(live, OSWindowButton.WindowAction.Close);
                ClickThroughRaycast(closeLive.gameObject, closeLive.transform as RectTransform, "Live close before reading Record");
                yield return Wait(() => !live.IsOpen && !live.IsClosing, "foreground Live closes before reading Record");
                yield return null;
            }
            storyBefore = JsonUtility.ToJson(S);
            bodyBefore = JsonUtility.ToJson(M.chapter04Story);
            Check((bool)typeof(OS_Handle).GetMethod("OpenRecordWindow", Members).Invoke(os, null),
                "Record opens through its ordinary desktop action");
            yield return Wait(() => Ready(recordWindow) && records.CanInteract, "Record is ready for a real selection click");
            recordWindow.BringToFront();
            var unit = records.RecordUnits.SingleOrDefault(item => item.RecordIndex == OSStoryRecordUnlocks.MotherLetterRecordIndex);
            Check(unit != null, "the record list contains exactly one Mother's Letter row");
            var listScroll = unit.GetComponentInParent<ScrollRect>();
            if (listScroll != null) { listScroll.StopMovement(); listScroll.verticalNormalizedPosition = 0f; }
            yield return null;
            yield return WaitForRecordRowPointer(unit, "Mother's Letter record row");
            ClickThroughRaycast(unit.gameObject, unit.transform as RectTransform, "Mother's Letter record row");
            Check(records.CurrentData?.RecordIndex == OSStoryRecordUnlocks.MotherLetterRecordIndex,
                "the production row click selects Mother's Letter");
            var title = Get(records, "detailTitle") as TMPro.TMP_Text;
            var attachment = Get(records, "attachmentButton") as Button;
            Check(title != null && title.text == data.GetLocalizedRecordName(record)
                && attachment != null && attachment.IsInteractable() && attachment.gameObject.activeInHierarchy,
                "the localized record title and clickable letter attachment are displayed");
            var detailScroll = attachment.GetComponentInParent<ScrollRect>();
            if (detailScroll != null) { detailScroll.StopMovement(); detailScroll.verticalNormalizedPosition = 0f; }
            yield return null; yield return Screenshot("06b-mothers-letter-record.png");
            ClickThroughRaycast(attachment.gameObject, attachment.transform as RectTransform, "Read letter attachment");
            yield return Wait(() => addition.IsLetterRecordOpen && Equals(Get(addition.RecordLetterPresentation, "CanInteract"), true),
                "the attachment opens a ready letter reader");
            var reader = addition.RecordLetterPresentation;
            Check(reader != mainPresentation && P == mainPresentation && !mainPresentation.IsModalOpen
                && reader.PaperRoot != null && reader.LetterEnvelope == null && !reader.IsAnimating,
                "record reading reuses the paper renderer without reopening the story envelope or replacing the story presentation");
            Check(addition.BlocksPaperInput && os.IsSystemInputLocked && !records.CanInteract,
                "the letter reader blocks desktop and underlying record input");
            Check(controls == null || !controls.CanMove && !controls.CanInteractive,
                "the letter reader holds both world controls while open");
            AssertRecordLetterText(reader);
            int pages = (int)Get(reader, "paperPages");
            Check(pages > 1, "the complete authored letter spans multiple rendered pages");
            yield return Screenshot("06c-mothers-letter-reader.png");
            for (int page = 1; page < pages; page++)
            {
                var next = Get(reader, "nextPageButton") as Button;
                Check(next != null && next.IsInteractable() && next.gameObject.activeInHierarchy,
                    "the next-page button is available before page " + (page + 1));
                ClickThroughRaycast(next.gameObject, next.transform as RectTransform, "letter next page " + (page + 1));
                Check((int)Get(reader, "paperPage") == page, "the next-page click advances exactly one page");
                yield return null;
            }
            Check(!(Get(reader, "nextPageButton") as Button).gameObject.activeInHierarchy,
                "the final letter page has no forward arrow");
            var previous = Get(reader, "previousPageButton") as Button;
            ClickThroughRaycast(previous.gameObject, previous.transform as RectTransform, "letter previous page");
            Check((int)Get(reader, "paperPage") == pages - 2, "the previous-page click returns exactly one page");
            yield return null;
            for (int language = 0; language < 10; language++)
            {
                int oldPage = (int)Get(reader, "paperPage");
                data.SetCurrentLanguage((LanguageState)language); yield return null;
                Check(data.GetLocalizedRecordDescription(record) ==
                    (string)recordLocales["os.record.6.description"][((LanguageState)language).ToString()],
                    "language " + (LanguageState)language + " resolves the authored record description");
                Check(addition.RecordLetterPresentation == reader && addition.IsLetterRecordOpen,
                    "language " + (LanguageState)language + " keeps the same record reader open");
                AssertRecordLetterText(reader);
                Check((int)Get(reader, "paperPage") == Mathf.Min(oldPage, (int)Get(reader, "paperPages") - 1),
                    "language " + (LanguageState)language + " preserves the nearest valid page");
            }
            data.SetCurrentLanguage(LanguageState.zh_CN); yield return null;
            settings.OpenStandalone(); yield return Wait(() => Ready(settings) && reader.Paused, "settings pause the record letter");
            int pausedPage = (int)Get(reader, "paperPage"); yield return Hold(.25f);
            Check((int)Get(reader, "paperPage") == pausedPage && addition.IsLetterRecordOpen,
                "settings retain the letter and its current page");
            settings.CloseFromScript(); yield return Wait(() => !reader.Paused, "closing settings resumes the same record letter");
            var outside = reader.PaperRoot.parent.Find("Outside") as RectTransform;
            Rect shade = ScreenRect(outside), paper = ScreenRect(reader.PaperRoot);
            Vector2 blank = new Vector2((shade.xMin + paper.xMin) * .5f, paper.center.y);
            Check(shade.Contains(blank) && !paper.Contains(blank), "the dismissal target is genuinely outside the letter paper");
            ClickAtThroughRaycast(outside.gameObject, blank, "letter blank-area dismissal");
            Check(!addition.IsLetterRecordOpen && addition.BlocksPaperInput && !records.TryOpenAttachment(),
                "blank-area dismissal closes the reader and blocks a second attachment click in the same frame");
            Check(controls == null || controls.CanMove == canMove && controls.CanInteractive == canInteract,
                "closing the record letter restores the prior world-control state");
            yield return null;
            Check(!addition.BlocksPaperInput && records.CanInteract, "the next frame releases the reader's input guard");
            Check(P == mainPresentation && !P.IsModalOpen && JsonUtility.ToJson(S) == storyBefore
                && JsonUtility.ToJson(M.chapter04Story) == bodyBefore && Step == 12 && data.CurrentChapterIndex == 4,
                "reading, paging, translating and closing the attachment leave both chapter stories unchanged");

            ClickThroughRaycast(attachment.gameObject, attachment.transform as RectTransform, "reopen letter before save replacement");
            yield return Wait(() => addition.IsLetterRecordOpen, "the same attachment can be reopened");
            data.ReplaceSaveData(Clone(data.SaveData));
            yield return Wait(() => !addition.IsLetterRecordOpen && !addition.BlocksPaperInput,
                "replacing the save automatically closes the old reader and releases its input guard");
            Check(addition.RecordLetterPresentation == null || !addition.RecordLetterPresentation.IsModalOpen,
                "save replacement leaves no orphan letter modal");
            records = os.RecordWindowController; recordWindow = records.GetComponent<OSWindowUnit>();
            unit = records.RecordUnits.SingleOrDefault(item => item.RecordIndex == OSStoryRecordUnlocks.MotherLetterRecordIndex);
            Check(unit != null, "the replacement save retains its Mother's Letter row");
            recordWindow.BringToFront();
            yield return WaitForRecordRowPointer(unit, "Mother's Letter row after save replacement");
            ClickThroughRaycast(unit.gameObject, unit.transform as RectTransform, "Mother's Letter row after save replacement");
            attachment = Get(records, "attachmentButton") as Button;
            yield return null;
            ClickThroughRaycast(attachment.gameObject, attachment.transform as RectTransform, "reopen letter before hiding OS");
            yield return Wait(() => addition.IsLetterRecordOpen, "the attachment opens against the replacement save");
            typeof(OS_Handle).GetMethod("ApplyPresentation", Members).Invoke(os, new object[] { false });
            yield return Wait(() => !addition.IsLetterRecordOpen && !addition.BlocksPaperInput,
                "hiding OS automatically closes the letter reader and releases its input guard");
            Check(!os.IsOSVisible && (addition.RecordLetterPresentation == null || !addition.RecordLetterPresentation.IsModalOpen),
                "a hidden OS retains no visible record letter modal");
            typeof(OS_Handle).GetMethod("ApplyPresentation", Members).Invoke(os, new object[] { true });
            yield return null;
            Check(JsonUtility.ToJson(S) == storyBefore && JsonUtility.ToJson(M.chapter04Story) == bodyBefore
                && (controls == null || controls.CanMove == canMove && controls.CanInteractive == canInteract),
                "reader cleanup preserves both story states and restores prior controls");
        }
        finally
        {
            addition.RecordLetterPresentation?.HideModal(true);
            if (!os.IsOSVisible) typeof(OS_Handle).GetMethod("ApplyPresentation", Members).Invoke(os, new object[] { true });
            data.SetCurrentLanguage(LanguageState.zh_CN);
            if (settings.IsOpen) settings.CloseFromScript();
            recordWindow.CloseFromScript();
            os.TVWindow.SetAlwaysOnTop(M.chapter04TVPinned);
            if (M.chapter04TVPinned != tvPinned) os.TVWindow.ToggleAlwaysOnTop();
            if (restoreLive) typeof(OS_Handle).GetMethod("OpenCharacterWindow", Members).Invoke(os, null);
        }
        if (restoreLive) yield return Wait(() => Ready(live), "Live reopens after the Record attachment fixture");
        yield return null;
    }

    static IEnumerator WaitForRecordRowPointer(RecordUnit unit, string label)
    {
        // The window's open sequence and each generated row's 0.15-second
        // fade are independent tweens. Window Ready does not promise that the
        // child's CanvasGroup has enabled raycasts in this same frame.
        ObserveRecordRowPointer(unit, label, "before ready wait");
        yield return Wait(() =>
        {
            if (unit == null || !unit.gameObject.activeInHierarchy) return false;
            var group = unit.GetComponent<CanvasGroup>();
            return group != null && group.alpha >= .999f && group.interactable && group.blocksRaycasts
                && RecordRowPointerReceiver(unit) == unit.gameObject;
        }, label + " finishes its own fade and becomes the topmost pointer target", 3f);
        ObserveRecordRowPointer(unit, label, "after ready wait");
    }

    static GameObject RecordRowPointerReceiver(RecordUnit unit)
    {
        if (unit == null) return null;
        Canvas.ForceUpdateCanvases();
        var hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(Pointer(unit.transform as RectTransform), hits);
        return hits.Count > 0 ? ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject) : null;
    }

    static void ObserveRecordRowPointer(RecordUnit unit, string label, string phase)
    {
        var group = unit != null ? unit.GetComponent<CanvasGroup>() : null;
        var receiver = RecordRowPointerReceiver(unit);
        observations.Add(new { context, type = "record row readiness", label, phase,
            frame = Time.frameCount, alpha = group != null ? group.alpha : -1f,
            interactable = group != null && group.interactable, blocksRaycasts = group != null && group.blocksRaycasts,
            active = unit != null && unit.gameObject.activeInHierarchy, topmostReceiver = receiver != null ? receiver.name : null });
    }

    static void AssertRecordLetterText(OSChapter03StoryPresentation reader)
    {
        var content = OSChapter03StoryContent.Instance;
        var body = Get(reader, "paperBody") as TMPro.TMP_Text;
        var title = Get(reader, "modalTitle") as TMPro.TMP_Text;
        Check(body != null && body.text == OSPalette.TintRichText(content.PaperBody("final_letter"))
            && title != null && title.text == content.Text("ui.paper.letter"),
            "the record reader uses the complete existing letter text and title in " + data.CurrentLanguageState);
        body.ForceMeshUpdate(true);
        Check((int)Get(reader, "paperPages") == body.textInfo.pageCount
            && body.pageToDisplay == (int)Get(reader, "paperPage") + 1,
            "letter pagination agrees with the actual rendered text in " + data.CurrentLanguageState);
    }

    static int MotherLetterTipCount()
    {
        if (!data.TryGetRecordData(OSStoryRecordUnlocks.MotherLetterRecordIndex, out var record)) return 0;
        string quotedName = "\"" + data.GetLocalizedRecordName(record).Trim() + "\"";
        var addition = AdditionHandle.Instance;
        int count = Equals(Get(addition, "isTipTextPlaying"), true)
            && (Get(addition, "tipTextLabel") as TMPro.TMP_Text)?.text.Contains(quotedName) == true ? 1 : 0;
        if (Get(addition, "tipTextQueue") is IEnumerable queued)
            foreach (var item in queued) if ((Get(item, "Message") as string)?.Contains(quotedName) == true) count++;
        return count;
    }

    static void AssertFrozenMemory()
    {
        int index = data.CurrentOSMemoryDataIndex; string before = MemoryText();
        Check(entry.BlocksMemoryAdvance && memory.IsChapter04OutroFrozen && !memory.CanContinue,
            "Memory is frozen with no continuation");
        Check(!memory.TryAdvance() && data.CurrentOSMemoryDataIndex == index && MemoryText() == before,
            "clicking Memory cannot add text or advance its last line");
        Check(!Equals(Get(memory, "isContinueTipVisible"), true), "Memory does not show a click-to-continue prompt");
    }
    static OSWindowButton WindowButton(OSWindowUnit window, OSWindowButton.WindowAction action)
        => window.GetComponentsInChildren<OSWindowButton>(true)
            .FirstOrDefault(button => button.LinkedWindow == window && button.Action == action);
    static void AssertLiveProtected(string label)
    {
        var live = os.CharacterWindowController.GetComponent<OSWindowUnit>();
        Check(live.IsOpen && !live.IsMinimized && os.IsWindowDismissalBlocked(live), "Live stays open and guarded during " + label);
        foreach (var action in new[] { OSWindowButton.WindowAction.Close, OSWindowButton.WindowAction.Minimize })
        {
            var button = WindowButton(live, action);
            Check(button != null && !button.IsInteractable, "Live " + action + " button is disabled during " + label);
            button.OnPointerClick(Pointer(button.transform as RectTransform));
        }
        live.Close(); live.Minimize();
        Check(live.IsOpen && !live.IsMinimized && !live.IsClosing,
            "Live title buttons and shared taskbar dismissal cannot interrupt " + label);
    }
    static bool NightOverlayVisible()
    {
        var overlay = Get(nightfall, "image") as RawImage;
        return overlay != null && overlay.enabled && overlay.gameObject.activeInHierarchy;
    }
    static int DarkPixels() => (Get(nightfall, "alpha") as byte[])?.Count(value => value != 0) ?? 0;
    static void AssertFullDaylight()
    {
        Check(entry.DaylightProgress >= 1f && nightfall.DebugDaylightProgress >= 1f && !nightfall.IsNight && !NightOverlayVisible(),
            "full daylight removes the OS veil and ends the night presentation");
        Check((Get(nightfall, "lifts") as ICollection)?.Count == 0,
            "full daylight releases the pointer's temporary night-layer override");
    }
    static IEnumerator DragMemory()
    {
        Check(memoryWindow.AllowsMove && Get(memoryWindow, "presentationBackgroundOwner") == null,
            "centred Memory accepts pointer interaction");
        memoryWindow.BringToFront(); Canvas.ForceUpdateCanvases();
        var title = Get(memoryWindow, "dragArea") as RectTransform;
        Check(title != null, "Memory exposes its real draggable title area");
        var pointer = Pointer(title); var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
        Check(hits.Count > 0 && memoryWindow.IsDragAreaTarget(hits[0].gameObject)
            && ExecuteEvents.GetEventHandler<IPointerDownHandler>(hits[0].gameObject) == memoryWindow.gameObject,
            "topmost title raycast reaches Memory's production drag handler");
        pointer.pointerCurrentRaycast = pointer.pointerPressRaycast = hits[0];
        Vector2 before = memoryWindow.WindowRect.anchoredPosition;
        ExecuteEvents.Execute(memoryWindow.gameObject, pointer, ExecuteEvents.pointerDownHandler);
        ExecuteEvents.Execute(memoryWindow.gameObject, pointer, ExecuteEvents.beginDragHandler);
        pointer.delta = new Vector2(48f, -18f); pointer.position += pointer.delta;
        ExecuteEvents.Execute(memoryWindow.gameObject, pointer, ExecuteEvents.dragHandler);
        ExecuteEvents.Execute(memoryWindow.gameObject, pointer, ExecuteEvents.endDragHandler);
        ExecuteEvents.Execute(memoryWindow.gameObject, pointer, ExecuteEvents.pointerUpHandler);
        Vector2 moved = memoryWindow.WindowRect.anchoredPosition;
        Check(Vector2.Distance(before, moved) > 2f, "dragging the title moves Memory after centering");
        yield return Hold(.2f);
        Check(Vector2.Distance(moved, memoryWindow.WindowRect.anchoredPosition) < .1f,
            "subsequent transition frames do not pull the player's moved Memory back to the centre");
    }
    static string MemoryText() => string.Join("\n", memory.TextUnits.Where(unit => unit != null)
        .Select(unit => string.Join("", unit.GetComponentsInChildren<TMPro.TMP_Text>(true).Select(text => text.text))));
    static void AssertSingleOption(string id)
    {
        var current = Get(Link, "activeEntry") as OSCharacterInteractionEntryData;
        Check(Link.IsWaitingForOption && current != null && current.Options.Count == 1 && current.Options[0].OptionId == id,
            "exactly one authored option is offered: " + id);
    }
    static IEnumerator SelectOption(string id)
    {
        Check(Link.TryGetActiveOptionTarget(id, out var target), "option exposes a pointer target: " + id);
        var receiver = target.GetComponentInParent<OSLinkMessageSelectUnit>();
        Check(receiver != null && receiver.IsInteractable, "option uses the real selectable row: " + id);
        ClickThroughRaycast(receiver.gameObject, target, "Live option " + id); yield return null;
    }
    static bool IsMessage(string suffix) => Link.DisplayedEntryId == Prefix + suffix && Ready(Link.LinkWindow);
    static void FinishMessage(string suffix, bool options)
    {
        Check(IsMessage(suffix), "message is displayed: " + suffix);
        if (options) Link.TryCompleteDisplayedOptionTextImmediately(Prefix + suffix);
        else Link.TryCompleteDisplayedTextImmediately(Prefix + suffix);
    }
    static void Remember(string name)
    {
        home.CaptureViewState(); OSSessionPersistence.Capture(data.SaveData); GameSessionFlow.Capture(data.SaveData);
        string json = JsonUtility.ToJson(data.SaveData, true); snapshots[name] = json;
        File.WriteAllText(Path.Combine(OutputDirectory, "snapshot-" + name + ".json"), json);
        observations.Add(new { context, snapshot = name, step = Step, elapsed = Elapsed, chapter = data.CurrentChapterIndex, progress = data.CurrentStateCheckProgress });
    }
    static IEnumerator Restore(string json, string label)
    {
        ReleaseInput(); entry.ResetPresentation(); Link.NotifyOSHidden(); story.ResetPresentation();
        var save = JsonUtility.FromJson<GameSaveData>(json); data.ReplaceSaveData(save);
        yield return OSSessionPersistence.Restore(save); Bind();
        Link.SetGlobalMessagesSuppressed(false); Link.NotifyOSBecameVisible();
        yield return null; yield return null;
        Check(ReferenceEquals(M, save.osProcess.memory) && !OSSessionPersistence.IsRestoring, "real session restore completes: " + label);
    }
    static void Bind()
    {
        os = OS_Handle.Instance; entry = os?.Chapter04Entry; memory = os?.MemoryWindowController;
        // This suite owns the entry boundary only. Keep the newly authored
        // auto-reading body out of record/installation timing assertions.
        os?.Chapter04Story?.DebugSetContent(OSChapter04StoryContent.FromJson("{\"version\":1,\"beats\":[]}"));
        home = memory?.GetComponent<OSChapter03HomeController>(); story = os?.Chapter03Story;
        memoryWindow = memory?.GetComponent<OSWindowUnit>(); settings = os?.transform.Find("Windows/WindowUnit_Option")?.GetComponent<OSWindowUnit>();
        stateWindow = os?.StateCheckWindowController?.GetComponent<OSWindowUnit>();
        nightfall = os?.GetComponent<OSChapter02NightfallPresentation>();
        Check(os != null && entry != null && home != null && story != null && memoryWindow != null && settings != null && stateWindow != null && nightfall != null,
            "entry and existing windows resolve");
        GameTest.Instance?.GetComponent<GameTestConsole>()?.SetOpen(false); Focus();
    }
    static void Sample()
    {
        if (entry == null || !monitorProgress) return;
        if (!Mathf.Approximately(data.CurrentStateCheckProgress, expectedProgress))
            throw new InvalidOperationException("Transition changed global State Check progress: " + expectedProgress + " -> " + data.CurrentStateCheckProgress);
        if (Step == observedStep) return; observedStep = Step;
        observations.Add(new { context, type = "step boundary", step = Step, elapsed = Elapsed,
            realtime = Time.realtimeSinceStartup, frame = Time.frameCount, progress = data.CurrentStateCheckProgress });
    }
    static void Focus()
    {
        mouse?.MakeCurrent(); keyboard?.MakeCurrent();
        foreach (var target in new MonoBehaviour[] { entry, story, nightfall, os?.Chapter03Entry, os?.Chapter02FinalTalk, os?.CursorRelease })
        {
            if (target == null || !(Get(target, "hasFocus") is bool value)) continue;
            if (!focusStates.ContainsKey(target)) focusStates.Add(target, value); Set(target, "hasFocus", true);
        }
    }
    static void ReleaseInput()
    {
        if (mouse != null) InputState.Change(mouse, new MouseState { position = new Vector2(-10000f, -10000f) }, InputState.currentUpdateType);
        if (keyboard != null) InputState.Change(keyboard, new KeyboardState(), InputState.currentUpdateType);
    }
    static PointerEventData Pointer(RectTransform target, int count = 1)
    {
        var bounds = ScreenRect(target);
        return new PointerEventData(EventSystem.current) { position = bounds.center, button = PointerEventData.InputButton.Left, clickCount = count };
    }
    static void ClickThroughRaycast(GameObject expectedReceiver, RectTransform target, string label, int count = 1)
    {
        Canvas.ForceUpdateCanvases();
        ClickAtThroughRaycast(expectedReceiver, ScreenRect(target).center, label, count);
    }
    static void ClickAtThroughRaycast(GameObject expectedReceiver, Vector2 position, string label, int count = 1)
    {
        Canvas.ForceUpdateCanvases();
        var pointer = new PointerEventData(EventSystem.current) { position = position, button = PointerEventData.InputButton.Left, clickCount = count };
        var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
        var actual = hits.Count > 0 ? ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject) : null;
        observations.Add(new { context, type = "topmost pointer raycast", label, position = new[] { pointer.position.x, pointer.position.y },
            expected = expectedReceiver.name, actual = actual != null ? actual.name : null,
            hits = hits.Take(6).Select(hit => new { name = hit.gameObject.name, hit.sortingOrder, hit.depth }).ToArray() });
        Check(actual == expectedReceiver, "topmost screen raycast reaches " + label + " without clicking through another window");
        Check(ExecuteEvents.Execute(actual, pointer, ExecuteEvents.pointerClickHandler), "production pointer handler executes: " + label);
    }
    static Rect ScreenRect(RectTransform target)
    {
        var corners = new Vector3[4]; target.GetWorldCorners(corners); var canvas = target.GetComponentInParent<Canvas>();
        var camera = canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
        var points = corners.Select(point => RectTransformUtility.WorldToScreenPoint(camera, point)).ToArray();
        return Rect.MinMaxRect(points.Min(point => point.x), points.Min(point => point.y), points.Max(point => point.x), points.Max(point => point.y));
    }
    static IEnumerator Screenshot(string name)
    {
        Canvas.ForceUpdateCanvases(); string path = Path.Combine(OutputDirectory, name); screenshots.Add(path); ScreenCapture.CaptureScreenshot(path);
        yield return null;
    }
    static IEnumerator Hold(float seconds)
    { float until = Time.realtimeSinceStartup + seconds; while (Time.realtimeSinceStartup < until) yield return null; }
    static IEnumerator Wait(Func<bool> predicate, string label, float seconds = 20f)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (!predicate() && Time.realtimeSinceStartup < until) yield return null;
        Check(predicate(), label);
    }
    static bool Ready(OSWindowUnit window) => window != null && window.IsOpen && !window.IsOpening && !window.IsClosing && !window.IsMinimized;
    static void Check(bool condition, string label)
    { if (!condition) throw new InvalidOperationException(label); checks.Add(context + ": " + label); }
    static string Describe() => "step=" + entry?.Step + ", elapsed=" + (data != null ? Elapsed : -1f)
        + ", live=" + os?.LinkMessageController?.DisplayedEntryId + ", chapter=" + data?.CurrentChapterIndex
        + ", memoryOpen=" + memoryWindow?.IsOpen + ", settings=" + settings?.IsOpen
        + ", TVinstalled=" + data?.IsTVInstalled
        + ", installer=" + os?.ToolWindowController?.ProgramLibrary?.Installer?.IsChapter04Installation;
    static GameSaveData Clone(GameSaveData value) => JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(value));
    static object Get(object target, string name) => target?.GetType().GetField(name, Members)?.GetValue(target)
        ?? target?.GetType().GetProperty(name, Members)?.GetValue(target);
    static void Set(object target, string name, object value) => target.GetType().GetField(name, Members).SetValue(target, value);
    static string Hash(string path)
    { if (!File.Exists(path)) return "absent"; using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(path))); }
}
