using UnityEngine;
using UnityEngine.UI;

public sealed partial class OSCursorExperimentController
{
    OSWindowUnit settingsWindow;
    Canvas settingsCanvas;
    GraphicRaycaster settingsRaycaster;
    GameObject settingsInputBlocker;
    bool settingsOverlayPrepared, ownsSettingsCanvas, ownsSettingsRaycaster;
    bool settingsOriginalOverrideSorting, settingsOriginalCanvasEnabled, settingsOriginalRaycasterEnabled;
    int settingsOriginalOrder, settingsOriginalLayer;

    public bool IsSettingsOpen => settingsWindow != null && settingsWindow.IsOpen
        && !settingsWindow.IsMinimized && settingsWindow.gameObject.activeInHierarchy;
    public bool IsSettingsModalActive => settingsOverlayPrepared && IsSettingsOpen;

    /// <summary>Called by the normal OS settings launcher, including keyboard Escape.</summary>
    public void NotifySettingsWindow(OSWindowUnit optionWindow)
    {
        if (settingsWindow != optionWindow)
        {
            RestoreSettingsPresentation();
            settingsWindow = optionWindow;
        }
        TickSettingsPause();
    }

    void ResolveSettingsPause()
    {
        if (settingsWindow == null && OS_Handle.Instance != null)
            settingsWindow = OS_Handle.Instance.transform.Find("Windows/WindowUnit_Option")?.GetComponent<OSWindowUnit>();
        TickSettingsPause();
    }

    void TickSettingsPause()
    {
        if (!IsActive || !IsSettingsOpen)
        {
            if (settingsOverlayPrepared)
            {
                RestoreSettingsPresentation();
                inputWarmup = true;
            }
            return;
        }
        if (!settingsOverlayPrepared)
        {
            SaveCheckpoint();
            ReleasePointer();
            paused = true;
            inputWarmup = true;
            PrepareSettingsPresentation();
        }
    }

    void PrepareSettingsPresentation()
    {
        if (settingsWindow == null || settingsOverlayPrepared) return;
        Canvas parentCanvas = settingsWindow.transform.parent != null
            ? settingsWindow.transform.parent.GetComponentInParent<Canvas>() : null;
        if (parentCanvas == null) return;
        settingsCanvas = settingsWindow.GetComponent<Canvas>();
        ownsSettingsCanvas = settingsCanvas == null;
        if (settingsCanvas == null) settingsCanvas = settingsWindow.gameObject.AddComponent<Canvas>();
        settingsOriginalOverrideSorting = settingsCanvas.overrideSorting;
        settingsOriginalCanvasEnabled = settingsCanvas.enabled;
        settingsOriginalOrder = settingsCanvas.sortingOrder;
        settingsOriginalLayer = settingsCanvas.sortingLayerID;
        settingsCanvas.enabled = true;
        settingsCanvas.overrideSorting = true;
        settingsCanvas.sortingLayerID = parentCanvas.sortingLayerID;
        settingsCanvas.sortingOrder = parentCanvas.sortingOrder + 20;
        if (ownsSettingsCanvas) settingsCanvas.worldCamera = parentCanvas.worldCamera;

        settingsRaycaster = settingsWindow.GetComponent<GraphicRaycaster>();
        ownsSettingsRaycaster = settingsRaycaster == null;
        if (settingsRaycaster == null) settingsRaycaster = settingsWindow.gameObject.AddComponent<GraphicRaycaster>();
        settingsOriginalRaycasterEnabled = settingsRaycaster.enabled;
        if (ownsSettingsRaycaster) CopyRaycasterSettings(parentCanvas.GetComponent<GraphicRaycaster>(), settingsRaycaster);
        settingsRaycaster.enabled = true;

        // A transparent, independently sorted cover blocks the desktop, while
        // leaving the settings controls on the layer immediately above it.
        settingsInputBlocker = new GameObject("CursorExperimentSettingsInputCover", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster), typeof(UnityEngine.UI.Image));
        settingsInputBlocker.layer = parentCanvas.gameObject.layer;
        var rect = (RectTransform)settingsInputBlocker.transform;
        rect.SetParent(parentCanvas.transform, false);
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        var coverCanvas = settingsInputBlocker.GetComponent<Canvas>();
        coverCanvas.overrideSorting = true;
        coverCanvas.sortingLayerID = parentCanvas.sortingLayerID;
        coverCanvas.sortingOrder = settingsCanvas.sortingOrder - 1;
        coverCanvas.worldCamera = parentCanvas.worldCamera;
        CopyRaycasterSettings(parentCanvas.GetComponent<GraphicRaycaster>(), settingsInputBlocker.GetComponent<GraphicRaycaster>());
        var coverImage = settingsInputBlocker.GetComponent<UnityEngine.UI.Image>();
        coverImage.color = Color.clear; coverImage.raycastTarget = true;
        settingsOverlayPrepared = true;
    }

    static void CopyRaycasterSettings(GraphicRaycaster source, GraphicRaycaster target)
    {
        if (source == null || target == null) return;
        target.ignoreReversedGraphics = source.ignoreReversedGraphics;
        target.blockingObjects = source.blockingObjects;
        target.blockingMask = source.blockingMask;
    }

    void RestoreSettingsPresentation()
    {
        if (!settingsOverlayPrepared) return;
        if (settingsInputBlocker != null)
        {
            settingsInputBlocker.SetActive(false);
            Destroy(settingsInputBlocker);
        }
        if (settingsRaycaster != null)
        {
            settingsRaycaster.enabled = settingsOriginalRaycasterEnabled;
            if (ownsSettingsRaycaster) { settingsRaycaster.enabled = false; Destroy(settingsRaycaster); }
        }
        if (settingsCanvas != null)
        {
            settingsCanvas.overrideSorting = settingsOriginalOverrideSorting;
            settingsCanvas.sortingOrder = settingsOriginalOrder;
            settingsCanvas.sortingLayerID = settingsOriginalLayer;
            settingsCanvas.enabled = settingsOriginalCanvasEnabled;
            if (ownsSettingsCanvas) Destroy(settingsCanvas);
        }
        settingsInputBlocker = null; settingsCanvas = null; settingsRaycaster = null;
        settingsOverlayPrepared = false;
        ownsSettingsCanvas = ownsSettingsRaycaster = false;
    }
}
