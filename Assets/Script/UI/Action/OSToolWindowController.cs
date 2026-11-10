using System;
using System.Collections.Generic;
using MrTGame.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class OSToolWindowController : MonoBehaviour, ICanvasElement
{
    public const int InitialPoolCapacity = 20;
    const float BottomTolerance = 1f;
    const string Prompt = "Tool:\\> ";
    const int BootLinesPerBatch = 4;
    static readonly string[] BootLines =
    {
        "[boot] session.start /local",
        "[read] tool.manifest ... 0x0000",
        "[check] manifest.signature ... OK",
        "[alloc] session.buffer ... OK",
        "[load] tool.kernel /segment:00",
        "[load] tool.kernel /segment:01",
        "[load] tool.kernel /segment:02",
        "[check] kernel.integrity ... OK",
        "[map] runtime.heap ... 0x0400",
        "[map] runtime.stack ... 0x0800",
        "[bind] runtime.dispatch ... OK",
        "[check] runtime.state ... OK",
        "[load] tool.file_service",
        "[bind] file.reader ... OK",
        "[bind] file.writer ... OK",
        "[check] file.handles ... OK",
        "[load] tool.crypto",
        "[read] crypto.providers ... 03",
        "[check] crypto.self_test ... OK",
        "[bind] crypto.dispatch ... OK",
        "[load] tool.archive",
        "[bind] archive.reader ... OK",
        "[bind] archive.index ... OK",
        "[check] archive.checksum ... OK",
        "[probe] memory.interface",
        "[read] interface.descriptor ... OK",
        "[bind] memory.transport ... OK",
        "[link] memory.interface ... OK",
        "[scan] local.devices /pass:01",
        "[scan] local.devices /pass:02",
        "[read] device.registry ... OK",
        "[check] device.routes ... OK",
        "[open] workspace.volume",
        "[read] workspace.header ... OK",
        "[map] workspace.blocks ... OK",
        "[mount] workspace ... OK",
        "[alloc] io.read_buffer ... 0x1000",
        "[alloc] io.write_buffer ... 0x2000",
        "[flush] io.pending ... 00",
        "[check] io.queue ... OK",
        "[load] tool.command_parser",
        "[bind] command.help ... OK",
        "[bind] command.clear ... OK",
        "[check] command.table ... OK",
        "[open] console.output",
        "[bind] console.text ... OK",
        "[bind] console.viewport ... OK",
        "[check] console.layout ... OK",
        "[open] console.input",
        "[bind] input.keyboard ... OK",
        "[alloc] input.history ... OK",
        "[check] input.queue ... OK",
        "[scan] session.references",
        "[check] session.permissions ... OK",
        "[check] session.dependencies ... OK",
        "[check] local.session ... OK",
        "[sync] workspace.index ... OK",
        "[sync] runtime.services ... OK",
        "[release] bootstrap.buffer ... OK",
        "[flush] bootstrap.log ... OK",
        "[check] tool.status ... ONLINE",
        "[bind] console.session ... OK",
        "[boot] initialization.complete",
        "[ready] Tool"
    };

    [Header("References")]
    [SerializeField] ScrollRect scrollRect;
    [SerializeField] RectTransform viewport;
    [SerializeField] RectTransform textGroup;
    [SerializeField] Scrollbar verticalScrollbar;
    [SerializeField] OSToolLineUnit lineTemplate;
    [SerializeField] RectTransform inputRow;
    [SerializeField] TMP_InputField commandInput;
    [SerializeField] OSToolProgramLibrary programLibrary;
    [SerializeField] OSToolDecorationController decoration;

    [Header("Initialization")]
    [SerializeField, Min(0.1f)] float initializationDuration = 3f;
    [SerializeField, Min(0.1f)] float initializationSpeed = 1.4f;

    [Header("Scrolling")]
    [SerializeField, Min(1f)] float scrollSensitivity = 20f;

    readonly List<OSToolLineUnit> pooledUnits =
        new List<OSToolLineUnit>(InitialPoolCapacity);

    OSWindowUnit boundWindow;
    OS_Handle boundOS;
    int nextUnitIndex;
    bool isPrepared;
    bool hasWarnedMissingTemplate;
    bool followLatest = true;
    bool scrollRefreshPending;
    bool hasScrollGeometry;
    float lastContentHeight;
    Vector2 lastViewportSize;
    readonly List<string> commandHistory = new List<string>();
    int historyIndex;
    string historyDraft = string.Empty;
    float initializationElapsed;
    int nextBootLine;
    OSToolBootShroud bootShroud;
    bool focusRequested;
    OSToolLineUnit titleLine;
    OSToolLineUnit descriptionLine;

    public int Capacity => pooledUnits.Count;
    public int ActiveLineCount => nextUnitIndex;
    public IReadOnlyList<OSToolLineUnit> PooledUnits => pooledUnits;
    public bool IsInitializing { get; private set; }
    public bool IsReady { get; private set; }
    public TMP_InputField CommandInput => commandInput;
    public RectTransform OutputRoot => textGroup;
    public OSToolProgramLibrary ProgramLibrary => programLibrary;
    public OSToolDecorationController Decoration => decoration;
    bool IsBusy => (programLibrary != null && (programLibrary.IsInstalling || programLibrary.IsUnpacking))
        || (decoration != null && decoration.IsAnimating);
    public IReadOnlyList<string> CommandHistory => commandHistory;
    public event Action InitializationCompleted;
    public event Action<string> CommandSubmitted;

    void Awake()
    {
        EnsurePrepared();
    }

    void OnEnable()
    {
        EnsurePrepared();
        if (scrollRect != null)
            scrollRect.onValueChanged.AddListener(HandleScrollChanged);
        if (commandInput != null)
            commandInput.onSubmit.AddListener(HandleCommandSubmitted);
        boundOS = OS_Handle.Instance;
        if (boundOS != null)
            boundOS.MouseActiveWindowChanged += HandleMouseActiveWindowChanged;
        GameEvents.LanguageChanged += HandleLanguageChanged;
        RefreshIntroduction();
        RequestScrollRefresh();
    }

    void OnDisable()
    {
        if (scrollRect != null)
            scrollRect.onValueChanged.RemoveListener(HandleScrollChanged);
        if (commandInput != null)
            commandInput.onSubmit.RemoveListener(HandleCommandSubmitted);
        if (boundOS != null)
            boundOS.MouseActiveWindowChanged -= HandleMouseActiveWindowChanged;
        boundOS = null;
        GameEvents.LanguageChanged -= HandleLanguageChanged;
        HideBootShroud(false);
        focusRequested = false;
        if (boundWindow != null && !boundWindow.IsOpen)
            ResetSession();
        CanvasUpdateRegistry.UnRegisterCanvasElementForRebuild(this);
    }

    void OnDestroy()
    {
        if (boundWindow != null)
        {
            boundWindow.Closed -= HandleWindowClosed;
            boundWindow.OpenStarted -= HandleContentInitializationRequested;
            boundWindow.ContentInitializationRequested -= HandleContentInitializationRequested;
        }
        if (bootShroud != null)
            Destroy(bootShroud.gameObject);
    }

    public void PrepareRuntime()
    {
        ResolveReferences();
        BindWindow();
        ConfigureScrollRect();
        isPrepared = BuildPool();
        if (!isPrepared)
            return;

        ResetSession();
    }

    public OSToolLineUnit AppendText(string text, bool richText = true)
    {
        if (!EnsurePrepared())
            return null;

        OSToolLineUnit unit = AcquireUnit();
        if (unit == null)
            return null;

        unit.transform.SetAsLastSibling();
        unit.BindText(text);
        if (unit.Label != null)
            unit.Label.richText = richText;
        KeepInputLast();
        RequestScrollRefresh();
        return unit;
    }

    public OSToolLineUnit AppendImage(
        Sprite sprite,
        string caption = null,
        float preferredHeight = -1f)
    {
        if (!EnsurePrepared())
            return null;

        OSToolLineUnit unit = AcquireUnit();
        if (unit == null)
            return null;

        unit.transform.SetAsLastSibling();
        unit.BindImage(sprite, caption, preferredHeight);
        if (unit.Label != null)
            unit.Label.richText = true;
        KeepInputLast();
        RequestScrollRefresh();
        return unit;
    }

    public void Clear()
    {
        if (!EnsurePrepared())
            return;

        programLibrary?.ClearOutput();
        ClearPool();
        titleLine = null;
        descriptionLine = null;
        ResetScroll();
    }

    bool EnsurePrepared()
    {
        if (!isPrepared)
            PrepareRuntime();
        return isPrepared;
    }

    void BindWindow()
    {
        OSWindowUnit window = GetComponent<OSWindowUnit>();
        if (boundWindow == window)
            return;

        if (boundWindow != null)
        {
            boundWindow.Closed -= HandleWindowClosed;
            boundWindow.OpenStarted -= HandleContentInitializationRequested;
            boundWindow.ContentInitializationRequested -= HandleContentInitializationRequested;
        }
        boundWindow = window;
        if (boundWindow != null)
        {
            boundWindow.Closed += HandleWindowClosed;
            boundWindow.OpenStarted += HandleContentInitializationRequested;
            boundWindow.ContentInitializationRequested += HandleContentInitializationRequested;
        }
    }

    void HandleWindowClosed(OSWindowUnit window)
    {
        // Keep this subscription while minimized: closing from the taskbar
        // must reset the session even when the window is already inactive.
        ResetSession();
    }

    void HandleContentInitializationRequested(OSWindowUnit window)
    {
        if (!EnsurePrepared() || IsInitializing || IsReady)
            return;

        ResetSession();
        if (GameDataManager.Instance != null && GameDataManager.Instance.HasToolInitialized)
        {
            FinishInitialization();
            return;
        }
        IsInitializing = true;
        AppendText(Prompt + "tool --initialize");
        AppendNextBootBatch();
    }

    void AppendNextBootBatch()
    {
        int end = Mathf.Min(nextBootLine + BootLinesPerBatch, BootLines.Length);
        while (nextBootLine < end)
            AppendText(BootLines[nextBootLine++]);
    }

    void Update()
    {
        bool visible = OS_Handle.Instance == null || OS_Handle.Instance.IsOSVisible;
        if (!visible)
        {
            SetPresentationVisible(false);
            return;
        }

        if (!IsInitializing)
            return;

        float delta = Time.unscaledDeltaTime;
        initializationElapsed += delta * initializationSpeed;
        ShowBootShroud();

        while (nextBootLine < BootLines.Length
            && initializationElapsed >= initializationDuration * nextBootLine / BootLines.Length)
        {
            AppendNextBootBatch();
        }

        if (initializationElapsed < initializationDuration)
            return;

        FinishInitialization();
    }

    void FinishInitialization()
    {
        IsInitializing = false;
        HideBootShroud(true);
        Clear();
        titleLine = AppendText(string.Empty);
        descriptionLine = AppendText(string.Empty);
        RefreshIntroduction();
        IsReady = true;
        if (inputRow != null)
            inputRow.gameObject.SetActive(true);
        SetPresentationVisible(true);
        GameDataManager.Instance?.MarkToolInitialized();
        programLibrary?.ShowInitialContents();
        InitializationCompleted?.Invoke();
    }

    void LateUpdate()
    {
        if (commandInput != null)
            commandInput.interactable = IsReady && !IsBusy
                && (OS_Handle.Instance == null || OS_Handle.Instance.IsOSVisible);
        if (!CanAcceptInput())
        {
            if (commandInput != null && commandInput.isFocused)
                commandInput.DeactivateInputField();
            return;
        }

        if (focusRequested)
        {
            focusRequested = false;
            commandInput.ActivateInputField();
            commandInput.caretPosition = commandInput.text.Length;
        }

        Keyboard keyboard = Keyboard.current;
        if (!commandInput.isFocused || keyboard == null || CursorHandle.Instance?.IsPointerInputSuppressed == true)
            return;

        // Leave IME candidate navigation to TMP while a composition is active.
        BaseInput input = EventSystem.current?.currentInputModule?.input;
        if (input != null && !string.IsNullOrEmpty(input.compositionString))
            return;

        if (keyboard.upArrowKey.wasPressedThisFrame)
            NavigateHistory(-1);
        else if (keyboard.downArrowKey.wasPressedThisFrame)
            NavigateHistory(1);
    }

    public void SetPresentationVisible(bool visible)
    {
        if (!visible)
        {
            HideBootShroud(false);
            focusRequested = false;
            commandInput?.DeactivateInputField();
        }
        if (commandInput != null)
            commandInput.interactable = visible && IsReady && !IsBusy;
    }

    public void RequestInputFocus()
    {
        OS_Handle os = OS_Handle.Instance;
        if (IsReady && (os == null || os.IsWindowMouseActive(boundWindow)))
            focusRequested = true;
    }

    void HandleMouseActiveWindowChanged(OSWindowUnit window)
    {
        if (window != boundWindow)
        {
            focusRequested = false;
            commandInput?.DeactivateInputField();
        }
    }

    bool CanAcceptInput()
    {
        if (GameTestConsole.BlocksGameplayInput)
            return false;
        OS_Handle os = OS_Handle.Instance;
        return IsReady && commandInput != null && isActiveAndEnabled
            && !IsBusy
            && boundWindow != null && boundWindow.IsOpen && !boundWindow.IsMinimized
            && (os == null || (os.IsOSVisible && !os.IsSystemInputLocked
                && os.IsWindowMouseActive(boundWindow)));
    }

    void HandleCommandSubmitted(string command)
    {
        SubmitCommand(command);
    }

    public bool SubmitCommand(string command)
    {
        if (!CanAcceptInput())
            return false;

        string value = (command ?? string.Empty).Trim();
        commandInput.SetTextWithoutNotify(string.Empty);
        followLatest = true;
        AppendText(Prompt + value, false);
        if (value.Length > 0)
        {
            commandHistory.Add(value);
            if (string.Equals(value, "cls", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "clear", StringComparison.OrdinalIgnoreCase))
            {
                Clear();
                programLibrary?.ShowPendingArchive();
            }
            else if (string.Equals(value, "help", StringComparison.OrdinalIgnoreCase))
            {
                AppendText(Localize("ui.os.tool.help", "help - List commands\ncls / clear - Clear the terminal\nUp / Down - Command history"));
                if (GameDataManager.Instance != null && GameDataManager.Instance.CanAccessToolPrograms)
                    AppendText(Localize("ui.os.tool.command.usage", "tool unpack --memory\ntool install <package-id>"));
            }
            else if (programLibrary != null && programLibrary.TryHandleCommand(value))
            {
                // The program library owns only the in-game package commands.
            }
            else
            {
                AppendText(Localize("ui.os.tool.unknown_command", "Error: unknown command: {0}").Replace("{0}", value), false);
            }
            CommandSubmitted?.Invoke(value);
        }

        historyIndex = commandHistory.Count;
        historyDraft = string.Empty;
        RequestInputFocus();
        return true;
    }

    public void NavigateHistory(int direction)
    {
        if (!CanAcceptInput() || CursorHandle.Instance?.IsPointerInputSuppressed == true
            || commandHistory.Count == 0 || direction == 0)
            return;

        if (historyIndex == commandHistory.Count)
            historyDraft = commandInput.text;
        historyIndex = Mathf.Clamp(historyIndex + Math.Sign(direction), 0, commandHistory.Count);
        commandInput.SetTextWithoutNotify(historyIndex < commandHistory.Count
            ? commandHistory[historyIndex] : historyDraft);
        commandInput.caretPosition = commandInput.text.Length;
        followLatest = true;
        RequestScrollRefresh();
    }

    public void FillCommand(string command)
    {
        if (!CanAcceptInput())
            return;
        commandInput.SetTextWithoutNotify(command ?? string.Empty);
        historyIndex = commandHistory.Count;
        historyDraft = commandInput.text;
        commandInput.caretPosition = commandInput.text.Length;
        followLatest = true;
        RefreshContentLayout();
        RequestInputFocus();
    }

    public void RefreshContentLayout()
    {
        KeepInputLast();
        if (textGroup != null)
            LayoutRebuilder.MarkLayoutForRebuild(textGroup);
        RequestScrollRefresh();
    }

    public void RevealLatestForScript()
    {
        followLatest = true;
        RefreshContentLayout();
        Canvas.ForceUpdateCanvases();
        if (scrollRect != null)
        {
            scrollRect.StopMovement();
            scrollRect.verticalNormalizedPosition = 0f;
        }
    }

    void ResetSession()
    {
        programLibrary?.ResetSession();
        IsInitializing = false;
        IsReady = false;
        initializationElapsed = 0f;
        nextBootLine = 0;
        focusRequested = false;
        HideBootShroud(false);
        commandHistory.Clear();
        historyIndex = 0;
        historyDraft = string.Empty;
        if (commandInput != null)
        {
            commandInput.DeactivateInputField();
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == commandInput.gameObject)
                EventSystem.current.SetSelectedGameObject(null);
            commandInput.SetTextWithoutNotify(string.Empty);
            commandInput.interactable = false;
        }
        if (inputRow != null)
            inputRow.gameObject.SetActive(false);
        titleLine = null;
        descriptionLine = null;
        ClearPool();
        ResetScroll();
    }

    // First boot dims the rest of the OS instead of reusing the prologue's
    // fault post-processing, so starting the toolbox never reads as a crash.
    void ShowBootShroud()
    {
        if (bootShroud == null)
            bootShroud = OSToolBootShroud.Create(boundWindow);
        bootShroud?.Show();
    }

    void HideBootShroud(bool fade)
    {
        if (bootShroud != null)
            bootShroud.Hide(fade);
    }

    void KeepInputLast()
    {
        if (inputRow != null)
            inputRow.SetAsLastSibling();
    }

    void HandleLanguageChanged(LanguageState language) => RefreshIntroduction();

    void RefreshIntroduction()
    {
        titleLine?.BindText(Localize("ui.os.tool.intro.title", "{0} [1.0]")
            .Replace("{0}", Localize("ui.os.app.tool.name", "Toolbox")));
        descriptionLine?.BindText(Localize("ui.os.tool.intro.description", "Local file terminal. Type help to list commands."));
        RequestScrollRefresh();
    }

    static string Localize(string key, string fallback)
    {
        return GameDataManager.Instance != null
            ? GameDataManager.Instance.GetLocalizedText(key, fallback) : fallback;
    }

    void ResolveReferences()
    {
        if (decoration == null)
            decoration = GetComponent<OSToolDecorationController>();
        if (programLibrary == null)
            programLibrary = GetComponent<OSToolProgramLibrary>();
        if (viewport == null)
            viewport = transform.Find("TextArea") as RectTransform;

        if (scrollRect == null && viewport != null)
            scrollRect = viewport.GetComponent<ScrollRect>();

        if (textGroup == null && viewport != null)
            textGroup = viewport.Find("TextGroup") as RectTransform;

        if (inputRow == null && textGroup != null)
            inputRow = textGroup.Find("InputRow") as RectTransform;
        if (commandInput == null && inputRow != null)
            commandInput = inputRow.GetComponentInChildren<TMP_InputField>(true);

        if (verticalScrollbar == null && viewport != null)
        {
            verticalScrollbar = viewport
                .Find("VerticalScrollbar")
                ?.GetComponent<Scrollbar>();
        }

        if (lineTemplate == null && textGroup != null)
        {
            OSToolLineUnit[] candidates = textGroup
                .GetComponentsInChildren<OSToolLineUnit>(true);
            for (int i = 0; i < candidates.Length; i++)
            {
                OSToolLineUnit candidate = candidates[i];
                if (candidate != null && candidate.transform.parent == textGroup)
                {
                    lineTemplate = candidate;
                    break;
                }
            }
        }
    }

    void ConfigureScrollRect()
    {
        if (scrollRect == null)
            return;

        scrollRect.content = textGroup;
        scrollRect.viewport = viewport;
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.inertia = true;
        scrollRect.scrollSensitivity = Mathf.Max(1f, scrollSensitivity);
        scrollRect.verticalScrollbar = verticalScrollbar;
        scrollRect.verticalScrollbarVisibility =
            ScrollRect.ScrollbarVisibility.Permanent;
        scrollRect.verticalScrollbarSpacing = 0f;
    }

    bool BuildPool()
    {
        pooledUnits.Clear();
        nextUnitIndex = 0;
        if (textGroup == null || lineTemplate == null)
        {
            WarnMissingTemplate();
            return false;
        }

        // Never display the template; growing the pool must clone authoring
        // state, including the original font, rather than a populated line.
        lineTemplate.Clear();
        OSToolLineUnit[] existingUnits = textGroup
            .GetComponentsInChildren<OSToolLineUnit>(true);
        for (int i = 0; i < existingUnits.Length; i++)
        {
            OSToolLineUnit unit = existingUnits[i];
            if (unit == null
                || unit == lineTemplate
                || unit.transform.parent != textGroup)
            {
                continue;
            }

            pooledUnits.Add(unit);
        }

        while (pooledUnits.Count < InitialPoolCapacity)
        {
            pooledUnits.Add(CreateUnit(pooledUnits.Count));
        }

        for (int i = 0; i < pooledUnits.Count; i++)
        {
            OSToolLineUnit unit = pooledUnits[i];
            if (unit == null)
                continue;

            unit.name = $"ToolLineUnit_{i:00}";
            unit.transform.SetSiblingIndex(i);
        }

        hasWarnedMissingTemplate = false;
        return true;
    }

    OSToolLineUnit CreateUnit(int index)
    {
        OSToolLineUnit unit = Instantiate(lineTemplate, textGroup);
        unit.name = $"ToolLineUnit_{index:00}";
        return unit;
    }

    OSToolLineUnit AcquireUnit()
    {
        if (nextUnitIndex == pooledUnits.Count)
            pooledUnits.Add(CreateUnit(nextUnitIndex));

        OSToolLineUnit unit = pooledUnits[nextUnitIndex];
        if (unit == null)
            pooledUnits[nextUnitIndex] = unit = CreateUnit(nextUnitIndex);

        nextUnitIndex++;
        return unit;
    }

    void ClearPool()
    {
        nextUnitIndex = 0;
        for (int i = 0; i < pooledUnits.Count; i++)
        {
            OSToolLineUnit unit = pooledUnits[i];
            if (unit == null)
                continue;

            unit.Clear();
        }
    }

    void ResetScroll()
    {
        followLatest = true;
        hasScrollGeometry = false;
        if (scrollRect != null)
            scrollRect.StopMovement();
        if (textGroup != null)
            textGroup.anchoredPosition = Vector2.zero;
        RequestScrollRefresh();
    }

    void OnRectTransformDimensionsChange()
    {
        if (isPrepared)
            RequestScrollRefresh();
    }

    void HandleScrollChanged(Vector2 position)
    {
        if (textGroup == null || viewport == null || scrollRect == null)
            return;

        // Layout changes also fire onValueChanged. Only unchanged geometry
        // can tell us whether the player has scrolled away from the bottom.
        if (!hasScrollGeometry
            || !Mathf.Approximately(lastContentHeight, textGroup.rect.height)
            || lastViewportSize != viewport.rect.size)
        {
            RequestScrollRefresh();
            return;
        }

        float hiddenHeight = Mathf.Max(
            0f, textGroup.rect.height - viewport.rect.height);
        followLatest = hiddenHeight <= BottomTolerance
            || scrollRect.verticalNormalizedPosition * hiddenHeight
                <= BottomTolerance;
    }

    void RequestScrollRefresh()
    {
        scrollRefreshPending = true;
        if (isActiveAndEnabled)
            CanvasUpdateRegistry.RegisterCanvasElementForLayoutRebuild(this);
    }

    void ICanvasElement.Rebuild(CanvasUpdate executing)
    {
        if (executing != CanvasUpdate.PostLayout
            || !scrollRefreshPending
            || !isActiveAndEnabled
            || scrollRect == null
            || textGroup == null
            || viewport == null)
        {
            return;
        }

        if (followLatest)
        {
            scrollRect.StopMovement();
            scrollRect.verticalNormalizedPosition = 0f;
        }

        lastContentHeight = textGroup.rect.height;
        lastViewportSize = viewport.rect.size;
        hasScrollGeometry = true;
        scrollRefreshPending = false;
    }

    void ICanvasElement.LayoutComplete()
    {
    }

    void ICanvasElement.GraphicUpdateComplete()
    {
    }

    bool ICanvasElement.IsDestroyed() => this == null;

    void WarnMissingTemplate()
    {
        if (hasWarnedMissingTemplate)
            return;

        hasWarnedMissingTemplate = true;
        Debug.LogWarning(
            "[OS Tool] TextGroup or ToolLineUnit template is missing.",
            this);
    }

    void OnValidate()
    {
        initializationSpeed = Mathf.Max(0.1f, initializationSpeed);
        scrollSensitivity = Mathf.Max(1f, scrollSensitivity);
        ResolveReferences();
        ConfigureScrollRect();
    }

    void Reset()
    {
        ResolveReferences();
        ConfigureScrollRect();
    }
}
