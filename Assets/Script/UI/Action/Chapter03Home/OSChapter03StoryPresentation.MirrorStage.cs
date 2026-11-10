using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class OSChapter03StoryPresentation
{
    sealed class MirrorDimState
    {
        public CanvasGroup group;
        public Image shade;
        public float alpha;
        public bool interactable, raycasts, added;
    }

    readonly List<MirrorDimState> mirrorDimStates = new List<MirrorDimState>();
    RectTransform mirrorInputShield;
    RectTransform mirrorPlaybackRoot;
    Canvas mirrorPlaybackCanvas;
    UnityEngine.UI.Button mirrorResumeButton;
    System.Action mirrorPlaybackResumed;
    bool mirrorStageLocked, mirrorStageDimmed;
    bool mirrorPlaybackPaused, mirrorPlaybackCanResume;
    public bool IsMirrorStageDimmed => mirrorStageDimmed;
    public UnityEngine.UI.Button MirrorResumeButton => mirrorResumeButton;
    public bool IsMirrorPlaybackPaused => mirrorPlaybackPaused;

    public void SetMirrorPlaybackPaused(bool on, bool canResume, System.Action resumed)
    {
        RequireInitialized();
        mirrorPlaybackPaused = on;
        mirrorPlaybackCanResume = on && canResume;
        mirrorPlaybackResumed = on ? resumed : null;
        if (on && mirrorPlaybackRoot == null)
        {
            // Draw after the mirror's shade. Its own group lets the final resume
            // control receive input while the paused mirror stays locked.
            mirrorPlaybackRoot = Root("Chapter03MirrorPlayback", mirrorRoot != null ? mirrorRoot : windowParent);
            mirrorPlaybackRoot.sizeDelta = new Vector2(36f, 36f);
            var inputGroup = mirrorPlaybackRoot.gameObject.AddComponent<CanvasGroup>();
            inputGroup.ignoreParentGroups = true;
            mirrorPlaybackCanvas = mirrorPlaybackRoot.gameObject.AddComponent<Canvas>();
            mirrorPlaybackCanvas.overrideSorting = true;
            mirrorPlaybackRoot.gameObject.AddComponent<GraphicRaycaster>();
            mirrorResumeButton = PixelButton(mirrorPlaybackRoot, "Resume", string.Empty,
                Vector2.zero, new Vector2(36f, 36f), ResumeMirrorPlayback);
            DrawPixelGlyph(PixelContent(mirrorResumeButton), "PausePixel", new[] {
                "##..##", "##..##", "##..##", "##..##",
                "##..##", "##..##", "##..##", "##..##"
            }, 2);
        }
        RefreshMirrorPlaybackPresentation();
    }

    void RefreshMirrorPlaybackPresentation()
    {
        if (mirrorPlaybackRoot == null) return;
        bool visible = mirrorPlaybackPaused && IsMirrorForeground;
        if (mirrorRoot != null)
        {
            if (mirrorPlaybackRoot.parent != mirrorRoot) mirrorPlaybackRoot.SetParent(mirrorRoot, false);
            mirrorPlaybackRoot.anchoredPosition = new Vector2(0f, 26f);
            mirrorPlaybackRoot.SetAsLastSibling();
        }
        if (mirrorPlaybackCanvas != null && rootCanvas != null)
        {
            mirrorPlaybackCanvas.overrideSorting = true;
            mirrorPlaybackCanvas.sortingLayerID = rootCanvas.sortingLayerID;
            // Live covers the paused reflection during her performance. Raise
            // only the final resume offer so its hit target clears the shield.
            mirrorPlaybackCanvas.sortingOrder = rootCanvas.sortingOrder + (mirrorPlaybackCanResume ? 6 : 1);
            mirrorPlaybackCanvas.worldCamera = rootCanvas.worldCamera;
            mirrorPlaybackCanvas.overrideSorting = mirrorPlaybackCanResume;
        }
        if (mirrorResumeButton != null)
            mirrorResumeButton.interactable = visible && mirrorPlaybackCanResume && mirrorPlaybackResumed != null;
        mirrorPlaybackRoot.gameObject.SetActive(visible);
    }

    void ResumeMirrorPlayback()
    {
        if (!mirrorPlaybackPaused || !mirrorPlaybackCanResume || !IsMirrorForeground
            || mirrorResumeButton == null || !mirrorResumeButton.IsInteractable()) return;
        // Consume this offer before invoking the owner, which may immediately
        // hide the mirror or reconstruct the presentation from saved state.
        var resumed = mirrorPlaybackResumed;
        mirrorPlaybackResumed = null;
        mirrorPlaybackCanResume = false;
        mirrorResumeButton.interactable = false;
        resumed?.Invoke();
    }

    public void SetMirrorStage(bool locked, bool dim, OSWindowUnit live, OSWindowUnit link, OSWindowUnit settings)
    {
        RequireInitialized();
        mirrorStageLocked = locked;
        if (locked && mirrorInputShield == null)
        {
            mirrorInputShield = Root("Chapter03MirrorInputShield", ZoomRoot);
            mirrorInputShield.sizeDelta = ZoomRoot.rect.size * 2f;
            var canvas = mirrorInputShield.gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingLayerID = rootCanvas.sortingLayerID;
            canvas.sortingOrder = rootCanvas.sortingOrder + 1;
            canvas.worldCamera = rootCanvas.worldCamera;
            mirrorInputShield.gameObject.AddComponent<GraphicRaycaster>();
            var shield = mirrorInputShield.gameObject.AddComponent<Image>();
            shield.color = Color.clear;
            shield.raycastTarget = true;
        }
        if (mirrorInputShield != null)
        {
            mirrorInputShield.sizeDelta = ZoomRoot.rect.size * 2f;
            RefreshMirrorInputShield();
        }
        if (dim)
        {
            foreach (var window in windowParent.GetComponentsInChildren<OSWindowUnit>(true))
            {
                if (window == live || window == link || window == settings
                    || live != null && window.transform.IsChildOf(live.transform)
                    || settings != null && window.transform.IsChildOf(settings.transform)) continue;
                var ancestor = window.transform.parent != null
                    ? window.transform.parent.GetComponentInParent<OSWindowUnit>() : null;
                if (ancestor != null && ancestor.transform.IsChildOf(windowParent)) continue;
                DimMirrorBackground(window.transform);
            }
            if (mirrorRoot != null) DimMirrorBackground(mirrorRoot);
        }
        else RestoreMirrorBackground();
        mirrorStageDimmed = dim;
    }

    void RefreshMirrorInputShield()
    {
        if (mirrorInputShield != null)
            mirrorInputShield.gameObject.SetActive(mirrorStageLocked && !paused && os != null && os.IsOSVisible);
    }

    void DimMirrorBackground(Transform target)
    {
        var group = target.GetComponent<CanvasGroup>();
        foreach (var state in mirrorDimStates)
            if (state.group == group && group != null)
            {
                if (state.shade != null)
                {
                    state.shade.color = OSWindowActivityDimming.DimColor;
                    state.shade.transform.SetAsLastSibling();
                }
                return;
            }
        bool added = group == null;
        if (added) group = target.gameObject.AddComponent<CanvasGroup>();
        // Keep the opaque black panels intact: lowering their group alpha
        // exposes the lighter graphics behind them instead of darkening them.
        var shade = Image((RectTransform)target, "Chapter03MirrorDim", OSWindowActivityDimming.DimColor, Vector2.zero, Vector2.zero);
        Stretch(shade.rectTransform);
        mirrorDimStates.Add(new MirrorDimState { group = group, shade = shade, alpha = group.alpha,
            interactable = group.interactable, raycasts = group.blocksRaycasts, added = added });
        group.interactable = false;
        group.blocksRaycasts = false;
    }

    void RestoreMirrorBackground()
    {
        foreach (var state in mirrorDimStates)
        {
            if (state.shade != null)
            {
                state.shade.gameObject.SetActive(false);
                Destroy(state.shade.gameObject);
            }
            if (state.group == null) continue;
            state.group.alpha = state.alpha;
            state.group.interactable = state.interactable;
            state.group.blocksRaycasts = state.raycasts;
            if (state.added) Destroy(state.group);
        }
        mirrorDimStates.Clear();
        mirrorStageDimmed = false;
    }

    void ResetMirrorStage()
    {
        mirrorPlaybackPaused = mirrorPlaybackCanResume = false;
        mirrorPlaybackResumed = null;
        if (mirrorResumeButton != null) mirrorResumeButton.onClick.RemoveAllListeners();
        DestroyRoot(mirrorPlaybackRoot);
        mirrorPlaybackRoot = null; mirrorPlaybackCanvas = null; mirrorResumeButton = null;
        RestoreMirrorBackground();
        DestroyRoot(mirrorInputShield);
        mirrorInputShield = null;
        mirrorStageLocked = false;
    }
}
