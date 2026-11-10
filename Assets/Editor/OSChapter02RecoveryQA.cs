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
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Real checkpoint and director integration, with disposable save/configuration
// copies. Production save files are fingerprinted and never written by this run.
public static class OSChapter02RecoveryQA
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    const string RecoverySequence = "chapter02.recovery";
    const string BugsEntry = "chapter02_recovery_bugs";
    const string DoneEntry = "chapter02_recovery_done";
    static readonly List<string> checks = new List<string>();
    static readonly HashSet<string> recordedChecks = new HashSet<string>(StringComparer.Ordinal);
    static readonly List<Binding> bindings = new List<Binding>();
    static GameSetup setup;
    static bool originalDemo;
    static string sceneHookFailure;
    sealed class Binding
    {
        public OSChapter01EndingDirector director;
        public OSChapter01EndingData original, copy;
    }
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
        setup = typeof(GameSetup).GetField("activeSetup", BindingFlags.Static | BindingFlags.NonPublic)
            ?.GetValue(null) as GameSetup;
        if (setup == null) return "Active GameSetup required";
        originalDemo = Field<bool>(setup, "isDemo");
        checks.Clear();recordedChecks.Clear();bindings.Clear();sceneHookFailure = null;
        OutputDirectory = Path.GetFullPath(Path.Combine("Temp", "Chapter02RecoveryQA", Guid.NewGuid().ToString("N")));
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
        var originalLanguage = data.CurrentLanguageState;
        bool background = Application.runInBackground;
        var resources = GameResourceSystem.Instance;
        bool resourceEnabled = resources != null && resources.enabled;
        string[] paths = { saves.SavePath, saves.SavePath + ".bak", saves.SavePath + ".tmp" };
        string[] hashes = null;
        var stack = new Stack<IEnumerator>();
        try
        {
            saves.EnterTestSession();
            hashes = paths.Select(Fingerprint).ToArray();
            data.ReplaceSaveData(JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(original)));
            data.SetCurrentLanguage(LanguageState.zh_CN);
            Application.runInBackground = true;
            if (resources != null) resources.enabled = false;
            SceneManager.sceneLoaded += OnSceneLoaded;
            stack.Push(Run());
            while (stack.Count > 0)
            {
                object next;
                try
                {
                    if (sceneHookFailure != null) throw new InvalidOperationException(sceneHookFailure);
                    if (!stack.Peek().MoveNext()) { (stack.Pop() as IDisposable)?.Dispose();continue; }
                    next = stack.Peek().Current;
                }
                catch (Exception error)
                {
                    Status = "FAILED: " + error;
                    Debug.LogError("[Chapter 2 Recovery QA] " + Status);
                    yield break;
                }
                if (next is IEnumerator nested) stack.Push(nested);else yield return next;
            }
            Check(paths.Select(Fingerprint).SequenceEqual(hashes), "production save files unchanged");
            Check(saves.IsTestSession && !saves.IsWriting, "autosave isolation remains active");
            Status = "PASS: " + checks.Count + " chapter recovery checks";
            Debug.Log("[Chapter 2 Recovery QA] " + Status + " | " + OutputDirectory);
        }
        finally
        {
            while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
            SceneManager.sceneLoaded -= OnSceneLoaded;
            saves.EnterTestSession();saves.EndSession();
            foreach (var binding in bindings)
            {
                if (binding.director != null)
                {
                    InvokeCancel(binding.director);
                    SetField(binding.director, "configuration", binding.original);
                }
                if (binding.copy != null) UnityEngine.Object.Destroy(binding.copy);
            }
            bindings.Clear();
            GameTest.Instance?.GetComponent<GameTestConsole>()?.SetOpen(false);
            // A test may end at a cinematic checkpoint. Leave presentation in the
            // menu before reconnecting the original save, without starting writes.
            GameManager.Instance?.OpenMainMenuWithoutSave();
            if (setup != null) SetField(setup, "isDemo", originalDemo);
            data.ReplaceSaveData(original);data.SetCurrentLanguage(originalLanguage);
            if (resources != null) resources.enabled = resourceEnabled;
            Application.runInBackground = background;
            if (hashes != null && !paths.Select(Fingerprint).SequenceEqual(hashes))
                Status = "FAILED: production save changed\n" + Status;
            if (Status == "running") Status = "FAILED: QA interrupted";
            File.WriteAllText(Path.Combine(OutputDirectory, "results.txt"), Status + "\n" + string.Join("\n", checks));
        }
    }

    static IEnumerator Run()
    {
        var data = GameDataManager.Instance;
        SetField(setup, "isDemo", true);
        yield return JumpEnding("demo checkpoint");
        var demo = OS_Handle.Instance.Chapter01Ending;
        yield return Wait(() => demo.Phase == OSChapter01EndingPhase.Completed, "demo reaches its existing end card", 12f);
        Check(Field<GameObject>(demo, "endCard").activeInHierarchy, "demo end card stays visible");
        Check(demo.GetComponent<OSChapter02RecoveryPresentation>()?.Stage != "repair_window"
            && GameObject.Find("Chapter02RecoveryRuntime") == null, "demo does not start recovery visuals");
        Check(data.CurrentChapterIndex == 1 && CursorHandle.Instance?.IsScriptControlled != true,
            "demo stays at Chapter 1 with usable pointer");
        yield return Shot("01-demo-unchanged");

        SetField(setup, "isDemo", false);
        yield return JumpEnding("full-game checkpoint");
        var os = OS_Handle.Instance;
        var ending = os.Chapter01Ending;
        int memoryBeforeRecovery = data.CurrentOSMemoryDataIndex;
        data.SetDesktopProcessIsNew(OSDesktopProcessKeys.StateCheck, false);
        var recovery = ending.GetComponent<OSChapter02RecoveryPresentation>();
        Check(recovery != null && recovery.IsConfigured, "authored two-frame bugs and swatter are present");
        Check(ending.Configuration.recoveryFreezeSeconds == 6f
            && ending.Configuration.recoverySwatSeconds >= 5f && ending.Configuration.recoverySwatSeconds <= 7f,
            "full game uses six-second freeze and five-to-seven-second hunt");
        yield return Wait(() => ending.Phase == OSChapter01EndingPhase.Glitch
            && Field<RawImage>(ending, "frozenFrame").gameObject.activeInHierarchy,
            "last animation frame is frozen", 12f);
        float freezeBegan = Time.unscaledTime;
        var frozen = Field<RawImage>(ending, "frozenFrame");
        var texture = frozen.texture;
        var material = Field<Material>(ending, "runtimeFreeze");
        Check(texture != null && material != null, "freeze has captured image and glitch material");
        float firstPhase = material.GetFloat("_Phase");
        yield return new WaitForSecondsRealtime(.32f);
        Check(ReferenceEquals(texture, frozen.texture) && material.GetFloat("_Phase") > firstPhase + .1f,
            "held image stays fixed while glitch shader keeps advancing");
        yield return Shot("02-live-glitch-on-held-frame");
        yield return Wait(() => recovery.Stage == "repair_window", "repair window begins", 9f);
        float frozenSeconds = Time.unscaledTime - freezeBegan;
        Check(frozenSeconds >= 5.8f && frozenSeconds <= 7.2f,
            "repair waits six seconds after last frame (" + frozenSeconds.ToString("F2") + "s)");
        Check(CursorHandle.Instance.IsScriptControlledBy(ending) && os.IsSystemInputLocked,
            "repair window owns cursor and OS input lock");
        Color tint = CursorHandle.Instance.ScriptTint;
        Check(tint.r > tint.g * 2f && tint.r > tint.b * 2f && CursorHandle.Instance.ScriptScaleMultiplier > 1f,
            "controlled pointer is red and enlarged");
        yield return Shot("03a-repair-window-emerging");
        yield return new WaitForSecondsRealtime(.46f);
        var runtimeRoot = Field<RectTransform>(recovery, "root");
        var panel = Field<RectTransform>(recovery, "panel");
        Check(runtimeRoot != null && panel != null && panel.localScale.x > .99f && panel.localScale.y > .99f,
            "repair panel expands to final size");
        var repairButton = panel.GetComponentInChildren<Button>();
        Check(repairButton != null && !repairButton.interactable,
            "physical clicks cannot skip the controlled repair click");
        ValidateRepairLanguages(panel);
        yield return Shot("03-repair-window-controlled-pointer");

        yield return Wait(() => recovery.Stage == "screen_falling", "screen breaks into falling blocks", 6f);
        var blocks = Field<RectTransform>(recovery, "blockLayer").GetComponentsInChildren<RawImage>();
        Check(blocks.Length >= 64 && blocks.All(b => ReferenceEquals(b.texture, texture)),
            "falling blocks preserve screenshot regions");
        Check(!frozen.gameObject.activeInHierarchy
            && Field<GameObject>(ending, "overlay").GetComponent<Image>().color.a < .01f,
            "falling screen reveals OS under transparent input blocker");
        Check(os.CharacterWindowController.CurrentExtendedAnimation == OSCharacterExtendedAnimation.Expend01,
            "character works while desktop is repaired");
        var bugImages = BugImages(recovery);
        Check(bugImages.Length == 4, "four bugs fly over the desktop");
        var flyFrames = Field<Sprite[]>(recovery, "flyFrames");
        Check(flyFrames.Length == 2 && flyFrames[0] != null && flyFrames[1] != null && flyFrames[0] != flyFrames[1],
            "flight uses two distinct authored frames");
        var observedFrames = new HashSet<Sprite>();
        Vector2 blockBefore = blocks[0].rectTransform.anchoredPosition;
        float flightDeadline = Time.unscaledTime + .36f;
        while (Time.unscaledTime < flightDeadline)
        { foreach (var bug in bugImages) observedFrames.Add(bug.sprite);yield return null; }
        Check(blocks[0].rectTransform.anchoredPosition.y < blockBefore.y, "screen blocks move downward");
        Check(observedFrames.Contains(flyFrames[0]) && observedFrames.Contains(flyFrames[1]),
            "bugs actually animate through both frames");
        yield return Shot("04-screen-blocks-reveal-os");

        yield return Wait(() => recovery.Stage == "swatting", "swatter hunt starts", 4f);
        float huntBegan = Time.unscaledTime;
        var link = os.LinkMessageController;
        yield return Wait(() => link.DisplayedSequenceId == RecoverySequence && link.DisplayedEntryId == BugsEntry,
            "character speaks during swatting", 1.5f);
        var bugsEntry = RecoveryEntry(BugsEntry);
        Check(bugsEntry.PartType == OSCharacterInteractionPartType.Text && bugsEntry.WaitForExternalCompletion
            && link.IsWaitingForExternalCompletion, "bug reaction is text held until the hunt completes");
        ValidateMessageLanguages(link, bugsEntry);
        var flattened = Field<Sprite>(recovery, "squashedSprite");
        var squashed = new HashSet<int>();
        var fallingX = new Dictionary<int, float>();
        var fallingY = new Dictionary<int, float>();
        bool verticalFall = false, outsideSeen = false, squashShot = false, swatterSeen = false;
        yield return Shot("05-bugs-before-swatting");
        while (recovery.Stage == "swatting" && Time.unscaledTime - huntBegan < 12f)
        {
            Vector2 pointer = CursorHandle.Instance.DisplayedScreenPosition;
            outsideSeen |= pointer.x < 0f || pointer.x > Screen.width || pointer.y < 0f || pointer.y > Screen.height;
            var swatter = Field<Image>(recovery, "swatter");
            swatterSeen |= swatter != null;
            if (swatter != null)
                Check(Vector2.Distance(swatter.rectTransform.pivot, new Vector2(.5f, .375f)) < .001f,
                    "cursor holds the swatter handle at its middle");
            Check(CursorHandle.Instance.IsScriptControlledBy(ending) && os.IsSystemInputLocked,
                "hunt retains scripted cursor and input lock");
            foreach (var bug in bugImages)
            {
                if (bug == null || bug.sprite != flattened) continue;
                int id = bug.GetInstanceID();Vector2 position = bug.rectTransform.anchoredPosition;
                squashed.Add(id);
                if (fallingX.TryGetValue(id, out float x))
                {
                    Check(Mathf.Abs(position.x - x) < .1f, "squashed bugs fall vertically without sideways drift");
                    verticalFall |= position.y < fallingY[id] - 1f;
                }
                fallingX[id] = position.x;fallingY[id] = position.y;
            }
            if (!squashShot && squashed.Count > 0)
            {
                squashShot = true;
                yield return new WaitForSecondsRealtime(.15f);
                yield return Shot("06-first-flattened-bug");
            }
            yield return null;
        }
        float huntSeconds = Time.unscaledTime - huntBegan;
        Check(huntSeconds >= 5f && huntSeconds <= 7.5f,
            "hunt fits five-to-seven-second pacing (" + huntSeconds.ToString("F2") + "s)");
        Check(outsideSeen && swatterSeen, "pointer exits screen and brings back swatter");
        Check(squashed.Count == 4 && verticalFall, "all four bugs flatten and fall");
        yield return Wait(() => ending.Phase == OSChapter01EndingPhase.RecoveryAwaitingConfirmation && !ending.IsPresenting,
            "repair waits for the player's confirmation", 3f);
        yield return null;yield return null;
        Check(data.CurrentChapterIndex == 1 && data.OSProcessData.chapter01Ending.completed,
            "finished swatting records completion without advancing the chapter");
        Check(recovery.Completed && !os.IsSystemInputLocked && !CursorHandle.Instance.IsScriptControlled,
            "completion returns normal OS and mouse control");
        Check(CursorHandle.Instance.ScriptScaleMultiplier == 1f && CursorHandle.Instance.ScriptTint == Color.white,
            "pointer restores original appearance");
        Check(os.CharacterWindowController.CurrentExtendedAnimation == OSCharacterExtendedAnimation.None
            && os.CharacterWindowController.CharacterAnimator.GetCurrentAnimatorStateInfo(0)
                .IsName("Base Layer.Player_UI_State_Normal"), "character returns to current healthy idle");
        Check(GameObject.Find("Chapter02RecoveryRuntime") == null && Field<Material>(ending, "runtimeFreeze") == null
            && !Field<GameObject>(ending, "overlay").activeInHierarchy,
            "completion leaves no recovery canvases or freeze material");
        yield return Wait(() => link.DisplayedSequenceId == RecoverySequence && link.DisplayedEntryId == DoneEntry
            && link.IsWaitingForOption, "post-repair confirmation is available", 10f);
        var doneEntry = RecoveryEntry(DoneEntry);
        Check(doneEntry.PartType == OSCharacterInteractionPartType.TextWithOptions
            && doneEntry.Options.Count == 1 && doneEntry.Options[0].OptionId == "confirm"
            && doneEntry.OptionTimeoutDelay <= 0f, "completion requires a single untimed confirm option");
        ValidateMessageLanguages(link, doneEntry);
        yield return Shot("07-recovery-awaiting-confirmation");
        yield return new WaitForSecondsRealtime(.6f);
        Check(data.CurrentChapterIndex == 1 && ending.Phase == OSChapter01EndingPhase.RecoveryAwaitingConfirmation
            && data.CurrentOSMemoryDataIndex == memoryBeforeRecovery,
            "leaving confirmation untouched does not advance chapter or memory");
        Check(!data.TryGetDesktopProcessIsNew(OSDesktopProcessKeys.StateCheck, out bool pendingNew) || !pendingNew,
            "StateCheck is not marked NEW before confirmation");

        string pendingJson = JsonUtility.ToJson(data.SaveData, true);
        var pendingRestore = JsonUtility.FromJson<GameSaveData>(pendingJson);
        pendingRestore.EnsureData();pendingRestore.EnsureData();
        Check(pendingRestore.osProcess.chapter01Ending.phase == OSChapter01EndingPhase.RecoveryAwaitingConfirmation
            && pendingRestore.osProcess.chapter01Ending.completed && pendingRestore.osProcess.currentChapterIndex == 1,
            "JSON normalization preserves finished recovery awaiting confirmation");
        File.WriteAllText(Path.Combine(OutputDirectory, "recovery-awaiting-confirmation.json"), pendingJson);
        data.ReplaceSaveData(pendingRestore);
        yield return null;
        yield return Wait(() => ending.Phase == OSChapter01EndingPhase.RecoveryAwaitingConfirmation
            && link.DisplayedSequenceId == RecoverySequence && link.DisplayedEntryId == DoneEntry
            && link.IsWaitingForOption, "loading pending confirmation restores its option", 10f);
        yield return new WaitForSecondsRealtime(.6f);
        Check(!ending.IsPresenting && !CursorHandle.Instance.IsScriptControlled && data.CurrentChapterIndex == 1
            && GameObject.Find("Chapter02RecoveryRuntime") == null,
            "loading pending confirmation does not replay the cursor or bug sequence");
        yield return Shot("08-restored-confirmation");
        yield return ConfirmRecoveryOption(os, "real post-repair confirmation");
        yield return Wait(() => ending.Phase == OSChapter01EndingPhase.Chapter02Started
            && data.CurrentChapterIndex == 2, "only confirmation enters Chapter 2", 3f);
        Check(data.TryGetDesktopProcessIsNew(OSDesktopProcessKeys.StateCheck, out bool isNew) && isNew,
            "confirmation persists StateCheck NEW");
        var stateCheckIcon = os.DesktopProcessUnits.FirstOrDefault(unit => unit != null && unit.ProcessKey == OSDesktopProcessKeys.StateCheck);
        Check(stateCheckIcon != null && stateCheckIcon.HasNewContent, "StateCheck NEW is visible on its desktop icon");
        Check(data.CurrentOSMemoryDataIndex == memoryBeforeRecovery, "confirmation never opens a Chapter 2 memory automatically");
        ValidateChapter02AwaitingUnlock(data);
        yield return Shot("09-chapter02-unlocked-os");

        string completeJson = JsonUtility.ToJson(data.SaveData, true);
        var restored = JsonUtility.FromJson<GameSaveData>(completeJson);
        restored.EnsureData();restored.EnsureData();
        Check(restored.osProcess.chapter01Ending.phase == OSChapter01EndingPhase.Chapter02Started
            && restored.osProcess.chapter01Ending.completed && restored.osProcess.currentChapterIndex == 2,
            "JSON and repeated normalization preserve Chapter02Started");
        File.WriteAllText(Path.Combine(OutputDirectory, "chapter02-complete.json"), completeJson);
        data.ReplaceSaveData(restored);
        yield return new WaitForSecondsRealtime(.25f);
        Check(!ending.IsPresenting && GameObject.Find("Chapter02RecoveryRuntime") == null,
            "loading completed snapshot does not replay recovery");

        // Interrupt the glitch with an equivalent loaded save, then cancel while
        // the controlled window is present. No screenshot/cursor state is saved.
        data.SetCurrentChapterIndex(1);
        Check(ending.DebugBeginAnimation(), "restart animation for interrupted-save check");
        yield return Wait(() => ending.Phase == OSChapter01EndingPhase.Glitch
            && Field<Material>(ending, "runtimeFreeze") != null, "interruption reaches live glitch", 6f);
        data.OSProcessData.chapter01Ending = JsonUtility.FromJson<OSChapter01EndingSaveData>(
            JsonUtility.ToJson(data.OSProcessData.chapter01Ending));
        yield return Wait(() => ending.Phase == OSChapter01EndingPhase.Animation && ending.IsPresenting,
            "loaded mid-glitch checkpoint safely restarts final animation", 2f);
        yield return Wait(() => recovery.Stage == "repair_window", "restarted save reaches repair window", 10f);
        InvokeCancel(ending);
        data.OSProcessData.chapter01Ending = new OSChapter01EndingSaveData();
        Check(!ending.IsPresenting && !CursorHandle.Instance.IsScriptControlled
            && Field<RectTransform>(recovery, "root") == null,
            "cancel releases scripted pointer and all temporary visuals");
        yield return null;
        Check(GameObject.Find("Chapter02RecoveryRuntime") == null, "cancel destroys runtime canvas");

        data.OSProcessData.chapter01Ending = new OSChapter01EndingSaveData
        { phase = OSChapter01EndingPhase.Completed, slideIndex = 5, completed = true };
        yield return Wait(() => ending.Phase == OSChapter01EndingPhase.Slides
            && data.OSProcessData.chapter01Ending.slideIndex == 0
            && !data.OSProcessData.chapter01Ending.completed,
            "legacy end-card save restarts the finished sequence at the first slide", 2f);
        var resumedEnding = JsonUtility.FromJson<OSChapter01EndingSaveData>(
            JsonUtility.ToJson(data.OSProcessData.chapter01Ending));
        resumedEnding.slideIndex = 1;
        resumedEnding.EnsureData();resumedEnding.EnsureData();
        data.OSProcessData.chapter01Ending = resumedEnding;
        yield return Wait(() => ending.Phase == OSChapter01EndingPhase.Slides
            && data.OSProcessData.chapter01Ending.slideIndex == 1
            && !data.OSProcessData.chapter01Ending.completed,
            "migrated save resumes its later slide without repeating the legacy restart", 2f);
        InvokeCancel(ending);
        data.OSProcessData.chapter01Ending = new OSChapter01EndingSaveData();
    }

    static IEnumerator JumpEnding(string label)
    {
        GameTest.Instance.GetComponent<GameTestConsole>()?.SetOpen(false);
        Check(GameTest.Instance.JumpToCheckpoint(GameTestCheckpoint.Chapter01EndingAnimation), label + " accepted");
        yield return Wait(() => !GameTest.Instance.IsCheckpointJumpRunning, label + " loaded", 65f);
        Check(GameTest.Instance.JumpSucceeded && OS_Handle.Instance?.Chapter01Ending != null
            && OS_Handle.Instance.Chapter01Ending.IsPresenting, label + " starts real ending director");
        Check(OS_Handle.Instance.Chapter01Ending.DebugBeginAnimation(),
            label + " skips slides and the door for recovery-specific checks");
        Check(GameAutosaveService.Instance.IsTestSession, label + " retains write isolation");
    }
    static Image[] BugImages(OSChapter02RecoveryPresentation recovery) =>
        Field<RectTransform>(recovery, "bugLayer").GetComponentsInChildren<Image>()
            .Where(image => image.name.StartsWith("Bug_", StringComparison.Ordinal)).ToArray();
    static OSCharacterInteractionEntryData RecoveryEntry(string entryId)
    {
        var data = GameDataManager.Instance;
        Check(data.OSCharacterInteractionData.TryGetSequence(RecoverySequence, out var sequence),
            "chapter recovery interaction sequence exists");
        Check(sequence.TryGetEntry(entryId, out var entry), "recovery entry exists: " + entryId);
        return entry;
    }
    static void ValidateMessageLanguages(OSLinkMessageController link, OSCharacterInteractionEntryData entry)
    {
        var data = GameDataManager.Instance;
        LanguageState originalLanguage = data.CurrentLanguageState;
        bool hasOptions = entry.PartType == OSCharacterInteractionPartType.TextWithOptions;
        try
        {
            foreach (LanguageState language in Enum.GetValues(typeof(LanguageState)))
            {
                data.SetCurrentLanguage(language);
                link.RefreshCurrentMessageLocalization();
                CompleteMessageText(link);
                Canvas.ForceUpdateCanvases();
                var label = Field<TMP_Text>(link, hasOptions ? "textWithOptionsLabel" : "textLabel");
                Check(data.TryGetLocalizedText(entry.LocalizationKey, out string expected)
                    && !string.IsNullOrWhiteSpace(expected) && label.text == expected,
                    "recovery message translated: " + entry.EntryId + " / " + language);
                ValidateMessageLabel(label, entry.EntryId + " / " + language);
                if (hasOptions)
                {
                    Check(link.TryGetActiveOptionTarget("confirm", out var target), "confirm option resolves: " + language);
                    var option = target.GetComponentInParent<OSLinkMessageSelectUnit>();
                    Check(data.TryGetLocalizedText(entry.Options[0].LocalizationKey, out string choice)
                        && !string.IsNullOrWhiteSpace(choice) && option.SelectText.text.Contains(choice),
                        "confirm option translated: " + language);
                    ValidateMessageLabel(option.SelectText, "confirm / " + language);
                }
            }
        }
        finally
        {
            data.SetCurrentLanguage(originalLanguage);
            link.RefreshCurrentMessageLocalization();
            CompleteMessageText(link);
            Canvas.ForceUpdateCanvases();
        }
        Check(hasOptions ? link.IsWaitingForOption : link.IsWaitingForExternalCompletion,
            "language switching preserves required wait: " + entry.EntryId);
    }
    static void CompleteMessageText(OSLinkMessageController link)
    {
        // Finish presentation-only tweens. This never selects an option or
        // completes the external wait guarding the swatting reaction.
        Field<Tween>(link, "reopenTween")?.Complete(true);
        Field<Tween>(link, "textTween")?.Complete(true);
    }
    static void ValidateMessageLabel(TMP_Text label, string labelId)
    {
        uint[] missing;
        bool covered = label != null && label.font != null && label.font.HasCharacters(label.text, out missing, true, true);
        Check(covered, "message font covers glyphs: " + labelId);
        label.ForceMeshUpdate();
        Rect rect = label.rectTransform.rect;Bounds bounds = label.textBounds;
        const float tolerance = 1f;
        Check(!label.isTextOverflowing && bounds.min.x >= rect.xMin - tolerance
            && bounds.max.x <= rect.xMax + tolerance && bounds.min.y >= rect.yMin - tolerance
            && bounds.max.y <= rect.yMax + tolerance, "message text fits: " + labelId);
    }
    public static IEnumerator ConfirmRecoveryOption(OS_Handle os, string label)
    {
        var link = os.LinkMessageController;
        yield return Wait(() => link.DisplayedSequenceId == RecoverySequence && link.DisplayedEntryId == DoneEntry
            && link.IsWaitingForOption && link.TryGetActiveOptionTarget("confirm", out _), label + " ready", 12f);
        Check(link.TryGetActiveOptionTarget("confirm", out var target), label + " target found");
        var unit = target.GetComponentInParent<OSLinkMessageSelectUnit>();
        Check(unit != null && unit.IsInteractable && unit.gameObject.activeInHierarchy
            && CursorHandle.Instance?.IsScriptControlled != true, label + " accepts physical input");
        Check(EventSystem.current != null, label + " has EventSystem");
        Canvas.ForceUpdateCanvases();
        var canvas = target.GetComponentInParent<Canvas>();
        var pointer = new PointerEventData(EventSystem.current)
        {
            button = PointerEventData.InputButton.Left,
            position = RectTransformUtility.WorldToScreenPoint(
                canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera,
                target.TransformPoint(target.rect.center))
        };
        var hits = new List<RaycastResult>();EventSystem.current.RaycastAll(pointer, hits);
        Check(hits.Count > 0 && hits[0].gameObject.GetComponentInParent<OSLinkMessageSelectUnit>() == unit,
            label + " real raycast reaches confirm option");
        pointer.pointerCurrentRaycast = hits[0];
        ExecuteEvents.Execute(unit.gameObject, pointer, ExecuteEvents.pointerClickHandler);
        yield return null;
    }
    static void ValidateChapter02AwaitingUnlock(GameDataManager data)
    {
        data.TryGetOSMemoryChapterProgressRange(2, out var range);
        var resolver = typeof(OSStateCheckWindowController).GetMethod("ResolveChapterRecordState",
            BindingFlags.Static | BindingFlags.NonPublic);
        Check(resolver != null, "StateCheck chapter state resolver exists");
        object state = resolver.Invoke(null, new object[] { 2, data.CurrentChapterIndex, data.CurrentOSMemoryDataIndex, range });
        Check(state != null && state.ToString() == "AwaitingUnlock", "StateCheck Chapter 2 awaits manual unlock");
    }
    static void ValidateRepairLanguages(RectTransform panel)
    {
        var data = GameDataManager.Instance;
        LanguageState originalLanguage = data.CurrentLanguageState;
        var languages = (LanguageState[])Enum.GetValues(typeof(LanguageState));
        var labels = panel.GetComponentsInChildren<TMP_Text>();
        Check(languages.Length == 10 && labels.Length == 2, "repair exposes two labels in ten supported languages");
        try
        {
            // LanguageChanged refreshes these runtime labels synchronously. No
            // extra frame or altered time scale is needed for the layout audit.
            foreach (LanguageState language in languages)
            {
                data.SetCurrentLanguage(language);
                Canvas.ForceUpdateCanvases();
                foreach (var label in labels)
                {
                    string key = "ui.os.chapter01_ending."
                        + (label.name == "Title" ? "display_repair" : "repair_pixels");
                    Check(label.GetComponent<OSLocalizedText>() != null
                        && data.TryGetLocalizedText(key, out string expected)
                        && !string.IsNullOrWhiteSpace(expected) && label.text == expected,
                        "repair switches text immediately: " + language + " / " + label.name);
                    uint[] missing;
                    bool covered = label.font != null && label.font.HasCharacters(label.text, out missing, true, true);
                    Check(covered, "repair font covers glyphs: " + language + " / " + label.name);
                    label.ForceMeshUpdate();
                    Rect rect = label.rectTransform.rect;Bounds bounds = label.textBounds;
                    const float tolerance = .75f;
                    Check(!label.isTextOverflowing && bounds.min.x >= rect.xMin - tolerance
                        && bounds.max.x <= rect.xMax + tolerance && bounds.min.y >= rect.yMin - tolerance
                        && bounds.max.y <= rect.yMax + tolerance,
                        "repair text fits: " + language + " / " + label.name);
                }
            }
        }
        finally
        {
            data.SetCurrentLanguage(originalLanguage);
            Canvas.ForceUpdateCanvases();
            foreach (var label in labels) label.ForceMeshUpdate();
        }
    }
    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        try
        {
            foreach (var root in scene.GetRootGameObjects())
            foreach (var ending in root.GetComponentsInChildren<OSChapter01EndingDirector>(true))
            {
                if (bindings.Any(binding => binding.director == ending)) continue;
                Check(ending.Configuration != null, "authored ending configuration exists");
                var copy = UnityEngine.Object.Instantiate(ending.Configuration);
                copy.hideFlags = HideFlags.DontSave;copy.name += " (RecoveryQA only)";
                copy.animationVideo = null;copy.animationFrames = Array.Empty<Sprite>();
                copy.initialBlackSeconds = .05f;copy.animationPlaceholderSeconds = .8f;
                copy.frozenSeconds = .1f;copy.glitchSeconds = .15f;copy.fadeOutSeconds = .1f;
                copy.recoveryFreezeSeconds = 6f;copy.recoverySwatSeconds = 6f;copy.chapter02MemoryIndex = -1;
                bindings.Add(new Binding { director = ending, original = ending.Configuration, copy = copy });
                SetField(ending, "configuration", copy);
            }
        }
        catch (Exception error) { sceneHookFailure = "Cannot bind isolated configuration: " + error; }
    }
    static void InvokeCancel(OSChapter01EndingDirector ending) =>
        typeof(OSChapter01EndingDirector).GetMethod("CancelPresentation", Private).Invoke(ending, null);
    static IEnumerator Wait(Func<bool> condition, string label, float seconds)
    {
        float deadline = Time.unscaledTime + seconds;
        while (!condition() && Time.unscaledTime < deadline) yield return null;
        Check(condition(), label);
    }
    static IEnumerator Shot(string name)
    {
        yield return new WaitForEndOfFrame();
        var image = ScreenCapture.CaptureScreenshotAsTexture();
        try { File.WriteAllBytes(Path.Combine(OutputDirectory, name + ".png"), image.EncodeToPNG()); }
        finally { UnityEngine.Object.Destroy(image); }
        yield return null;
    }
    static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Private).GetValue(owner);
    static void SetField(object owner, string name, object value) => owner.GetType().GetField(name, Private).SetValue(owner, value);
    static void Check(bool ok, string label)
    {
        if (!ok) throw new InvalidOperationException(label);
        if (recordedChecks.Add(label)) checks.Add(label);
    }
    static string Fingerprint(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "absent";
        using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(path)));
    }
}
