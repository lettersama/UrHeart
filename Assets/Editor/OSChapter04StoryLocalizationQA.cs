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
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

// Opt-in, short rendering audit. The real checkpoint provides the real Live
// window, fonts and chat factories; no story traversal or synthetic translation
// is used. All save writes remain disabled in an isolated test session.
public static class OSChapter04StoryLocalizationQA
{
    const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    const string SourcePath = "Tools/Chapter04Story/source.zh-CN.json";
    const string LocalesPath = "Tools/Chapter04Story/locales.json";
    static readonly LanguageState[] Languages = {
        LanguageState.zh_CN, LanguageState.zh_TW, LanguageState.en, LanguageState.ja, LanguageState.ko,
        LanguageState.fr, LanguageState.de, LanguageState.es, LanguageState.pt_BR, LanguageState.ru
    };
    static readonly string[] WarningKeys = {
        "chapter04.failure_warning.title", "chapter04.failure_warning.body", "chapter04.failure_warning.confirm"
    };
    static readonly List<string> checks = new List<string>(), failures = new List<string>(),
        runtimeErrors = new List<string>(), screenshots = new List<string>();
    static readonly List<object> observations = new List<object>(), languageSummaries = new List<object>();
    static readonly Dictionary<MonoBehaviour, bool> focusStates = new Dictionary<MonoBehaviour, bool>();
    static readonly Dictionary<OSChapter04StoryContent.Beat, float> delays = new Dictionary<OSChapter04StoryContent.Beat, float>();
    static GameDataManager data;
    static GameSetup setup;
    static OS_Handle os;
    static OSChapter04StoryDirector story;
    static OSChapter04ChatView view;
    static ScrollRect scroll;
    static RectTransform footer;
    static Mouse mouse;
    static Keyboard keyboard;
    static JObject locales;
    static List<OSCharacterInteractionEntryData> entries;
    static string context;
    static bool cancelled, viewWasEnabled, viewFrozen;
    public static string Status { get; private set; } = "idle";
    public static string OutputDirectory { get; private set; }
    public static void Cancel() => cancelled = true;

    sealed class RenderedEntry
    {
        public OSCharacterInteractionEntryData entry;
        public TMP_Text body;
        public readonly List<TMP_Text> playerReplies = new List<TMP_Text>();
        public readonly List<Button> buttons = new List<Button>();
    }

    [MenuItem("Tools/MrTGame/Chapter 4 Story/Localization QA (Fresh Play Menu)")]
    static void MenuStart() => Debug.Log("[Chapter04 Story Localization QA] " + Start());

    public static string Start()
    {
        if (Status == "running") return Status;
        var saves = GameAutosaveService.Instance;
        var menu = MainMenuHandle.Instance;
        if (!Application.isPlaying || GameDataManager.Instance == null || !GameDataManager.Instance.IsInitialized
            || saves == null || GameManager.Instance == null || GameTest.Instance == null
            || SceneLoadManager.Instance == null || GameSessionFlow.Instance == null || EventSystem.current == null)
            return "Fresh Play Mode with initialized GameDataManager and main menu required";
        if (saves.IsTestSession || saves.IsIsolatedStorage)
            return "Fresh Play Mode required to preserve the original Editor save path";
        if (saves.IsBusy || saves.IsWriting || GameManager.Instance.IsTransitioning
            || SceneLoadManager.Instance.IsBusy || GameSessionFlow.Instance.IsBusy || GameTest.Instance.IsCheckpointJumpRunning)
            return "Wait for active save/scene operation";
        if (GameManager.Instance.Phase != GamePhase.Boot || menu == null || !menu.IsMainMenuVisible || menu.IsOptionPanelVisible)
            return "Run from the idle main menu before starting or continuing a game";
        setup = typeof(GameSetup).GetField("activeSetup", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as GameSetup;
        if (setup == null) return "Active GameSetup is required";
        checks.Clear(); failures.Clear(); runtimeErrors.Clear(); screenshots.Clear(); observations.Clear();
        languageSummaries.Clear(); focusStates.Clear(); delays.Clear();
        cancelled = viewFrozen = false; context = "initialization"; data = GameDataManager.Instance;
        OutputDirectory = Path.GetFullPath(Path.Combine("Tools", "Chapter04Story", "Validation", "Localization_1103",
            "runtime-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8)));
        Directory.CreateDirectory(OutputDirectory); Status = "running";
        data.StartCoroutine(Guarded());
        return Status;
    }

    static IEnumerator Guarded()
    {
        var saves = GameAutosaveService.Instance;
        var originalSave = data.SaveData;
        string originalSaveJson = JsonUtility.ToJson(originalSave);
        var language = data.CurrentLanguageState;
        var resources = GameResourceSystem.Instance;
        bool resourcesEnabled = resources != null && resources.enabled;
        bool background = Application.runInBackground, demo = (bool)Get(setup, "isDemo");
        float volume = AudioListener.volume;
        var random = UnityEngine.Random.state;
        var previousMouse = Mouse.current; var previousKeyboard = Keyboard.current;
        string originalSavePath = saves.SavePath;
        string[] files = new[] { originalSavePath, originalSavePath + ".bak", originalSavePath + ".tmp",
            SourcePath, LocalesPath, "Assets/Resources/Chapter04/story.json", "Assets/Data/LocalizationData.asset",
            "Assets/Data/OSCharacterInteractionData.asset", "Assets/Data/RecordSO.asset" }
            .Concat(Directory.GetFiles("Assets/Scenes", "*.unity", SearchOption.AllDirectories)).ToArray();
        string[] before = null;
        var work = new Stack<IEnumerator>();
        Exception failure = null;
        Application.logMessageReceived += CaptureError;
        try
        {
            try
            {
                saves.EnterTestSession(); before = files.Select(Hash).ToArray();
                saves.UseIsolatedStorageForTests(Path.Combine(OutputDirectory, "isolated-save")); saves.EnterTestSession();
                data.ReplaceSaveData(JsonUtility.FromJson<GameSaveData>(originalSaveJson));
                Set(setup, "isDemo", false); data.SetCurrentLanguage(LanguageState.zh_CN);
                if (resources != null) resources.enabled = false;
                Application.runInBackground = true; AudioListener.volume = 0f;
                mouse = InputSystem.AddDevice<Mouse>("Chapter04StoryLocalizationQAMouse");
                keyboard = InputSystem.AddDevice<Keyboard>("Chapter04StoryLocalizationQAKeyboard");
                InputState.Change(mouse, new MouseState { position = new Vector2(-10000f, -10000f) }, InputState.currentUpdateType);
                InputState.Change(keyboard, new KeyboardState(), InputState.currentUpdateType);
                work.Push(Run());
            }
            catch (Exception error) { failure = error; }
            while (failure == null && work.Count > 0)
            {
                object next = null;
                try
                {
                    Focus();
                    if (cancelled) throw new OperationCanceledException("Chapter04 localization QA cancelled");
                    if (!work.Peek().MoveNext()) { (work.Pop() as IDisposable)?.Dispose(); continue; }
                    next = work.Peek().Current;
                }
                catch (Exception error) { failure = error; }
                if (failure != null) break;
                if (next is IEnumerator nested) work.Push(nested); else yield return next;
            }
            if (failure != null) failures.Add(context + ": " + failure);
            Check(before != null && files.Select(Hash).SequenceEqual(before), "formal save, story sources, localization asset and scenes unchanged");
        }
        finally
        {
            while (work.Count > 0) (work.Pop() as IDisposable)?.Dispose();
            try
            {
                if (view != null)
                {
                    ClearRows(); view.ReleaseView();
                    if (viewFrozen) view.enabled = viewWasEnabled;
                }
                foreach (var pair in delays) pair.Key.autoAdvanceDelay = pair.Value;
                story?.DebugSetFocus(false); story?.ResetPresentation(); os?.Chapter04Entry?.ResetPresentation();
                os?.Chapter03Story?.ResetPresentation();
                os?.MemoryWindowController?.GetComponent<OSChapter03HomeController>()?.Suspend();
                os?.LinkMessageController?.NotifyOSHidden();
                if (GameTest.Instance?.IsCheckpointJumpRunning == true) GameTest.Instance.StopAllCoroutines();
                GameTest.Instance?.GetComponent<GameTestConsole>()?.SetOpen(false);
                foreach (var pair in focusStates) if (pair.Key != null) Set(pair.Key, "hasFocus", pair.Value);
                saves.EnterTestSession(); saves.EndSession(); GameManager.Instance?.OpenMainMenuWithoutSave();
                data.ReplaceSaveData(originalSave); data.SetCurrentLanguage(language); Set(setup, "isDemo", demo);
                if (resources != null) resources.enabled = resourcesEnabled;
                Application.runInBackground = background; AudioListener.volume = volume;
                if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
                if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
                UnityEngine.Random.state = random;
                Check(ReferenceEquals(data.SaveData, originalSave) && JsonUtility.ToJson(originalSave) == originalSaveJson,
                    "cleanup restores the original in-memory save object without changing its data");
                Check(data.CurrentLanguageState == language && GameSetup.IsDemo == demo
                    && Application.runInBackground == background && AudioListener.volume == volume
                    && (resources == null || resources.enabled == resourcesEnabled)
                    && focusStates.All(pair => pair.Key == null || Equals(Get(pair.Key, "hasFocus"), pair.Value))
                    && (previousMouse == null || !previousMouse.added || Mouse.current == previousMouse)
                    && (previousKeyboard == null || !previousKeyboard.added || Keyboard.current == previousKeyboard)
                    && UnityEngine.Random.state.Equals(random), "cleanup restores language, Demo, resources, audio, background, focus, input and random state");
                Check(saves.IsTestSession && saves.IsIsolatedStorage && !saves.SessionActive,
                    "cleanup leaves writes disabled in the isolated test session while returning to the menu");
                Check(before != null && files.Select(Hash).SequenceEqual(before), "cleanup preserves all formal file hashes");
            }
            catch (Exception error) { failures.Add("cleanup: " + error); }
            Application.logMessageReceived -= CaptureError;
            Status = failures.Count == 0 && runtimeErrors.Count == 0
                ? "PASS: " + checks.Count + " checks" : "FAILED: " + failures.Count + " checks, " + runtimeErrors.Count + " runtime errors";
            File.WriteAllText(Path.Combine(OutputDirectory, "results.json"), JsonConvert.SerializeObject(new {
                status = Status, context, time = DateTime.UtcNow.ToString("O"), checks, failures, runtimeErrors,
                languageSummaries, observations, screenshots, originalSavePath, originalFiles = files,
                originalHashes = before, finalHashes = files.Select(Hash).ToArray(),
                note = "Real Chapter04StoryStart checkpoint and OSChapter04ChatView Bubble/Reply factories. All 85 story lines and 27 options are rendered in ten languages, including sent player bubbles and hover text. Three warning keys receive translation/glyph coverage only, not warning-dialog layout certification. The view is reacquired while disabled so LateUpdate cannot rebuild individual specimens. No complete story traversal; only in-memory automatic delays are paused. Formal saves remain protected; stop Play Mode before normal play."
            }, Formatting.Indented));
            File.WriteAllText(Path.Combine(OutputDirectory, "results.txt"), Status + "\n" + string.Join("\n", failures)
                + "\n" + string.Join("\n", runtimeErrors));
            Debug.Log("[Chapter04 Story Localization QA] " + Status + " | " + OutputDirectory);
            data = null; setup = null; os = null; story = null; view = null; scroll = null; footer = null;
            mouse = null; keyboard = null; entries = null; locales = null; viewFrozen = false;
        }
    }

    static IEnumerator Run()
    {
        ReadCatalog();
        foreach (var beat in OSChapter04StoryContent.Instance.Beats)
        { delays.Add(beat, beat.autoAdvanceDelay); beat.autoAdvanceDelay = -1f; }
        context = "real Chapter04StoryStart checkpoint";
        Require(GameTest.Instance.JumpToCheckpoint(GameTestCheckpoint.Chapter04StoryStart), "checkpoint accepts the jump");
        yield return Wait(() => !GameTest.Instance.IsCheckpointJumpRunning && !GameManager.Instance.IsTransitioning
            && !SceneLoadManager.Instance.IsBusy && OS_Handle.Instance != null, "checkpoint finished", 80f);
        os = OS_Handle.Instance; story = os.Chapter04Story;
        Require(story != null, "production chapter-four story director resolves");
        GameTest.Instance.GetComponent<GameTestConsole>()?.SetOpen(false); Focus();
        yield return Wait(() => story.IsAvailable && (view = story.GetComponent<OSChapter04ChatView>()) != null
            && view.OwnsLayout && view.Root != null && view.CurrentBubble != null, "real chat view acquired", 25f);
        Require(GameTest.Instance.JumpSucceeded && savesAreIsolated(), "checkpoint is isolated and successful");
        Require(story.State.currentEntryId == OSChapter04StoryContent.Instance.FirstEntryId,
            "only the first real story beat is entered");

        // Disabling invokes Release. Acquire again on this same disabled view,
        // retaining the real host and factories while preventing LateUpdate from
        // replacing the QA specimens with the running first line.
        viewWasEnabled = view.enabled; view.enabled = false; viewFrozen = true;
        Require((bool)Call(view, "Acquire"), "disabled production view reacquires its original host");
        var carrier = Get(view, "originalArea") as CanvasGroup;
        Require(carrier != null, "original message carrier CanvasGroup resolves");
        carrier.alpha = 0f; carrier.blocksRaycasts = false; carrier.interactable = true;
        scroll = Get(view, "scroll") as ScrollRect; footer = Get(view, "footer") as RectTransform;
        Require(scroll != null && scroll.content != null && scroll.viewport != null && footer != null,
            "actual content, viewport, footer and scroll rect resolve");

        foreach (var language in Languages)
        {
            context = language + " / translation coverage";
            data.SetCurrentLanguage(language); yield return null; yield return null;
            VerifyTranslations(language);
            OSCharacterInteractionEntryData tallestBody = null, tallestReplies = null;
            float bodyHeight = -1f, replyHeight = -1f;
            int covered = 0, scrolling = 0, wrapped = 0;
            foreach (var entry in entries)
            {
                context = language + " / " + entry.EntryId;
                var rendered = BuildEntry(entry, true);
                yield return null;
                Layout();
                Require(!view.enabled && view.OwnsLayout && rendered.body != null,
                    "specimen survives a frame without automatic chat rebuilding");
                string body = story.GetLineText(entry.EntryId);
                Check(rendered.body.text == body, "real body text matches localized production lookup");
                var metrics = new List<object> { Inspect(rendered.body, entry.LocalizationKey, "Live bubble") };
                covered++;
                if (rendered.body.textInfo.lineCount > 1) wrapped++;
                if (rendered.body.rectTransform.rect.height > bodyHeight)
                { bodyHeight = rendered.body.rectTransform.rect.height; tallestBody = entry; }
                float totalReplyHeight = rendered.buttons.Sum(button => ((RectTransform)button.transform).rect.height);
                if (rendered.buttons.Count > 0 && totalReplyHeight > replyHeight)
                { replyHeight = totalReplyHeight; tallestReplies = entry; }
                for (int i = 0; i < entry.Options.Count; i++)
                {
                    var option = entry.Options[i]; var button = rendered.buttons[i];
                    var label = button.transform.Find("Label")?.GetComponent<TMP_Text>();
                    var black = button.transform.Find("HoverFill/BlackLabel")?.GetComponent<TMP_Text>();
                    Require(label != null && black != null, "real reply and hover text labels resolve");
                    string expected = (string)Call(view, "OptionText", entry.EntryId, option.OptionId);
                    Check(label.text == expected && black.text == expected && rendered.playerReplies[i].text == expected,
                        "option button, hover copy and sent bubble use the same production prefix stripping");
                    metrics.Add(Inspect(label, option.LocalizationKey, "Reply button"));
                    metrics.Add(Inspect(black, option.LocalizationKey, "Reply hover"));
                    metrics.Add(Inspect(rendered.playerReplies[i], option.LocalizationKey, "Player bubble"));
                    Check(button.IsActive() && button.IsInteractable()
                        && Contains(view.Root.rect, BoundsIn(view.Root, (RectTransform)button.transform), 1f),
                        "reply button is usable and contained by the real chat root");
                    if (i > 0)
                    {
                        Rect previous = BoundsIn(footer, (RectTransform)rendered.buttons[i - 1].transform);
                        Rect current = BoundsIn(footer, (RectTransform)button.transform);
                        Check(current.yMax <= previous.yMin - 2f, "adjacent reply buttons retain their gap without overlap");
                    }
                    covered++;
                }
                InspectDate();
                Check(scroll.viewport.rect.height > 20f, "reply footer leaves a usable chat viewport");
                Check(BoundsIn(view.Root, footer).yMax <= BoundsIn(view.Root, scroll.viewport).yMin - 5f,
                    "reply footer stays below the scrolling text area");
                bool needsScroll = scroll.content.rect.height > scroll.viewport.rect.height + 1f;
                if (needsScroll) { scrolling++; VerifyScrollEnds(); }
                observations.Add(new {
                    language = language.ToString(), entryId = entry.EntryId, bodyKey = entry.LocalizationKey,
                    optionKeys = entry.Options.Select(option => option.LocalizationKey).ToArray(), metrics,
                    viewport = RectData(scroll.viewport.rect), content = RectData(scroll.content.rect),
                    footer = RectData(footer.rect), requiresScrolling = needsScroll
                });
            }
            context = language + " / auxiliary warning glyphs";
            foreach (string key in WarningKeys)
            {
                var label = (TMP_Text)Call(view, "Text", "WarningGlyphProbe", footer, data.GetLocalizedText(key));
                Glyphs(label, key); UnityEngine.Object.DestroyImmediate(label.gameObject);
            }
            Check(covered == 112, "all 85 body and 27 option keys received actual chat rendering checks");
            Check(scrolling > 0 && wrapped > 0, "authored long content wraps and uses the scrolling viewport");
            languageSummaries.Add(new {
                language = language.ToString(), localizedKeys = locales.Properties().Count(), renderedChatKeys = covered,
                renderedBodies = entries.Count, renderedOptions = entries.Sum(entry => entry.Options.Count),
                scrollingEntries = scrolling, wrappedBodies = wrapped, tallestBody = tallestBody?.EntryId,
                tallestBodyHeight = bodyHeight, tallestReplyGroup = tallestReplies?.EntryId, tallestReplyGroupHeight = replyHeight
            });
            if (language == LanguageState.en || language == LanguageState.ru)
            {
                Require(tallestBody != null && tallestReplies != null, "longest body and reply screenshot targets resolve");
                yield return CaptureLongest(language, tallestBody, "longest-body");
                yield return CaptureLongest(language, tallestReplies, "longest-replies");
            }
        }
        context = "completed coverage";
        Check(languageSummaries.Count == 10, "all ten languages completed the rendering audit");
        Check(screenshots.Count == 8 && screenshots.All(path => File.Exists(path) && new FileInfo(path).Length > 128),
            "English and Russian longest body/reply groups have nonempty top and bottom screenshots");
        Check(story.State.currentEntryId == OSChapter04StoryContent.Instance.FirstEntryId,
            "QA did not advance or traverse the authored story");
    }

    static bool savesAreIsolated() => GameAutosaveService.Instance.IsTestSession && GameAutosaveService.Instance.IsIsolatedStorage;

    static void ReadCatalog()
    {
        context = "authored catalog";
        var source = JObject.Parse(File.ReadAllText(SourcePath));
        locales = JObject.Parse(File.ReadAllText(LocalesPath));
        entries = new List<OSCharacterInteractionEntryData>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var authored in source["entries"])
        {
            string id = (string)authored["entryId"];
            Require(data.OSCharacterInteractionData.TryGetGlobalEntry(id, out var entry), "production entry resolves: " + id);
            Require(entry.LocalizationKey == (string)authored["localizationKey"], "body key matches the authored source: " + id);
            entries.Add(entry); Require(keys.Add(entry.LocalizationKey), "unique body key: " + entry.LocalizationKey);
            var options = (JArray)authored["options"];
            Require(entry.Options.Count == options.Count, "production option count matches source: " + id);
            for (int i = 0; i < entry.Options.Count; i++)
            {
                Require(entry.Options[i].OptionId == (string)options[i]["optionId"]
                    && entry.Options[i].LocalizationKey == (string)options[i]["localizationKey"], "production option identity matches source: " + id + "/" + i);
                Require(keys.Add(entry.Options[i].LocalizationKey), "unique option key: " + entry.Options[i].LocalizationKey);
            }
        }
        Require(entries.Count == 85 && keys.Count == 112, "catalog contains 85 body keys and 27 option keys");
        foreach (string key in WarningKeys) keys.Add(key);
        Require(keys.SetEquals(locales.Properties().Select(property => property.Name)) && keys.Count == 115,
            "115-key localization catalog consists exactly of the story, options and warning UI");
    }

    static void VerifyTranslations(LanguageState language)
    {
        foreach (var property in locales.Properties())
        {
            var matches = data.LocalizationData.Entries.Where(entry => entry != null && entry.Key == property.Name).ToArray();
            Require(matches.Length == 1, "localized key has one production definition: " + property.Name);
            var own = matches[0].Values.Where(value => value != null && value.Language == language).ToArray();
            string expected = (string)property.Value[language.ToString()];
            Check(own.Length == 1 && !string.IsNullOrWhiteSpace(own[0].Text) && own[0].Text == expected
                && data.GetLocalizedText(property.Name) == expected, "own-language text is installed without fallback: " + property.Name);
        }
    }

    static RenderedEntry BuildEntry(OSCharacterInteractionEntryData entry, bool includeSentReplies)
    {
        ClearRows(); Call(view, "AddConversationDate");
        var result = new RenderedEntry { entry = entry, body = (TMP_Text)Call(view, "Bubble", story.GetLineText(entry.EntryId), false) };
        float height = 0f;
        for (int i = 0; i < entry.Options.Count; i++)
        {
            string value = (string)Call(view, "OptionText", entry.EntryId, entry.Options[i].OptionId);
            var button = (Button)Call(view, "Reply", value, i, height);
            result.buttons.Add(button); height += ((RectTransform)button.transform).rect.height + 3f;
            if (includeSentReplies) result.playerReplies.Add((TMP_Text)Call(view, "Bubble", value, true));
        }
        if (entry.Options.Count == 0)
        {
            var next = (Button)Call(view, "Reply", null, 0, 0f);
            height = ((RectTransform)next.transform).rect.height + 3f;
        }
        height = Mathf.Max(23f, height);
        footer.offsetMax = new Vector2(-6f, 5f + height);
        scroll.GetComponent<RectTransform>().offsetMin = new Vector2(6f, height + 12f);
        Layout(); scroll.StopMovement(); scroll.verticalNormalizedPosition = 1f;
        return result;
    }

    static void ClearRows()
    {
        if (view == null) return;
        var content = Get(view, "content") as RectTransform;
        var replyRoot = Get(view, "footer") as RectTransform;
        foreach (var root in new[] { content, replyRoot })
            if (root != null) for (int i = root.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(root.GetChild(i).gameObject);
        (Get(view, "rowObjects") as IList)?.Clear(); (Get(view, "actions") as IList)?.Clear();
        Set(view, "currentLabel", null); Set(view, "conversationDateLabel", null); Set(view, "treatmentAction", null);
    }

    static object Inspect(TMP_Text label, string key, string kind)
    {
        Require(label != null, kind + " label exists");
        label.ForceMeshUpdate();
        string plain = label.GetParsedText();
        uint[] missing = Glyphs(label, key);
        Rect rect = label.rectTransform.rect;
        var visible = label.textInfo.characterInfo.Take(label.textInfo.characterCount).Where(character => character.isVisible).ToArray();
        bool fits = visible.All(character => character.bottomLeft.x >= rect.xMin - 1.25f
            && character.topRight.x <= rect.xMax + 1.25f && character.bottomLeft.y >= rect.yMin - 1.25f
            && character.topRight.y <= rect.yMax + 1.25f);
        Check(rect.width > 0f && rect.height > 0f && !float.IsNaN(rect.height) && !float.IsInfinity(rect.height)
            && !label.isTextTruncated && !label.isTextOverflowing && fits, kind + " fits its allocated text rectangle: " + key);
        Check(label.maxVisibleCharacters >= plain.Length && label.textInfo.characterCount > 0,
            kind + " renders the full text rather than a typewriter prefix: " + key);
        float unwrapped = label.GetPreferredValues(label.text, float.PositiveInfinity, float.PositiveInfinity).x;
        Check(unwrapped <= rect.width + 1f || label.textInfo.lineCount > 1,
            kind + " wraps content wider than the text rectangle: " + key);
        if (data.CurrentLanguageState == LanguageState.ko && data.LocalizationData.KoreanFont != null)
            Check(label.font == data.LocalizationData.KoreanFont, kind + " uses the configured Korean font: " + key);
        return new {
            key, kind, text = label.text, parsed = plain, font = label.font != null ? label.font.name : null,
            fontSize = label.fontSize, lineCount = label.textInfo.lineCount, characterCount = label.textInfo.characterCount,
            rect = RectData(rect), unwrappedWidth = unwrapped, glyphBoundsFit = fits,
            missingGlyphs = missing.Select(value => "U+" + value.ToString("X4")).ToArray(),
            overflowing = label.isTextOverflowing, truncated = label.isTextTruncated
        };
    }

    static uint[] Glyphs(TMP_Text label, string key)
    {
        string text = label.richText ? label.GetParsedText() : label.text;
        // A newly created auxiliary probe has no generated textInfo yet.
        if (string.IsNullOrEmpty(text)) text = label.text;
        string glyphs = new string((text ?? string.Empty).Where(character => !char.IsWhiteSpace(character)).ToArray());
        uint[] missing = null;
        bool covered = label.font != null && label.font.HasCharacters(glyphs, out missing, true, true);
        Check(covered, "zero missing glyphs: " + key + (missing == null || missing.Length == 0 ? "" : " [" + string.Join(",", missing) + "]"));
        return missing ?? Array.Empty<uint>();
    }

    static void InspectDate()
    {
        var date = view.ConversationDateText;
        Require(date != null, "real conversation date row exists");
        date.ForceMeshUpdate(); Glyphs(date, OSChapter04ChatView.ConversationDateKey);
        Check(!date.isTextTruncated && !date.isTextOverflowing, "localized conversation date fits its fixed row");
    }

    static void VerifyScrollEnds()
    {
        scroll.StopMovement(); scroll.verticalNormalizedPosition = 1f; Canvas.ForceUpdateCanvases();
        Rect top = BoundsIn(scroll.viewport, scroll.content);
        Check(Mathf.Abs(top.yMax - scroll.viewport.rect.yMax) < 1.5f, "scroll reaches the top of the full content");
        scroll.StopMovement(); scroll.verticalNormalizedPosition = 0f; Canvas.ForceUpdateCanvases();
        Rect bottom = BoundsIn(scroll.viewport, scroll.content);
        Check(Mathf.Abs(bottom.yMin - scroll.viewport.rect.yMin) < 1.5f, "scroll reaches the bottom of the full content");
    }

    static IEnumerator CaptureLongest(LanguageState language, OSCharacterInteractionEntryData entry, string kind)
    {
        context = language + " / " + kind + " / " + entry.EntryId;
        var rendered = BuildEntry(entry, false);
        yield return null; Layout();
        scroll.StopMovement(); scroll.verticalNormalizedPosition = 1f;
        yield return Screenshot(language + "-" + kind + "-" + entry.EntryId + "-top.png");
        if (scroll.content.rect.height > scroll.viewport.rect.height + 1f) VerifyScrollEnds();
        scroll.StopMovement(); scroll.verticalNormalizedPosition = 0f;
        yield return Screenshot(language + "-" + kind + "-" + entry.EntryId + "-bottom.png");
        if (kind == "longest-body")
        {
            Check(scroll.content.rect.height > scroll.viewport.rect.height + 1f,
                "the longest actual body requires and supports scrolling");
            Rect end = BoundsIn(scroll.viewport, rendered.body.rectTransform);
            Check(end.yMin >= scroll.viewport.rect.yMin - 1f && end.yMin <= scroll.viewport.rect.yMax,
                "the longest body's final line is reachable at the bottom");
            // Exercise the actual smooth-wheel handler as well as direct jumps.
            scroll.StopMovement(); scroll.verticalNormalizedPosition = 1f;
            scroll.OnScroll(new PointerEventData(EventSystem.current) { scrollDelta = new Vector2(0f, -120f) });
            float until = Time.realtimeSinceStartup + .4f;
            while (Time.realtimeSinceStartup < until) yield return null;
            Check(scroll.verticalNormalizedPosition < .999f, "real wheel handler scrolls long authored text");
        }
    }

    static void Layout()
    {
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
        Canvas.ForceUpdateCanvases();
    }

    static IEnumerator Screenshot(string name)
    {
        Layout(); yield return null;
        string path = Path.Combine(OutputDirectory, name); screenshots.Add(path);
        ScreenCapture.CaptureScreenshot(path);
        yield return Wait(() => File.Exists(path) && new FileInfo(path).Length > 128, "screenshot written: " + name, 12f);
    }

    static IEnumerator Wait(Func<bool> predicate, string label, float seconds = 20f)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (!predicate() && Time.realtimeSinceStartup < until) yield return null;
        Require(predicate(), label);
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

    static Rect BoundsIn(RectTransform space, RectTransform target)
    {
        var corners = new Vector3[4]; target.GetWorldCorners(corners);
        var local = corners.Select(space.InverseTransformPoint).ToArray();
        return Rect.MinMaxRect(local.Min(point => point.x), local.Min(point => point.y), local.Max(point => point.x), local.Max(point => point.y));
    }
    static bool Contains(Rect outer, Rect inner, float tolerance) => inner.xMin >= outer.xMin - tolerance
        && inner.xMax <= outer.xMax + tolerance && inner.yMin >= outer.yMin - tolerance && inner.yMax <= outer.yMax + tolerance;
    static float[] RectData(Rect value) => new[] { value.x, value.y, value.width, value.height };
    static void Check(bool condition, string label)
    { if (condition) checks.Add(context + ": " + label); else failures.Add(context + ": " + label); }
    static void Require(bool condition, string label)
    { if (!condition) throw new InvalidOperationException(label); checks.Add(context + ": " + label); }
    static void CaptureError(string message, string trace, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) runtimeErrors.Add(type + ": " + message + "\n" + trace); }
    static object Get(object target, string name) => target?.GetType().GetField(name, Members)?.GetValue(target)
        ?? target?.GetType().GetProperty(name, Members)?.GetValue(target);
    static void Set(object target, string name, object value)
    {
        var field = target.GetType().GetField(name, Members);
        if (field == null) throw new MissingFieldException(target.GetType().Name, name);
        field.SetValue(target, value);
    }
    static object Call(object target, string name, params object[] arguments)
    {
        var method = target.GetType().GetMethod(name, Members);
        if (method == null) throw new MissingMethodException(target.GetType().Name, name);
        return method.Invoke(target, arguments);
    }
    static string Hash(string path)
    { if (!File.Exists(path)) return "absent"; using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(path))); }
}
