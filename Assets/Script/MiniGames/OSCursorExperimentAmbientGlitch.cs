using System.Collections;
using UnityEngine;

// A brief desktop signal fault while the captured pointer is being studied.
// Presentation only: never changes fault resources, input, or shared VFX profiles.
[DisallowMultipleComponent]
[DefaultExecutionOrder(650)]
public sealed class OSCursorExperimentAmbientGlitch : MonoBehaviour
{
    const float PulseSeconds = .25f;
    const string MaterialPath = "UI/CursorExperimentSignalTransition";
    static readonly int DistortionId = Shader.PropertyToID("_Distortion");
    static readonly int ClockId = Shader.PropertyToID("_SignalClock");
    static readonly int OpacityId = Shader.PropertyToID("_Opacity");
    static readonly int SeedId = Shader.PropertyToID("_Seed");
    static readonly int NativeSizeId = Shader.PropertyToID("_NativeSize");
    readonly System.Random random = new System.Random();
    OS_Handle os;
    OSWindowUnit settings;
    OSMemoryProcessSaveData observed;
    RectTransform overlay, ownedVfxRoot;
    UnityEngine.UI.RawImage frameImage;
    Material runtimeMaterial;
    RenderTexture capturedFrame;
    Coroutine captureRoutine;
    bool capturePending;
    float captureDeadline;
    float remaining, pulseTime = -1f;
    bool hasFocus;

    void OnEnable() { os = GetComponent<OS_Handle>(); hasFocus = Application.isFocused; }
    void OnApplicationFocus(bool focused) { hasFocus = focused; if (!focused) HidePulse(); }
    void OnDisable() { ResetEffect(); }
    void OnDestroy()
    {
        bool removeVfx = ownedVfxRoot != null && ownedVfxRoot.childCount == 1
            && overlay != null && overlay.parent == ownedVfxRoot;
        if (overlay != null) Destroy(overlay.gameObject);
        if (removeVfx) Destroy(ownedVfxRoot.gameObject);
        if (runtimeMaterial != null) Destroy(runtimeMaterial);
    }

    void Update()
    {
        // Hidden Game views can stall WaitForEndOfFrame. Skip that one pulse;
        // a missing screenshot must never turn into an opaque black overlay.
        if (capturePending && Time.realtimeSinceStartup > captureDeadline) HidePulse();
        var data = GameDataManager.Instance;
        var p = data != null ? data.OSProcessData.memory : null;
        bool captured = data != null && data.CurrentChapterIndex == 2 && p != null
            && p.chapter02ClawCursorCaptured && p.chapter02CursorExperimentRound < 4;
        var c = os != null ? os.CursorExperiment : null;
        if (settings == null && os != null)
            settings = os.transform.Find("Windows/WindowUnit_Option")?.GetComponent<OSWindowUnit>();
        bool settingsOpen = settings != null && settings.IsOpen && !settings.IsMinimized
            && settings.gameObject.activeInHierarchy;
        bool available = hasFocus && os != null && os.IsOSVisible
            && os.CursorExperimentInterior?.IsScreenObscured != true
            && !OSSessionPersistence.IsRestoring && GameSessionFlow.Instance?.IsBusy != true
            && !GameTestConsole.BlocksGameplayInput && !os.IsSystemInputLockedWithoutCursorExperiment && !settingsOpen
            && (c == null || !c.IsActive || (!c.IsPaused && !c.IsSettingsOpen));
        Tick(Mathf.Min(Time.unscaledDeltaTime, .05f), p, captured, available,
            c != null && c.IsActive && c.Phase == OSCursorExperimentController.ExperimentPhase.Collision);
    }

    void Tick(float dt, OSMemoryProcessSaveData p, bool captured, bool available, bool collision)
    {
        if (!captured) { ResetEffect(); return; }
        if (!ReferenceEquals(observed, p))
        {
            ResetEffect(); observed = p; remaining = NextInterval();
        }
        // No catch-up pulses after alt-tab/settings. Wall contact owns its own
        // feedback and takes priority over this occasional environmental beat.
        if (!available || collision) { HidePulse(); return; }
        remaining -= dt;
        if (pulseTime >= 0f)
        {
            pulseTime += dt;
            if (pulseTime >= PulseSeconds) HidePulse();
            else RenderPulse();
        }
        else if (!capturePending && remaining <= 0f)
        {
            remaining = NextInterval();
            BeginPulse();
        }
    }

    float NextInterval() => 12f + (float)random.NextDouble() * 3f;

    void BeginPulse()
    {
        if (!EnsureOverlay()) return;
        if (!isActiveAndEnabled || Application.isBatchMode || Screen.width < 1 || Screen.height < 1
            || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
        capturePending = true;
        captureDeadline = Time.realtimeSinceStartup + .75f;
        captureRoutine = StartCoroutine(CapturePulse());
    }

    IEnumerator CapturePulse()
    {
        // The overlay is hidden during capture, so it cannot feed back into
        // itself. Reuse a GPU texture; no CPU screenshot/readback every 12s.
        yield return new WaitForEndOfFrame();
        if (!capturePending) yield break;
        bool captured = false;
        try
        {
            if (capturedFrame == null || !capturedFrame.IsCreated()
                || capturedFrame.width != Screen.width || capturedFrame.height != Screen.height)
            {
                ReleaseFrame();
                capturedFrame = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32)
                { name = "Runtime_CursorExperimentFaultFrame", hideFlags = HideFlags.DontSave,
                    filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                if (!capturedFrame.Create()) throw new System.InvalidOperationException("Signal capture surface unavailable.");
            }
            ScreenCapture.CaptureScreenshotIntoRenderTexture(capturedFrame);
            captured = true;
        }
        catch (System.Exception) { ReleaseFrame(); /* Skip an unavailable frame without affecting input. */ }
        capturePending = false; captureRoutine = null;
        if (!captured) yield break;
        frameImage.texture = capturedFrame;
        runtimeMaterial.SetFloat(SeedId, (float)random.NextDouble() * 4096f);
        overlay.SetAsLastSibling();
        pulseTime = 0f; overlay.gameObject.SetActive(true); RenderPulse();
    }

    bool EnsureOverlay()
    {
        if (overlay != null) return runtimeMaterial != null;
        Material template = Resources.Load<Material>(MaterialPath);
        if (template == null || template.shader == null || !template.shader.isSupported) return false;
        var addition = AdditionHandle.Instance;
        var parent = addition != null ? addition.GetComponent<Canvas>() : null;
        if (parent == null) return false;
        var vfx = addition.transform.Find("VFX") as RectTransform;
        if (vfx == null)
        {
            var node = new GameObject("VFX", typeof(RectTransform));
            node.layer = parent.gameObject.layer;
            vfx = (RectTransform)node.transform;
            vfx.SetParent(addition.transform, false);
            vfx.anchorMin = Vector2.zero; vfx.anchorMax = Vector2.one;
            vfx.offsetMin = vfx.offsetMax = Vector2.zero;
            ownedVfxRoot = vfx;
        }
        var go = new GameObject("CursorExperimentSignalFault", typeof(RectTransform), typeof(Canvas));
        go.layer = parent.gameObject.layer;
        overlay = (RectTransform)go.transform; overlay.SetParent(vfx, false);
        overlay.anchorMin = Vector2.zero; overlay.anchorMax = Vector2.one;
        overlay.offsetMin = overlay.offsetMax = Vector2.zero;
        var canvas = go.GetComponent<Canvas>();
        canvas.overrideSorting = true; canvas.sortingLayerID = parent.sortingLayerID;
        var desktop = os != null ? os.GetComponent<Canvas>() : null;
        // The fault belongs to the monitor, not to either scrolling desktop.
        // Draw over both pages and the breathing vignette, below settings/fades.
        canvas.sortingOrder = Mathf.Max(parent.sortingOrder, desktop != null ? desktop.sortingOrder : 10) + 12;
        canvas.worldCamera = parent.worldCamera;
        var frameNode = new GameObject("DisplacedScreenPatches", typeof(RectTransform), typeof(UnityEngine.UI.RawImage));
        frameNode.layer = go.layer;
        var rect = (RectTransform)frameNode.transform; rect.SetParent(overlay, false);
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        frameImage = frameNode.GetComponent<UnityEngine.UI.RawImage>();
        frameImage.raycastTarget = false;
        runtimeMaterial = new Material(template) { name = "Runtime_CursorExperimentAmbientPatches", hideFlags = HideFlags.DontSave };
        runtimeMaterial.SetFloat("_PatchOnly", 1f);
        runtimeMaterial.SetFloat("_HasCapture", 1f);
        runtimeMaterial.SetFloat("_Blackout", 0f);
        runtimeMaterial.SetFloat("_CaptureFlipY", SystemInfo.graphicsUVStartsAtTop ? 1f : 0f);
        frameImage.material = runtimeMaterial;
        overlay.gameObject.SetActive(false);
        return true;
    }

    void RenderPulse()
    {
        if (overlay == null || runtimeMaterial == null) return;
        // Hard displacement, a clean gap, then a stronger break and short echo.
        // The rest of the screen remains live; only the affected rectangles copy
        // from the captured frame. No scanline Images or brightness veil.
        bool gap = pulseTime >= .065f && pulseTime < .095f;
        float strength = pulseTime < .095f ? .85f : pulseTime < .18f ? 1f : .62f;
        runtimeMaterial.SetFloat(DistortionId, strength);
        runtimeMaterial.SetFloat(ClockId, pulseTime);
        runtimeMaterial.SetFloat(OpacityId, gap ? 0f : 1f);
        // Fixed art-pixel grid at all display resolutions, including 4K.
        runtimeMaterial.SetVector(NativeSizeId, new Vector4(640f, 360f, 0f, 0f));
    }

    void HidePulse()
    {
        pulseTime = -1f;
        capturePending = false;
        if (captureRoutine != null) StopCoroutine(captureRoutine);
        captureRoutine = null;
        if (overlay != null) overlay.gameObject.SetActive(false);
    }

    void ReleaseFrame()
    {
        if (frameImage != null) frameImage.texture = null;
        if (capturedFrame != null) { capturedFrame.Release(); Destroy(capturedFrame); capturedFrame = null; }
    }
    public void ResetEffect() { HidePulse(); ReleaseFrame(); remaining = 0f; observed = null; }

#if UNITY_EDITOR
    public float DebugRemaining => remaining;
    public float DebugPulseTime => pulseTime;
    public RectTransform DebugOverlay => overlay;
    public bool DebugCapturePending => capturePending;
    public Material DebugMaterial => runtimeMaterial;
    public RenderTexture DebugCapturedFrame => capturedFrame;
    public void DebugRequestPulse() { HidePulse(); BeginPulse(); }
    public void DebugStep(float seconds, bool captured = true, bool available = true, bool collision = false)
    {
        float left = Mathf.Max(0, seconds);
        do
        {
            float dt = Mathf.Min(left, .02f);
            Tick(dt, GameDataManager.Instance.OSProcessData.memory, captured, available, collision);
            left -= dt;
        } while (left > .00001f);
    }
#endif
}
