using System;
using System.Collections.Generic;
using MrTGame.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>Development-only runtime entry point for GameTest jumps.</summary>
[DefaultExecutionOrder(-500)]
[DisallowMultipleComponent]
public sealed class GameTestConsole : MonoBehaviour
{
    const string Prefix = "ui.debug_console.";
    const float Width = 620, Height = 508, Margin = 24, ContentWidth = Width - Margin * 2;
    const float ControlsTop = 48, ControlsHeight = 370;
    const float Chapter02ShortcutsHeight = 260;
    const float Chapter03ShortcutsHeight = 226;
    const float Chapter04ShortcutsHeight = 128;
    static readonly Color32 Paper = new Color32(220, 216, 207, 255);
    static readonly Color32 Muted = new Color32(153, 149, 142, 255);
    static readonly Color32 Line = new Color32(69, 67, 63, 255);
    static readonly Color32 Ink = new Color32(17, 17, 16, 255);
    static readonly Color32 Accent = new Color32(183, 196, 170, 255);
    static readonly LanguageState[] Languages = (LanguageState[])Enum.GetValues(typeof(LanguageState));
    static readonly string[] LanguageNames =
    {
        "简体中文", "繁體中文", "English", "日本語", "한국어",
        "Français", "Deutsch", "Español", "Português (BR)", "Русский"
    };
    static GameTestConsole current;
    static int closedFrame = -1;
    public static bool BlocksGameplayInput => current != null
        && (current.IsOpen || current.Test != null && current.IsJumpRunning || closedFrame == Time.frameCount);
    public bool IsOpen => root != null && root.activeSelf;
    bool IsJumpRunning => Test != null && (Test.IsCheckpointJumpRunning || Test.IsMemoryJumpRunning);
    bool CheckpointAvailabilityChanged =>
        chapter02Available != GameTest.IsCheckpointAvailable(GameTestCheckpoint.Chapter02Start)
        || chapter03Available != GameTest.IsCheckpointAvailable(GameTestCheckpoint.Chapter03MemoryStart)
        || chapter04Available != GameTest.IsCheckpointAvailable(GameTestCheckpoint.Chapter04StoryStart);

    GameTest Test;
    GameSetup setup;
    UnityEngine.UI.Toggle fastMemoryToggle;
    GameObject root, languageLayer;
    RectTransform panel;
    RectTransform scrollContent, chapter02Section, chapter03Section, chapter04Section, memorySection;
    UnityEngine.UI.ScrollRect checkpointScroll;
    UnityEngine.UI.LayoutElement scrollLayout;
    TMP_InputField indexInput;
    TMP_Text status, languageName;
    UnityEngine.UI.Image statusDot;
    CanvasGroup controls;
    TMP_FontAsset font;
    GameObject previousSelection;
    bool cursorWasVisible, awaitingJump, chapter02Available, chapter03Available, chapter04Available;
    CursorLockMode cursorLock;
    readonly List<OSLocalizedText> bindings = new List<OSLocalizedText>();
    readonly Dictionary<TMP_FontAsset, Material> cleanMaterials = new Dictionary<TMP_FontAsset, Material>();
    readonly List<TMP_Text> nativeLanguageLabels = new List<TMP_Text>();
    readonly List<UnityEngine.UI.Image> languageMarkers = new List<UnityEngine.UI.Image>();
    readonly List<KeyValuePair<TMP_Text, RectTransform>> groupHeadings = new List<KeyValuePair<TMP_Text, RectTransform>>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { current = null;closedFrame = -1; }
    void Awake()
    {
        Test = GetComponent<GameTest>();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        current = this;
#else
        enabled = false;
#endif
    }
    void OnEnable()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        current = this;
        GameEvents.LanguageChanged += RefreshLanguage;
#endif
    }
    void OnDisable()
    {
        GameEvents.LanguageChanged -= RefreshLanguage;
        SetOpen(false);
        if (current == this) current = null;
    }
    void OnDestroy()
    {
        foreach (var material in cleanMaterials.Values)
            if (material != null) Destroy(material);
        cleanMaterials.Clear();
    }
    void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.backquoteKey.wasPressedThisFrame) SetOpen(!IsOpen);
        else if (IsOpen && keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            if (languageLayer != null && languageLayer.activeSelf) languageLayer.SetActive(false);
            else SetOpen(false);
        }
        if (awaitingJump && Test != null)
        {
            ShowJumpStatus();
            if (!IsJumpRunning)
            {
                awaitingJump = false;
                if (Test.JumpSucceeded) SetOpen(false);
            }
        }
        if (controls != null) controls.interactable = Test != null && !IsJumpRunning;
        if (IsOpen && CheckpointAvailabilityChanged)
            RefreshCheckpointAvailability();
        if (IsOpen) RefreshFastMemoryToggle();
    }
    void LateUpdate()
    {
        if (!IsOpen) return;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }
    public void SetOpen(bool open)
    {
#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
        return;
#else
        if (open == IsOpen) return;
        if (open)
        {
            if (root == null) BuildWindow();
            previousSelection = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            cursorWasVisible = Cursor.visible;cursorLock = Cursor.lockState;
            root.SetActive(true);languageLayer.SetActive(false);
            RefreshFastMemoryToggle();
            RefreshCheckpointAvailability();
            RefreshLanguage(CurrentLanguage);
            if (IsJumpRunning) ShowJumpStatus();
            else ShowStatus("status_ready", "就绪");
            EventSystem.current?.SetSelectedGameObject(null);
        }
        else
        {
            indexInput.DeactivateInputField();
            languageLayer.SetActive(false);root.SetActive(false);
            closedFrame = Time.frameCount;
            Cursor.visible = cursorWasVisible;Cursor.lockState = cursorLock;
            EventSystem.current?.SetSelectedGameObject(
                previousSelection != null && previousSelection.activeInHierarchy ? previousSelection : null);
        }
#endif
    }
    public void SubmitIndex()
    {
        if (Test == null || IsJumpRunning) return;
        if (!int.TryParse(indexInput.text.Trim(), out int index) || index < 0
            || GameDataManager.Instance == null || !GameDataManager.Instance.TryGetOSMemoryData(index, out var target))
        {
            ShowStatus("status_invalid_index", "编号不存在，请输入有效的回忆序列号。", true);
            return;
        }
        if (!GameTest.IsMemoryIndexAvailable(index))
        {
            ShowStatus("status_demo_unavailable", "Demo 模式仅开放序章、第一章和第二章。", true);
            return;
        }
        if (target.IsInteractiveSceneEntry && !GameDataManager.Instance.IsMemoryRepair02Installed)
        {
            ShowStatus("status_upgrade_required", "需要回忆升级 #2，请先选择“交互插画前”。", true);
            return;
        }
        Test.JumpToMemoryIndex(index);
        awaitingJump = Test.IsMemoryJumpRunning;
        ShowJumpStatus();
    }
    public void SelectCheckpoint(GameTestCheckpoint checkpoint, int heartTalkReleaseChoice = 1)
    {
        if (Test == null || IsJumpRunning) return;
        languageLayer.SetActive(false);
        awaitingJump = Test.JumpToCheckpoint(checkpoint, heartTalkReleaseChoice);
        ShowJumpStatus();
    }
    void ShowJumpStatus()
    {
        OSLocalizedText.SetRuntime(status, Test.JumpStatusKey, Test.JumpStatusFallback, Test.JumpStatusArguments);
        CleanFont(status);status.color = Paper;
        statusDot.color = IsJumpRunning || Test.JumpSucceeded ? Accent : new Color32(199, 148, 134, 255);
    }
    void ShowStatus(string suffix, string fallback, bool error = false)
    {
        OSLocalizedText.SetRuntime(status, Prefix + suffix, fallback);
        CleanFont(status);status.color = error ? Paper : Muted;
        statusDot.color = error ? new Color32(199, 148, 134, 255) : Accent;
    }
    LanguageState CurrentLanguage => GameDataManager.Instance != null
        ? GameDataManager.Instance.CurrentLanguageState : LanguageState.zh_CN;
    void SelectLanguage(LanguageState language)
    {
        GameDataManager.Instance?.SetCurrentLanguage(language);
        languageLayer.SetActive(false);
        EventSystem.current?.SetSelectedGameObject(null);
    }
    void RefreshLanguage(LanguageState language)
    {
        if (root == null) return;
        var data = GameDataManager.Instance != null ? GameDataManager.Instance.LocalizationData : null;
        foreach (var binding in bindings)
        {
            binding.Refresh(data, language);
            CleanFont(binding.TargetText);
        }
        int selected = Array.IndexOf(Languages, language);
        languageName.text = selected >= 0 ? LanguageNames[selected] : LanguageNames[0];
        ApplyNativeFont(languageName, language);
        for (int i = 0; i < nativeLanguageLabels.Count; i++)
        {
            ApplyNativeFont(nativeLanguageLabels[i], Languages[i]);
            languageMarkers[i].enabled = i == selected;
        }
        foreach (var item in groupHeadings)
        {
            float labelWidth = Mathf.Min(300f, Mathf.Ceil(item.Key.preferredWidth) + 2f);
            item.Key.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, labelWidth);
            item.Value.anchoredPosition = new Vector2(Margin + labelWidth + 14f, item.Value.anchoredPosition.y);
            item.Value.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, ContentWidth - labelWidth - 14f);
        }
    }
    void ApplyNativeFont(TMP_Text label, LanguageState language)
    {
        var data = GameDataManager.Instance != null ? GameDataManager.Instance.LocalizationData : null;
        label.font = language == LanguageState.ko && data?.KoreanFont != null ? data.KoreanFont : font;
        CleanFont(label);
    }
    void CleanFont(TMP_Text label)
    {
        if (label == null || label.font == null) return;
        if (!cleanMaterials.TryGetValue(label.font, out var material))
        {
            material = new Material(label.font.material) { name = "Console / " + label.font.name };
            material.SetFloat("_OutlineWidth", 0f);material.SetFloat("_FaceDilate", 0f);
            material.DisableKeyword("UNDERLAY_ON");material.DisableKeyword("UNDERLAY_INNER");
            cleanMaterials.Add(label.font, material);
        }
        label.fontSharedMaterial = material;
    }

    void BuildWindow()
    {
        font = TMP_Settings.defaultFontAsset;
        foreach (var candidate in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            if (candidate.name == "WenYuanRoundedSCVF_SDF") { font = candidate;break; }
        root = new GameObject("GameTestConsoleCanvas", typeof(RectTransform), typeof(Canvas),
            typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
        root.transform.SetParent(transform, false);
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;canvas.sortingOrder = 32760;
        var scaler = root.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1120, 630);
        scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.Expand;
        var backdrop = Rect("Backdrop", root.transform);Stretch(backdrop);
        Image(backdrop, new Color(0, 0, 0, .64f), true);
        panel = Rect("Window", backdrop);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(.5f, .5f);
        panel.sizeDelta = new Vector2(Width, Height);Image(panel, Ink, true);
        Outline(panel, Width, Height, new Color32(120, 116, 110, 255));
        Rule(panel, "HeaderRule", 1, 40, Width - 2, 1);
        Label(panel, "Prompt", ">_", 22, 8, 26, 26, 16, Paper);
        LocalizedLabel(panel, "Title", "title", "测试控制台", 57, 8, 326, 26, 17);
        var languageButton = Button(panel, "LanguageButton", 402, 5, 154, 30, () =>
        {
            indexInput.DeactivateInputField();
            languageLayer.SetActive(!languageLayer.activeSelf);
        });
        languageName = Label(languageButton, "LanguageName", "简体中文", 8, 0, 125, 30, 12, Muted);
        Chevron(languageButton, 140, 14, true);
        Rule(panel, "CloseSeparator", 566, 13, 1, 14);
        var close = Button(panel, "CloseButton", 578, 5, 28, 30, () => SetOpen(false));
        PixelClose(close, 13, 14);
        var content = BuildScrollArea();
        LocalizedLabel(content, "ChooseStart", "choose_start", "选择起点", Margin, 54 - ControlsTop, ContentWidth, 24, 16);
        Group(content, "Prologue", "prologue", "序章", 88 - ControlsTop);
        CheckpointRow(content, "01", "序章_心脏治疗", "heart_heal", "心脏治疗", 110 - ControlsTop, GameTestCheckpoint.PrologueHeartHeal);
        CheckpointRow(content, "02", "序章_故障", "fault_event", "故障事件", 144 - ControlsTop, GameTestCheckpoint.PrologueFault);
        Group(content, "Chapter01", "chapter01", "第一章", 195 - ControlsTop);
        CheckpointRow(content, "03", "第一章_工具箱解锁前", "before_tool_unlock", "工具箱解锁前", 217 - ControlsTop, GameTestCheckpoint.Chapter01BeforeToolUnlock);
        CheckpointRow(content, "04", "第一章_交互插画前", "before_picture", "交互插画前", 251 - ControlsTop, GameTestCheckpoint.Chapter01BeforeInteractivePicture);
        CheckpointRow(content, "05", "第一章_结尾动画", "ending_animation", "结尾动画", 285 - ControlsTop, GameTestCheckpoint.Chapter01EndingAnimation);
        chapter02Section = At(content, "Chapter02Shortcuts", 0, 335 - ControlsTop, Width, Chapter02ShortcutsHeight);
        Group(chapter02Section, "Chapter02", "chapter02", "第二章", 0);
        CheckpointRow(chapter02Section, "06", "第二章_开始", "chapter02_start", "开场", 22, GameTestCheckpoint.Chapter02Start);
        CheckpointRow(chapter02Section, "07", "第二章_三号升级后", "chapter02_after_upgrade03", "升级 #3 后", 56, GameTestCheckpoint.Chapter02AfterUpgrade03);
        CheckpointRow(chapter02Section, "08", "第二章_抓娃娃机", "checkpoint_chapter02_claw_machine", "抓娃娃机", 90, GameTestCheckpoint.Chapter02ClawMachine);
        CheckpointRow(chapter02Section, "09", "第二章_鼠标测试完毕", "checkpoint_chapter02_cursor_experiment_end", "鼠标测试后", 124, GameTestCheckpoint.Chapter02CursorExperimentEnd);
        CheckpointRow(chapter02Section, "10", "第二章_回家", "checkpoint_chapter02_return_home", "回家", 158, GameTestCheckpoint.Chapter02ReturnHome);
        CheckpointRow(chapter02Section, "11", "第二章_章末", "checkpoint_chapter02_hidden_memory", "隐藏回忆", 192, GameTestCheckpoint.Chapter02HiddenMemory);
        CheckpointRow(chapter02Section, "12", "第二章_章末谈心", "checkpoint_chapter02_final_talk", "章末谈心", 226, GameTestCheckpoint.Chapter02FinalTalk);
        chapter03Section = At(content, "Chapter03Shortcuts", 0, 0, Width, Chapter03ShortcutsHeight);
        Group(chapter03Section, "Chapter03", "chapter03", "第三章", 0);
        CheckpointRow(chapter03Section, "13", "第三章_Demo结束点", "checkpoint_chapter03_demo_end", "Demo 结尾", 22, GameTestCheckpoint.Chapter03DemoEnd);
        CheckpointRow(chapter03Section, "14", "第三章_回忆区开场", "checkpoint_chapter03_memory_start", "回忆开场", 56, GameTestCheckpoint.Chapter03MemoryStart);
        CheckpointRow(chapter03Section, "15", "第三章_妈妈卧室", "checkpoint_chapter03_bedroom", "妈妈卧室", 90, GameTestCheckpoint.Chapter03Bedroom);
        CheckpointRow(chapter03Section, "16", "第三章_实验室门口", "checkpoint_chapter03_lab_entrance", "实验室门口", 124, GameTestCheckpoint.Chapter03LabEntrance);
        CheckpointRow(chapter03Section, "17", "第三章_遗书关闭后", "checkpoint_chapter03_letter_closed", "遗书关闭后", 158, GameTestCheckpoint.Chapter03LetterClosed);
        CheckpointRow(chapter03Section, "18", "第三章_遗书关闭前", "checkpoint_chapter03_before_letter_close", "遗书关闭前", 192, GameTestCheckpoint.Chapter03BeforeLetterClose);
        chapter04Section = At(content, "Chapter04Shortcuts", 0, 0, Width, Chapter04ShortcutsHeight);
        Group(chapter04Section, "Chapter04", "chapter04", "第四章", 0);
        CheckpointRow(chapter04Section, "19", "第四章_TV与Live正文", "checkpoint_chapter04_story_start", "TV 对话开场", 22, GameTestCheckpoint.Chapter04StoryStart);
        CheckpointRow(chapter04Section, "20", "第四章_最终心脏治疗", "checkpoint_chapter04_final_heart_heal", "最终心脏治疗", 56, GameTestCheckpoint.Chapter04FinalHeartHeal);
        CheckpointRow(chapter04Section, "21", "第四章_最终治疗妈妈前", "checkpoint_chapter04_final_mother", "妈妈音符前", 90, GameTestCheckpoint.Chapter04FinalMother);
        memorySection = At(content, "MemoryJump", 0, 335 - ControlsTop, Width, 80);
        Rule(memorySection, "MemoryRule", Margin, 0, ContentWidth, 1);
        LocalizedLabel(memorySection, "MemoryHeading", "memory_jump", "回忆跳转", Margin, 12, ContentWidth, 22, 14);
        var field = At(memorySection, "MemoryIndex", Margin, 42, 444, 34);
        var inputBackground = Image(field, new Color32(13, 13, 12, 255), true);
        Outline(field, 444, 34, Muted);
        var viewport = Rect("TextArea", field);Stretch(viewport, 12, 4);
        viewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
        var inputText = Label(viewport, "InputText", "", 0, 0, 420, 26, 15, Paper);
        Stretch(inputText.rectTransform);inputText.enableAutoSizing = false;
        inputText.overflowMode = TextOverflowModes.Overflow;
        var placeholder = LocalizedLabel(viewport, "Placeholder", "memory_placeholder", "例如 1270", 0, 0, 420, 26, 13);
        placeholder.color = Muted;Stretch(placeholder.rectTransform);
        indexInput = field.gameObject.AddComponent<TMP_InputField>();
        indexInput.targetGraphic = inputBackground;indexInput.textViewport = viewport;
        indexInput.textComponent = inputText;indexInput.placeholder = placeholder;
        indexInput.contentType = TMP_InputField.ContentType.IntegerNumber;
        indexInput.characterLimit = 10;indexInput.lineType = TMP_InputField.LineType.SingleLine;
        indexInput.customCaretColor = true;indexInput.caretColor = Paper;indexInput.caretWidth = 1;
        indexInput.selectionColor = new Color(.55f, .53f, .49f, .35f);
        indexInput.onSubmit.AddListener(_ => SubmitIndex());
        var submit = Button(memorySection, "SubmitButton", 480, 42, 116, 34, SubmitIndex, true);
        var submitLabel = LocalizedLabel(submit, "Label", "jump", "跳转", 8, 0, 100, 34, 14);
        submitLabel.alignment = TextAlignmentOptions.Center;submitLabel.color = Ink;
        statusDot = Image(At(panel, "StatusDot", Margin, 440, 5, 5), Accent);
        status = LocalizedLabel(panel, "Status", "status_ready", "就绪", 39, 424, 315, 38, 12);
        status.textWrappingMode = TextWrappingModes.Normal;status.fontSizeMin = 10;status.color = Muted;
        BuildFastMemoryToggle();
        Rule(panel, "FooterRule", 1, 474, Width - 2, 1);
        var note = LocalizedLabel(panel, "TestSessionNote", "test_session_note", "测试跳转不会写入存档", Margin, 480, 363, 20, 10);
        note.color = Muted;note.fontSizeMin = 9;
        var shortcut = LocalizedLabel(panel, "Shortcut", "shortcut_hint", "~ 开关 · Esc 关闭", 397, 480, 199, 20, 10);
        shortcut.alignment = TextAlignmentOptions.MidlineRight;shortcut.color = Muted;shortcut.fontSizeMin = 9;
        BuildLanguages();RefreshCheckpointAvailability();RefreshLanguage(CurrentLanguage);
        root.SetActive(false);
    }

    void BuildFastMemoryToggle()
    {
        var row = At(panel, "FastMemoryToggle", 378, 427, 218, 34);
        Image(row, Color.clear, true);
        var box = At(row, "Box", 8, 10, 14, 14);
        var background = Image(box, Color.white);
        Outline(box, 14, 14, Muted);
        var check = Image(At(box, "Check", 3, 3, 8, 8), Accent);
        fastMemoryToggle = row.gameObject.AddComponent<UnityEngine.UI.Toggle>();
        fastMemoryToggle.targetGraphic = background;fastMemoryToggle.graphic = check;
        fastMemoryToggle.toggleTransition = UnityEngine.UI.Toggle.ToggleTransition.None;
        var colors = fastMemoryToggle.colors;
        colors.normalColor = Ink;colors.highlightedColor = new Color32(40, 39, 35, 255);
        colors.pressedColor = new Color32(57, 54, 48, 255);colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = Ink;colors.fadeDuration = .08f;fastMemoryToggle.colors = colors;
        var navigation = fastMemoryToggle.navigation;
        navigation.mode = UnityEngine.UI.Navigation.Mode.None;fastMemoryToggle.navigation = navigation;
        LocalizedLabel(row, "Label", "fast_memory", "回忆快进 (Ctrl)", 32, 0, 186, 34, 12);
        fastMemoryToggle.onValueChanged.AddListener(value =>
        {
            if (setup != null) setup.IsOpenFastMemory = value;
            RefreshFastMemoryToggle();
        });
        RefreshFastMemoryToggle();
    }

    void RefreshFastMemoryToggle()
    {
        if (fastMemoryToggle == null) return;
        if (setup == null) setup = FindFirstObjectByType<GameSetup>();
        fastMemoryToggle.interactable = setup != null;
        // Inspector edits and console clicks share the same runtime flag.
        fastMemoryToggle.SetIsOnWithoutNotify(setup != null && setup.IsOpenFastMemory);
    }

    RectTransform BuildScrollArea()
    {
        var area = At(panel, "Controls", 0, ControlsTop, Width, ControlsHeight);
        controls = area.gameObject.AddComponent<CanvasGroup>();
        checkpointScroll = area.gameObject.AddComponent<OSSmoothScrollRect>();
        var viewport = At(area, "Viewport", 0, 0, Width - 16, ControlsHeight);
        Image(viewport, Color.clear, true);
        viewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
        scrollContent = Rect("Content", viewport);
        scrollContent.anchorMin = new Vector2(0, 1);
        scrollContent.anchorMax = Vector2.one;
        scrollContent.pivot = new Vector2(0, 1);
        scrollContent.sizeDelta = new Vector2(0, ControlsHeight);
        // Existing rows use authored fixed coordinates; this layout element
        // supplies their total height without introducing a second row layout.
        scrollLayout = scrollContent.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
        var fitter = scrollContent.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
        fitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
        var track = At(area, "VerticalScrollbar", Width - 14, 4, 8, ControlsHeight - 8);
        Image(track, Line, true);
        var sliding = Rect("SlidingArea", track);Stretch(sliding, 1, 1);
        var handle = Rect("Handle", sliding);Stretch(handle);
        var handleImage = Image(handle, Muted, true);
        var scrollbar = track.gameObject.AddComponent<UnityEngine.UI.Scrollbar>();
        scrollbar.direction = UnityEngine.UI.Scrollbar.Direction.BottomToTop;
        scrollbar.handleRect = handle;scrollbar.targetGraphic = handleImage;
        var navigation = scrollbar.navigation;
        navigation.mode = UnityEngine.UI.Navigation.Mode.None;scrollbar.navigation = navigation;
        checkpointScroll.viewport = viewport;checkpointScroll.content = scrollContent;
        checkpointScroll.horizontal = false;checkpointScroll.vertical = true;
        checkpointScroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
        checkpointScroll.inertia = false;checkpointScroll.scrollSensitivity = 30f;
        checkpointScroll.verticalScrollbar = scrollbar;
        checkpointScroll.verticalScrollbarVisibility = UnityEngine.UI.ScrollRect.ScrollbarVisibility.AutoHide;
        return scrollContent;
    }

    void RefreshCheckpointAvailability()
    {
        bool chapter02 = GameTest.IsCheckpointAvailable(GameTestCheckpoint.Chapter02Start);
        bool chapter03 = GameTest.IsCheckpointAvailable(GameTestCheckpoint.Chapter03MemoryStart);
        bool chapter04 = GameTest.IsCheckpointAvailable(GameTestCheckpoint.Chapter04StoryStart);
        bool changed = chapter02Available != chapter02 || chapter03Available != chapter03
            || chapter04Available != chapter04;
        chapter02Available = chapter02;chapter03Available = chapter03;chapter04Available = chapter04;
        chapter02Section.gameObject.SetActive(chapter02);
        chapter03Section.gameObject.SetActive(chapter03);
        chapter04Section.gameObject.SetActive(chapter04);
        float contentHeight = 335 - ControlsTop + (chapter02 ? Chapter02ShortcutsHeight + 16 : 0);
        chapter03Section.anchoredPosition = new Vector2(0, -contentHeight);
        if (chapter03) contentHeight += Chapter03ShortcutsHeight + 16;
        chapter04Section.anchoredPosition = new Vector2(0, -contentHeight);
        if (chapter04) contentHeight += Chapter04ShortcutsHeight + 16;
        memorySection.anchoredPosition = new Vector2(0, -contentHeight);
        contentHeight += 80;
        scrollLayout.preferredHeight = contentHeight;
        scrollContent.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, contentHeight);
        if (changed)
        {
            checkpointScroll.StopMovement();
            scrollContent.anchoredPosition = Vector2.zero;
        }
    }
    void BuildLanguages()
    {
        var layer = Rect("LanguagePicker", panel);Stretch(layer);languageLayer = layer.gameObject;
        var catcher = Image(layer, Color.clear, true);
        var dismiss = layer.gameObject.AddComponent<UnityEngine.UI.Button>();
        dismiss.targetGraphic = catcher;dismiss.transition = UnityEngine.UI.Selectable.Transition.None;
        dismiss.onClick.AddListener(() => languageLayer.SetActive(false));
        var popup = At(layer, "Options", 306, 42, 290, 182);
        Image(popup, new Color32(23, 23, 21, 255), true);Outline(popup, 290, 182, Muted);
        for (int i = 0; i < Languages.Length; i++)
        {
            var language = Languages[i];
            var option = Button(popup, "Language_" + language, 8 + i / 5 * 140, 8 + i % 5 * 33, 134, 33,
                () => SelectLanguage(language));
            languageMarkers.Add(Image(At(option, "Selected", 5, 14, 4, 4), Accent));
            nativeLanguageLabels.Add(Label(option, "NativeName", LanguageNames[i], 16, 0, 115, 33, 12, Paper));
        }
        languageLayer.SetActive(false);
    }
    void Group(Transform parent, string name, string suffix, string fallback, float y)
    {
        var heading = LocalizedLabel(parent, name, suffix, fallback, Margin, y, 300, 16, 11);
        heading.color = Muted;heading.enableAutoSizing = false;
        var line = Rule(parent, name + "Rule", Margin + 70, y + 9, ContentWidth - 70, 1);
        groupHeadings.Add(new KeyValuePair<TMP_Text, RectTransform>(heading, line));
    }
    void CheckpointRow(Transform parent, string number, string name, string suffix, string fallback, float y,
        GameTestCheckpoint checkpoint, bool fullKey = false)
    {
        var row = Button(parent, name, Margin, y, ContentWidth, 34, () => SelectCheckpoint(checkpoint));
        Label(row, "Number", number, 12, 0, 32, 34, 11, Muted);
        LocalizedLabel(row, "Title", suffix, fallback, 53, 0, ContentWidth - 91, 34, 15, fullKey);
        Chevron(row, ContentWidth - 21, 16, false);
        Rule(row, "RowRule", 0, 33, ContentWidth, 1);
    }
    TMP_Text LocalizedLabel(Transform parent, string name, string suffix, string fallback,
        float x, float y, float w, float h, float size, bool fullKey = false)
    {
        var label = Label(parent, name, fallback, x, y, w, h, size, Paper);
        var binding = label.gameObject.AddComponent<OSLocalizedText>();
        binding.SetLocalization(fullKey ? suffix : Prefix + suffix, fallback);bindings.Add(binding);
        return label;
    }
    TMP_Text Label(Transform parent, string name, string text, float x, float y, float w, float h, float size, Color color)
    {
        var label = At(parent, name, x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
        label.font = font;CleanFont(label);label.text = text;label.color = color;
        label.fontSize = size;label.enableAutoSizing = true;label.fontSizeMax = size;label.fontSizeMin = size - 2f;
        label.textWrappingMode = TextWrappingModes.NoWrap;label.overflowMode = TextOverflowModes.Overflow;
        label.richText = false;label.raycastTarget = false;label.alignment = TextAlignmentOptions.MidlineLeft;
        return label;
    }
    RectTransform Button(Transform parent, string name, float x, float y, float w, float h,
        UnityEngine.Events.UnityAction action, bool filled = false)
    {
        var rect = At(parent, name, x, y, w, h);
        var image = Image(rect, Color.white, true);
        var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();button.targetGraphic = image;
        var colors = button.colors;
        colors.normalColor = filled ? new Color32(165, 160, 150, 255) : Ink;
        colors.highlightedColor = filled ? Paper : new Color32(40, 39, 35, 255);
        colors.pressedColor = filled ? Muted : new Color32(57, 54, 48, 255);
        colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = filled ? new Color32(66, 64, 60, 255) : Ink;
        colors.fadeDuration = .08f;button.colors = colors;button.onClick.AddListener(action);
        var navigation = button.navigation;navigation.mode = UnityEngine.UI.Navigation.Mode.None;button.navigation = navigation;
        return rect;
    }
    static void PixelClose(Transform parent, int x, int y)
    {
        for (int i = -3; i <= 3; i++)
        {
            Image(At(parent, "CrossA", x + i, y + i, 1, 1), Paper);
            if (i != 0) Image(At(parent, "CrossB", x + i, y - i, 1, 1), Paper);
        }
    }
    static void Chevron(Transform parent, float x, float y, bool down)
    {
        for (int i = 0; i < 4; i++)
        {
            Image(At(parent, "ArrowA", x + (down ? i - 3 : i), y + (down ? i : i - 3), 1, 1), Muted);
            if (i < 3) Image(At(parent, "ArrowB", x + (down ? 3 - i : i), y + (down ? i : 3 - i), 1, 1), Muted);
        }
    }
    static void Outline(Transform parent, float w, float h, Color color)
    {
        Image(At(parent, "Top", 0, 0, w, 1), color);Image(At(parent, "Bottom", 0, h - 1, w, 1), color);
        Image(At(parent, "Left", 0, 1, 1, h - 2), color);Image(At(parent, "Right", w - 1, 1, 1, h - 2), color);
    }
    static RectTransform Rule(Transform parent, string name, float x, float y, float w, float h)
    { var rect = At(parent, name, x, y, w, h);Image(rect, Line);return rect; }
    static UnityEngine.UI.Image Image(RectTransform rect, Color color, bool hit = false)
    { var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();image.color = color;image.raycastTarget = hit;return image; }
    static RectTransform Rect(string name, Transform parent)
    { var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();rect.SetParent(parent, false);return rect; }
    static RectTransform At(Transform parent, string name, float x, float y, float w, float h)
    {
        var rect = Rect(name, parent);rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y);rect.sizeDelta = new Vector2(w, h);return rect;
    }
    static void Stretch(RectTransform rect, float x = 0, float y = 0)
    { rect.anchorMin = Vector2.zero;rect.anchorMax = Vector2.one;rect.offsetMin = new Vector2(x, y);rect.offsetMax = new Vector2(-x, -y); }
}
