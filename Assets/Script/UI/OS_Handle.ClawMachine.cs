using System.Collections;
using UnityEngine;

public sealed partial class OS_Handle
{
    [Header("Chapter 02 Claw Machine")]
    [SerializeField] OSWindowUnit clawMachineWindow;
    [SerializeField] OSClawMachineAppWindowController clawMachineApp;
    [SerializeField] DesktopProcessUnit clawMachineDesktopProcess;
    [SerializeField] OSDesktopProcessGrid clawMachineSourceGrid;
    [SerializeField] OSWindowUnit clawMachineGameWindow;
    [SerializeField] OSClawMachineGameWindowController clawMachineGame;
    Coroutine clawMachineOpenRoutine;
    OSClawMachineStoryDirector clawMachineStory;

    public OSClawMachineAppWindowController ClawMachineApp => clawMachineApp;
    public OSClawMachineGameWindowController ClawMachineGame => clawMachineGame;
    public OSClawMachineStoryDirector ClawMachineStory => clawMachineStory != null
        ? clawMachineStory : (clawMachineStory = GetComponent<OSClawMachineStoryDirector>());
    public bool IsClawMachineMessageProtected => ClawMachineStory != null && ClawMachineStory.IsMessageProtected;
    public bool IsChapter02ClawOfferProtected => IsChapter02ClawMachineDiscovered()
        && !IsChapter02ClawMachineOnDesktop() && clawMachineWindow != null && clawMachineWindow.IsOpen;

    bool IsChapter02ClawMachineDiscovered()
    {
        GameDataManager data = GameDataManager.Instance;
        return data != null && data.CurrentChapterIndex == 2
            && data.OSProcessData.memory.chapter02ClawMachineDiscovered;
    }

    bool IsChapter02ClawMachineOnDesktop()
    {
        GameDataManager data = GameDataManager.Instance;
        return IsChapter02ClawMachineDiscovered()
            && data.OSProcessData.memory.chapter02ClawMachineOnDesktop;
    }

    public bool RequestChapter02ClawMachineLaunch()
    {
        ResolveClawMachineReferences();
        if (GameDataManager.Instance?.OSProcessData.memory.chapter02CursorExperimentRound > 0) return false;
        if (!IsChapter02ClawMachineDiscovered() || !IsOSVisible || IsSystemInputLocked
            || clawMachineApp == null || clawMachineGameWindow == null || clawMachineGame == null)
            return false;

        OpenWindow(clawMachineGameWindow, clawMachineDesktopProcess,
            "WindowUnit_ClawMachineGame");
        if (!clawMachineGameWindow.IsOpen || clawMachineGameWindow.IsMinimized)
            return false;

        // The normal minimize path keeps the memory cursor and taskbar entry,
        // and its controller also cancels the companion 1-bit presentation.
        if (memoryWindow != null && memoryWindow.IsOpen && !memoryWindow.IsMinimized)
            memoryWindow.Minimize();

        clawMachineGame.RefreshLanguage();
        clawMachineGameWindow.BringToFront();
        NotifyWindowLauncherPressed(clawMachineGameWindow);
        clawMachineApp.RequestLaunch();
        return true;
    }

    public void OpenChapter02ClawMachineWindow(bool animate = true)
    {
        ResolveClawMachineReferences();
        if (clawMachineWindow == null || clawMachineApp == null || !IsChapter02ClawMachineDiscovered()) return;
        if (GameDataManager.Instance.OSProcessData.memory.chapter02CursorExperimentRound > 0)
        {
            clawMachineWindow.PrepareRuntimeClosed();
            if (clawMachineDesktopProcess != null) clawMachineDesktopProcess.gameObject.SetActive(false);
            return;
        }

        if (clawMachineDesktopProcess != null)
        {
            if (IsChapter02ClawMachineOnDesktop())
                clawMachineSourceGrid?.Remove(clawMachineDesktopProcess);
            InitializeDesktopProcessUnit(clawMachineDesktopProcess);
            // A pre-discovery icon can already have a locked state in the save.
            // Discovery must clear that receipt as well as showing the object.
            SetDesktopProcessLocked(clawMachineDesktopProcess, false);
            clawMachineDesktopProcess.gameObject.SetActive(true);
            if (IsChapter02ClawMachineOnDesktop())
                EnsureDesktopProcessLayout(clawMachineDesktopProcess);
            else
                PutClawMachineEntryBackInOffer();
        }
        clawMachineApp.RefreshLanguage();
        if (clawMachineOpenRoutine != null)
        {
            StopCoroutine(clawMachineOpenRoutine);
            clawMachineOpenRoutine = null;
        }
        // Once extracted, the same entry lives on the desktop (or in a folder).
        // Restoring a save must not display an empty offer window beside Live.
        if (IsChapter02ClawMachineOnDesktop())
        {
            clawMachineWindow.PrepareRuntimeClosed();
            RefreshEvent01LinkCloseSuppression();
            return;
        }
        bool alreadyVisible = clawMachineWindow.IsOpen && !clawMachineWindow.IsMinimized;
        if (alreadyVisible)
        {
            clawMachineWindow.SetAlwaysOnTop(true);
            clawMachineWindow.BringToFront();
            NotifyWindowLauncherPressed(clawMachineWindow);
            RefreshEvent01LinkCloseSuppression();
            return;
        }

        // EntryPresentationStarted runs before the new LinkMessage has finished
        // its layout. Open on the next frame so its actual box is the anchor.
        // Delaying the opening also avoids fighting OSWindowUnit's height tween.
        clawMachineOpenRoutine = StartCoroutine(OpenClawMachineAfterMessageLayout(animate));
    }

    IEnumerator OpenClawMachineAfterMessageLayout(bool animate)
    {
        yield return null;
        clawMachineOpenRoutine = null;
        if (clawMachineWindow == null || clawMachineApp == null || !IsChapter02ClawMachineDiscovered()
            || IsChapter02ClawMachineOnDesktop())
            yield break;

        OSWindowUnit messageWindow = linkMessageController?.LinkWindow;
        bool messageVisible = messageWindow != null && messageWindow.IsOpen && !messageWindow.IsMinimized;
        if (messageVisible)
            messageWindow.GetComponent<OSLinkMessageWindowLayout>()?.RefreshLayout();
        clawMachineApp.PositionBesideMessage(messageVisible ? messageWindow.WindowRect : null,
            characterWindow?.WindowRect, osCanvas != null ? osCanvas.transform as RectTransform : null);
        bool opened = OpenWindow(clawMachineWindow, clawMachineDesktopProcess,
            "WindowUnit_ClawMachine", animate ? .3f : 0f);
        if (clawMachineWindow.IsOpen)
        {
            // The collecting memory may itself be pinned. The discovered app
            // must still appear above it; the title-bar pin remains user-owned.
            clawMachineWindow.SetAlwaysOnTop(true);
            clawMachineWindow.BringToFront();
            clawMachineApp.PlayReveal(animate);
        }
        if (opened) NotifyWindowLauncherPressed(clawMachineWindow);
        RefreshEvent01LinkCloseSuppression();
    }

    // Future story progress uses this entry point; discovery alone never calls it.
    public bool PlaceChapter02ClawMachineOnDesktop()
    {
        ResolveClawMachineReferences();
        if (!IsChapter02ClawMachineDiscovered() || clawMachineDesktopProcess == null
            || desktopProcessGrid == null || folderProcessGrid == null) return false;

        GameDataManager data = GameDataManager.Instance;
        bool previouslyPlaced = data.OSProcessData.memory.chapter02ClawMachineOnDesktop;
        data.OSProcessData.memory.chapter02ClawMachineOnDesktop = true;
        clawMachineSourceGrid?.Remove(clawMachineDesktopProcess);
        clawMachineDesktopProcess.gameObject.SetActive(true);
        EnsureDesktopProcessLayout(clawMachineDesktopProcess);
        if (!desktopProcessGrid.Contains(clawMachineDesktopProcess)
            && !folderProcessGrid.Contains(clawMachineDesktopProcess))
        {
            data.OSProcessData.memory.chapter02ClawMachineOnDesktop = previouslyPlaced;
            if (!previouslyPlaced) PutClawMachineEntryBackInOffer();
            return false;
        }
        OnChapter02ClawMachineMovedToDesktop();
        CaptureDesktopProcessLayouts();
        return true;
    }

    void OnChapter02ClawMachineMovedToDesktop()
    {
        if (clawMachineOpenRoutine != null)
        {
            StopCoroutine(clawMachineOpenRoutine);
            clawMachineOpenRoutine = null;
        }
        clawMachineApp?.StopReveal();
        clawMachineWindow?.CloseFromScript();
        RefreshEvent01LinkCloseSuppression();
    }

    void PutClawMachineEntryBackInOffer()
    {
        if (clawMachineDesktopProcess == null || clawMachineSourceGrid == null) return;
        desktopProcessGrid?.Remove(clawMachineDesktopProcess);
        folderProcessGrid?.Remove(clawMachineDesktopProcess);
        _desktopLayoutUnits.Remove(clawMachineDesktopProcess);
        clawMachineSourceGrid.TryPlace(clawMachineDesktopProcess, 0, out _);
        clawMachineSourceGrid.ApplyLayout();
    }

    public void ResetChapter02ClawMachinePresentation()
    {
        ResetCursorExperimentPresentation();
        ClawMachineGame?.SuspendCursorTrap();
        ClawMachineStory?.ResetPresentation();
        if (clawMachineOpenRoutine != null)
        {
            StopCoroutine(clawMachineOpenRoutine);
            clawMachineOpenRoutine = null;
        }
        ResolveClawMachineReferences();
        clawMachineApp?.StopReveal();
        clawMachineWindow?.PrepareRuntimeClosed();
        clawMachineGameWindow?.PrepareRuntimeClosed();
        PutClawMachineEntryBackInOffer();
        if (clawMachineDesktopProcess != null)
            clawMachineDesktopProcess.gameObject.SetActive(false);
        RefreshEvent01LinkCloseSuppression();
    }

    void ResolveClawMachineReferences()
    {
        if (clawMachineGameWindow == null)
            clawMachineGameWindow = transform.Find("Windows/WindowUnit_ClawMachineGame")?.GetComponent<OSWindowUnit>();
        if (clawMachineGame == null && clawMachineGameWindow != null)
            clawMachineGame = clawMachineGameWindow.GetComponent<OSClawMachineGameWindowController>();
        if (clawMachineWindow == null)
            clawMachineWindow = transform.Find("Windows/WindowUnit_ClawMachine")?.GetComponent<OSWindowUnit>();
        if (clawMachineApp == null && clawMachineWindow != null)
            clawMachineApp = clawMachineWindow.GetComponent<OSClawMachineAppWindowController>();
        if (clawMachineSourceGrid == null && clawMachineWindow != null)
            clawMachineSourceGrid = clawMachineWindow.GetComponentInChildren<OSDesktopProcessGrid>(true);
        if (clawMachineDesktopProcess == null)
            clawMachineDesktopProcess = clawMachineSourceGrid != null
                ? clawMachineSourceGrid.GetComponentInChildren<DesktopProcessUnit>(true)
                : transform.Find("DesktopProcessGroup/DesktopProcessUnit_ClawMachine")?.GetComponent<DesktopProcessUnit>();
    }
}
