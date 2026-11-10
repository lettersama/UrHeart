using System.Collections.Generic;
using UnityEngine;

// The OS is a root Canvas, whose transform belongs to Unity/CanvasScaler.
// Zoom its presentation groups together about the same screen centre instead:
// child window/icon coordinates, parent paths and the input transform agree.
[DisallowMultipleComponent]
[DefaultExecutionOrder(-100)]
public sealed class OSCursorExperimentFocusPresentation : MonoBehaviour
{
    const float FadeSeconds = .5f, Zoom = 1.06f, BreathSeconds = 5.6f;
    const float CaptureJumpZoom = 1.04f, CapturePeakZoom = 1.10f;
    const float CaptureRiseSeconds = .12f, CaptureSettleSeconds = .24f;
    const string MaterialPath = "UI/CursorExperimentFocus";
    const string ShaderName = "UI/OS/CursorExperimentFocus";
    static readonly int StrengthId = Shader.PropertyToID("_Strength");
    static readonly int BreathId = Shader.PropertyToID("_Breath");
    static readonly int NativeSizeId = Shader.PropertyToID("_NativeSize");
    static readonly string[] DesktopRoots =
    {
        "Bk", "DesktopProcessGroup", "ProcessGroup", "HeartBeatPanel",
        "Windows", "OS_VFX", "Testobj", "ClawStoryOverlay"
    };

    sealed class PresentationRoot
    {
        public RectTransform rect;
        public Vector3 position, scale, appliedPosition, appliedScale;
    }

    OS_Handle os;
    OSCursorExperimentInteriorPresentation interior;
    OSMemoryProcessSaveData observed;
    RectTransform desktopRect, overlay, ownedVfxRoot;
    Canvas desktopCanvas;
    OSWindowUnit settingsWindow;
    Material runtimeMaterial;
    readonly List<PresentationRoot> roots = new List<PresentationRoot>();
    float amount, breathClock, captureImpactTime, currentZoom = 1f;
    bool hasFocus, ownsScale, visualSuspended, resumeWithoutAdvance;
    bool observedCaptured, captureImpactActive, restoreStablePending;

    public float CurrentZoom => currentZoom;

    // Interior takes a clean snapshot before composing the desktop pan. The
    // focus clock survives this ownership handoff, including an impact in flight.
    public void ReleaseDesktopForInterior() => RestoreDesktop();

    void OnEnable()
    {
        os = GetComponent<OS_Handle>();
        hasFocus = Application.isFocused;
    }

    void OnApplicationFocus(bool focused)
    {
        hasFocus = focused;
        // Alt-tab is a pause, not another capture. Retain the exact desktop
        // transform and shader phase, so returning never replays the push-in.
        if (!focused) resumeWithoutAdvance = true;
    }

    void OnDisable() => ResetPresentation();

    void OnDestroy()
    {
        ResetPresentation();
        bool removeVfx = ownedVfxRoot != null && ownedVfxRoot.childCount == 1
            && overlay != null && overlay.parent == ownedVfxRoot;
        if (overlay != null) Destroy(overlay.gameObject);
        if (removeVfx) Destroy(ownedVfxRoot.gameObject);
        if (runtimeMaterial != null) Destroy(runtimeMaterial);
    }

    void Update()
    {
        var c = os != null ? os.CursorExperiment : null;
        Tick(Mathf.Min(Time.unscaledDeltaTime, .05f), c, IsAvailable(c));
    }

    void LateUpdate()
    {
        var p = GameDataManager.Instance != null ? GameDataManager.Instance.OSProcessData.memory : null;
        // The claw writes its capture receipt during Update. Catch that edge
        // before the same frame renders instead of waiting for a story message
        // or the next frame's experiment-window intake.
        if (p != null && p.chapter02ClawCursorCaptured
            && (!ReferenceEquals(observed, p) || !observedCaptured))
        {
            var c = os != null ? os.CursorExperiment : null;
            Tick(0f, c, IsAvailable(c));
        }
        // Settings can open after our early Update. Restore before rendering,
        // rather than displaying one zoomed settings frame or saving a crop.
        if (hasFocus && amount > 0f && !IsAvailable(os != null ? os.CursorExperiment : null))
            SuspendVisual();
    }

    bool IsAvailable(OSCursorExperimentController c)
    {
        if (settingsWindow == null && os != null)
            settingsWindow = os.transform.Find("Windows/WindowUnit_Option")?.GetComponent<OSWindowUnit>();
        bool settingsOpen = settingsWindow != null && settingsWindow.IsOpen
            && !settingsWindow.IsMinimized && settingsWindow.gameObject.activeInHierarchy;
        return hasFocus && os != null && os.IsOSVisible
            && !OSSessionPersistence.IsRestoring && GameSessionFlow.Instance?.IsBusy != true
            && !GameTestConsole.BlocksGameplayInput && !settingsOpen
            && !os.IsSystemInputLockedWithoutCursorExperiment
            && (c == null || !c.IsSettingsOpen);
    }

    void Tick(float dt, OSCursorExperimentController c, bool available)
    {
        var data = GameDataManager.Instance;
        var p = data != null ? data.OSProcessData.memory : null;
        bool progressChanged = !ReferenceEquals(observed, p);
        if (progressChanged)
        {
            ResetPresentation();
            observed = p;
        }
        bool captured = p != null && p.chapter02ClawCursorCaptured;
        bool freshCapture = !progressChanged && captured && !observedCaptured;
        observedCaptured = captured;
        if (progressChanged && captured) restoreStablePending = true;
        if (freshCapture)
        {
            // One impact per actual uncaptured -> captured edge. A saved
            // captured state and an alt-tab can never generate another impact.
            amount = 1f;
            captureImpactTime = 0f;
            captureImpactActive = true;
            restoreStablePending = false;
        }
        bool relevant = data != null && data.CurrentChapterIndex == 2 && p != null
            && p.chapter02ClawCursorCaptured && p.chapter02CursorExperimentRound < 4;
        if (!relevant || os == null || !os.IsOSVisible || OSSessionPersistence.IsRestoring
            || GameSessionFlow.Instance?.IsBusy == true) { HideImmediately(); return; }

        // Keep both the full-screen zoom and breathing image exactly as they
        // were while unfocused. Do this before checking pointer ownership:
        // the gameplay controllers correctly release their input on focus loss.
        if (!hasFocus) return;
        if (!available) { SuspendVisual(); return; }

        if (interior == null && os != null)
            interior = os.GetComponent<OSCursorExperimentInteriorPresentation>();
        bool inInterior = interior != null && interior.IsEngaged;
        var window = c != null ? c.Window : null;
        bool inExperiment = c != null && c.IsActive && window != null && window.IsOpen
            && !window.IsClosing && !window.IsMinimized && window.gameObject.activeInHierarchy;
        var claw = os != null ? os.ClawMachineGame : null;
        bool inCapture = !inExperiment && claw != null && claw.IsPresentationVisible
            && claw.Phase >= OSClawMachineGameWindowController.ClawPhase.CursorClosing;
        if (!inExperiment && !inCapture && !inInterior)
        {
            HideImmediately();
            return;
        }

        bool focused = inInterior || inCapture || (c != null
            && (c.Phase == OSCursorExperimentController.ExperimentPhase.Intake
                || (c.Phase == OSCursorExperimentController.ExperimentPhase.Recapture && c.PhaseTime >= .62f)));
        if (visualSuspended || resumeWithoutAdvance) dt = 0f;
        visualSuspended = false;
        resumeWithoutAdvance = false;
        if (restoreStablePending && focused)
        {
            amount = 1f;
            captureImpactActive = false;
            restoreStablePending = false;
            dt = 0f;
        }
        amount = Mathf.MoveTowards(amount, focused ? 1f : 0f, dt / FadeSeconds);
        if (amount <= 0f)
        {
            HideOverlay();
            ClearZoom();
            return;
        }

        breathClock += dt;
        if (!focused) captureImpactActive = false;
        if (captureImpactActive)
        {
            captureImpactTime += dt;
            if (captureImpactTime >= CaptureRiseSeconds + CaptureSettleSeconds)
                captureImpactActive = false;
        }
        float eased = Mathf.SmoothStep(0f, 1f, amount);
        float zoom = Mathf.Lerp(1f, Zoom, eased);
        if (captureImpactActive)
        {
            zoom = captureImpactTime <= CaptureRiseSeconds
                ? Mathf.Lerp(CaptureJumpZoom, CapturePeakZoom,
                    1f - Mathf.Pow(1f - Mathf.Clamp01(captureImpactTime / CaptureRiseSeconds), 3f))
                : Mathf.Lerp(CapturePeakZoom, Zoom,
                    Mathf.SmoothStep(0f, 1f, (captureImpactTime - CaptureRiseSeconds) / CaptureSettleSeconds));
        }
        currentZoom = zoom;
        if (inInterior)
        {
            RestoreDesktop();
            interior.SetFocusZoom(zoom);
        }
        else if (!ApplyDesktopZoom(zoom)) { HideImmediately(); return; }

        if (EnsureOverlay())
        {
            overlay.gameObject.SetActive(true);
            float breath = .5f + .5f * Mathf.Sin(breathClock * Mathf.PI * 2f / BreathSeconds);
            runtimeMaterial.SetFloat(StrengthId, Mathf.Lerp(.50f, .62f, breath) * eased);
            runtimeMaterial.SetFloat(BreathId, breath);
            runtimeMaterial.SetVector(NativeSizeId, new Vector4(Mathf.Max(1, overlay.rect.width), Mathf.Max(1, overlay.rect.height), 0, 0));
        }
    }

    bool ApplyDesktopZoom(float zoom)
    {
        if (desktopCanvas == null && os != null)
        {
            desktopCanvas = os.GetComponent<Canvas>();
            desktopRect = os.transform as RectTransform;
        }
        if (desktopCanvas == null || desktopRect == null) return false;
        if (!ownsScale)
        {
            roots.Clear();
            foreach (string path in DesktopRoots)
            {
                var rect = os.transform.Find(path) as RectTransform;
                if (rect == null || rect.parent != desktopRect) continue;
                roots.Add(new PresentationRoot
                {
                    rect = rect, position = rect.anchoredPosition3D, scale = rect.localScale,
                    appliedPosition = rect.anchoredPosition3D, appliedScale = rect.localScale
                });
            }
            ownsScale = roots.Count > 0;
        }
        Vector3 centre = desktopRect.rect.center;
        foreach (var root in roots)
        {
            if (root.rect == null) continue;
            // If another presentation writes a group, adopt its new base value
            // instead of restoring a stale snapshot over that presentation.
            if ((root.rect.anchoredPosition3D - root.appliedPosition).sqrMagnitude > .000001f)
                root.position = root.rect.anchoredPosition3D;
            if ((root.rect.localScale - root.appliedScale).sqrMagnitude > .000001f)
                root.scale = root.rect.localScale;
            // Compute from current anchor geometry, so resizing the viewport
            // during focus does not bake the previous resolution into a group.
            Vector3 baseLocal = root.rect.localPosition + root.position - root.rect.anchoredPosition3D;
            Vector3 shift = (baseLocal - centre) * (zoom - 1f);
            shift.z = 0f;
            root.appliedPosition = root.position + shift;
            root.appliedScale = Vector3.Scale(root.scale, new Vector3(zoom, zoom, 1f));
            root.rect.anchoredPosition3D = root.appliedPosition;
            root.rect.localScale = root.appliedScale;
        }
        return ownsScale;
    }

    void RestoreDesktop()
    {
        if (!ownsScale) return;
        foreach (var root in roots)
        {
            if (root.rect == null) continue;
            if ((root.rect.anchoredPosition3D - root.appliedPosition).sqrMagnitude < .000001f)
                root.rect.anchoredPosition3D = root.position;
            if ((root.rect.localScale - root.appliedScale).sqrMagnitude < .000001f)
                root.rect.localScale = root.scale;
        }
        roots.Clear();
        ownsScale = false;
    }

    void HideImmediately()
    {
        amount = 0f;
        captureImpactActive = false;
        captureImpactTime = 0f;
        visualSuspended = false;
        resumeWithoutAdvance = false;
        HideOverlay();
        ClearZoom();
    }

    void ClearZoom()
    {
        RestoreDesktop();
        currentZoom = 1f;
        if (interior == null && os != null)
            interior = os.GetComponent<OSCursorExperimentInteriorPresentation>();
        if (interior != null) interior.SetFocusZoom(1f);
    }

    void SuspendVisual()
    {
        // Settings/console use the unzoomed desktop while they are open. Keep
        // progression, then restore that same view immediately on dismissal.
        visualSuspended = true;
        HideOverlay();
        ClearZoom();
    }

    void HideOverlay()
    {
        if (overlay != null) overlay.gameObject.SetActive(false);
        if (runtimeMaterial != null) runtimeMaterial.SetFloat(StrengthId, 0f);
    }

    public void ResetPresentation()
    {
        HideImmediately();
        breathClock = 0f;
        observed = null;
        observedCaptured = false;
        restoreStablePending = false;
    }

    bool EnsureOverlay()
    {
        if (overlay != null) return true;
        var addition = AdditionHandle.Instance;
        Canvas canvas = addition != null ? addition.GetComponent<Canvas>() : null;
        if (canvas == null) return false;
        Material template = Resources.Load<Material>(MaterialPath);
        Shader shader = template != null ? template.shader : Shader.Find(ShaderName);
        if (shader == null) return false;
        runtimeMaterial = template != null ? new Material(template) : new Material(shader);
        runtimeMaterial.name = "Runtime_CursorExperimentFocus";
        runtimeMaterial.hideFlags = HideFlags.DontSave;
        runtimeMaterial.SetFloat(StrengthId, 0f);

        var vfx = addition.transform.Find("VFX") as RectTransform;
        if (vfx == null)
        {
            var vfxNode = new GameObject("VFX", typeof(RectTransform));
            vfxNode.layer = canvas.gameObject.layer;
            vfx = (RectTransform)vfxNode.transform;
            vfx.SetParent(addition.transform, false);
            vfx.anchorMin = Vector2.zero; vfx.anchorMax = Vector2.one;
            vfx.offsetMin = vfx.offsetMax = Vector2.zero;
            ownedVfxRoot = vfx;
        }

        var node = new GameObject("CursorExperimentScreenFocus", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.Image));
        node.layer = canvas.gameObject.layer;
        overlay = (RectTransform)node.transform;
        overlay.SetParent(vfx, false);
        overlay.anchorMin = Vector2.zero;
        overlay.anchorMax = Vector2.one;
        overlay.offsetMin = overlay.offsetMax = Vector2.zero;
        var layer = node.GetComponent<Canvas>();
        layer.overrideSorting = true;
        layer.sortingLayerID = canvas.sortingLayerID;
        // AdditionCanvas is order 3; the OS desktop is order 10. Inherit the
        // requested VFX hierarchy, but draw this shader image above the OS.
        // Settings hide it before rendering; loading/fades keep their own order.
        layer.sortingOrder = Mathf.Max(canvas.sortingOrder,
            desktopCanvas != null ? desktopCanvas.sortingOrder : 10) + 11;
        layer.worldCamera = canvas.worldCamera;
        var image = node.GetComponent<UnityEngine.UI.Image>();
        image.raycastTarget = false;
        image.color = Color.white;
        image.material = runtimeMaterial;
        return true;
    }

#if UNITY_EDITOR
    public float DebugAmount => amount;
    public bool DebugOwnsScale => ownsScale;
    public bool DebugVisualSuspended => visualSuspended;
    public float DebugBreathClock => breathClock;
    public bool DebugCaptureImpactActive => captureImpactActive;
    public float DebugCaptureImpactTime => captureImpactTime;
    public int DebugRootCount => roots.Count;
    public RectTransform DebugOverlay => overlay;
    public bool DebugVignetteVisible => overlay != null && overlay.gameObject.activeInHierarchy;
    public void DebugStep(float seconds, bool available = true)
    {
        float left = Mathf.Max(0, seconds);
        do
        {
            float dt = Mathf.Min(left, .02f);
            Tick(dt, os != null ? os.CursorExperiment : null, available);
            left -= dt;
        } while (left > .00001f);
    }
#endif
}
