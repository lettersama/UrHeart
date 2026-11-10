using System.Collections.Generic;
using UnityEngine;

public sealed partial class OS_Handle
{
    [Header("Chapter 04 TV")]
    [SerializeField] DesktopProcessUnit tvDesktopProcess;
    [SerializeField] OSWindowUnit tvWindow;
    [SerializeField] OSTVWindowController tvController;
    bool resolvingChapter04TV;
    OSProcessSaveData observedTVProcessState;
    OSMemoryProcessSaveData observedTVMemoryState;
    bool hasRefreshedTVState, observedTVInstalled;

    public DesktopProcessUnit TVDesktopIcon => tvDesktopProcess;
    public OSWindowUnit TVWindow => tvWindow;
    public OSTVWindowController TVController => tvController;

    internal void ResolveChapter04TVReferences()
    {
        if (resolvingChapter04TV) return;
        resolvingChapter04TV = true;
        try
        {
            Transform desktopRoot = transform.Find("DesktopProcessGroup");
            Transform windowsRoot = transform.Find("Windows");
            if (tvDesktopProcess == null && desktopRoot != null)
                tvDesktopProcess = desktopRoot.Find("DesktopProcessUnit_TV")?.GetComponent<DesktopProcessUnit>();
            if (tvWindow == null && windowsRoot != null)
                tvWindow = windowsRoot.Find("WindowUnit_TV")?.GetComponent<OSWindowUnit>();
            if (!Application.isPlaying) return;

            if (tvDesktopProcess == null && desktopRoot != null)
            {
                DesktopProcessUnit template = gramophoneDesktopProcess != null
                    ? gramophoneDesktopProcess : memoryDesktopProcess;
                if (template != null)
                {
                    // Inactive staging prevents the clone from registering the source identity.
                    var staging = new GameObject("TV Icon Staging");
                    staging.SetActive(false);
                    staging.transform.SetParent(desktopRoot, false);
                    GameObject clone = Instantiate(template.gameObject, staging.transform, false);
                    clone.SetActive(false);
                    clone.name = "DesktopProcessUnit_TV";
                    tvDesktopProcess = clone.GetComponent<DesktopProcessUnit>();
                    tvDesktopProcess.SetRequiresDoubleClick(false);
                    if (tvDesktopProcess.ProcessIcon != null)
                    {
                        tvDesktopProcess.ProcessIcon.sprite = OSTVProgramAssets.Icon;
                        // The cloned Gramophone uses an 18x23 rect. Keep TV's
                        // authored pixels square instead of stretching its sprite.
                        tvDesktopProcess.ProcessIcon.preserveAspect = true;
                        tvDesktopProcess.ProcessIcon.rectTransform.sizeDelta = OSTVProgramAssets.Icon.rect.size;
                    }
                    OSLocalizedText.SetRuntime(tvDesktopProcess.ProcessName, "ui.os.tv.title", "TV");
                    clone.transform.SetParent(desktopRoot, false);
                    Destroy(staging);
                }
            }
            if (tvWindow == null && windowsRoot != null)
            {
                tvController = OSTVWindowController.Create(windowsRoot,
                    gramophoneWindow != null ? gramophoneWindow : memoryWindow);
                tvWindow = tvController.GetComponent<OSWindowUnit>();
            }
            if (tvWindow != null && tvController == null)
                tvController = tvWindow.GetComponent<OSTVWindowController>();
            if (tvDesktopProcess != null)
            {
                tvDesktopProcess.SetRequiresDoubleClick(false);
                if (desktopProcessUnits == null) desktopProcessUnits = new List<DesktopProcessUnit>();
                if (!desktopProcessUnits.Contains(tvDesktopProcess)) desktopProcessUnits.Add(tvDesktopProcess);
            }
            if (tvWindow != null && !windowUnits.Contains(tvWindow)) windowUnits.Add(tvWindow);
        }
        finally { resolvingChapter04TV = false; }
    }

    internal void RefreshChapter04TV()
    {
        ResolveChapter04TVReferences();
        if (tvDesktopProcess == null) return;
        GameDataManager data = GameDataManager.Instance;
        OSProcessSaveData processState = data?.OSProcessData;
        OSMemoryProcessSaveData memoryState = processState?.memory;
        bool installed = data?.IsToolProgramInstalled(OSToolProgramIds.TV) == true;
        bool saveChanged = !ReferenceEquals(observedTVProcessState, processState)
            || !ReferenceEquals(observedTVMemoryState, memoryState);
        bool refreshState = !hasRefreshedTVState || saveChanged || observedTVInstalled != installed;
        observedTVProcessState = processState;
        observedTVMemoryState = memoryState;
        observedTVInstalled = installed;
        hasRefreshedTVState = true;
        if (tvDesktopProcess.gameObject.activeSelf != installed)
            tvDesktopProcess.gameObject.SetActive(installed);
        if (refreshState)
        {
            tvController?.RestoreSavedPresentation();
            if (installed)
            {
                InitializeDesktopProcessUnit(tvDesktopProcess);
                desktopProcessGrid?.EnsureDirectChildrenRegistered();
            }
        }
        if (!installed && tvWindow != null && tvWindow.IsOpen)
            tvWindow.CloseSilently();
    }

    public bool OpenTVWindow()
    {
        if (!IsOSVisible || IsSystemInputLocked
            || GameDataManager.Instance?.IsToolProgramInstalled(OSToolProgramIds.TV) != true)
            return false;
        ResolveChapter04TVReferences();
        if (Chapter04Entry != null && !Chapter04Entry.TryOpenTV()) return false;
        bool changed = OpenWindow(tvWindow, tvDesktopProcess, "TV");
        if (tvWindow != null && tvWindow.IsOpen && !tvWindow.IsMinimized)
        {
            Chapter04Entry?.OnTVOpened();
            Chapter04Story?.ResumeDialogue();
        }
        return changed;
    }
}
