using MrTGame.Narrative;
using UnityEngine;

public sealed partial class OSMemorySubwindowPlayer
{
    const string CollectingForegroundSequenceId = "chapter02.materials.collecting";

    OSMemorySubwindowSequence foregroundSequence;
    OS_Handle foregroundOS;
    OSWindowUnit foregroundOwnerWindow, foregroundSettingsWindow;
    Canvas foregroundCanvas;
    bool foregroundReturnedToMemory, foregroundStoryTookOver;
    bool foregroundOpeningFinished;
    bool foregroundCanvasCaptured;
    bool foregroundOriginalEnabled, foregroundOriginalOverrideSorting;
    int foregroundOriginalSortingLayer, foregroundOriginalSortingOrder;

    // The pool remains a child of Memory. A nested canvas changes drawing order
    // only, without changing window focus, pinning, layout or saved state.
    void BeginForeground(OSMemorySubwindowSequence sequence)
    {
        EndForeground();
        if (sequence == null || poolRoot == null)
            return;

        foregroundSequence = sequence;
        foregroundOwnerWindow = GetComponent<OSWindowUnit>();
        BindForegroundOS();
        RefreshForeground();
    }

    void EndForeground()
    {
        RestoreForegroundCanvas();
        if (foregroundOS != null)
            foregroundOS.MouseActiveWindowChanged -= HandleForegroundMouseActiveWindowChanged;
        foregroundOS = null;
        foregroundOwnerWindow = null;
        foregroundSettingsWindow = null;
        foregroundSequence = null;
        foregroundReturnedToMemory = false;
        foregroundStoryTookOver = false;
        foregroundOpeningFinished = false;
    }

    public void NotifyReadingAdvanced()
    {
        if (foregroundSequence == null || !foregroundSequence.KeepOpenUntilCancelled)
            return;
        foregroundReturnedToMemory = true;
        RefreshForeground();
    }

    void LateUpdate()
    {
        if (foregroundSequence == null)
            return;
        if (currentSequence != foregroundSequence)
        {
            EndForeground();
            return;
        }
        BindForegroundOS();
        RefreshForeground();
    }

    void BindForegroundOS()
    {
        OS_Handle next = OS_Handle.Instance;
        if (foregroundOS == next)
            return;
        if (foregroundOS != null)
            foregroundOS.MouseActiveWindowChanged -= HandleForegroundMouseActiveWindowChanged;
        foregroundOS = next;
        foregroundSettingsWindow = null;
        if (foregroundOS != null)
            foregroundOS.MouseActiveWindowChanged += HandleForegroundMouseActiveWindowChanged;
    }

    void HandleForegroundMouseActiveWindowChanged(OSWindowUnit focusedWindow)
    {
        // A null focus also occurs while hiding the OS or restoring a session.
        // Only an actual transfer to another ordinary window consumes the hold.
        if (focusedWindow == null || foregroundSequence == null
            || !foregroundSequence.KeepOpenUntilCancelled || !ForegroundEnvironmentAvailable())
            return;
        if (focusedWindow == foregroundOwnerWindow
            || (foregroundOwnerWindow != null
                && focusedWindow.transform.IsChildOf(foregroundOwnerWindow.transform)))
            return;
        if (focusedWindow == foregroundSettingsWindow)
            return;

        foregroundReturnedToMemory = true;
        RefreshForeground();
    }

    void RefreshForeground()
    {
        if (foregroundSequence == null || poolRoot == null)
        {
            RestoreForegroundCanvas();
            return;
        }

        if (foregroundSequence.SequenceId == CollectingForegroundSequenceId && foregroundOS != null
            && (foregroundOS.IsChapter02CollectionMessageProtected
                || foregroundOS.IsChapter02ClawOfferProtected
                || foregroundOS.IsClawMachineMessageProtected
                || foregroundOS.IsCursorExperimentMessageProtected
                || foregroundOS.IsCursorExperimentStoryProtected))
        {
            // The collection illustration must not cover the later Live/pointer
            // introduction again after that story briefly hides or changes beats.
            foregroundReturnedToMemory = true;
            foregroundStoryTookOver = true;
        }

        if (!foregroundOpeningFinished)
        {
            bool allOpen = true;
            for (int i = 0; i < foregroundSequence.Shots.Count; i++)
            {
                if (i >= pool.Length || pool[i] == null || !pool[i].IsFullyOpen)
                {
                    allOpen = false;
                    break;
                }
            }
            foregroundOpeningFinished = allOpen;
        }

        bool requested = !foregroundSequence.KeepOpenUntilCancelled
            || foregroundSequence.SequenceId == OSMemoryWarehouseDoorPresentation.SequenceId
            || !foregroundReturnedToMemory
            || (!foregroundStoryTookOver && !foregroundOpeningFinished)
            || IsForegroundMotionActive();

        if (requested && ForegroundEnvironmentAvailable())
            ApplyForegroundCanvas();
        else
            RestoreForegroundCanvas();
    }

    bool IsForegroundMotionActive()
    {
        OSMemoryShotContent content = CurrentContent;
        if (content == null)
            return false;

        var map = content.GetComponent<OSMemoryMapPresentation>();
        if (map != null)
        {
            switch (map.Phase)
            {
                case OSMemoryMapPresentation.MapPhase.HandEntering:
                case OSMemoryMapPresentation.MapPhase.FirstTap:
                case OSMemoryMapPresentation.MapPhase.DoubleTap:
                case OSMemoryMapPresentation.MapPhase.HandLeaving:
                case OSMemoryMapPresentation.MapPhase.Route:
                case OSMemoryMapPresentation.MapPhase.Arrival:
                case OSMemoryMapPresentation.MapPhase.FinalHold:
                    return true;
            }
        }
        var face = content.GetComponent<OSMemoryFaceRepairPresentation>();
        return face != null && face.IsPlayingAction;
    }

    bool ForegroundEnvironmentAvailable()
    {
        if (!Application.isPlaying || !isActiveAndEnabled
            || poolRoot == null || !poolRoot.gameObject.activeInHierarchy
            || foregroundOS == null || !foregroundOS.IsOSVisible
            || OSSessionPersistence.IsRestoring || GameSessionFlow.Instance?.IsBusy == true
            || GameTestConsole.BlocksGameplayInput
            || foregroundOwnerWindow == null || !foregroundOwnerWindow.IsOpen
            || foregroundOwnerWindow.IsMinimized || foregroundOwnerWindow.IsClosing)
            return false;

        if (foregroundSettingsWindow == null)
            foregroundSettingsWindow = foregroundOS.transform.Find("Windows/WindowUnit_Option")
                ?.GetComponent<OSWindowUnit>();
        return foregroundSettingsWindow == null || !foregroundSettingsWindow.IsOpen
            || foregroundSettingsWindow.IsMinimized
            || !foregroundSettingsWindow.gameObject.activeInHierarchy;
    }

    Canvas FindForegroundParentCanvas()
    {
        Canvas effective = null;
        for (Transform parent = poolRoot.parent; parent != null; parent = parent.parent)
        {
            Canvas candidate = parent.GetComponent<Canvas>();
            if (candidate == null || !candidate.isActiveAndEnabled)
                continue;
            effective = candidate;
            if (candidate.overrideSorting)
                break;
        }
        return effective;
    }

    void ApplyForegroundCanvas()
    {
        Canvas parentCanvas = FindForegroundParentCanvas();
        if (parentCanvas == null)
        {
            RestoreForegroundCanvas();
            return;
        }
        if (foregroundCanvas == null)
        {
            foregroundCanvas = poolRoot.GetComponent<Canvas>();
            if (foregroundCanvas == null)
            {
                foregroundCanvas = poolRoot.gameObject.AddComponent<Canvas>();
                foregroundCanvas.overrideSorting = false;
                // Keep the content's canvas alive when returning to Memory's
                // inherited sorting; disabling it can suppress its graphics.
                foregroundCanvas.enabled = true;
            }
        }
        if (!foregroundCanvasCaptured)
        {
            foregroundOriginalEnabled = foregroundCanvas.enabled;
            foregroundOriginalOverrideSorting = foregroundCanvas.overrideSorting;
            foregroundOriginalSortingLayer = foregroundCanvas.sortingLayerID;
            foregroundOriginalSortingOrder = foregroundCanvas.sortingOrder;
            foregroundCanvasCaptured = true;
        }
        foregroundCanvas.sortingLayerID = parentCanvas.sortingLayerID;
        foregroundCanvas.sortingOrder = parentCanvas.sortingOrder + 1;
        foregroundCanvas.overrideSorting = true;
        foregroundCanvas.enabled = true;
    }

    void RestoreForegroundCanvas()
    {
        if (!foregroundCanvasCaptured)
            return;
        foregroundCanvasCaptured = false;
        if (foregroundCanvas == null)
            return;
        foregroundCanvas.overrideSorting = false;
        foregroundCanvas.sortingLayerID = foregroundOriginalSortingLayer;
        foregroundCanvas.sortingOrder = foregroundOriginalSortingOrder;
        foregroundCanvas.overrideSorting = foregroundOriginalOverrideSorting;
        foregroundCanvas.enabled = foregroundOriginalEnabled;
    }
}
