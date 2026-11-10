using System.Collections;
using UnityEngine;

// A captured monitor frame, not a live post-process. The story owns all timing:
// holding Present's clock also holds every displaced rectangular patch.
[DisallowMultipleComponent]
public sealed class OSCursorExperimentSignalTransition : MonoBehaviour
{
    const string MaterialPath = "UI/CursorExperimentSignalTransition";
    const float CaptureTimeout = .75f;
    static readonly int DistortionId = Shader.PropertyToID("_Distortion");
    static readonly int ClockId = Shader.PropertyToID("_SignalClock");
    static readonly int OpacityId = Shader.PropertyToID("_Opacity");
    static readonly int BlackoutId = Shader.PropertyToID("_Blackout");
    static readonly int HasCaptureId = Shader.PropertyToID("_HasCapture");
    static readonly int NativeSizeId = Shader.PropertyToID("_NativeSize");

    RectTransform overlay, ownedVfxRoot;
    UnityEngine.UI.Image backdrop;
    UnityEngine.UI.RawImage frameImage;
    Material runtimeMaterial;
    Texture2D capturedFrame;
    Coroutine captureRoutine;
    float captureDeadline, distortion, signalClock, opacity, blackout;
    bool capturePending;

    public bool IsReady { get; private set; }
    public bool HasCapturedFrame => capturedFrame != null;
    public bool UsesFallback => IsReady && (capturedFrame == null || runtimeMaterial == null);

    public void BeginCapture()
    {
        ResetPresentation();
        EnsureOverlay();
        capturePending = true;
        captureDeadline = Time.realtimeSinceStartup + CaptureTimeout;
        if (!isActiveAndEnabled || Application.isBatchMode
            || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null
            || Screen.width < 1 || Screen.height < 1)
        {
            CompleteCapture(null);
            return;
        }
        // Capture only once, after the old claw's suspended pose has rendered.
        // This overlay stays inactive until then, so it cannot capture itself.
        captureRoutine = StartCoroutine(CaptureAtEndOfFrame());
    }

    IEnumerator CaptureAtEndOfFrame()
    {
        yield return new WaitForEndOfFrame();
        Texture2D frame = null;
        try { frame = ScreenCapture.CaptureScreenshotAsTexture(); }
        catch (System.Exception) { /* A signal-loss fallback must never block the mainline. */ }
        captureRoutine = null;
        if (!capturePending)
        {
            if (frame != null) Destroy(frame);
            yield break;
        }
        CompleteCapture(frame);
    }

    void Update()
    {
        // WaitForEndOfFrame can stall while the Editor's Game view is hidden.
        // A missing frame becomes a deterministic black signal instead of a
        // permanently pending handoff. This timer never animates the shader.
        if (!capturePending || Time.realtimeSinceStartup < captureDeadline) return;
        if (captureRoutine != null) StopCoroutine(captureRoutine);
        captureRoutine = null;
        CompleteCapture(null);
    }

    void CompleteCapture(Texture2D frame)
    {
        capturePending = false;
        capturedFrame = frame;
        if (capturedFrame != null)
        {
            capturedFrame.name = "Runtime_CursorExperimentSuspendedFrame";
            capturedFrame.hideFlags = HideFlags.DontSave;
            capturedFrame.filterMode = FilterMode.Point;
            capturedFrame.wrapMode = TextureWrapMode.Clamp;
        }
        EnsureOverlay();
        frameImage.texture = capturedFrame != null ? capturedFrame : Texture2D.blackTexture;
        IsReady = true;
        Render();
    }

    /// <summary>Only this supplied clock changes the fault pattern; no _Time is used.</summary>
    public void Present(float distortion, float clock, float opacity, float blackout = 0f)
    {
        this.distortion = SafeUnit(distortion);
        signalClock = float.IsNaN(clock) || float.IsInfinity(clock) ? 0f : Mathf.Max(0f, clock);
        this.opacity = SafeUnit(opacity);
        this.blackout = SafeUnit(blackout);
        if (IsReady) Render();
    }

    static float SafeUnit(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Clamp01(value);

    void Render()
    {
        if (overlay == null) return;
        overlay.gameObject.SetActive(IsReady && opacity > 0f);
        if (!IsReady || opacity <= 0f) return;
        bool shaderReady = runtimeMaterial != null;
        // The shader ignores screenshot alpha. Its fallback needs a black
        // backing, but the normal path fades only once (not two stacked layers).
        backdrop.color = new Color(0, 0, 0, shaderReady ? 0f : opacity);
        frameImage.enabled = shaderReady || (capturedFrame != null && blackout < 1f);
        float tint = shaderReady ? 1f : 1f - blackout;
        frameImage.color = new Color(tint, tint, tint, shaderReady ? 1f : opacity);
        if (shaderReady)
        {
            runtimeMaterial.SetFloat(DistortionId, distortion);
            runtimeMaterial.SetFloat(ClockId, signalClock);
            runtimeMaterial.SetFloat(OpacityId, opacity);
            runtimeMaterial.SetFloat(BlackoutId, blackout);
            runtimeMaterial.SetFloat(HasCaptureId, capturedFrame != null ? 1f : 0f);
            runtimeMaterial.SetVector(NativeSizeId, new Vector4(Mathf.Max(1f, overlay.rect.width), Mathf.Max(1f, overlay.rect.height), 0, 0));
        }
    }

    void EnsureOverlay()
    {
        if (overlay != null) return;
        var addition = AdditionHandle.Instance;
        var canvas = addition != null ? addition.GetComponent<Canvas>() : null;
        RectTransform parent = null;
        if (canvas != null)
        {
            parent = addition.transform.Find("VFX") as RectTransform;
            if (parent == null)
            {
                var vfx = new GameObject("VFX", typeof(RectTransform));
                vfx.layer = canvas.gameObject.layer;
                parent = (RectTransform)vfx.transform;
                Stretch(parent, addition.transform);
                ownedVfxRoot = parent;
            }
        }
        var go = new GameObject("CursorExperimentSignalTransition", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.Image));
        go.layer = canvas != null ? canvas.gameObject.layer : gameObject.layer;
        overlay = (RectTransform)go.transform;
        var layer = go.GetComponent<Canvas>();
        if (parent != null)
        {
            Stretch(overlay, parent);
            layer.sortingLayerID = canvas.sortingLayerID;
            layer.worldCamera = canvas.worldCamera;
        }
        else layer.renderMode = RenderMode.ScreenSpaceOverlay;
        layer.overrideSorting = true;
        layer.sortingOrder = 23;
        backdrop = go.GetComponent<UnityEngine.UI.Image>();
        backdrop.raycastTarget = false;
        backdrop.color = Color.black;
        var frameNode = new GameObject("FrozenFrame", typeof(RectTransform), typeof(UnityEngine.UI.RawImage));
        frameNode.layer = go.layer;
        Stretch((RectTransform)frameNode.transform, overlay);
        frameImage = frameNode.GetComponent<UnityEngine.UI.RawImage>();
        frameImage.raycastTarget = false;
        frameImage.texture = Texture2D.blackTexture;
        var template = Resources.Load<Material>(MaterialPath);
        if (template != null && template.shader != null && template.shader.isSupported)
        {
            runtimeMaterial = new Material(template) { name = "Runtime_CursorExperimentSignalTransition", hideFlags = HideFlags.DontSave };
            frameImage.material = runtimeMaterial;
        }
        overlay.gameObject.SetActive(false);
    }

    static void Stretch(RectTransform rect, Transform parent)
    {
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    public void ResetPresentation()
    {
        capturePending = false;
        if (captureRoutine != null) StopCoroutine(captureRoutine);
        captureRoutine = null;
        IsReady = false;
        distortion = signalClock = opacity = blackout = 0f;
        if (overlay != null) overlay.gameObject.SetActive(false);
        if (frameImage != null) frameImage.texture = Texture2D.blackTexture;
        if (capturedFrame != null) Destroy(capturedFrame);
        capturedFrame = null;
    }

    void OnDisable() => ResetPresentation();
    void OnDestroy()
    {
        ResetPresentation();
        bool removeVfx = ownedVfxRoot != null && ownedVfxRoot.childCount == 1 && overlay != null && overlay.parent == ownedVfxRoot;
        if (overlay != null) Destroy(overlay.gameObject);
        if (removeVfx) Destroy(ownedVfxRoot.gameObject);
        if (runtimeMaterial != null) Destroy(runtimeMaterial);
    }

#if UNITY_EDITOR
    public RectTransform DebugOverlay => overlay;
    public Material DebugMaterial => runtimeMaterial;
#endif
}
