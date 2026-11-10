using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using MrTGame.Core;
using MrTGame.Systems;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;

// Opt-in Play Mode checks. Uses real checkpoint/UI APIs with cloned save data and
// isolated storage. Stops at the menu afterward; leave Play Mode after inspection.
public static class OSEmptyEnergyReminderQA
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    const string Sequence = OSCharacterInteractionIds.Chapter01MemoryRepair;
    const string Recall = "chapter01_memory_mother_daughter_recall";
    static readonly List<string> checks = new List<string>();
    static OS_Handle os;
    static OSLinkMessageController link;
    static OSWindowUnit live;
    static int emptyEvents, failures, gameOvers, reminderStarts;
    static string lastReminder;
    static float lastReminderTime;
    static Vector2 shakeRest;
    public static string Status { get; private set; } = "idle";
    public static string OutputDirectory { get; private set; }

    [MenuItem("Tools/QA/Verify Empty Energy Reminder")]
    public static void RunFromMenu() => Debug.Log(Start());

    public static string Start()
    {
        if (Status == "running") return Status;
        var saves = GameAutosaveService.Instance;
        if (!Application.isPlaying || saves == null || GameDataManager.Instance == null
            || GameResourceSystem.Instance == null || GameManager.Instance == null
            || SceneLoadManager.Instance == null || GameTest.Instance == null)
            return "Play Mode initialization required";
        if (saves.IsBusy || saves.IsWriting || GameManager.Instance.IsTransitioning
            || SceneLoadManager.Instance.IsBusy || GameTest.Instance.IsCheckpointJumpRunning)
            return "Wait for active save/scene operation";
        checks.Clear(); os = null; link = null; live = null;
        emptyEvents = failures = gameOvers = reminderStarts = 0;
        OutputDirectory = Path.GetFullPath(Path.Combine("Temp", "OSEmptyEnergyReminderQA", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(OutputDirectory); Status = "running";
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
        bool resourcesEnabled = resources.enabled, background = Application.runInBackground;
        string[] paths = { saves.SavePath, saves.SavePath + ".bak", saves.SavePath + ".tmp" };
        string[] hashes = null;
        var work = new Stack<IEnumerator>();
        try
        {
            saves.EnterTestSession(); hashes = paths.Select(Fingerprint).ToArray();
            saves.UseIsolatedStorageForTests(OutputDirectory); saves.EnterTestSession();
            data.ReplaceSaveData(Clone(original));
            resources.enabled = false; Application.runInBackground = true;
            GameEvents.EmptyEnergyUsed += OnEmpty;
            GameEvents.ResourceFailed += OnFailure;
            GameEvents.GameOver += OnGameOver;
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
                    Status = "FAILED: " + error;
                    Debug.LogError("[Empty Energy QA] " + Status);
                    yield break;
                }
                if (next is IEnumerator nested) work.Push(nested); else yield return next;
            }
            Check(paths.Select(Fingerprint).SequenceEqual(hashes), "normal save files remain unchanged");
            Check(saves.IsTestSession && !saves.IsWriting, "test session still blocks normal autosave");
            Status = "PASS: " + checks.Count + " empty-energy and message lifecycle checks";
        }
        finally
        {
            while (work.Count > 0) (work.Pop() as IDisposable)?.Dispose();
            GameEvents.EmptyEnergyUsed -= OnEmpty;
            GameEvents.ResourceFailed -= OnFailure;
            GameEvents.GameOver -= OnGameOver;
            if (link != null) { link.EntryPresentationStarted -= OnStarted; link.NotifyOSHidden(); }
            GameTest.Instance?.GetComponent<GameTestConsole>()?.SetOpen(false);
            saves.EnterTestSession(); saves.EndSession();
            GameManager.Instance?.OpenMainMenuWithoutSave();
            data.ReplaceSaveData(original); data.SetCurrentLanguage(language);
            if (resources != null) resources.enabled = resourcesEnabled;
            Application.runInBackground = background;
            if (hashes != null && !paths.Select(Fingerprint).SequenceEqual(hashes))
                Status = "FAILED: normal save files changed\n" + Status;
            if (Status == "running") Status = "FAILED: QA interrupted before completion";
            File.WriteAllText(Path.Combine(OutputDirectory, "results.txt"), Status + "\n" + string.Join("\n", checks));
            Debug.Log("[Empty Energy QA] " + Status + " | " + OutputDirectory);
        }
    }

    static IEnumerator Run()
    {
        GameTest.Instance.GetComponent<GameTestConsole>()?.SetOpen(false);
        Check(GameTest.Instance.JumpToCheckpoint(GameTestCheckpoint.Chapter01BeforeToolUnlock), "real chapter-one checkpoint accepted");
        yield return Wait(() => !GameTest.Instance.IsCheckpointJumpRunning && !GameManager.Instance.IsTransitioning
            && !SceneLoadManager.Instance.IsBusy && OS_Handle.Instance != null, "checkpoint ready", 70f);
        Check(GameTest.Instance.JumpSucceeded, "checkpoint completed successfully");
        os = OS_Handle.Instance; link = os.LinkMessageController;
        live = os.CharacterWindowController.GetComponent<OSWindowUnit>();
        link.EntryPresentationStarted += OnStarted;
        GameTest.Instance.GetComponent<GameTestConsole>()?.SetOpen(false);
        FocusGameView();
        yield return Wait(() => Application.isFocused, "Unity Game view has application focus", 12f);
        var data = GameDataManager.Instance; var resources = GameResourceSystem.Instance;
        data.SetCurrentLanguage(LanguageState.zh_CN);
        ResetMessages(true); ValidateData(data); ValidateResourceEvents(data, resources);
        Invoke(os, "ResetEmptyEnergyReminder");
        resources.ApplySnapshot(80, 0, 10);
        os.OpenCharacterWindowForReconnect(0f);
        os.CharacterWindowController.ShowAnimaState(false);
        os.CharacterWindowController.PlayResourceAnimation();
        yield return Wait(() => live.IsOpen && !live.IsOpening && !live.IsClosing, "real Live window is ready");
        Check(data.OSResourceState.IsEnergyLow && Field<string>(os.CharacterWindowController, "currentResourceAnimationGroup") == "LowBattery",
            "zero energy selects the existing LowBattery animation group");
        yield return Wait(EnergyHudIsZero, "energy HUD settles at visible 000 after resource fixtures", 30f);
        yield return Hold(.2f);
        Check(reminderStarts == 0 && !Field<bool>(os, "emptyEnergyReminderPending"), "loading or broadcasting zero does not invent a reminder");

        // Actual memory option: maintenance still advances at zero and emits one attempt.
        var memory = os.MemoryWindowController;
        var memoryWindow = memory.GetComponent<OSWindowUnit>();
        memoryWindow.OpenStandalone(0f);
        Check(memory.ShowData(1170), "real paid memory node 1170 opens");
        os.NotifyWindowPointerPressed(memoryWindow);
        yield return Wait(() => memoryWindow.IsOpen && !memoryWindow.IsOpening, "memory window ready for its real input API");
        if (!memory.IsCurrentTextComplete) memory.TryAdvance();
        yield return Wait(() => memory.IsCurrentTextComplete, "paid choice text completed");
        var paid = memory.CurrentData.Options.First(o => o.ResourceChange.Battery < 0);
        int beforeEvents = emptyEvents;
        Check(memory.IsOptionAvailable(paid), "paid choice remains available at zero energy");
        memory.SelectOption(paid);
        Check(memory.CurrentData.DataIndex == paid.TargetDataIndex && data.GetOSResourceValue(ResourceType.Battery) == 0
            && emptyEvents == beforeEvents + 1, "zero-energy memory choice advances and emits exactly one reminder request");
        resources.Modify(ResourceType.Battery, -1); resources.Modify(ResourceType.Battery, -1);
        Check(Field<bool>(os, "emptyEnergyReminderPending") && reminderStarts == 0,
            "same-frame empty spends merge into one deferred request");
        memoryWindow.CloseSilently();
        link.SetGlobalMessagesSuppressed(false);
        yield return Wait(() => reminderStarts == 1, "zero-to-zero spend displays one random reminder");
        Check(IsReminder(lastReminder), "random reminder belongs to the three authored entries");
        Check(Field<object>(os.CharacterWindowController, "chapter01ShakeTween") != null, "reminder starts the Live DOShake tween");
        yield return Hold(.65f);
        Check(Vector2.Distance(live.WindowRect.anchoredPosition, shakeRest) < .05f, "Live shake returns exactly to its rest position");
        yield return Wait(() => link.DisplayedEntryId == lastReminder && !link.IsPresenting, "reminder finishes typing");
        Check(EnergyHudIsZero(), "screenshot energy HUD visibly agrees with zero-energy model");
        string png = Path.Combine(OutputDirectory, "empty-energy-reminder.png");
        ScreenCapture.CaptureScreenshot(png);
        yield return Wait(() => File.Exists(png), "reminder screenshot saved", 10f);
        resources.Modify(ResourceType.Battery, -1);
        Check(!Field<bool>(os, "emptyEnergyReminderPending"), "spend inside eight-second cooldown is not queued");
        yield return Wait(() => !link.LinkWindow.IsOpen && !link.LinkWindow.IsClosing, "reminder closes naturally after its read hold");
        Check(reminderStarts == 1, "same-frame requests and cooldown produce no duplicate messages");
        // Keep the real save history: the same repeatable line must remain usable.
        string repeatedId = lastReminder;
        string repeatedSignal = "energy.empty." + repeatedId.Substring("energy_empty_".Length);
        Check((bool)typeof(OSLinkMessageController).GetMethod("TryPresentIdleGlobalSignal", Private)
            .Invoke(link, new object[] { repeatedSignal }), "the same reminder signal can be queued again without clearing history");
        yield return Wait(() => reminderStarts == 2 && lastReminder == repeatedId, "the same authored reminder starts a second time");
        yield return Wait(() => !link.LinkWindow.IsOpen && !link.LinkWindow.IsClosing, "repeated reminder completes its natural read hold");
        yield return Wait(() => Time.unscaledTime >= lastReminderTime + 8.1f, "real eight-second cooldown elapses", 10f);
        resources.ApplySnapshot(80, 1, 10); resources.Modify(ResourceType.Battery, -3);
        yield return Wait(() => reminderStarts == 3, "positive-to-zero spend can remind again after cooldown");

        // A real story option owns the channel until the player's response.
        ResetMessages(false); Invoke(os, "ResetEmptyEnergyReminder");
        data.OSCharacterInteractionState.ActivateSequence(Sequence);
        Check(link.TryPresentEntry(Recall), "real story choice starts");
        var optionTextLabel = Field<TMPro.TMP_Text>(link, "textWithOptionsLabel");
        yield return Wait(() => link.DisplayedEntryId == Recall && link.IsPresenting && !link.IsWaitingForOption
            && Field<object>(link, "textTween") != null && !string.IsNullOrEmpty(optionTextLabel.text),
            "story choice is visibly partway through typing");
        live.Minimize();
        Check(live.IsMinimized && !link.isActiveAndEnabled, "minimizing Live suspends in-progress typing");
        yield return Hold(.2f);
        live.RestoreAndBringToFront();
        yield return Wait(() => link.DisplayedEntryId == Recall && link.IsPresenting && link.isActiveAndEnabled,
            "restoring Live resumes the interrupted story choice");
        yield return Wait(() => link.DisplayedEntryId == Recall && link.IsWaitingForOption, "story choice reaches its player response");
        live.Minimize();
        Check(live.IsMinimized && link.IsWaitingForOption, "minimizing Live preserves its unanswered choice");
        yield return Hold(.2f);
        live.RestoreAndBringToFront();
        yield return null;
        Check(link.DisplayedEntryId == Recall && link.IsWaitingForOption && link.isActiveAndEnabled
            && link.TryGetActiveOptionTarget("reassure_mother_daughter", out var restoredChoice)
            && restoredChoice.GetComponentInParent<OSLinkMessageSelectUnit>().IsInteractable,
            "restored waiting choice remains interactive before the real pointer click");
        int starts = reminderStarts;
        resources.Modify(ResourceType.Battery, -1);
        yield return Hold(.25f);
        Check(reminderStarts == starts && link.DisplayedEntryId == Recall && link.IsWaitingForOption
            && Field<bool>(os, "emptyEnergyReminderPending"), "empty energy never replaces a waiting story option");
        int storyWindowsClosed = 0;
        Action<OSWindowUnit> onStoryClosed = _ => storyWindowsClosed++;
        link.LinkWindow.Closed += onStoryClosed;
        try
        {
            ClickRecallOption(data);
            DumpDiagnostic("immediately after real story pointer click");
            yield return Wait(() => reminderStarts == starts + 1, "pending reminder waits until the story releases Live");
            Check(storyWindowsClosed == 1, "mother-and-daughter choice closes its window exactly once");
        }
        finally { link.LinkWindow.Closed -= onStoryClosed; }

        // The completed text's authored reading hold is also protected.
        ResetMessages(false); Invoke(os, "ResetEmptyEnergyReminder");
        data.OSCharacterInteractionState.ActivateSequence(Sequence);
        const string holdEntry = "chapter01_memory_upgrade_02";
        Check(link.TryPresentEntry(holdEntry), "real reading-hold story starts");
        yield return Wait(() => link.DisplayedEntryId == holdEntry && link.LinkWindow.IsOpen && !link.LinkWindow.IsOpening,
            "reading-hold message is visible");
        link.TryCompleteDisplayedTextImmediately(holdEntry);
        yield return Wait(() => !link.IsPresenting, "story typing completes while its reading hold remains");
        starts = reminderStarts; resources.Modify(ResourceType.Battery, -1);
        yield return Hold(.25f);
        Check(reminderStarts == starts && link.DisplayedEntryId == holdEntry && link.LinkWindow.IsOpen,
            "reminder respects completed text still held for reading");
        resources.Modify(ResourceType.Battery, 25);
        yield return Hold(.15f);
        Check(!Field<bool>(os, "emptyEnergyReminderPending"), "charging above zero cancels the waiting reminder");
        yield return Wait(() => !link.LinkWindow.IsOpen, "story read hold closes normally");
        yield return Hold(.2f);
        Check(reminderStarts == starts, "cancelled reminder does not appear after the story closes");

        ResetMessages(true); Invoke(os, "ResetEmptyEnergyReminder");
        resources.ApplySnapshot(80, 0, 10); resources.Modify(ResourceType.Battery, -1);
        Check(Field<bool>(os, "emptyEnergyReminderPending"), "save replacement fixture has a waiting reminder");
        data.ReplaceSaveData(Clone(data.SaveData));
        yield return Hold(.15f);
        Check(!Field<bool>(os, "emptyEnergyReminderPending"), "replacing the save discards transient reminder state");
        link.SetGlobalMessagesSuppressed(false);
        yield return Hold(.2f);
        Check(reminderStarts == starts, "restoring a zero-energy save does not replay transient reminders");

        // Completed text remains visible while disabled, then gets its full authored
        // reading time on every restore, without presenting/completing the entry again.
        Invoke(os, "ResetEmptyEnergyReminder");
        resources.ApplySnapshot(80, 25, 10);
        yield return ValidateReadHoldRestore("chapter01_memory_upgrade_02", false, false, false);
        yield return ValidateReadHoldRestore("energy_empty_1", true, false, true);
        yield return ValidateReadHoldRestore("energy_empty_2", true, true, false);
    }

    static IEnumerator ValidateReadHoldRestore(string entryId, bool global, bool deactivateObject, bool repeatMinimize)
    {
        var data = GameDataManager.Instance;
        ResetMessages(false);
        int started = 0, textCompleted = 0, presentationCompleted = 0;
        Action<string, OSCharacterInteractionEntryData> onStart = (_, entry) => { if (entry.EntryId == entryId) started++; };
        Action<string, OSCharacterInteractionEntryData> onText = (_, entry) => { if (entry.EntryId == entryId) textCompleted++; };
        Action<string, OSCharacterInteractionEntryData> onComplete = (_, entry) => { if (entry.EntryId == entryId) presentationCompleted++; };
        link.EntryPresentationStarted += onStart;
        link.EntryTextPlaybackCompleted += onText;
        link.EntryPresentationCompleted += onComplete;
        try
        {
            OSCharacterInteractionEntryData authored;
            if (global)
            {
                Check(data.OSCharacterInteractionData.TryGetGlobalEntry(entryId, out authored), entryId + " global fixture exists");
                Check((bool)typeof(OSLinkMessageController).GetMethod("TryPresentIdleGlobalSignal", Private)
                    .Invoke(link, new object[] { authored.TriggerTargetId }), entryId + " global reading-hold fixture starts");
            }
            else
            {
                data.OSCharacterInteractionState.ActivateSequence(Sequence);
                Check(data.OSCharacterInteractionData.TryGetSequence(Sequence, out var sequence)
                    && sequence.TryGetEntry(entryId, out _), entryId + " sequence fixture exists");
                sequence.TryGetEntry(entryId, out authored);
                Check(link.TryPresentEntry(entryId), entryId + " sequence reading-hold fixture starts");
            }
            Check(Mathf.Approximately(authored.AutoCloseDelay, 4f), entryId + " authors exactly four seconds of reading time");
            yield return Wait(() => link.DisplayedEntryId == entryId && link.LinkWindow.IsOpen && !link.LinkWindow.IsOpening,
                entryId + " reading-hold fixture is visible");
            link.TryCompleteDisplayedTextImmediately(entryId);
            yield return Wait(() => !link.IsPresenting && link.DisplayedEntryId == entryId,
                entryId + " completes typing before suspension");
            Check(started == 1 && textCompleted == 1 && presentationCompleted == 1,
                entryId + " emits each presentation event once before suspension");
            var label = link.DisplayedTextLabel;
            string completeText = label.text;
            int visibleCharacters = label.maxVisibleCharacters;
            Func<bool> sameCompletedText = () => link.isActiveAndEnabled && !link.IsPresenting && !link.IsWaitingForOption
                && link.DisplayedEntryId == entryId && label.text == completeText
                && label.maxVisibleCharacters == visibleCharacters && Field<object>(link, "textTween") == null
                && started == 1 && textCompleted == 1 && presentationCompleted == 1;

            yield return Hold(.65f);
            if (deactivateObject) live.gameObject.SetActive(false); else live.Minimize();
            Check(!link.isActiveAndEnabled && (deactivateObject || live.IsMinimized),
                entryId + " suspends after some reading time has elapsed");
            yield return Hold(4.25f);
            Check(link.LinkWindow.IsOpen && !link.LinkWindow.IsClosing && link.DisplayedEntryId == entryId,
                entryId + " stays available while hidden longer than its original timer");
            float restoredAt = Time.unscaledTime;
            if (deactivateObject) live.gameObject.SetActive(true); else live.RestoreAndBringToFront();
            yield return null;
            Check(sameCompletedText(), entryId + " restores complete text without retyping or replaying message events");

            if (repeatMinimize)
            {
                yield return Hold(1.15f);
                Check(sameCompletedText() && link.LinkWindow.IsOpen && !link.LinkWindow.IsClosing,
                    entryId + " remains in its renewed reading hold before a second minimize");
                link.LinkWindow.Minimize();
                Check(link.LinkWindow.IsMinimized && !link.isActiveAndEnabled,
                    entryId + " can also suspend by minimizing Link directly");
                yield return Hold(.25f);
                restoredAt = Time.unscaledTime;
                link.LinkWindow.RestoreAndBringToFront();
                yield return null;
                Check(sameCompletedText(), entryId + " second restore still preserves the same completed text and events");
            }

            // Observe the actual close transition, not the tween's configured duration.
            while (link.LinkWindow.IsOpen && !link.LinkWindow.IsClosing && Time.unscaledTime < restoredAt + 6f)
            {
                FocusGameView();
                yield return null;
            }
            float closeDelay = Time.unscaledTime - restoredAt;
            Check((!link.LinkWindow.IsOpen || link.LinkWindow.IsClosing) && closeDelay >= 3.95f && closeDelay < 5.5f,
                entryId + " starts closing only after a fresh full four-second hold (observed " + closeDelay.ToString("F3") + "s)");
            yield return Wait(() => !link.LinkWindow.IsOpen && !link.LinkWindow.IsClosing,
                entryId + " finishes closing after the restored hold");
            Check(started == 1 && textCompleted == 1 && presentationCompleted == 1 && string.IsNullOrEmpty(link.DisplayedEntryId),
                entryId + " clears normally without duplicating presentation or completion events");
        }
        finally
        {
            link.EntryPresentationStarted -= onStart;
            link.EntryTextPlaybackCompleted -= onText;
            link.EntryPresentationCompleted -= onComplete;
            if (!live.gameObject.activeSelf) live.gameObject.SetActive(true);
            if (live.IsMinimized) live.RestoreAndBringToFront();
            if (link.LinkWindow.IsMinimized) link.LinkWindow.RestoreAndBringToFront();
        }
    }

    static void ValidateResourceEvents(GameDataManager data, GameResourceSystem resources)
    {
        resources.ApplySnapshot(80, 2, 10); int n = emptyEvents, f = failures, g = gameOvers;
        resources.Modify(ResourceType.Battery, -3);
        Check(emptyEvents == n + 1 && data.GetOSResourceValue(ResourceType.Battery) == 0, "resource positive-to-zero emits one event");
        resources.Modify(ResourceType.Battery, -3);
        Check(emptyEvents == n + 2, "resource zero-to-zero emits another event");
        resources.Modify(ResourceType.Battery, 0); resources.Modify(ResourceType.Battery, 1);
        Check(emptyEvents == n + 2 && failures == f && gameOvers == g, "zero delta and recharge do not remind; battery depletion never fails");
        data.ModifyOSResourceValue(ResourceType.Battery, -2);
        data.ModifyOSResourceValue(ResourceType.Battery, -2);
        Check(emptyEvents == n + 4, "data fallback covers positive-to-zero and zero-to-zero attempts");
        var option = JsonUtility.FromJson<OSCharacterInteractionOptionData>(
            "{\"optionId\":\"qa_paid\",\"resourceChange\":{\"heart\":0,\"battery\":-1,\"fault\":0}}");
        Check(data.TryApplyCharacterOptionResources("qa_energy", "qa_entry", option) && emptyEvents == n + 5,
            "Live option direct-write path emits a zero-energy attempt");
        Check(!data.TryApplyCharacterOptionResources("qa_energy", "qa_entry", option) && emptyEvents == n + 5,
            "Live option receipt blocks duplicate spend and reminder");
        resources.ApplySnapshot(80, 0, 10);
        Check(!(bool)Invoke(resources, "HasFailedResource"), "zero-energy snapshot is not a failed resource state");
        resources.ApplySnapshot(1, 20, 10); resources.Modify(ResourceType.Heart, -1);
        Check(failures == f + 1 && gameOvers == g + 1, "heart depletion keeps its existing failure signal");
        resources.ApplySnapshot(80, 20, 99); resources.Modify(ResourceType.Fault, 1);
        Check(failures == f + 2 && gameOvers == g + 2, "fault overflow keeps its existing failure signal");
        resources.ApplySnapshot(80, 0, 10);
    }

    static void ClickRecallOption(GameDataManager data)
    {
        Check(link.TryGetActiveOptionTarget("reassure_mother_daughter", out var target), "real reassurance option has a pointer target");
        var option = target.GetComponentInParent<OSLinkMessageSelectUnit>();
        var events = EventSystem.current;
        Check(events != null && events.isActiveAndEnabled && option != null && option.IsInteractable,
            "reassurance option and EventSystem accept input");
        Canvas.ForceUpdateCanvases();
        var canvas = target.GetComponentInParent<Canvas>().rootCanvas;
        var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        var pointer = new PointerEventData(events)
        {
            position = RectTransformUtility.WorldToScreenPoint(camera, target.TransformPoint(target.rect.center)),
            button = PointerEventData.InputButton.Left,
            clickCount = 1
        };
        var hits = new List<RaycastResult>();
        events.RaycastAll(pointer, hits);
        Check(hits.Count > 0 && ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject) == option.gameObject,
            "frontmost UI raycast reaches the visible reassurance option");
        pointer.pointerCurrentRaycast = hits[0];
        var hit = hits[0].gameObject;
        int beforeHeart = data.GetOSResourceValue(ResourceType.Heart);
        ExecuteEvents.ExecuteHierarchy(hit, pointer, ExecuteEvents.pointerDownHandler);
        ExecuteEvents.ExecuteHierarchy(hit, pointer, ExecuteEvents.pointerUpHandler);
        Check(ExecuteEvents.ExecuteHierarchy(hit, pointer, ExecuteEvents.pointerClickHandler) == option.gameObject,
            "left pointer click dispatches through the visible option");
        Check(data.GetOSResourceValue(ResourceType.Heart) == beforeHeart + 5,
            "clicking reassurance awards exactly five heart");
        Check(!link.IsWaitingForOption && link.DisplayedEntryId != Recall
            && (!link.LinkWindow.IsOpen || link.LinkWindow.IsClosing), "selected story text clears and its window begins closing");
        ExecuteEvents.Execute(option.gameObject, pointer, ExecuteEvents.pointerClickHandler);
        Check(data.GetOSResourceValue(ResourceType.Heart) == beforeHeart + 5,
            "a duplicate pointer click cannot award heart twice");
    }

    static void ValidateData(GameDataManager data)
    {
        string[] chinese = { "虽然勉强还能用..但是需要充电啦，已经没有电了...", "需要充电!!", "已经完全没有电用啦.." };
        for (int i = 0; i < 3; i++)
        {
            string id = "energy_empty_" + i;
            Check(data.OSCharacterInteractionData.TryGetGlobalEntry(id, out var entry)
                && entry.TriggerTargetId == "energy.empty." + i && entry.Repeatable
                && entry.PartType == OSCharacterInteractionPartType.Text && entry.Options.Count == 0
                && entry.DisplayPolicy == OSCharacterInteractionDisplayPolicy.DoText && entry.AutoCloseDelay >= 4f,
                id + " is a repeatable plain reminder with an authored reading hold");
            Check(data.GetLocalizedText(entry.LocalizationKey, entry.FallbackText) == chinese[i], id + " preserves requested Chinese copy");
            var localized = data.LocalizationData.Entries.SingleOrDefault(e => e.Key == entry.LocalizationKey);
            Check(localized != null && localized.Values.Count == 10 && localized.Values.Select(v => v.Language).Distinct().Count() == 10
                && localized.Values.All(v => !string.IsNullOrWhiteSpace(v.Text)), id + " has ten explicit localized texts");
        }
    }

    static void ResetMessages(bool suppress)
    {
        link.NotifyOSHidden();
        GameDataManager.Instance.OSProcessData.characterInteraction = new OSCharacterInteractionSaveData();
        link.SetGlobalMessagesSuppressed(suppress); link.NotifyOSBecameVisible();
    }
    static void OnEmpty() => emptyEvents++;
    static void OnFailure(ResourceType type) => failures++;
    static void OnGameOver() => gameOvers++;
    static bool IsReminder(string id) => id == "energy_empty_0" || id == "energy_empty_1" || id == "energy_empty_2";
    static void OnStarted(string sequence, OSCharacterInteractionEntryData entry)
    {
        if (!IsReminder(entry.EntryId)) return;
        reminderStarts++; lastReminder = entry.EntryId; lastReminderTime = Time.unscaledTime;
        shakeRest = live.WindowRect.anchoredPosition;
    }
    static IEnumerator Wait(Func<bool> condition, string label, float seconds = 12f)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (!condition() && Time.realtimeSinceStartup < deadline) { FocusGameView(); yield return null; }
        Check(condition(), label);
    }
    static IEnumerator Hold(float seconds)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < until) { FocusGameView(); yield return null; }
    }
    static void FocusGameView()
    {
        Type type = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
        if (type != null) EditorWindow.GetWindow(type).Focus();
    }
    static GameSaveData Clone(GameSaveData value) => JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(value));
    static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Private).GetValue(owner);
    static object Invoke(object owner, string name) => owner.GetType().GetMethod(name, Private).Invoke(owner, null);
    static void Check(bool condition, string label)
    {
        if (!condition)
        {
            DumpDiagnostic("FAILED: " + label);
            throw new InvalidOperationException(label);
        }
        checks.Add(label);
    }
    static bool EnergyHudIsZero()
    {
        if (os == null) return false;
        var views = os.GetComponentsInChildren<OSResourceStateView>(true)
            .Where(v => v.ResourceType == ResourceType.Battery && v.gameObject.activeInHierarchy).ToArray();
        return views.Length > 0 && views.All(v => v.DisplayedValue == 0 && v.QueuedValue == 0
            && v.PendingAnimationCount == 0 && Field<TMPro.TMP_Text>(v, "valueLabel").text.StartsWith("000", StringComparison.Ordinal));
    }
    static void DumpDiagnostic(string label)
    {
        if (string.IsNullOrEmpty(OutputDirectory)) return;
        try
        {
            var data = GameDataManager.Instance;
            var lines = new List<string> { "--- " + label + " ---", "frame=" + Time.frameCount + " time=" + Time.unscaledTime,
                "events=" + emptyEvents + " starts=" + reminderStarts + " last=" + lastReminder,
                "focused=" + Application.isFocused + " phase=" + GameManager.Instance?.Phase + " chapter=" + data?.CurrentChapterIndex,
                "energy=" + data?.GetOSResourceValue(ResourceType.Battery) + " console=" + GameTestConsole.BlocksGameplayInput,
                "restoring=" + OSSessionPersistence.IsRestoring + " busy=" + (GameSessionFlow.Instance?.IsBusy == true) };
            AddFields(lines, "os", os, "IsOSVisible", "IsSystemInputLocked", "emptyEnergyReminderPending", "emptyEnergyReminderFrame", "nextEmptyEnergyReminderAt");
            if (os != null) lines.Add("sameSave=" + ReferenceEquals(Field<GameSaveData>(os, "emptyEnergyReminderSave"), data?.SaveData));
            AddFields(lines, "character", os?.CharacterWindowController, "CurrentState", "CurrentExtendedAnimation", "IsTransitioning", "currentResourceAnimationGroup");
            AddWindow(lines, "Live", live); AddWindow(lines, "LinkWindow", link?.LinkWindow);
            AddFields(lines, "link", link, "isOSVisible", "IsPresenting", "hasDisplayedMessage", "IsWaitingForOption",
                "IsWaitingForExternalCompletion", "DisplayedSequenceId", "DisplayedEntryId", "GlobalMessagesSuppressed", "AreGlobalMessagesSuppressed",
                "pendingSequenceTween", "pendingGlobalTween", "reconnectAnimationTween", "nextEntryTween", "autoCloseTween", "presentationVersion",
                "restartAutoCloseAfterRestore", "resumePresentationAfterOwnerRestore");
            if (link != null) lines.Add("settingsOpen=" + Invoke(link, "IsOptionCountdownSettingsOpen"));
            if (data != null) lines.Add("savedInteraction=" + JsonUtility.ToJson(data.OSCharacterInteractionState));
            if (os != null)
                foreach (var view in os.GetComponentsInChildren<OSResourceStateView>(true).Where(v => v.ResourceType == ResourceType.Battery))
                    lines.Add("energyHUD=" + view.DisplayedValue + " queued=" + view.QueuedValue + " animations=" + view.PendingAnimationCount
                        + " text=" + Field<TMPro.TMP_Text>(view, "valueLabel")?.text);
            File.AppendAllText(Path.Combine(OutputDirectory, "diagnostic.txt"), string.Join("\n", lines) + "\n\n");
        }
        catch (Exception error)
        {
            File.AppendAllText(Path.Combine(OutputDirectory, "diagnostic.txt"), "Diagnostic error: " + error + "\n");
        }
    }
    static void AddWindow(List<string> lines, string prefix, OSWindowUnit window)
    {
        AddFields(lines, prefix, window, "IsOpen", "IsMinimized", "IsOpening", "IsClosing", "IsPointerManipulating");
        if (window != null) lines.Add(prefix + ".active=" + window.gameObject.activeInHierarchy);
    }
    static void AddFields(List<string> lines, string prefix, object owner, params string[] names)
    {
        if (owner == null) { lines.Add(prefix + "=null"); return; }
        foreach (string name in names)
        {
            var flags = Private | BindingFlags.Public;
            var field = owner.GetType().GetField(name, flags);
            object value = field != null ? field.GetValue(owner) : owner.GetType().GetProperty(name, flags)?.GetValue(owner);
            lines.Add(prefix + "." + name + "=" + (value ?? "null"));
        }
    }
    static string Fingerprint(string path)
    {
        if (!File.Exists(path)) return "missing";
        using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(path)));
    }
}
