using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using MrTGame.Core;
using Newtonsoft.Json;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Opt-in menu-only regression. Production receipts are hashed, never opened for
// writing. All state-store fixtures live in Temp; no checkpoint or helper runs.
public static class PostGameFarewellQA
{
    static readonly List<string> checks = new List<string>();
    static readonly List<string> errors = new List<string>();
    static readonly List<string> screenshots = new List<string>();
    static readonly List<object> observations = new List<object>();
    static readonly LanguageState[] Languages = {
        LanguageState.ko, LanguageState.zh_CN, LanguageState.zh_TW, LanguageState.en,
        LanguageState.ja, LanguageState.fr, LanguageState.de, LanguageState.es,
        LanguageState.pt_BR, LanguageState.ru
    };
    static string context, fixtureDirectory;
    static bool cancelled, achievementHooksOnly;
    static PostGameFarewellWindow window;
    public static string Status { get; private set; } = "idle";
    public static string OutputDirectory { get; private set; }

    [MenuItem("Tools/MrTGame/Post Game Farewell/Run Menu QA")]
    static void MenuStart() => Debug.Log("[Farewell QA] " + Start());
    public static void Cancel() => cancelled = true;

    public static string Start() => Start(false);

    public static string StartAchievementHooks() => Start(true);

    static string Start(bool achievementsOnly)
    {
        if (Status == "running") return Status;
        var data = GameDataManager.Instance;
        var saves = GameAutosaveService.Instance;
        var menu = MainMenuHandle.Instance;
        if (!Application.isPlaying || data == null || !data.IsInitialized || saves == null
            || menu == null || EventSystem.current == null || GameManager.Instance == null)
            return "Fresh Play Mode with initialized GameDataManager and main menu required";
        if (saves.IsTestSession || saves.IsIsolatedStorage)
            return "Fresh Play Mode required; this QA preserves the existing save path";
        if (saves.IsBusy || saves.IsWriting || GameManager.Instance.IsTransitioning
            || SceneLoadManager.Instance?.IsBusy == true || GameTest.Instance?.IsCheckpointJumpRunning == true)
            return "Wait for active save/scene operation";
        if (GameManager.Instance.Phase != GamePhase.Boot || !menu.IsMainMenuVisible || menu.IsOptionPanelVisible)
            return "Run from the idle main menu before starting or continuing a game";
        if (PostGameFarewellWindow.Current != null)
            return "Close the existing farewell window before running QA";
        checks.Clear(); errors.Clear(); screenshots.Clear(); observations.Clear();
        cancelled = false; context = "initialization"; window = null; achievementHooksOnly = achievementsOnly;
        OutputDirectory = Path.GetFullPath(Path.Combine("Tools", "PostGameFarewell", "Validation", "runtime",
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8)));
        fixtureDirectory = Path.GetFullPath(Path.Combine("Temp", "PostGameFarewellQA", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(OutputDirectory);
        Directory.CreateDirectory(fixtureDirectory);
        Status = "running";
        data.StartCoroutine(Guarded());
        return Status;
    }

    static IEnumerator Guarded()
    {
        var data = GameDataManager.Instance;
        var saves = GameAutosaveService.Instance;
        var originalSave = data.SaveData;
        var originalLanguage = data.CurrentLanguageState;
        var eventSystem = EventSystem.current;
        var originalSelection = eventSystem.currentSelectedGameObject;
        bool originalBackground = Application.runInBackground;
        string originalSavePath = saves.SavePath;
        string originalSaveJson = JsonUtility.ToJson(originalSave);
        Dictionary<string, string> before = null, after = null;
        IDisposable writeScope = null;
        var work = new Stack<IEnumerator>();
        Exception failure = null;
        Application.logMessageReceived += CaptureError;
        try
        {
            try
            {
                writeScope = saves.SuspendWrites();
                before = ProtectedFiles(originalSavePath);
                Application.runInBackground = true;
                work.Push(Run());
            }
            catch (Exception exception) { failure = exception; }
            while (failure == null && work.Count != 0)
            {
                object next = null;
                try
                {
                    if (cancelled) throw new OperationCanceledException("Farewell QA cancelled");
                    if (!work.Peek().MoveNext()) { (work.Pop() as IDisposable)?.Dispose(); continue; }
                    next = work.Peek().Current;
                }
                catch (Exception exception) { failure = exception; }
                if (failure != null) break;
                if (next is IEnumerator nested) work.Push(nested); else yield return next;
            }
            if (failure != null) Status = "FAILED at " + context + ": " + failure;
            else Status = errors.Count == 0 ? "PASS" : "FAILED: runtime errors";
        }
        finally
        {
            while (work.Count != 0) (work.Pop() as IDisposable)?.Dispose();
            try
            {
                // Destroy only this fixture's window. Its callback either counts
                // invocations or commits the Temp fixture; it never sends delivery.
                if (window != null) UnityEngine.Object.DestroyImmediate(window.gameObject);
                data.SetCurrentLanguage(originalLanguage);
                if (eventSystem != null)
                    eventSystem.SetSelectedGameObject(originalSelection != null && originalSelection.activeInHierarchy
                        ? originalSelection : null);
                Application.runInBackground = originalBackground;
                Check(ReferenceEquals(data.SaveData, originalSave) && JsonUtility.ToJson(data.SaveData) == originalSaveJson,
                    "original in-memory save object and contents unchanged");
                Check(data.CurrentLanguageState == originalLanguage && saves.SavePath == originalSavePath
                    && !saves.IsTestSession && !saves.IsIsolatedStorage, "language and save session restored");
                Check(eventSystem == null || eventSystem.currentSelectedGameObject == originalSelection,
                    "original UI selection restored");
                after = ProtectedFiles(originalSavePath);
                Check(SameFiles(before, after), "formal saves and farewell receipts have identical paths and SHA-256 hashes");
            }
            catch (Exception exception) { Status = "FAILED during cleanup: " + exception + "\n" + Status; }
            finally
            {
                // Language restoration happens while writes remain suspended.
                // A failed UI cleanup must not leave an autosave suspension alive.
                try { if (data != null) data.SetCurrentLanguage(originalLanguage); }
                finally { writeScope?.Dispose(); Application.runInBackground = originalBackground; }
                Application.logMessageReceived -= CaptureError;
            }
            if (Status == "running") Status = "FAILED: interrupted at " + context;
            if (Status == "PASS" && errors.Count != 0) Status = "FAILED: runtime errors during cleanup";
            if (Status == "PASS") Status += ": " + checks.Count + " checks";
            File.WriteAllText(Path.Combine(OutputDirectory, "results.json"), JsonConvert.SerializeObject(new {
                status = Status, context, achievementHooksOnly, checks, errors, observations, screenshots, fixtureDirectory,
                originalLanguage = originalLanguage.ToString(), originalSavePath,
                originalFiles = before, finalFiles = after,
                note = achievementHooksOnly
                    ? "Menu-only achievement QA with SuspendWrites and temporary receipt stores. Exercises production retained-receipt discovery, completion arming, Show, Close and committed achievement getter across an unfinished New Game and a simulated restart. Cached save/receipt fields are restored. No checkpoint, production save/receipt write, external helper or desktop delivery."
                    : "Menu-only QA with SuspendWrites and temporary receipt stores. No checkpoint, saved completion change, external helper, desktop delivery or production receipt write. Korean is the first window language. The real attachment button starts collapsed, toggles both ways, and preserves its state across language changes; the same expanded window refreshes through all ten languages."
            }, Formatting.Indented));
            File.WriteAllText(Path.Combine(OutputDirectory, "results.txt"), Status + "\n" + string.Join("\n", checks));
            Debug.Log("[Farewell QA] " + Status + " | " + OutputDirectory);
            window = null;
        }
    }

    static IEnumerator Run()
    {
        if (achievementHooksOnly)
        {
            context = "achievement fixture isolation";
            VerifyEditorLifecycleGate();
            yield return AchievementHooks();
            yield break;
        }
        context = "policy and temporary state store";
        VerifyPolicy();
        VerifyEditorLifecycleGate();
        VerifyStore();
        context = "Korean first opening";
        var data = GameDataManager.Instance;
        data.SetCurrentLanguage(LanguageState.ko);
        yield return null;
        Check(PostGameFarewellContent.IsReady(), "Korean content is complete before opening");
        int closedCount = 0;
        var selectionBeforeOpen = EventSystem.current.currentSelectedGameObject;
        window = PostGameFarewellWindow.Show(MainMenuHandle.Instance.transform, () => closedCount++);
        Check(window != null && PostGameFarewellWindow.Current == window, "production window opens from the main menu");
        Check(PostGameFarewellWindow.Show(MainMenuHandle.Instance.transform) == window,
            "a repeated Show call reuses the active window");
        yield return null; yield return null;
        Check(EventSystem.current.currentSelectedGameObject == window.CloseButton.gameObject,
            "opening focuses the actual close button");
        yield return VerifyAttachmentToggle();

        for (int index = 0; index < Languages.Length; index++)
        {
            var language = Languages[index];
            context = "window / " + language;
            data.SetCurrentLanguage(language);
            yield return null; yield return null;
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(window.Scroll.content);
            VerifyAttachmentVisibility(true, "language change preserves the expanded attachment");
            VerifyWindow(language);
            var scroll = window.Scroll;
            Check(scroll.content.rect.height > scroll.viewport.rect.height + 20f,
                "complete dialogue and letter exceed the viewport and require scrolling");
            scroll.StopMovement(); scroll.verticalNormalizedPosition = 1f;
            yield return null; yield return null;
            Rect topBounds = BoundsIn(scroll.viewport, window.BodyLabel.rectTransform);
            Check(Mathf.Abs(scroll.verticalNormalizedPosition - 1f) < .005f
                && topBounds.yMax <= scroll.viewport.rect.yMax + 1f
                && topBounds.yMax >= scroll.viewport.rect.yMax - 12f, "top of the message is reachable");
            yield return Screenshot(index.ToString("00") + "-" + language + "-top.png");

            scroll.StopMovement(); scroll.verticalNormalizedPosition = 0f;
            yield return null; yield return null;
            Rect lastBounds = BoundsIn(scroll.viewport, window.LetterAfterCatLabel.rectTransform);
            Rect contentBounds = BoundsIn(scroll.viewport, scroll.content);
            Check(Mathf.Abs(scroll.verticalNormalizedPosition) < .005f
                && lastBounds.yMin >= scroll.viewport.rect.yMin - 1f
                && lastBounds.yMax <= scroll.viewport.rect.yMax + 1f,
                "the complete final letter paragraph is visible at the bottom");
            Check(Mathf.Abs(contentBounds.yMin - scroll.viewport.rect.yMin) < 1.5f,
                "bottom scrolling reaches the actual end of content");
            yield return Screenshot(index.ToString("00") + "-" + language + "-bottom.png");
            observations.Add(new {
                language = language.ToString(), body = window.BodyLabel.text,
                letter = RenderedLetter(), filename = PostGameFarewellContent.FileName,
                bodyFont = window.BodyLabel.font.name, catFont = window.CatLabel.font.name,
                attachmentExpanded = window.IsAttachmentExpanded,
                viewport = RectData(scroll.viewport.rect), content = RectData(scroll.content.rect),
                finalParagraphAtBottom = RectData(lastBounds), missingGlyphs = 0
            });
        }

        context = "language refresh and actual close";
        data.SetCurrentLanguage(LanguageState.ko);
        yield return null; yield return null;
        VerifyWindow(LanguageState.ko);
        Check(window.Scroll.verticalNormalizedPosition > .995f, "language refresh returns the long letter to its top");
        Check(window.CloseButton.IsActive() && window.CloseButton.IsInteractable(), "actual close button is usable");
        var button = window.CloseButton;
        button.onClick.Invoke(); button.onClick.Invoke();
        yield return null; yield return null;
        Check(closedCount == 1, "two close button events dismiss only once");
        Check(window == null && PostGameFarewellWindow.Current == null, "close destroys the production window and clears Current");
        Check(EventSystem.current.currentSelectedGameObject == selectionBeforeOpen,
            "window close restores the previous menu selection");
        Check(screenshots.Count == Languages.Length * 2 + 1 && screenshots.All(path => new FileInfo(path).Length > 128),
            "the collapsed attachment and all ten languages have nonempty PNG captures");
    }

    static IEnumerator AchievementHooks()
    {
        context = "retained completion across New Game and restart";
        var service = PostGameFarewellService.Current;
        var saves = GameAutosaveService.Instance;
        const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic;
        string[] names = { "store", "state", "receiptSaveId", "storageUnavailable", "retainedAchievementMilestone",
            "openedThisRun", "storedSnapshot", "<HasSave>k__BackingField" };
        var fields = names.Select((name, index) => (index < 6 ? typeof(PostGameFarewellService) : typeof(GameAutosaveService))
            .GetField(name, members)).ToArray();
        Check(fields.All(field => field != null), "all temporary receipt and committed-save fixture fields resolve");
        object[] targets = { service, service, service, service, service, service, saves, saves };
        object[] originals = fields.Select((field, index) => field.GetValue(targets[index])).ToArray();
        var readRetained = typeof(PostGameFarewellService).GetMethod("ReadRetainedReceipts", members);
        var showReceipt = typeof(PostGameFarewellService).GetMethod("ShowCommittedReceipt", members);
        var armReceipt = typeof(PostGameFarewellService).GetMethod("ArmCommittedCompletion", members);
        Check(readRetained != null && showReceipt != null && armReceipt != null, "production retention, Show and completion hooks resolve");
        string root = Path.Combine(fixtureDirectory, "retained-playthroughs");
        void Select(string id, PostGameFarewellStateStore selectedStore, PostGameFarewellState selectedState)
        {
            fields[0].SetValue(service, selectedStore); fields[1].SetValue(service, selectedState);
            fields[2].SetValue(service, id); fields[3].SetValue(service, false);
        }
        try
        {
            var firstSave = AchievementSave(Guid.NewGuid().ToString("N"), true);
            var firstStore = new PostGameFarewellStateStore(PostGameFarewellDelivery.GetSaveStateDirectory(root, firstSave.farewellSaveId));
            var proposal = new PostGameFarewellState { completed = true, completedRunId = "fixture-first-completion", deliveryAttempted = true };
            Check(firstStore.TrySave(proposal, out var firstState, out var error), "old completed receipt persists only to Temp: " + error);
            Check(!firstState.ShouldShow(firstState.completedRunId, false) && !firstState.ShouldShow("later", true)
                && firstState.ShouldShow(service.RunId, false), "the gift waits for the completing process to exit and a later launch");
            string firstReceiptHash = Hash(firstStore.FilePath);
            var newSave = AchievementSave(Guid.NewGuid().ToString("N"), false);
            fields[6].SetValue(saves, newSave); fields[7].SetValue(saves, true);
            fields[4].SetValue(service, 0); fields[5].SetValue(service, false);
            Select(null, null, null);
            var recovered = (List<PostGameFarewellStateStore.CompletedReceipt>)readRetained.Invoke(service, new object[] { root });
            Check(!saves.StoredFinalEndingCompleted && recovered.Count == 1 && recovered[0].SaveId == firstSave.farewellSaveId,
                "startup recovers the old pending letter while the current autosave is an unfinished new game");
            Check(service.CommittedAchievementMilestone == 0 && recovered[0].State.ShouldShow(service.RunId, false),
                "the retained letter is eligible on restart but does not grant achievement 11 before Show");
            Check(Hash(firstStore.FilePath) == firstReceiptHash, "discovery preserves the old completion and delivery record bytes");
            var newStore = new PostGameFarewellStateStore(PostGameFarewellDelivery.GetSaveStateDirectory(root, newSave.farewellSaveId));
            Check(newStore.TryRead(out var newState, out error) && !newState.completed && !File.Exists(newStore.FilePath),
                "the unfinished new playthrough has no completion or delivery receipt");
            Select(newSave.farewellSaveId, newStore, newState);
            armReceipt.Invoke(service, null);
            Check(!File.Exists(newStore.FilePath), "the production completion hook cannot arm an unfinished new save");

            Select(firstSave.farewellSaveId, firstStore, recovered[0].State);
            showReceipt.Invoke(service, null);
            window = PostGameFarewellWindow.Current;
            Check(window != null && service.CommittedAchievementMilestone == (int)StoryAchievementMilestone.SecretGiftReceived,
                "production Show opens the old letter and grants 11 while the current new game remains unfinished");
            Check(firstStore.TryRead(out var shown, out error) && shown.windowShown && !shown.windowDismissed,
                "production Show commits its source receipt before Close: " + error);
            // Exercise a poll selecting a different ledger while the old window is open.
            Select(newSave.farewellSaveId, newStore, newState);
            Check(service.CommittedAchievementMilestone == (int)StoryAchievementMilestone.SecretGiftReceived,
                "switching the selected ledger cannot lose an already committed letter achievement");
            yield return null; yield return null;
            Check(window.CloseButton.IsActive() && window.CloseButton.IsInteractable(), "the retained letter has a usable actual Close button");
            var close = window.CloseButton;
            close.onClick.Invoke(); close.onClick.Invoke();
            yield return null; yield return null;
            Check(window == null && PostGameFarewellWindow.Current == null, "repeated actual Close events destroy the window safely");
            Check(firstStore.TryRead(out var dismissed, out error) && dismissed.windowDismissed,
                "production Close writes the old window's source ledger even after the active ledger changed");
            Check(!File.Exists(newStore.FilePath), "closing the old letter does not create a false receipt for the unfinished new game");
            Check(firstStore.TrySave(proposal, out var merged, out error) && merged.windowShown && merged.windowDismissed,
                "a stale completion writer cannot erase the shown or dismissed letter");
            firstReceiptHash = Hash(firstStore.FilePath);

            // Simulate a fresh process with no cached service fields or loaded save.
            Select(null, null, null); fields[4].SetValue(service, 0); fields[7].SetValue(saves, false);
            readRetained.Invoke(service, new object[] { root });
            Check(service.CommittedAchievementMilestone == (int)StoryAchievementMilestone.SecretGiftReceived,
                "a fresh startup recovers achievement 11 for Steam retry even without a current autosave");
            fields[7].SetValue(saves, true);
            Check(service.CommittedAchievementMilestone == (int)StoryAchievementMilestone.SecretGiftReceived
                && !dismissed.ShouldShow("another-launch", false), "an unfinished new save preserves 11 without reopening a dismissed letter");

            newSave.osProcess.memory.chapter04Story.finalTreatmentStage = OSChapter04FinalTreatmentStage.Completed;
            Select(newSave.farewellSaveId, newStore, newState);
            armReceipt.Invoke(service, null);
            Check(newStore.TryRead(out var newlyCompleted, out error) && newlyCompleted.completed
                && newlyCompleted.completedRunId == service.RunId && newlyCompleted.OwnerIsAlive()
                && !newlyCompleted.ShouldShow(service.RunId, true), "a later second completion creates its own receipt and still waits for exit");
            Check(Hash(firstStore.FilePath) == firstReceiptHash, "the second completion preserves the first playthrough's receipt bytes");
            Check(PostGameFarewellStateStore.ReadCompleted(root, null).Count == 2,
                "both completed playthroughs remain discoverable under their original identities");
            var legacyStore = new PostGameFarewellStateStore(root);
            Check(legacyStore.TrySave(proposal, out _, out error), "a pre-save-ID legacy fixture persists");
            var badStore = new PostGameFarewellStateStore(PostGameFarewellDelivery.GetSaveStateDirectory(root, Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(badStore.DirectoryPath); File.WriteAllText(badStore.FilePath, "broken JSON");
            string invalidHash = Hash(badStore.FilePath);
            var readErrors = new List<string>();
            var all = PostGameFarewellStateStore.ReadCompleted(root, readErrors.Add);
            Check(all.Count == 3 && all.Any(item => item.SaveId == string.Empty) && readErrors.Count == 1,
                "legacy receipts are recovered and one corrupt sibling cannot hide other completed playthroughs");
            Check(Hash(badStore.FilePath) == invalidHash, "corrupt sibling bytes remain untouched");
            string missingRoot = Path.Combine(fixtureDirectory, "never-played");
            Check(PostGameFarewellStateStore.ReadCompleted(missingRoot, null).Count == 0 && !Directory.Exists(missingRoot),
                "a player with no completed receipts gets no letter and discovery creates no files");
            observations.Add(new {
                test = "production Show / Close / completion / retained-receipt discovery and getter", firstSaveId = firstSave.farewellSaveId,
                newSaveId = newSave.farewellSaveId, firstReceipt = firstStore.FilePath,
                newReceipt = newStore.FilePath, productionWrites = false, desktopDelivery = false
            });
        }
        finally
        {
            if (window != null) UnityEngine.Object.DestroyImmediate(window.gameObject);
            window = null;
            for (int index = 0; index < fields.Length; index++) fields[index].SetValue(targets[index], originals[index]);
            Check(fields.Select((field, index) => Equals(field.GetValue(targets[index]), originals[index])).All(value => value),
                "every injected service receipt and autosave cache field is restored");
        }
    }

    static GameSaveData AchievementSave(string saveId, bool completed)
    {
        var save = new GameSaveData { farewellSaveId = saveId, hasStartedGame = true };
        save.EnsureData();
        save.osProcess.memory.chapter04Story.finalTreatmentStage = completed
            ? OSChapter04FinalTreatmentStage.Completed : OSChapter04FinalTreatmentStage.None;
        return save;
    }


    static IEnumerator VerifyAttachmentToggle()
    {
        context = "attachment collapse and expansion";
        VerifyAttachmentVisibility(false, "the attachment is collapsed on first opening");
        Check(window.AttachmentButton != null && window.AttachmentButton.IsActive()
            && window.AttachmentButton.IsInteractable(), "the actual attachment toggle button is usable");
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(window.Scroll.content);
        float collapsedHeight = window.Scroll.content.rect.height;
        window.Scroll.StopMovement(); window.Scroll.verticalNormalizedPosition = 0f;
        yield return null; yield return null;
        Rect buttonBounds = BoundsIn(window.Scroll.viewport, (RectTransform)window.AttachmentButton.transform);
        Check(buttonBounds.yMin >= window.Scroll.viewport.rect.yMin - 1f
            && buttonBounds.yMax <= window.Scroll.viewport.rect.yMax + 1f,
            "the collapsed attachment button is reachable by scrolling to the end of the message");
        yield return Screenshot("00-ko-attachment-collapsed.png");

        window.AttachmentButton.onClick.Invoke();
        yield return null; yield return null;
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(window.Scroll.content);
        VerifyAttachmentVisibility(true, "clicking the actual attachment button reveals all three letter sections");
        float expandedHeight = window.Scroll.content.rect.height;
        Check(expandedHeight > collapsedHeight + 20f, "expansion adds the complete letter to the scrollable layout");
        VerifyWindow(LanguageState.ko);

        window.AttachmentButton.onClick.Invoke();
        yield return null; yield return null;
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(window.Scroll.content);
        VerifyAttachmentVisibility(false, "clicking the attachment button again collapses the letter");
        Check(Mathf.Abs(window.Scroll.content.rect.height - collapsedHeight) < 1f,
            "collapsing removes the letter's height from the scrollable layout");
        GameDataManager.Instance.SetCurrentLanguage(LanguageState.zh_CN);
        yield return null; yield return null;
        VerifyAttachmentVisibility(false, "language change preserves a collapsed attachment");
        Check(window.BodyLabel.text == PostGameFarewellContent.ReturnMessage
            && window.AttachmentLabel.text == PostGameFarewellContent.Text("ui.attachment") + "  " + PostGameFarewellContent.FileName,
            "collapsed message and attachment filename refresh to the new language without text arrows");

        window.AttachmentButton.onClick.Invoke();
        yield return null; yield return null;
        VerifyAttachmentVisibility(true, "the actual button reopens the attachment after a language change");
        VerifyWindow(LanguageState.zh_CN);
        GameDataManager.Instance.SetCurrentLanguage(LanguageState.ko);
        yield return null; yield return null;
        VerifyAttachmentVisibility(true, "switching back to Korean preserves the expanded attachment");
        observations.Add(new {
            test = "actual attachment button", defaultExpanded = false, buttonClicks = 3,
            collapsedLanguageChangePreserved = true, expandedLanguageChangePreserved = true,
            koreanCollapsedHeight = collapsedHeight, koreanExpandedHeight = expandedHeight
        });
    }

    static void VerifyAttachmentVisibility(bool expanded, string label)
    {
        Check(window.IsAttachmentExpanded == expanded
            && window.LetterBeforeCatLabel.gameObject.activeInHierarchy == expanded
            && window.CatLabel.gameObject.activeInHierarchy == expanded
            && window.LetterAfterCatLabel.gameObject.activeInHierarchy == expanded
            && window.AttachmentLabel.gameObject.activeInHierarchy, label);
    }

    static void VerifyPolicy()
    {
        Check(PostGameFarewellState.CanRun(false, false, false, false, false), "ordinary player build allows farewell");
        for (int flags = 1; flags < 32; flags++)
            Check(!PostGameFarewellState.CanRun((flags & 1) != 0, (flags & 2) != 0, (flags & 4) != 0,
                (flags & 8) != 0, (flags & 16) != 0), "editor/demo/test/test-session/isolated exclusion mask " + flags);
        var state = new PostGameFarewellState { completedRunId = "completed-run" };
        Check(!state.ShouldShow("next-run", false), "unfinished games never show the return message");
        state.completed = true;
        Check(!state.ShouldShow("completed-run", false), "completion process cannot show its own next-launch message");
        Check(!state.ShouldShow("next-run", true), "another launch waits while the completing process is alive");
        Check(state.ShouldShow("next-run", false), "next launch after owner exit shows the message");
        state.windowDismissed = true;
        Check(!state.ShouldShow("third-run", false), "dismissal prevents all later launches from showing it");
        state.completedProcessId = int.MaxValue;
        Check(!state.OwnerIsAlive(), "a nonexistent owner PID is treated as exited");
        using (var process = System.Diagnostics.Process.GetCurrentProcess())
        {
            state.completedProcessId = process.Id;
            state.completedProcessStartTimeUtcTicks = process.StartTime.ToUniversalTime().Ticks;
            Check(state.OwnerIsAlive(), "PID and exact process start time identify the live owner");
            state.completedProcessStartTimeUtcTicks++;
            Check(!state.OwnerIsAlive(), "a reused PID with a different start time is not the owner");
        }
    }

    static void VerifyEditorLifecycleGate()
    {
        var service = PostGameFarewellService.Current;
        Check(Application.isEditor && service != null
            && service == GameDataManager.Instance.GetComponent<PostGameFarewellService>()
            && !string.IsNullOrEmpty(service.RunId), "the initialized production lifecycle service is attached to GameDataManager");
        const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic;
        var storeField = typeof(PostGameFarewellService).GetField("store", members);
        var stateField = typeof(PostGameFarewellService).GetField("state", members);
        Check(storeField != null && stateField != null && storeField.GetValue(service) == null
            && stateField.GetValue(service) == null, "Editor service has never opened the production receipt store");
        var before = ProtectedFiles(GameAutosaveService.Instance.SavePath);
        service.NotifyCompletedReturnToMenu();
        service.NotifyCompletedReturnToMenu();
        Check(storeField.GetValue(service) == null && stateField.GetValue(service) == null,
            "actual completion-return hook is blocked in Editor before reading or arming a production receipt");
        Check(PostGameFarewellWindow.Current == null
            && SameFiles(before, ProtectedFiles(GameAutosaveService.Instance.SavePath)),
            "repeated Editor completion-return hooks create no window and change no saves or delivery files");
        observations.Add(new {
            test = "actual Editor completion-return hook", calls = 2,
            storedCompletion = GameAutosaveService.Instance.StoredFinalEndingCompleted,
            productionReceiptStoreInitialized = false, protectedFilesUnchanged = true
        });
    }

    static void VerifyStore()
    {
        var store = new PostGameFarewellStateStore(Path.Combine(fixtureDirectory, "normal"));
        Check(store.TryRead(out var fresh, out var error) && fresh != null && !fresh.completed
            && !fresh.windowDismissed && error == null, "missing receipt loads as an unarmed state");
        Check(!File.Exists(store.FilePath), "reading a missing receipt does not create it");
        var proposed = new PostGameFarewellState {
            completed = true, completedRunId = "first-run", completedProcessId = 111,
            completedProcessStartTimeUtcTicks = 222, deliveryAttempted = true
        };
        Check(store.TrySave(proposed, out var armed, out error) && error == null,
            "first completion is durably saved to a temporary receipt");
        Check(new PostGameFarewellStateStore(store.DirectoryPath).TryRead(out var reopened, out error)
            && reopened.completed && reopened.completedRunId == "first-run" && reopened.deliveryAttempted,
            "a new store instance reads the durable completion and delivery claim");
        var stale = JsonUtility.FromJson<PostGameFarewellState>(JsonUtility.ToJson(armed));
        armed.windowDismissed = true; armed.deliveryStarted = true;
        Check(store.TrySave(armed, out var dismissed, out error) && dismissed.windowDismissed,
            "closing is durably recorded");
        stale.completed = false; stale.completedRunId = "replacement-run";
        stale.completedProcessId = 333; stale.completedProcessStartTimeUtcTicks = 444;
        stale.deliveryAttempted = false;
        Check(store.TrySave(stale, out var merged, out error) && merged.completed && merged.windowDismissed
            && merged.deliveryAttempted && merged.deliveryStarted,
            "a stale writer cannot undo completion, dismissal or delivery flags");
        Check(merged.completedRunId == "first-run" && merged.completedProcessId == 111
            && merged.completedProcessStartTimeUtcTicks == 222, "an armed receipt retains its original process ownership");
        Check(store.TryRead(out reopened, out error) && !reopened.ShouldShow("later-run", false),
            "a later process observes the permanent one-time dismissal");
        string hashBeforeLock = Hash(store.FilePath);
        using (var gate = new FileStream(Path.Combine(store.DirectoryPath, "lifecycle.lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None))
            Check(!store.TrySave(proposed, out _, out error) && !string.IsNullOrEmpty(error),
                "competing receipt lock fails softly");
        Check(Hash(store.FilePath) == hashBeforeLock, "lock contention preserves the existing receipt");

        VerifyRejectedReceipt("future", "{\"version\":2,\"completed\":false}");
        VerifyRejectedReceipt("corrupt", "{ this is not JSON");
        VerifyRejectedReceipt("incomplete", "{\"version\":1,\"completed\":true}");
        Check(Directory.GetFiles(fixtureDirectory, "*.tmp", SearchOption.AllDirectories).Length == 0,
            "successful and failed receipt writes leave no temporary payloads");
    }

    static void VerifyRejectedReceipt(string name, string payload)
    {
        var store = new PostGameFarewellStateStore(Path.Combine(fixtureDirectory, name));
        Directory.CreateDirectory(store.DirectoryPath); File.WriteAllText(store.FilePath, payload);
        string before = Hash(store.FilePath);
        Check(!store.TryRead(out var rejected, out var error) && rejected == null && !string.IsNullOrEmpty(error),
            name + " receipt read fails softly");
        Check(!store.TrySave(new PostGameFarewellState(), out _, out error) && !string.IsNullOrEmpty(error),
            name + " receipt cannot be silently replaced");
        Check(Hash(store.FilePath) == before, name + " receipt bytes are preserved for diagnosis");
    }

    static void VerifyWindow(LanguageState language)
    {
        Check(PostGameFarewellContent.IsReady(), "all required keys have this language's own translation");
        Check(window.BodyLabel.text == PostGameFarewellContent.ReturnMessage,
            "all seven return paragraphs match the localized source exactly");
        window.CatLabel.ForceMeshUpdate();
        Check(window.CatLabel.GetParsedText() == PostGameFarewellContent.Cat,
            "the rendered cat preserves the exact ASCII shape, including <3");
        Check(RenderedLetter() == PostGameFarewellContent.Letter,
            "before-cat, rendered cat and after-cat reconstruct the exact exported letter");
        Check(window.AttachmentLabel.text == PostGameFarewellContent.Text("ui.attachment") + "  " + PostGameFarewellContent.FileName,
            "attachment label names the localized text file");
        var closeLabel = window.CloseButton.GetComponentInChildren<TMP_Text>();
        Check(closeLabel != null && closeLabel.text == PostGameFarewellContent.Text("ui.close"),
            "close button uses this language's label");
        string filename = PostGameFarewellContent.FileName;
        Check(Path.GetFileName(filename) == filename && filename.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
            && filename.IndexOfAny(Path.GetInvalidFileNameChars()) < 0, "localized attachment name is a valid plain .txt filename");
        foreach (var label in window.GetComponentsInChildren<TMP_Text>(true))
        {
            label.ForceMeshUpdate();
            string plain = label.richText ? label.GetParsedText() : label.text;
            string glyphs = new string(plain.Where(character => !char.IsWhiteSpace(character)).ToArray());
            uint[] missing = null;
            bool covered = label.font != null && label.font.HasCharacters(glyphs, out missing, true, true);
            Check(covered, label.name + " has zero missing glyphs" + (missing == null ? "" : ": " + string.Join(",", missing)));
            Check(!label.isTextOverflowing && !label.isTextTruncated, label.name + " is not truncated");
            if (language == LanguageState.ko && GameDataManager.Instance.LocalizationData.KoreanFont != null)
                Check(label.font == GameDataManager.Instance.LocalizationData.KoreanFont,
                    label.name + " uses the configured Korean font");
        }
        if (language != LanguageState.ko && GameDataManager.Instance.LocalizationData.KoreanFont != null)
            Check(window.BodyLabel.font != GameDataManager.Instance.LocalizationData.KoreanFont,
                "switching away from Korean restores the base body font");
        CheckCatSpacing();
    }

    static string RenderedLetter() => window.LetterBeforeCatLabel.text + window.CatLabel.GetParsedText()
        + window.LetterAfterCatLabel.text;

    static void CheckCatSpacing()
    {
        var info = window.CatLabel.textInfo;
        var firstLine = Enumerable.Range(0, info.characterCount).Select(index => info.characterInfo[index])
            .Where(character => character.lineNumber == 0 && character.character != '\n' && character.character != '\r').ToArray();
        Check(firstLine.Length >= 5, "cat first line has all authored characters");
        float advance = firstLine[1].xAdvance - firstLine[0].xAdvance;
        Check(advance > 0f && Enumerable.Range(2, firstLine.Length - 2)
            .All(index => Mathf.Abs(firstLine[index].xAdvance - firstLine[index - 1].xAdvance - advance) < .05f),
            "cat characters have equal horizontal advances");
    }

    static IEnumerator Screenshot(string name)
    {
        Canvas.ForceUpdateCanvases();
        string path = Path.Combine(OutputDirectory, name);
        ScreenCapture.CaptureScreenshot(path);
        float deadline = Time.realtimeSinceStartup + 8f;
        do { yield return null; } while ((!File.Exists(path) || new FileInfo(path).Length < 128)
            && Time.realtimeSinceStartup < deadline);
        Check(File.Exists(path) && new FileInfo(path).Length > 128, "saved screenshot " + name);
        screenshots.Add(path);
    }

    static Rect BoundsIn(RectTransform parent, RectTransform target)
    {
        var corners = new Vector3[4]; target.GetWorldCorners(corners);
        var points = corners.Select(point => parent.InverseTransformPoint(point)).ToArray();
        return Rect.MinMaxRect(points.Min(point => point.x), points.Min(point => point.y),
            points.Max(point => point.x), points.Max(point => point.y));
    }
    static float[] RectData(Rect rect) => new[] { rect.x, rect.y, rect.width, rect.height };
    static Dictionary<string, string> ProtectedFiles(string savePath)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(savePath))
            foreach (string suffix in new[] { "", ".bak", ".tmp" }) paths.Add(Path.GetFullPath(savePath + suffix));
        foreach (string directory in new[] { Path.Combine(Application.persistentDataPath, "Saves"), PostGameFarewellDelivery.StateDirectory })
            if (Directory.Exists(directory))
                foreach (string path in Directory.GetFiles(directory, "*", SearchOption.AllDirectories)) paths.Add(Path.GetFullPath(path));
        return paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(path => path, Hash, StringComparer.OrdinalIgnoreCase);
    }
    static bool SameFiles(Dictionary<string, string> before, Dictionary<string, string> after)
        => before != null && after != null && before.Count == after.Count
            && before.All(pair => after.TryGetValue(pair.Key, out var hash) && hash == pair.Value);
    static string Hash(string path)
    {
        if (!File.Exists(path)) return "missing";
        using (var hash = SHA256.Create()) using (var input = File.OpenRead(path))
            return BitConverter.ToString(hash.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
    }
    static void CaptureError(string message, string stack, LogType type)
    {
        if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert)
            errors.Add(message + "\n" + stack);
    }
    static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        checks.Add(context + ": " + label);
    }
}
