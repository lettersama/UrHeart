using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A temporary copy of the authored new-game loading panel. Its owner lives in
/// GameSetUp, so rebuilding GameUI cannot destroy the opening presentation.
/// </summary>
public sealed class SessionLoadPanelPresentation : MonoBehaviour
{
    public struct Timing
    {
        public float coverIn, textInDelay, textIn, imageInDelay, imageIn, minimum;
        public float imageOut, textOutDelay, textOut, coverOutDelay, coverOut;
    }

    Image background;
    TMP_Text label;
    Image progressImage;
    Material progressMaterial;
    Texture2D cellularTexture;
    Vector2Int textureSize;
    Timing timing;
    float startedAt, backendProgress;
    bool exiting;
    public bool IsRunning { get; private set; }
    public TMP_Text Label => label;
    public Image Background => background;
    public Image ProgressImage => progressImage;

    internal void Initialize(GameObject sourcePanel, CanvasScaler sourceScaler,
        Image sourceBackground, TMP_Text sourceLabel, Image sourceProgress, Timing authoredTiming,
        int sortingOrder)
    {
        timing = authoredTiming;
        Canvas canvas = GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.pixelPerfect = true;
        canvas.sortingOrder = sortingOrder;
        if (sourceScaler != null)
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(sourceScaler), GetComponent<CanvasScaler>());

        GameObject panel = Instantiate(sourcePanel, transform, false);
        panel.name = "LoadPanel";
        background = FindCopy<Image>(sourcePanel.transform, sourceBackground.transform, panel.transform);
        label = FindCopy<TMP_Text>(sourcePanel.transform, sourceLabel.transform, panel.transform);
        progressImage = FindCopy<Image>(sourcePanel.transform, sourceProgress.transform, panel.transform);
        Canvas panelCanvas = panel.GetComponent<Canvas>();
        panelCanvas.enabled = true;
        panelCanvas.overrideSorting = true;
        panelCanvas.sortingOrder = sortingOrder;
        panel.GetComponent<GraphicRaycaster>().enabled = true;
        background.raycastTarget = true;
        progressImage.raycastTarget = false;
        progressImage.type = Image.Type.Simple;
        progressMaterial = new Material(sourceProgress.material) { name = "Session Loading Heartbeat", hideFlags = HideFlags.DontSave };
        progressImage.material = progressMaterial;
        progressMaterial.SetFloat("_Progress", 0f);
        SetAlpha(background, 0f);SetAlpha(label, 0f);SetAlpha(progressImage, 0f);
        panel.SetActive(true);
        startedAt = Time.realtimeSinceStartup;
        IsRunning = true;
        Canvas.ForceUpdateCanvases();
        RefreshMaterialSize();
    }

    public IEnumerator WaitUntilReadyToLoad()
    {
        float readyAt = Mathf.Max(0f, Mathf.Max(timing.coverIn, timing.textInDelay)) + Mathf.Max(0f, timing.textIn);
        while (Time.realtimeSinceStartup - startedAt < readyAt) yield return null;
    }

    public void SetBackendProgress(float progress)
    {
        backendProgress = Mathf.Max(backendProgress, Mathf.Clamp01(progress));
    }

    public IEnumerator Complete(Action onReveal)
    {
        SetBackendProgress(1f);
        while (Time.realtimeSinceStartup - startedAt < Mathf.Max(0f, timing.minimum)) yield return null;
        exiting = true;
        progressMaterial.SetFloat("_Progress", 1f);
        float textOutStart = Mathf.Max(0f, timing.textOutDelay);
        float coverOutStart = Mathf.Max(Mathf.Max(0f, timing.coverOutDelay), textOutStart + Mathf.Max(0f, timing.textOut));
        float duration = Mathf.Max(Mathf.Max(0f, timing.imageOut), coverOutStart + Mathf.Max(0f, timing.coverOut));
        bool revealed = false;
        for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
        {
            SetAlpha(progressImage, 1f - Progress(elapsed, 0f, timing.imageOut));
            SetAlpha(label, 1f - Progress(elapsed, textOutStart, timing.textOut));
            SetAlpha(background, 1f - Progress(elapsed, coverOutStart, timing.coverOut));
            if (!revealed && elapsed >= coverOutStart)
            {
                revealed = true;
                onReveal?.Invoke();
            }
            yield return null;
        }
        if (!revealed) onReveal?.Invoke();
        IsRunning = false;
        gameObject.SetActive(false);
    }

    void Update()
    {
        if (!IsRunning) return;
        RefreshMaterialSize();
        if (exiting) return;
        float elapsed = Time.realtimeSinceStartup - startedAt;
        float textStart = Mathf.Max(Mathf.Max(0f, timing.coverIn), Mathf.Max(0f, timing.textInDelay));
        float imageStart = Mathf.Max(Mathf.Max(0f, timing.imageInDelay), textStart + Mathf.Max(0f, timing.textIn));
        SetAlpha(background, Progress(elapsed, 0f, timing.coverIn));
        SetAlpha(label, Progress(elapsed, textStart, timing.textIn));
        SetAlpha(progressImage, Progress(elapsed, imageStart, timing.imageIn));
        float visibleProgress = Mathf.Min(backendProgress, Progress(elapsed, imageStart, timing.minimum - imageStart));
        progressMaterial.SetFloat("_Progress", Mathf.Max(progressMaterial.GetFloat("_Progress"), visibleProgress));
    }

    void RefreshMaterialSize()
    {
        Vector2 size = progressImage.rectTransform.rect.size;
        Vector2Int required = new Vector2Int(Mathf.Max(1, Mathf.RoundToInt(size.x)), Mathf.Max(1, Mathf.RoundToInt(size.y)));
        progressMaterial.SetVector("_RectSize", new Vector4(Mathf.Max(1, size.x), Mathf.Max(1, size.y), 0f, 0f));
        if (cellularTexture != null && textureSize == required) return;
        if (cellularTexture != null) Destroy(cellularTexture);
        textureSize = required;
        cellularTexture = LoadHeartbeatCellularTextureBuilder.Create(required.x, required.y);
        progressMaterial.SetTexture("_CellularTex", cellularTexture);
    }

    static float Progress(float elapsed, float start, float duration)
    {
        if (elapsed < start) return 0f;
        return duration <= 0f ? 1f : Mathf.Clamp01((elapsed - start) / duration);
    }

    static void SetAlpha(Graphic graphic, float alpha)
    {
        Color color = graphic.color;color.a = alpha;graphic.color = color;
    }

    static T FindCopy<T>(Transform sourceRoot, Transform source, Transform copyRoot) where T : Component
    {
        if (source == sourceRoot) return copyRoot.GetComponent<T>();
        string path = source.name;
        for (Transform parent = source.parent; parent != sourceRoot; parent = parent.parent)
            path = parent.name + "/" + path;
        return copyRoot.Find(path).GetComponent<T>();
    }

    void OnDestroy()
    {
        if (progressMaterial != null) Destroy(progressMaterial);
        if (cellularTexture != null) Destroy(cellularTexture);
    }
}
