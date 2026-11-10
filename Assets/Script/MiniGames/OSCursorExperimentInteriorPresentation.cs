using System.Collections.Generic;
using UnityEngine;

// A frozen signal failure hides the switch from desktop to the inner OS.
// Transient effect state is never persisted: interrupted captures resume in the cage.
[DisallowMultipleComponent]
[DefaultExecutionOrder(560)]
public sealed class OSCursorExperimentInteriorPresentation : MonoBehaviour
{
    const float CorruptSeconds = .28f, FrozenSeconds = .22f, BlackoutSeconds = .20f, RecoverSeconds = .18f;
    static readonly string[] DesktopPaths = { "Bk", "DesktopProcessGroup", "ProcessGroup", "HeartBeatPanel", "Windows", "OS_VFX", "Testobj", "ClawStoryOverlay" };
    enum Stage { Dormant, Corrupting, Frozen, Blackout, Recovering, OpeningLive, Active }
    sealed class RootPose
    {
        public RectTransform rect;
        public Vector3 position, scale;
    }
    sealed class WindowPose
    {
        public OSWindowUnit window;
        public Transform parent;
        public int sibling;
        public Vector3 scale;
        public Vector2 position, size;
        public bool ownsLayout, allowsMove, allowsResize;
    }
    OS_Handle os;
    OSCursorExperimentController experiment;
    OSCursorExperimentFocusPresentation focus;
    OSCursorExperimentInteriorBackground background;
    OSCursorExperimentSignalTransition signal;
    RectTransform desktop, screenRoot, zoomRoot;
    OSWindowUnit live, settings;
    WindowPose experimentPose, livePose;
    readonly List<RootPose> roots = new List<RootPose>();
    OSMemoryProcessSaveData observed;
    Stage stage;
    float elapsed, pan, zoom = 1f;
    Vector2 capturedScreenPoint;
    bool hasFocus, skipAdvance, sawUncaptured, freshCapture;
    Canvas settingsCanvas;
    Canvas runtimeSettingsCanvas;
    UnityEngine.UI.GraphicRaycaster ownedSettingsRaycaster;
    bool ownsSettingsCanvas, settingsSortOverride, settingsCanvasEnabled;
    int settingsSortOrder, settingsSortLayer;
    Camera settingsCamera;
    Vector3 settingsHome, settingsScale;
    bool settingsLayoutOwned;

    public bool IsEngaged => stage != Stage.Dormant;
    public bool IsTransitioning => IsEngaged && stage != Stage.Active;
    public bool IsReadyForStory => stage == Stage.Active;
    public bool IsScreenObscured => stage == Stage.Corrupting || stage == Stage.Frozen
        || stage == Stage.Blackout || stage == Stage.Recovering;
    public bool HasFreshCapture => freshCapture;
    public RectTransform ScreenRoot => screenRoot;
    public RectTransform ZoomRoot => zoomRoot;
    public OSCursorExperimentInteriorBackground Background => background;

    void OnEnable() { os = GetComponent<OS_Handle>(); desktop = transform as RectTransform; hasFocus = Application.isFocused; }
    void OnApplicationFocus(bool focused) { hasFocus = focused; skipAdvance = true; }
    void OnDisable() => ResetPresentation();
    void OnDestroy()
    {
        ResetPresentation();
        if (ownedSettingsRaycaster != null) Destroy(ownedSettingsRaycaster);
        if (runtimeSettingsCanvas != null) Destroy(runtimeSettingsCanvas);
        if (screenRoot != null) Destroy(screenRoot.gameObject);
    }

    void Update()
    {
        var p = GameDataManager.Instance != null ? GameDataManager.Instance.OSProcessData.memory : null;
        if (!ReferenceEquals(observed, p))
        {
            ResetPresentation(); observed = p;
            sawUncaptured = p != null && !p.chapter02ClawCursorCaptured;
        }
        if (p != null && !p.chapter02ClawCursorCaptured) sawUncaptured = true;
        if (p != null && p.chapter02ClawCursorCaptured && sawUncaptured)
        { freshCapture = true; sawUncaptured = false; }
        if (!IsEngaged) return;
        if (p == null || !p.chapter02ClawCursorCaptured || GameDataManager.Instance.CurrentChapterIndex != 2)
        { ResetPresentation(); return; }
        bool available = Available;
        if (background != null) background.Paused = !available;
        if (!available) { skipAdvance = true; return; }
        float dt = skipAdvance ? 0f : Mathf.Min(Time.unscaledDeltaTime, .05f);
        skipAdvance = false;
        Tick(dt);
    }

    bool SettingsOpen => settings != null && settings.IsOpen && !settings.IsMinimized && settings.gameObject.activeInHierarchy;
    bool Available => hasFocus && os != null && os.IsOSVisible && !SettingsOpen
        && !GameTestConsole.BlocksGameplayInput && !OSSessionPersistence.IsRestoring
        && GameSessionFlow.Instance?.IsBusy != true && !os.IsSystemInputLockedWithoutCursorExperiment;

    public void BeginEntry(Vector2 from, bool resume)
    {
        if (IsEngaged) return;
        os = GetComponent<OS_Handle>(); desktop = transform as RectTransform;
        experiment = os.CursorExperiment;
        live = os.CharacterWindowController != null ? os.CharacterWindowController.GetComponent<OSWindowUnit>() : null;
        settings = os.transform.Find("Windows/WindowUnit_Option")?.GetComponent<OSWindowUnit>();
        if (experiment == null || desktop == null) return;
        observed = GameDataManager.Instance.OSProcessData.memory;
        focus = GetComponent<OSCursorExperimentFocusPresentation>();
        zoom = focus != null ? focus.CurrentZoom : 1f;
        focus?.ReleaseDesktopForInterior();
        CaptureRoots();
        EnsureScreen();
        PrepareSettingsOverlay();
        capturedScreenPoint = from;
        os.LinkMessageController?.NotifyOSHidden();
        os.GetComponent<OSCursorExperimentLiveReaction>()?.PausePresentation();
        elapsed = 0f; pan = resume ? 1f : 0f;
        stage = resume ? Stage.OpeningLive : Stage.Corrupting;
        freshCapture = false;
        if (resume)
        {
            MountDestination();
            OpenLive();
        }
        else
        {
            // Keep the suspended claw on the old desktop until its rendered
            // frame has been captured. The destination is completely hidden.
            screenRoot.gameObject.SetActive(false);
            ApplyComposition();
            signal = GetComponent<OSCursorExperimentSignalTransition>();
            if (signal == null) signal = gameObject.AddComponent<OSCursorExperimentSignalTransition>();
            signal.BeginCapture();
        }
        os.CursorExperimentStory?.ApplyStoryHold();
    }

    void MountDestination()
    {
        os.RetireCapturedClawForInterior();
        // The experiment (256x208) and Live (150x180) read as one centred group with
        // a 36-unit gutter and shared top edge, instead of Live hugging the right edge.
        experimentPose = MountWindow(experiment.Window, new Vector2(-93f, 4f), new Vector2(256, 208));
        if (live != null)
        {
            // A silent close preserves the current message entry and skips the
            // unrelated "Live was closed" reaction while it leaves the desktop.
            livePose = MountWindow(live, new Vector2(146f, 18f), new Vector2(150, 180));
            live.PrepareRuntimeClosed();
        }
        pan = 1f;
        screenRoot.gameObject.SetActive(true);
        background.SetVisibility(1f);
        ApplyComposition();
        bool released = observed.chapter02CursorExperimentRound >= 4;
        if (released) experiment.Window.PrepareRuntimeClosed();
        else
        {
            experiment.Window.OpenStandalone(0f);
            experiment.Window.SetAlwaysOnTop(true);
            experiment.Window.BringToFront();
        }
        // Both a restored save and the hidden fresh transition land directly
        // in the closed cage. The transfer animation is intentionally skipped.
        if (observed.chapter02CursorExperimentRound == 0)
        {
            observed.chapter02CursorExperimentRound = 1;
            observed.chapter02CursorExperimentPhase = (int)OSCursorExperimentController.ExperimentPhase.Intake;
            observed.chapter02CursorExperimentHasPosition = false;
        }
        if (!released)
        {
            experiment.Begin(capturedScreenPoint, true);
            os.CursorExperimentStory?.ApplyStoryHold();
        }
        Canvas.ForceUpdateCanvases();
    }

    void Tick(float dt)
    {
        if (stage == Stage.Corrupting)
        {
            if (signal == null || !signal.IsReady) return;
            elapsed += dt;
            float t = Mathf.Clamp01(elapsed / CorruptSeconds);
            signal.Present(Mathf.SmoothStep(0f, 1f, t), elapsed, 1f);
            if (elapsed >= CorruptSeconds)
            {
                signal.Present(1f, CorruptSeconds, 1f);
                stage = Stage.Frozen; elapsed = 0f;
                MountDestination();
            }
        }
        else if (stage == Stage.Frozen)
        {
            elapsed += dt;
            // Neither shader time nor the screenshot advances during the stall.
            if (elapsed >= FrozenSeconds)
            {
                signal.Present(0f, CorruptSeconds, 1f, 1f);
                stage = Stage.Blackout; elapsed = 0f;
            }
        }
        else if (stage == Stage.Blackout)
        {
            elapsed += dt;
            if (elapsed >= BlackoutSeconds) { stage = Stage.Recovering; elapsed = 0f; }
        }
        else if (stage == Stage.Recovering)
        {
            elapsed += dt;
            float t = Mathf.Clamp01(elapsed / RecoverSeconds);
            // Reveal only the new room from black; never dissolve the discarded
            // desktop back over the cage during recovery.
            signal.Present(0f, CorruptSeconds, 1f - Mathf.SmoothStep(0f, 1f, t), 1f);
            if (t >= 1f)
            {
                signal.ResetPresentation();
                stage = Stage.OpeningLive; elapsed = 0f;
                OpenLive();
            }
        }
        else if (stage == Stage.OpeningLive)
        {
            if (live == null || (live.IsOpen && !live.IsOpening && !live.IsClosing && live.gameObject.activeInHierarchy))
            {
                stage = Stage.Active;
                os.LinkMessageController?.NotifyOSBecameVisible();
            }
            else if (!live.IsOpening && !live.IsClosing) OpenLive();
        }
    }

    void OpenLive()
    {
        if (live == null) return;
        os.OpenCharacterWindowForReconnect(.28f);
    }

    void LateUpdate()
    {
        if (!IsEngaged) return;
        ApplyComposition();
    }

    public void SetFocusZoom(float value)
    {
        zoom = Mathf.Clamp(value, 1f, 1.2f);
        if (IsEngaged) ApplyComposition();
    }

    void CaptureRoots()
    {
        roots.Clear();
        foreach (string path in DesktopPaths)
        {
            var rect = os.transform.Find(path) as RectTransform;
            if (rect != null && rect.parent == desktop)
                roots.Add(new RootPose { rect = rect, position = rect.anchoredPosition3D, scale = rect.localScale });
        }
    }

    void EnsureScreen()
    {
        if (screenRoot != null) return;
        screenRoot = FullRect("CursorExperimentScreen", desktop);
        zoomRoot = FullRect("Presentation", screenRoot);
        background = OSCursorExperimentInteriorBackground.Create(zoomRoot);
    }

    RectTransform FullRect(string label, RectTransform parent)
    {
        var node = new GameObject(label, typeof(RectTransform)); node.layer = parent.gameObject.layer;
        var rt = (RectTransform)node.transform; rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(.5f, .5f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return rt;
    }

    WindowPose MountWindow(OSWindowUnit window, Vector2 position, Vector2 size)
    {
        var rt = window.WindowRect;
        var pose = new WindowPose { window = window, parent = rt.parent, sibling = rt.GetSiblingIndex(), scale = rt.localScale,
            position = rt.anchoredPosition, size = rt.sizeDelta, allowsMove = window.AllowsMove, allowsResize = window.AllowsResize,
            ownsLayout = !window.HasTemporaryOpeningLayout };
        if (pose.ownsLayout) window.SetTemporaryOpeningLayout(size, position, size);
        rt.SetParent(zoomRoot, false);
        rt.anchoredPosition = position; rt.localScale = pose.scale;
        window.ConfigureInteraction(false, false);
        return pose;
    }

    void ApplyComposition()
    {
        if (!IsEngaged || desktop == null || screenRoot == null) return;
        float width = desktop.rect.width;
        Vector3 centre = desktop.rect.center;
        foreach (var pose in roots)
        {
            if (pose.rect == null) continue;
            Vector3 baseLocal = pose.rect.localPosition + pose.position - pose.rect.anchoredPosition3D;
            Vector3 shift = (baseLocal - centre) * (zoom - 1f); shift.z = 0f;
            pose.rect.anchoredPosition3D = pose.position + shift + new Vector3(-width * pan, 0, 0);
            pose.rect.localScale = Vector3.Scale(pose.scale, new Vector3(zoom, zoom, 1f));
        }
        screenRoot.anchoredPosition = new Vector2(width * (1f - pan), 0);
        zoomRoot.localScale = new Vector3(zoom, zoom, 1f);
        if (SettingsOpen && settingsLayoutOwned)
        {
            // Settings retain their path and are drawn on a separate upper
            // canvas; counter the desktop travel without saving that offset.
            var rt = settings.WindowRect;
            rt.position = desktop.TransformPoint(settingsHome);
            Vector3 parentScale = rt.parent.lossyScale;
            Vector3 scale = desktop.lossyScale;
            rt.localScale = new Vector3(settingsScale.x * scale.x / parentScale.x, settingsScale.y * scale.y / parentScale.y, settingsScale.z);
        }
    }

    void PrepareSettingsOverlay()
    {
        if (settings == null) return;
        var rt = settings.WindowRect;
        settingsHome = desktop.InverseTransformPoint(rt.position);
        settingsScale = rt.localScale;
        settingsLayoutOwned = !settings.HasTemporaryOpeningLayout;
        if (settingsLayoutOwned) settings.SetTemporaryOpeningLayout(settings.RecordedSize, rt.anchoredPosition, settings.RecordedSize);
        settingsCanvas = settings.GetComponent<Canvas>();
        ownsSettingsCanvas = settingsCanvas == null || settingsCanvas == runtimeSettingsCanvas;
        if (settingsCanvas == null) runtimeSettingsCanvas = settingsCanvas = settings.gameObject.AddComponent<Canvas>();
        settingsSortOverride = settingsCanvas.overrideSorting; settingsSortOrder = settingsCanvas.sortingOrder;
        settingsSortLayer = settingsCanvas.sortingLayerID; settingsCamera = settingsCanvas.worldCamera;
        settingsCanvasEnabled = settingsCanvas.enabled;
        settingsCanvas.enabled = true;
        settingsCanvas.overrideSorting = true;
        var parentCanvas = os.GetComponent<Canvas>();
        settingsCanvas.sortingLayerID = parentCanvas.sortingLayerID;
        settingsCanvas.sortingOrder = parentCanvas.sortingOrder + 20;
        settingsCanvas.worldCamera = parentCanvas.worldCamera;
        if (settings.GetComponent<UnityEngine.UI.GraphicRaycaster>() == null)
        {
            ownedSettingsRaycaster = settings.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            var source = parentCanvas.GetComponent<UnityEngine.UI.GraphicRaycaster>();
            if (source != null)
            {
                ownedSettingsRaycaster.ignoreReversedGraphics = source.ignoreReversedGraphics;
                ownedSettingsRaycaster.blockingObjects = source.blockingObjects;
                ownedSettingsRaycaster.blockingMask = source.blockingMask;
            }
        }
        if (ownedSettingsRaycaster != null) ownedSettingsRaycaster.enabled = true;
    }

    public void ResetPresentation()
    {
        stage = Stage.Dormant;
        signal?.ResetPresentation();
        RestoreWindow(experimentPose); RestoreWindow(livePose);
        experimentPose = livePose = null;
        foreach (var pose in roots)
            if (pose.rect != null) { pose.rect.anchoredPosition3D = pose.position; pose.rect.localScale = pose.scale; }
        roots.Clear();
        if (settings != null && settingsLayoutOwned)
        { settings.ClearTemporaryOpeningLayout(); settings.WindowRect.localScale = settingsScale; }
        settingsLayoutOwned = false;
        if (ownedSettingsRaycaster != null) ownedSettingsRaycaster.enabled = false;
        if (settingsCanvas != null)
        {
            // Reuse owned components. A same-frame reset/reload must not adopt
            // components that are already queued for delayed destruction.
            if (ownsSettingsCanvas) settingsCanvas.enabled = false;
            else
            {
                settingsCanvas.overrideSorting = settingsSortOverride; settingsCanvas.sortingOrder = settingsSortOrder;
                settingsCanvas.sortingLayerID = settingsSortLayer; settingsCanvas.worldCamera = settingsCamera;
                settingsCanvas.enabled = settingsCanvasEnabled;
            }
        }
        settingsCanvas = null; ownsSettingsCanvas = false;
        if (screenRoot != null) screenRoot.gameObject.SetActive(false);
        elapsed = pan = 0f; zoom = 1f;
        freshCapture = sawUncaptured = false;
        observed = null;
    }

    static void RestoreWindow(WindowPose pose)
    {
        if (pose == null || pose.window == null || pose.parent == null) return;
        var rt = pose.window.WindowRect;
        rt.SetParent(pose.parent, false);
        rt.SetSiblingIndex(Mathf.Clamp(pose.sibling, 0, pose.parent.childCount - 1));
        rt.localScale = pose.scale;
        if (pose.ownsLayout) pose.window.ClearTemporaryOpeningLayout();
        // Fixed topmost windows deliberately ignore position in ApplyLayout.
        // Restore their authoring pose explicitly, including nested overrides.
        if (!pose.ownsLayout || (pose.window.IsForceTopmost && !pose.allowsMove))
            pose.window.SetOpeningLayout(pose.position, pose.size);
        pose.window.ConfigureInteraction(pose.allowsMove, pose.allowsResize);
    }

#if UNITY_EDITOR
    public float DebugPan => pan;
    public float DebugElapsed => elapsed;
    public string DebugStage => stage.ToString();
    public OSCursorExperimentInteriorBackground DebugBackground => background;
    public void DebugStep(float seconds)
    {
        float left = Mathf.Max(0, seconds);
        do { float dt = Mathf.Min(left, .02f); Tick(dt); left -= dt; } while (left > .00001f);
        ApplyComposition();
    }
#endif
}
