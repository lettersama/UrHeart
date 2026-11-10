using UnityEngine;
using UnityEngine.UI;

public sealed partial class OSChapter04FinalHealDirector
{
    Canvas settingsCanvas;
    GraphicRaycaster settingsRaycaster;
    bool settingsRaised, ownsSettingsCanvas, ownsSettingsRaycaster;
    bool previousSettingsOverride, previousSettingsCanvasEnabled, previousSettingsRaycasterEnabled;
    int previousSettingsOrder, previousSettingsLayer;

    // The treatment is above ordinary OS windows. Its pause menu must be
    // above that layer too, without changing the authored scene or window pin.
    void RefreshSettingsLayer()
    {
        bool open = settings != null && settings.IsOpen && !settings.IsMinimized && settings.gameObject.activeInHierarchy;
        if (!open) { RestoreSettingsLayer(); return; }
        if (settingsRaised) return;
        var parent = settings.transform.parent?.GetComponentInParent<Canvas>();
        if (parent == null) return;
        settingsCanvas = settings.GetComponent<Canvas>();
        ownsSettingsCanvas = settingsCanvas == null;
        if (settingsCanvas == null) settingsCanvas = settings.gameObject.AddComponent<Canvas>();
        previousSettingsOverride = settingsCanvas.overrideSorting;
        previousSettingsCanvasEnabled = settingsCanvas.enabled;
        previousSettingsOrder = settingsCanvas.sortingOrder;
        previousSettingsLayer = settingsCanvas.sortingLayerID;
        settingsCanvas.enabled = settingsCanvas.overrideSorting = true;
        settingsCanvas.sortingLayerID = parent.sortingLayerID;
        settingsCanvas.sortingOrder = OSChapter04FinalHealPresentation.SortingOrder + 10;
        if (ownsSettingsCanvas) settingsCanvas.worldCamera = parent.rootCanvas.worldCamera;
        settingsRaycaster = settings.GetComponent<GraphicRaycaster>();
        ownsSettingsRaycaster = settingsRaycaster == null;
        if (settingsRaycaster == null) settingsRaycaster = settings.gameObject.AddComponent<GraphicRaycaster>();
        previousSettingsRaycasterEnabled = settingsRaycaster.enabled;
        if (ownsSettingsRaycaster)
        {
            var source = parent.GetComponent<GraphicRaycaster>();
            if (source != null)
            {
                settingsRaycaster.ignoreReversedGraphics = source.ignoreReversedGraphics;
                settingsRaycaster.blockingObjects = source.blockingObjects;
                settingsRaycaster.blockingMask = source.blockingMask;
            }
        }
        settingsRaycaster.enabled = true;
        settingsRaised = true;
    }

    void RestoreSettingsLayer()
    {
        if (!settingsRaised) return;
        if (settingsRaycaster != null)
        {
            settingsRaycaster.enabled = previousSettingsRaycasterEnabled;
            if (ownsSettingsRaycaster) { settingsRaycaster.enabled = false; Destroy(settingsRaycaster); }
        }
        if (settingsCanvas != null)
        {
            settingsCanvas.overrideSorting = previousSettingsOverride;
            settingsCanvas.sortingOrder = previousSettingsOrder;
            settingsCanvas.sortingLayerID = previousSettingsLayer;
            settingsCanvas.enabled = previousSettingsCanvasEnabled;
            if (ownsSettingsCanvas) Destroy(settingsCanvas);
        }
        settingsCanvas = null; settingsRaycaster = null;
        settingsRaised = ownsSettingsCanvas = ownsSettingsRaycaster = false;
    }
}
