using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using MrTGame.Core;
using MrTGame.Systems;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// Opt-in integration check of the production Live/chat window. It uses real
// pointer handlers and isolated storage; no generated dialogue or sprite copies.
public static class OSChapter04ChatLayoutQA
{
    const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly List<string> checks = new List<string>(), errors = new List<string>(), screenshots = new List<string>();
    static readonly List<object> observations = new List<object>();
    static readonly Dictionary<MonoBehaviour, bool> focusStates = new Dictionary<MonoBehaviour, bool>();
    static readonly Dictionary<OSChapter04StoryContent.Beat, float> delays = new Dictionary<OSChapter04StoryContent.Beat, float>();
    static GameDataManager data;
    static GameSetup setup;
    static OS_Handle os;
    static OSChapter04StoryDirector story;
    static Mouse mouse;
    static Keyboard keyboard;
    static bool cancelled, hasTypingSpeed;
    static float typingSpeed;
    static string context;
    static OSChapter04StoryState State => story.State;
    static OSLinkMessageController Link => os.LinkMessageController;
    static OSChapter04ChatView View => story != null ? story.GetComponent<OSChapter04ChatView>() : null;
    static OSWindowUnit Host => Get(View, "HostWindow") as OSWindowUnit;
    static RectTransform Root => Get(View, "Root") as RectTransform;
    static RectTransform Portrait => Get(View, "PortraitRect") as RectTransform;
    static TMP_Text ConversationDate => Get(View, "ConversationDateText") as TMP_Text;
    public static string Status { get; private set; } = "idle";
    public static string OutputDirectory { get; private set; }
    public static void Cancel() => cancelled = true;

    public static string Start()
    {
        var saves = GameAutosaveService.Instance;
        if (Status == "running") return Status;
        if (!Application.isPlaying || saves == null || GameDataManager.Instance == null || GameTest.Instance == null
            || GameManager.Instance == null || SceneLoadManager.Instance == null || GameSessionFlow.Instance == null)
            return "Play Mode initialization required";
        if (saves.IsBusy || saves.IsWriting || GameManager.Instance.IsTransitioning || SceneLoadManager.Instance.IsBusy
            || GameSessionFlow.Instance.IsBusy || GameTest.Instance.IsCheckpointJumpRunning)
            return "Wait for active save/scene operation";
        if (saves.IsTestSession) return "Fresh Play Mode required to protect the original Editor save path";
        setup = typeof(GameSetup).GetField("activeSetup", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as GameSetup;
        if (setup == null) return "Active GameSetup is required";
        checks.Clear(); errors.Clear(); screenshots.Clear(); observations.Clear(); focusStates.Clear(); delays.Clear();
        cancelled = hasTypingSpeed = false; context = "initialization"; data = GameDataManager.Instance;
        OutputDirectory = Path.GetFullPath(Path.Combine("Temp", "OSChapter04ChatLayoutQA", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(OutputDirectory); Status = "running";
        data.StartCoroutine(Guarded()); return Status;
    }

    static IEnumerator Guarded()
    {
        var saves = GameAutosaveService.Instance;
        var original = data.SaveData;
        var language = data.CurrentLanguageState;
        var resources = GameResourceSystem.Instance;
        bool resourcesEnabled = resources != null && resources.enabled;
        bool background = Application.runInBackground, demo = (bool)Get(setup, "isDemo");
        float volume = AudioListener.volume;
        var random = UnityEngine.Random.state;
        var previousMouse = Mouse.current; var previousKeyboard = Keyboard.current;
        string[] files = new[] { saves.SavePath, saves.SavePath + ".bak", saves.SavePath + ".tmp",
            "Assets/Data/RecordSO.asset", "Assets/Art/UI/Record/ResearcherPaperDoll.png", "Assets/Art/UI/Record/RobotPaperDoll.png",
            "Assets/Resources/Chapter04/story.json", "Assets/Data/OSCharacterInteractionData.asset", "Assets/Data/LocalizationData.asset" }
            .Concat(Directory.GetFiles("Assets/Scenes", "*.unity", SearchOption.AllDirectories)).ToArray();
        string[] hashes = null;
        var work = new Stack<IEnumerator>(); Exception failure = null;
        Application.logMessageReceived += CaptureError;
        try
        {
            saves.EnterTestSession(); hashes = files.Select(Hash).ToArray();
            saves.UseIsolatedStorageForTests(OutputDirectory); saves.EnterTestSession();
            data.ReplaceSaveData(JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(original)));
            data.SetCurrentLanguage(LanguageState.zh_CN); Set(setup, "isDemo", false);
            if (resources != null) resources.enabled = false;
            Application.runInBackground = true; AudioListener.volume = 0f;
            mouse = InputSystem.AddDevice<Mouse>("Chapter04ChatLayoutQAMouse");
            keyboard = InputSystem.AddDevice<Keyboard>("Chapter04ChatLayoutQAKeyboard"); ReleaseInput();
            work.Push(Run());
            while (work.Count > 0)
            {
                object next = null;
                try
                {
                    Focus();
                    if (cancelled) throw new OperationCanceledException("Chapter04 chat layout QA cancelled");
                    if (!work.Peek().MoveNext()) { (work.Pop() as IDisposable)?.Dispose(); continue; }
                    next = work.Peek().Current;
                }
                catch (Exception error) { failure = error; }
                if (failure != null)
                {
                    Status = "FAILED at " + context + "\n" + Describe() + "\n" + failure;
                    ScreenCapture.CaptureScreenshot(Path.Combine(OutputDirectory, "failure.png"));
                    Debug.LogError("[Chapter04 Chat Layout QA] " + Status); break;
                }
                if (next is IEnumerator nested) work.Push(nested); else yield return next;
            }
            if (failure == null)
            {
                Check(files.Select(Hash).SequenceEqual(hashes), "formal saves, source dialogue, original sprites and scenes remain unchanged");
                Status = errors.Count == 0 ? "PASS" : "FAILED: runtime errors";
            }
        }
        finally
        {
            while (work.Count > 0) (work.Pop() as IDisposable)?.Dispose();
            try
            {
                foreach (var pair in delays) pair.Key.autoAdvanceDelay = pair.Value;
                if (hasTypingSpeed && os?.LinkMessageController != null) Set(Link, "charactersPerSecond", typingSpeed);
                story?.DebugSetFocus(false); story?.ResetPresentation(); os?.Chapter04Entry?.ResetPresentation();
                os?.Chapter03Story?.ResetPresentation();
                os?.MemoryWindowController?.GetComponent<OSChapter03HomeController>()?.Suspend();
                os?.LinkMessageController?.NotifyOSHidden();
                if (GameTest.Instance?.IsCheckpointJumpRunning == true) GameTest.Instance.StopAllCoroutines();
                GameTest.Instance?.GetComponent<GameTestConsole>()?.SetOpen(false);
                foreach (var pair in focusStates) if (pair.Key != null) Set(pair.Key, "hasFocus", pair.Value);
                saves.EnterTestSession(); saves.EndSession(); GameManager.Instance?.OpenMainMenuWithoutSave();
                data.ReplaceSaveData(original); data.SetCurrentLanguage(language); Set(setup, "isDemo", demo);
                if (resources != null) resources.enabled = resourcesEnabled;
                Application.runInBackground = background; AudioListener.volume = volume;
                if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
                if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
                UnityEngine.Random.state = random;
                if (!ReferenceEquals(data.SaveData, original) || data.CurrentLanguageState != language || GameSetup.IsDemo != demo
                    || Application.runInBackground != background || AudioListener.volume != volume
                    || resources != null && resources.enabled != resourcesEnabled
                    || hashes != null && !files.Select(Hash).SequenceEqual(hashes))
                    throw new InvalidOperationException("Original data / session settings were not restored");
                checks.Add("cleanup: original save object, source files and session settings restored");
            }
            catch (Exception error) { Status = "FAILED during cleanup: " + error + "\n" + Status; }
            Application.logMessageReceived -= CaptureError;
            if (Status == "PASS" && errors.Count != 0) Status = "FAILED: cleanup logged runtime errors";
            if (Status == "running") Status = "FAILED: interrupted at " + context;
            if (Status == "PASS") Status += ": " + checks.Count + " checks";
            File.WriteAllText(Path.Combine(OutputDirectory, "results.json"), JsonConvert.SerializeObject(new {
                status = Status, context, checks, observations, errors, screenshots,
                originalFiles = files, originalHashes = hashes, finalHashes = files.Select(Hash).ToArray(),
                note = "Actual checkpoint 19, authored lines 01-05, original record sprites and actual window/button pointer handlers. Only in-memory auto-reading delays and typing speed are temporarily changed."
            }, Formatting.Indented));
            File.WriteAllText(Path.Combine(OutputDirectory, "results.txt"), Status + "\n" + string.Join("\n", checks));
            Debug.Log("[Chapter04 Chat Layout QA] " + Status + " | " + OutputDirectory);
            data = null; setup = null; os = null; story = null; mouse = null; keyboard = null;
        }
    }

    static IEnumerator Run()
    {
        context = "console checkpoint 19";
        foreach (var beat in OSChapter04StoryContent.Instance.Beats)
        { delays.Add(beat, beat.autoAdvanceDelay); beat.autoAdvanceDelay = -1f; }
        Check(GameTest.Instance.JumpToCheckpoint(GameTestCheckpoint.Chapter04StoryStart), "production chapter-four checkpoint accepts the jump");
        yield return Wait(() => !GameTest.Instance.IsCheckpointJumpRunning && !GameManager.Instance.IsTransitioning
            && !SceneLoadManager.Instance.IsBusy && OS_Handle.Instance != null, "checkpoint finished", 80f);
        Bind();
        yield return Wait(() => story.IsAvailable && View?.OwnsLayout == true && Ready(Host)
            && State.currentEntryId == "chapter04_story_01", "first authored message is ready in the expanded Live window");
        Check(GameTest.Instance.JumpSucceeded && GameAutosaveService.Instance.IsTestSession, "checkpoint uses isolated storage");
        CheckLayout(false);
        var originalHostLayout = Get(View, "HostLayoutBeforeExpansion") as OSWindowLayoutSaveData ?? Host.CaptureLayout();
        Check(originalHostLayout != null && Host.HasTemporaryOpeningLayout, "Live expansion preserves an ordinary layout for chapter exit");

        context = "chat pointer input";
        yield return Complete();
        yield return CheckContinueIconHover(ContinueButton());
        var openingScroll = Root.GetComponentInChildren<UnityEngine.UI.ScrollRect>();
        openingScroll.StopMovement(); openingScroll.verticalNormalizedPosition = 1f;
        yield return null;
        Check(Contains(ScreenRect(View.Viewport), ScreenRect(ConversationDate.rectTransform), 2f),
            "the initial conversation-date message is visible at the top of the history");
        yield return Screenshot("00-initial-conversation-date-message.png");
        openingScroll.StopMovement(); openingScroll.verticalNormalizedPosition = 0f;
        Click(ContinueButton(), "first continue");
        yield return Wait(() => State.currentEntryId == "chapter04_story_02" && Link.DisplayedEntryId == State.currentEntryId
            && !State.currentTextCompleted && View.CurrentBubble != null, "second authored line is typing");
        yield return null;
        int visit = State.currentVisit;
        CheckContinueIcon(ContinueButton());
        Click(ContinueButton(), "skip current typing");
        yield return Wait(() => State.currentTextCompleted && !story.IsOptionGestureBlocked, "one click finishes typing");
        Check(State.currentVisit == visit && State.currentEntryId == "chapter04_story_02", "a typing click does not also advance the dialogue");
        Click(ContinueButton(), "continue completed line");
        yield return Wait(() => State.currentEntryId == "chapter04_story_03", "the next click advances exactly one message");
        yield return Complete(); Click(ContinueButton(), "continue to authored choice");
        yield return Wait(() => State.currentEntryId == "chapter04_story_04", "authored choice line reached");
        yield return Complete();
        yield return Wait(() => Link.IsWaitingForOption && !story.IsOptionGestureBlocked, "authored choice is interactive");
        context = "reply hover and option display";
        var choiceButton = ContinueButton();
        var choiceLabel = choiceButton.transform.Find("Label")?.GetComponent<TMP_Text>();
        string choiceEntryId = State.currentEntryId;
        string choiceOptionId = story.CurrentEntry.Options[0].OptionId;
        string authoredChoice = story.GetOptionText(choiceEntryId, choiceOptionId);
        Check(choiceLabel != null && authoredChoice.StartsWith("1.", StringComparison.Ordinal)
            && choiceLabel.text == authoredChoice.Substring(2) && !HasOptionNumber(choiceLabel.text),
            "the authored numbered option displays only its message, preserving the remaining punctuation");
        string displayedChoice = choiceLabel.text;
        yield return CheckReplyHover(choiceButton);
        visit = State.currentVisit;
        Click(choiceButton, "authored player response");
        yield return Wait(() => State.currentEntryId == "chapter04_story_05" && Root.GetComponentsInChildren<UnityEngine.UI.Image>()
            .Any(image => image.name == "PlayerAvatar"), "accepted choice creates a right-side player message");
        Check(State.history.Count(record => record != null && record.visit == visit && !string.IsNullOrEmpty(record.optionId)) == 1,
            "the player reply is backed by exactly one saved option receipt");
        Check(State.history.Single(record => record != null && record.visit == visit).optionId == choiceOptionId
            && story.GetOptionText(choiceEntryId, choiceOptionId) == authoredChoice,
            "option display cleanup leaves the authored text and saved option ID unchanged");
        Check(PlayerReplyLabels().Any(label => label.text == displayedChoice)
            && PlayerReplyLabels().All(label => !HasOptionNumber(label.text)),
            "the sent player bubble matches the unnumbered button text");
        yield return Complete();
        yield return null; yield return null;
        CheckLayout(true);
        var scroll = Root.GetComponentInChildren<UnityEngine.UI.ScrollRect>();
        Check(scroll != null && scroll.content.rect.height > scroll.viewport.rect.height, "authored history grows beyond the viewport");
        Check(scroll.verticalNormalizedPosition <= .01f, "new authored messages scroll to the bottom");
        Check(ScreenRect(View.CurrentBubble.rectTransform).yMax > ScreenRect(View.Viewport).yMin,
            "the latest message remains inside the visible scroll region");
        var theater = os.TVController?.Screen?.GetComponent<OSChapter04TheaterView>();
        Check(theater != null, "the actual TV theater view is available for the layout screenshot");
        float theaterWaitStarted = Time.realtimeSinceStartup;
        yield return Wait(() => theater.Stage.CurrentPhase == OSChapter04TheaterStage.Phase.Walk
            && !theater.IsAnimationPaused, "the real city-rise animation reaches its unpaused walking loop before capture");
        yield return null;
        observations.Add(new { context, type = "TV ready for layout capture", phase = theater.Stage.CurrentPhase.ToString(),
            paused = theater.IsAnimationPaused, requestedState = theater.RequestedStateId,
            waitedSeconds = Time.realtimeSinceStartup - theaterWaitStarted });
        yield return Screenshot("01-live-chat-native-avatars.png");
        context = "smooth wheel and scrolling date";
        yield return CheckSmoothWheel(scroll);

        context = "Escape with pinned conversation";
        yield return EscapeWithPinnedConversation();
        context = "one-window drag";
        yield return DragHost();
        context = "shared minimize and close";
        visit = State.currentVisit; int historyCount = State.history.Count;
        ClickWindow(OSWindowButton.WindowAction.Minimize);
        yield return Wait(() => Host.IsMinimized && !story.IsAvailable, "shared minimize suspends the entire Live conversation");
        yield return Hold(.25f);
        Check(State.currentVisit == visit && State.history.Count == historyCount, "minimizing does not advance or duplicate history");
        os.OpenCharacterWindowForReconnect(0f);
        yield return Wait(() => Ready(Host) && story.IsAvailable && Root.gameObject.activeInHierarchy, "opening Live restores portrait and chat together");
        ClickWindow(OSWindowButton.WindowAction.Close);
        yield return Wait(() => !Host.IsOpen && !Host.IsClosing && !story.IsAvailable, "shared close suspends the whole conversation");
        Check(!Root.gameObject.activeInHierarchy || EffectiveAlpha(Root) <= .01f, "chat cannot remain visible after the Live window closes");
        os.OpenCharacterWindowForReconnect(0f);
        yield return Wait(() => Ready(Host) && story.IsAvailable && Root.gameObject.activeInHierarchy, "Live launch reopens the same conversation");
        Check(State.currentVisit == visit && State.history.Count == historyCount, "reopening retains the current line and player reply");

        context = "save restoration";
        OSSessionPersistence.Capture(data.SaveData); GameSessionFlow.Capture(data.SaveData);
        string saved = JsonUtility.ToJson(data.SaveData, true);
        File.WriteAllText(Path.Combine(OutputDirectory, "snapshot-expanded-chat.json"), saved);
        story.ResetPresentation(); os.Chapter04Entry.ResetPresentation(); Link.NotifyOSHidden();
        var restored = JsonUtility.FromJson<GameSaveData>(saved);
        data.ReplaceSaveData(restored); yield return OSSessionPersistence.Restore(restored); Bind();
        Link.SetGlobalMessagesSuppressed(false); Link.NotifyOSBecameVisible();
        yield return Wait(() => story.IsAvailable && View?.OwnsLayout == true && Ready(Host), "real session restoration rebuilds the expanded window");
        Check(State.currentVisit == visit && State.history.Count == historyCount, "restoration does not duplicate a message or player reply");
        CheckLayout(true);
        yield return Screenshot("02-restored-live-chat.png");

        context = "chapter exit";
        originalHostLayout = Get(View, "HostLayoutBeforeExpansion") as OSWindowLayoutSaveData ?? Host.CaptureLayout();
        var host = Host; var root = Root;
        data.SetCurrentChapterIndex(5); yield return null; yield return null;
        Check(!View.OwnsLayout && !root.gameObject.activeInHierarchy, "leaving chapter four releases and hides the chat extension");
        Check(!host.HasTemporaryOpeningLayout && Vector2.Distance(host.WindowRect.rect.size, originalHostLayout.size) < .1f
            && Vector2.Distance(host.WindowRect.anchoredPosition, originalHostLayout.anchoredPosition) < .1f,
            "ordinary Live window size and position are restored after leaving the chapter");
        data.SetCurrentChapterIndex(4);
        yield return Wait(() => story.IsAvailable && View.OwnsLayout && Ready(Host), "returning to chapter four reacquires the unified window");
        Check(State.currentVisit == visit && State.history.Count == historyCount, "chapter exit and return preserve story progress");
    }

    static void CheckLayout(bool expectPlayer)
    {
        Canvas.ForceUpdateCanvases();
        var actualLive = os.CharacterWindowController.GetComponent<OSWindowUnit>();
        Check(Host == actualLive && Root != null && Root.IsChildOf(Host.transform), "visible conversation is a child of the original Live window");
        Check(Portrait != null && Portrait.IsChildOf(Host.transform), "the original character portrait shares the same window host");
        var hostBounds = ScreenRect(Host.WindowRect); var chatBounds = ScreenRect(Root); var portraitBounds = ScreenRect(Portrait);
        var tvBounds = ScreenRect(os.TVWindow.WindowRect);
        Check(Contains(hostBounds, chatBounds, 2f) && Contains(hostBounds, portraitBounds, 2f), "portrait and conversation stay inside their one outer window");
        Check(portraitBounds.xMax <= chatBounds.xMin + 2f, "conversation extends on the right without covering the portrait");
        Check(!hostBounds.Overlaps(tvBounds), "default TV and unified Live layout do not overlap");
        Check(ConversationDate != null && ConversationDate.text == "3014年2月8日" && ConversationDate.gameObject.activeInHierarchy,
            "the initial conversation-date message displays 3014年2月8日");
        var scroll = Root.GetComponentInChildren<UnityEngine.UI.ScrollRect>();
        var firstRow = scroll != null && scroll.content != null
            ? scroll.content.Cast<Transform>().FirstOrDefault(child => child.gameObject.activeSelf) : null;
        Check(firstRow != null && ConversationDate.transform.parent == firstRow,
            "the conversation date is the first scrolling content row rather than a pinned header");
        var dateLabels = Root.GetComponentsInChildren<TMP_Text>().Where(text => text.name == "DateText").ToArray();
        Check(dateLabels.Length == 1 && dateLabels[0] == ConversationDate
            && Root.GetComponentsInChildren<TMP_Text>().Count(text => text.text == "3014年2月8日") == 1,
            "the conversation contains only its one opening date message");
        Check(string.IsNullOrEmpty(story.DateText) && string.IsNullOrEmpty(story.GetDateKey(State.currentEntryId)),
            "the current story line has no historical date text or key");
        Check(Root.GetComponentsInChildren<OSWindowButton>(true).Length == 0,
            "conversation extension has no duplicate minimize or close controls");
        Check(EffectiveAlpha(Link.LinkWindow.transform) <= .01f
            && Link.LinkWindow.GetComponentsInParent<CanvasGroup>(true).Any(group => !group.blocksRaycasts),
            "the old Link window is an invisible non-blocking typewriter carrier");
        Check(data.TryGetRecordData(3, out var liveRecord), "original researcher paper-doll record resolves");
        Check(data.TryGetRecordData(2, out var playerRecord), "original robot paper-doll record resolves");
        var images = Root.GetComponentsInChildren<UnityEngine.UI.Image>(true);
        var liveAvatars = images.Where(image => image.name == "LiveAvatar").ToArray();
        var playerAvatars = images.Where(image => image.name == "PlayerAvatar").ToArray();
        Check(liveAvatars.Length > 0 && liveAvatars.All(image => image.sprite == liveRecord.RecordIcon), "Live avatars use the exact existing ResearcherPaperDoll sprite");
        Check(!expectPlayer || playerAvatars.Length > 0 && playerAvatars.All(image => image.sprite == playerRecord.RecordIcon),
            "player avatars use the exact existing RobotPaperDoll sprite");
        if (expectPlayer) Check(PlayerReplyLabels().All(text => !HasOptionNumber(text.text)),
            "player reply history remains free of option-number prefixes after rebuilding or restoration");
        Check(liveAvatars.Concat(playerAvatars).All(image => image.color == Color.white && image.preserveAspect
            && Vector2.Distance(image.rectTransform.rect.size, new Vector2(24f, 24f)) < .1f), "both avatar kinds retain native 24x24 geometry without tinting");
        if (!State.currentTextCompleted || story.CurrentEntry?.PartType == OSCharacterInteractionPartType.Text)
            CheckContinueIcon(ContinueButton());
        foreach (var text in Root.GetComponentsInChildren<TMP_Text>())
        {
            text.ForceMeshUpdate();
            if (string.IsNullOrWhiteSpace(text.text)) continue;
            // The typing bubble grows with the visible prefix, while TMP still
            // holds the complete source string behind maxVisibleCharacters.
            // Validate its full bounds after completion; static/history labels
            // are always checked, including during the first typing frame.
            if (text == View.CurrentBubble && !State.currentTextCompleted) continue;
            Check(!text.isTextOverflowing, "text fits its layout: " + text.name);
        }
        observations.Add(new { context, host = BoundsData(hostBounds), portrait = BoundsData(portraitBounds), chat = BoundsData(chatBounds),
            tv = BoundsData(tvBounds), liveAvatars = liveAvatars.Length, playerAvatars = playerAvatars.Length, entry = State.currentEntryId });
    }

    static void CheckContinueIcon(UnityEngine.UI.Button button)
    {
        var icon = button.transform.Find("ContinueIcon")?.GetComponent<UnityEngine.UI.Image>();
        var covered = button.transform.Find("HoverFill/BlackContinueIcon")?.GetComponent<UnityEngine.UI.Image>();
        var memory = os.MemoryWindowController;
        Check(memory != null && memory.ContinueTipSprite != null && icon != null && covered != null
            && icon.enabled && covered.enabled && icon.sprite == memory.ContinueTipSprite && covered.sprite == icon.sprite,
            "ordinary continue and typing use the exact original Memory continue sprite");
        Check(Vector2.Distance(memory.ContinueTipNativeSize, new Vector2(10f, 7f)) < .01f
            && Vector2.Distance(icon.rectTransform.rect.size, memory.ContinueTipNativeSize) < .01f
            && Vector2.Distance(covered.rectTransform.rect.size, memory.ContinueTipNativeSize) < .01f
            && icon.preserveAspect && covered.preserveAspect && !icon.raycastTarget && !covered.raycastTarget,
            "both continue images preserve native 10x7 geometry and let the reply receive input");
        Check(button.transform.Find("Label") == null && button.transform.Find("HoverFill/BlackLabel") == null
            && SameColor(icon.color, Color.white) && SameColor(covered.color, Color.black),
            "ordinary continue has an untinted image and a black clipped copy without a text label");
    }

    static IEnumerator CheckContinueIconHover(UnityEngine.UI.Button button)
    {
        CheckContinueIcon(button);
        var hover = button.GetComponent<OSChapter04ChatReplyHover>();
        var icon = button.transform.Find("ContinueIcon").GetComponent<UnityEngine.UI.Image>();
        var fill = button.transform.Find("HoverFill").GetComponent<UnityEngine.UI.Image>();
        var covered = fill.transform.Find("BlackContinueIcon").GetComponent<UnityEngine.UI.Image>();
        Check(hover != null && fill.GetComponent<UnityEngine.UI.RectMask2D>() != null && hover.FillAmount <= .001f,
            "the continue icon shares the existing clipped hover fill");
        HoverPointer(button, true);
        yield return Hold(.15f);
        Check(hover.FillAmount >= .999f && SameColor(icon.color, Color.black) && SameColor(covered.color, Color.black)
            && Vector2.Distance(ScreenRect(icon.rectTransform).position, ScreenRect(covered.rectTransform).position) < 1f,
            "full continue hover turns the native icon black and keeps the clipped copy aligned");
        yield return Screenshot("05-continue-icon-hover-filled.png");
        HoverPointer(button, false);
        yield return Hold(.15f);
        Check(hover.FillAmount <= .001f && fill.rectTransform.rect.width <= .01f && SameColor(icon.color, Color.white),
            "leaving continue restores the original sprite color and clears the hover fill");
    }

    static IEnumerator CheckReplyHover(UnityEngine.UI.Button button)
    {
        var hover = button.GetComponent<OSChapter04ChatReplyHover>();
        var fill = button.transform.Find("HoverFill")?.GetComponent<UnityEngine.UI.Image>();
        var label = button.transform.Find("Label")?.GetComponent<TMP_Text>();
        var coveredLabel = fill != null ? fill.transform.Find("BlackLabel")?.GetComponent<TMP_Text>() : null;
        var buttonRect = button.transform as RectTransform;
        Check(hover != null && fill != null && label != null && coveredLabel != null
            && fill.GetComponent<UnityEngine.UI.RectMask2D>() != null,
            "the real reply has a clipped hover fill and aligned contrast text");
        Check(button.transition == UnityEngine.UI.Selectable.Transition.None && !fill.raycastTarget,
            "the reply fill owns hover visuals without competing ColorTint or pointer interception");
        Color normal = new Color32(0x87, 0x82, 0x7e, 255);
        Check(hover.FillAmount <= .001f && fill.rectTransform.rect.width <= .01f && SameColor(label.color, normal),
            "a new reply starts with no grey fill and its normal readable label");

        HoverPointer(button, true);
        Check(hover.FillAmount <= .001f, "pointer enter begins the fill animation without snapping to full grey");
        yield return Hold(.15f);
        Check(hover.FillAmount >= .999f
            && Mathf.Abs(fill.rectTransform.rect.width - (buttonRect.rect.width - 2f)) < .01f
            && Mathf.Abs(fill.rectTransform.rect.height - (buttonRect.rect.height - 2f)) < .01f
            && Vector2.Distance(fill.rectTransform.anchoredPosition, new Vector2(1f, -1f)) < .01f,
            "hover fills the interior while preserving a one-pixel border on every side");
        Check(SameColor(label.color, Color.black) && SameColor(coveredLabel.color, Color.black)
            && coveredLabel.text == label.text
            && Vector2.Distance(ScreenRect(label.rectTransform).position, ScreenRect(coveredLabel.rectTransform).position) < 1f,
            "full hover keeps the black label readable and its clipped copy aligned");
        yield return Screenshot("04-reply-hover-filled.png");
        HoverPointer(button, false);
        yield return Hold(.15f);
        Check(hover.FillAmount <= .001f && fill.rectTransform.rect.width <= .01f && SameColor(label.color, normal),
            "pointer exit removes the fill and restores normal label contrast");

        HoverPointer(button, true);
        yield return Hold(.035f);
        float partial = hover.FillAmount;
        Check(partial > 0f && partial < 1f, "the entering fill has an observable intermediate width");
        HoverPointer(button, false);
        Check(Mathf.Abs(hover.FillAmount - partial) < .001f,
            "reversing hover mid-animation starts from the current width without a snap");
        yield return Hold(.15f);
        Check(hover.FillAmount <= .001f && SameColor(label.color, normal),
            "a reversed hover finishes empty with readable text");

        var settings = os.transform.Find("Windows/WindowUnit_Option")?.GetComponent<OSWindowUnit>();
        Check(settings != null && !settings.IsOpen, "the normal settings window is available to pause reply input");
        bool hidden = false;
        try
        {
            HoverPointer(button, true);
            yield return Hold(.15f);
            settings.OpenStandalone(0f);
            yield return Wait(() => Ready(settings) && !story.IsAvailable && !button.interactable,
                "opening settings disables the reply through the production story input gate");
            yield return null; yield return null;
            Check(hover.FillAmount <= .001f && fill.rectTransform.rect.width <= .01f && SameColor(label.color, normal),
                "disabling a hovered reply clears its fill and restores normal label contrast");
            HoverPointer(button, true, false);
            yield return Hold(.15f);
            Check(hover.FillAmount <= .001f, "a disabled reply cannot start another hover animation");
            settings.CloseFromScript();
            yield return Wait(() => !settings.IsOpen && !settings.IsClosing && story.IsAvailable && button.interactable,
                "closing settings restores the same reply without changing the choice");
            yield return Hold(.15f);
            Check(hover.FillAmount >= .999f,
                "a pointer still inside the reply resumes hover when input returns, without leaving and re-entering");
            button.gameObject.SetActive(false); hidden = true;
            Check(hover.FillAmount <= .001f && fill.rectTransform.rect.width <= .01f,
                "hiding a hovered reply clears its animation immediately");
            button.gameObject.SetActive(true); hidden = false;
            yield return null;
            Check(hover.FillAmount <= .001f && SameColor(label.color, normal),
                "a restored reply does not inherit the old hover fill");
        }
        finally
        {
            if (settings != null && settings.IsOpen) settings.CloseFromScript();
            if (hidden && button != null) button.gameObject.SetActive(true);
            if (button != null) ExecuteEvents.Execute(button.gameObject, Pointer(buttonRect), ExecuteEvents.pointerExitHandler);
        }
    }

    static void HoverPointer(UnityEngine.UI.Button button, bool enter, bool checkRaycast = true)
    {
        Canvas.ForceUpdateCanvases();
        var pointer = Pointer(button.transform as RectTransform);
        if (enter && checkRaycast)
        {
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
            Check(hits.Count > 0 && ExecuteEvents.GetEventHandler<IPointerEnterHandler>(hits[0].gameObject) == button.gameObject,
                "the topmost real raycast reaches the reply hover target");
        }
        bool handled = enter ? ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerEnterHandler)
            : ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerExitHandler);
        Check(handled, "the production reply handles pointer " + (enter ? "enter" : "exit"));
    }

    static bool SameColor(Color a, Color b) => Mathf.Abs(a.r - b.r) < .001f
        && Mathf.Abs(a.g - b.g) < .001f && Mathf.Abs(a.b - b.b) < .001f && Mathf.Abs(a.a - b.a) < .001f;
    static bool HasOptionNumber(string value) => System.Text.RegularExpressions.Regex.IsMatch(value ?? string.Empty, @"^\s*\d+\.");
    static IEnumerable<TMP_Text> PlayerReplyLabels() => Root.GetComponentsInChildren<TMP_Text>()
        .Where(text => text.name == "Text" && text.transform.parent != null && text.transform.parent.parent != null
            && text.transform.parent.parent.name == "PlayerReply");

    static IEnumerator CheckSmoothWheel(UnityEngine.UI.ScrollRect scroll)
    {
        Check(scroll is OSSmoothScrollRect, "the real chat uses the shared smooth wheel implementation");
        Check(EventSystem.current != null, "the active UI event system supplies scroll events");
        scroll.StopMovement(); scroll.verticalNormalizedPosition = 0f;
        yield return null;
        var viewportBounds = ScreenRect(scroll.viewport);
        Check(ScreenRect(ConversationDate.rectTransform).yMin >= viewportBounds.yMax - 2f,
            "the initial date scrolls fully out of view when newer messages fill the chat");

        var module = EventSystem.current.currentInputModule as UnityEngine.InputSystem.UI.InputSystemUIInputModule;
        float tick = module != null ? Mathf.Max(.001f, Mathf.Abs(module.scrollDeltaPerTick)) : 1f;
        float expectedScreenDistance = 28f * Screen.height / 360f;
        float initialY = ScreenRect(scroll.content).yMin;
        ScrollWheel(scroll, tick);
        Check(Mathf.Abs(ScreenRect(scroll.content).yMin - initialY) < .01f,
            "a complete wheel tick queues movement without an immediate content jump");
        yield return null; yield return null;
        float earlyDistance = initialY - ScreenRect(scroll.content).yMin;
        Check(earlyDistance > 0f && earlyDistance < expectedScreenDistance - .1f,
            "the wheel tick travels part of its distance across render frames");
        yield return Hold(.65f);
        float fullDistance = initialY - ScreenRect(scroll.content).yMin;
        Check(Mathf.Abs(fullDistance - expectedScreenDistance) <= Mathf.Max(1f, expectedScreenDistance * .035f),
            "one physical wheel tick settles at 28 units of the 360-high reference UI");

        ScrollWheel(scroll, tick);
        yield return null; yield return null;
        float reverseY = scroll.content.anchoredPosition.y;
        ScrollWheel(scroll, -tick);
        Check(Mathf.Abs(scroll.content.anchoredPosition.y - reverseY) < .01f,
            "a reversed wheel input also avoids an immediate jump");
        yield return null; yield return null;
        Check(scroll.content.anchoredPosition.y > reverseY,
            "reversing the wheel changes direction promptly without finishing the old glide");
        yield return Hold(.65f);

        ScrollWheel(scroll, tick);
        yield return null; yield return null;
        scroll.StopMovement();
        float stoppedY = scroll.content.anchoredPosition.y;
        yield return Hold(.3f);
        Check(Mathf.Abs(scroll.content.anchoredPosition.y - stoppedY) < .01f,
            "StopMovement cancels the remaining wheel travel without delayed drift");
        scroll.verticalNormalizedPosition = 1f;
        yield return null;
        Check(Contains(ScreenRect(scroll.viewport), ScreenRect(ConversationDate.rectTransform), 2f),
            "scrolling back to the beginning reveals the original date message");
        yield return Screenshot("03-date-message-at-history-start.png");
        observations.Add(new { context, type = "one physical wheel tick", eventDelta = tick,
            expectedScreenDistance, earlyDistance, fullDistance, screenHeight = Screen.height });
        scroll.StopMovement(); scroll.verticalNormalizedPosition = 0f;
        yield return null;
    }

    static void ScrollWheel(UnityEngine.UI.ScrollRect scroll, float delta)
    {
        var pointer = new PointerEventData(EventSystem.current) {
            position = ScreenRect(scroll.viewport).center, scrollDelta = new Vector2(0f, delta)
        };
        Check(ExecuteEvents.Execute(scroll.gameObject, pointer, ExecuteEvents.scrollHandler),
            "the production scroll event handler receives a complete wheel tick");
    }

    static IEnumerator EscapeWithPinnedConversation()
    {
        var host = Host; var tv = os.TVWindow; var carrier = Link.LinkWindow;
        bool hostPinned = host.IsAlwaysOnTop, tvPinned = tv.IsAlwaysOnTop;
        bool tvPreference = data.OSProcessData.memory.chapter04TVPinned;
        int carrierClosed = 0;
        Action<OSWindowUnit> onCarrierClosed = window => carrierClosed++;
        carrier.Closed += onCarrierClosed;
        try
        {
            if (!host.IsAlwaysOnTop) host.ToggleAlwaysOnTop();
            if (!tv.IsAlwaysOnTop) tv.ToggleAlwaysOnTop();
            yield return null;
            Check(Ready(host) && Ready(tv) && host.IsAlwaysOnTop && tv.IsAlwaysOnTop,
                "Live and TV are both visible and pinned for the Escape regression");
            var windows = Get(os, "windowUnits") as IEnumerable<OSWindowUnit>;
            Check(windows != null && !windows.Any(window => window != null && window != carrier
                && window.IsOpen && !window.IsMinimized && window.gameObject.activeInHierarchy
                && !window.IsAlwaysOnTop && !window.IsForceTopmost),
                "no ordinary visible window remains eligible for Escape");
            int visit = State.currentVisit, historyCount = State.history.Count;
            string entryId = State.currentEntryId, displayedId = Link.DisplayedEntryId;
            int version = (int)Get(Link, "presentationVersion");
            bool completed = State.currentTextCompleted;
            Check(!os.TryCloseFrontmostNormalWindow(), "Escape ignores the invisible Link playback carrier when all visible windows are pinned");
            yield return Hold(.25f);
            Check(carrierClosed == 0 && carrier.IsOpen && !carrier.IsClosing && Link.IsPresenting,
                "Escape never closes the hidden typewriter carrier");
            Check(State.currentVisit == visit && State.history.Count == historyCount && State.currentEntryId == entryId
                && State.currentTextCompleted == completed && Link.DisplayedEntryId == displayedId
                && (int)Get(Link, "presentationVersion") == version,
                "Escape does not restart the current text or change its saved visit");
            Check(Ready(host) && Ready(tv) && host.IsAlwaysOnTop && tv.IsAlwaysOnTop && story.IsAvailable,
                "Escape keeps both pinned visible windows and their conversation available");
        }
        finally
        {
            carrier.Closed -= onCarrierClosed;
            if (host != null && host.IsAlwaysOnTop != hostPinned) host.ToggleAlwaysOnTop();
            if (tv != null && tv.IsAlwaysOnTop != tvPinned) tv.ToggleAlwaysOnTop();
            data.OSProcessData.memory.chapter04TVPinned = tvPreference;
        }
        Check(host.IsAlwaysOnTop == hostPinned && tv.IsAlwaysOnTop == tvPinned
            && data.OSProcessData.memory.chapter04TVPinned == tvPreference,
            "Escape regression restores the prior Live and TV pin preferences");
    }

    static IEnumerator DragHost()
    {
        var title = Get(Host, "dragArea") as RectTransform;
        Check(Host.AllowsMove && title != null, "unified window keeps the existing draggable title");
        Canvas.ForceUpdateCanvases();
        var pointer = Pointer(title); var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
        Check(hits.Count > 0 && Host.IsDragAreaTarget(hits[0].gameObject)
            && ExecuteEvents.GetEventHandler<IPointerDownHandler>(hits[0].gameObject) == Host.gameObject,
            "real screen raycast reaches the common Live title");
        pointer.pointerCurrentRaycast = pointer.pointerPressRaycast = hits[0];
        Vector2 beforeHost = Host.WindowRect.anchoredPosition;
        Vector2 beforeHostScreen = ScreenRect(Host.WindowRect).center;
        Vector2 beforeChat = ScreenRect(Root).center, beforePortrait = ScreenRect(Portrait).center;
        ExecuteEvents.Execute(Host.gameObject, pointer, ExecuteEvents.pointerDownHandler);
        ExecuteEvents.Execute(Host.gameObject, pointer, ExecuteEvents.beginDragHandler);
        pointer.delta = new Vector2(32f, -20f); pointer.position += pointer.delta;
        ExecuteEvents.Execute(Host.gameObject, pointer, ExecuteEvents.dragHandler);
        ExecuteEvents.Execute(Host.gameObject, pointer, ExecuteEvents.endDragHandler);
        ExecuteEvents.Execute(Host.gameObject, pointer, ExecuteEvents.pointerUpHandler);
        Canvas.ForceUpdateCanvases();
        Vector2 moved = Host.WindowRect.anchoredPosition;
        Vector2 delta = ScreenRect(Host.WindowRect).center - beforeHostScreen;
        Vector2 chatDelta = ScreenRect(Root).center - beforeChat, portraitDelta = ScreenRect(Portrait).center - beforePortrait;
        observations.Add(new { context, type = "common title drag", before = new[] { beforeHost.x, beforeHost.y },
            moved = new[] { moved.x, moved.y }, screenDelta = new[] { delta.x, delta.y },
            chatScreenDelta = new[] { chatDelta.x, chatDelta.y }, portraitScreenDelta = new[] { portraitDelta.x, portraitDelta.y } });
        // Screen Space Camera canvases can map many screen pixels to less than
        // one world unit. Measure movement in UI units, and shared motion in pixels.
        Check(Vector2.Distance(beforeHost, moved) > 2f && delta.magnitude > 2f, "dragging the common title moves the window");
        Check(Vector2.Distance(chatDelta, delta) < .5f && Vector2.Distance(portraitDelta, delta) < .5f,
            "portrait and chat move by exactly the same amount");
        yield return Hold(.25f);
        Check(Vector2.Distance(Host.WindowRect.anchoredPosition, moved) < .1f, "story updates preserve the player's dragged position");
    }

    static IEnumerator Complete()
    {
        yield return Wait(() => story.IsAvailable && Link.DisplayedEntryId == State.currentEntryId && Link.IsPresenting, "current production typewriter is ready");
        if (!State.currentTextCompleted) Check(story.TryCompleteCurrentText(), "complete current authored text");
        yield return Wait(() => State.currentTextCompleted && State.currentCueCommitted && State.currentResourcesCommitted
            && !story.IsOptionGestureBlocked, "text and input receipts complete");
        yield return null;
    }
    static UnityEngine.UI.Button ContinueButton()
    {
        var footer = Get(View, "footer") as RectTransform;
        var button = footer != null ? footer.GetComponentsInChildren<UnityEngine.UI.Button>().FirstOrDefault(candidate => candidate.interactable) : null;
        Check(button != null && (button.transform.Find("ContinueIcon") != null || button.transform.Find("Label") != null),
            "visible footer action exists with its native continue icon or authored option label"); return button;
    }
    static void Click(UnityEngine.UI.Button button, string label) => ClickThroughRaycast(button.gameObject, (RectTransform)button.transform, label);
    static void ClickWindow(OSWindowButton.WindowAction action)
    {
        var button = Host.GetComponentsInChildren<OSWindowButton>().FirstOrDefault(candidate => candidate.LinkedWindow == Host && candidate.Action == action);
        Check(button != null, "shared window action exists: " + action);
        ClickThroughRaycast(button.gameObject, (RectTransform)button.transform, "shared " + action);
    }
    static void ClickThroughRaycast(GameObject receiver, RectTransform target, string label)
    {
        Canvas.ForceUpdateCanvases(); var pointer = Pointer(target); var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
        var actual = hits.Count > 0 ? ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject) : null;
        observations.Add(new { context, type = "pointer", label, expected = receiver.name, actual = actual?.name,
            hits = hits.Take(5).Select(hit => hit.gameObject.name).ToArray() });
        Check(actual == receiver, "topmost real raycast reaches " + label);
        Check(ExecuteEvents.Execute(actual, pointer, ExecuteEvents.pointerClickHandler), "pointer click executes " + label);
    }
    static void Bind()
    {
        os = OS_Handle.Instance; story = os?.Chapter04Story;
        Check(story != null && os.CharacterWindowController != null, "chapter and Live controllers resolve");
        story.DebugSetContent(null); GameTest.Instance.GetComponent<GameTestConsole>()?.SetOpen(false);
        if (!hasTypingSpeed) { typingSpeed = (float)Get(Link, "charactersPerSecond"); hasTypingSpeed = true; }
        Set(Link, "charactersPerSecond", 5f); Focus();
    }
    static void Focus()
    {
        mouse?.MakeCurrent(); keyboard?.MakeCurrent();
        foreach (var target in new MonoBehaviour[] { story, os?.Chapter04Entry, os?.Chapter03Story, os?.Chapter03Entry,
            os?.Chapter02FinalTalk, os?.CursorRelease, os?.GetComponent<OSChapter02NightfallPresentation>() })
        {
            if (target == null || !(Get(target, "hasFocus") is bool value)) continue;
            if (!focusStates.ContainsKey(target)) focusStates.Add(target, value); Set(target, "hasFocus", true);
        }
        story?.DebugSetFocus(true);
    }
    static void ReleaseInput()
    {
        if (mouse != null) InputState.Change(mouse, new MouseState { position = new Vector2(-10000, -10000) }, InputState.currentUpdateType);
        if (keyboard != null) InputState.Change(keyboard, new KeyboardState(), InputState.currentUpdateType);
    }
    static PointerEventData Pointer(RectTransform target) => new PointerEventData(EventSystem.current)
    { position = ScreenRect(target).center, button = PointerEventData.InputButton.Left, clickCount = 1 };
    static Rect ScreenRect(RectTransform target)
    {
        var corners = new Vector3[4]; target.GetWorldCorners(corners); var canvas = target.GetComponentInParent<Canvas>();
        var camera = canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
        var points = corners.Select(point => RectTransformUtility.WorldToScreenPoint(camera, point)).ToArray();
        return Rect.MinMaxRect(points.Min(point => point.x), points.Min(point => point.y), points.Max(point => point.x), points.Max(point => point.y));
    }
    static bool Contains(Rect outer, Rect inner, float tolerance) => inner.xMin >= outer.xMin - tolerance && inner.xMax <= outer.xMax + tolerance
        && inner.yMin >= outer.yMin - tolerance && inner.yMax <= outer.yMax + tolerance;
    static float[] BoundsData(Rect rect) => new[] { rect.x, rect.y, rect.width, rect.height };
    static float EffectiveAlpha(Transform target)
    { float alpha = 1f; foreach (var group in target.GetComponentsInParent<CanvasGroup>(true)) alpha *= group.alpha; return alpha; }
    static IEnumerator Screenshot(string name)
    {
        Canvas.ForceUpdateCanvases(); string path = Path.Combine(OutputDirectory, name); screenshots.Add(path); ScreenCapture.CaptureScreenshot(path);
        yield return null; yield return null;
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
    static void CaptureError(string condition, string trace, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(type + ": " + condition + "\n" + trace); }
    static string Describe() => "entry=" + story?.State?.currentEntryId + ", available=" + story?.IsAvailable + ", view=" + View?.OwnsLayout;
    static object Get(object target, string name) => target?.GetType().GetField(name, Members)?.GetValue(target)
        ?? target?.GetType().GetProperty(name, Members)?.GetValue(target);
    static void Set(object target, string name, object value) => target.GetType().GetField(name, Members).SetValue(target, value);
    static string Hash(string path)
    { if (!File.Exists(path)) return "absent"; using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(path))); }
}
