using UnityEngine;

public sealed partial class OS_Handle
{
    OSMotherHeartWindowController motherHeartController;
    OSChapter04GoldenKeyWindow motherKeyController;
    bool resolvingMotherHeart;
    OSChapter04StoryState motherHeartAccessState;
    bool motherHeartAccessUnlocked;
    int motherHeartAccessChapter = -1;

    public OSMotherHeartWindowController MotherHeartController => motherHeartController;
    public OSChapter04GoldenKeyWindow MotherKeyController => motherKeyController;
    public DesktopProcessUnit MotherHeartDesktopIcon => lockHeartDesktopProcess;
    public bool IsMotherHeartUnlocked => GameDataManager.Instance?.OSProcessData.memory.chapter04Story != null
        && GameDataManager.Instance.OSProcessData.memory.chapter04Story.motherGiftStage >= OSChapter04MotherGiftStage.Unlocked;

    internal void ResolveChapter04MotherHeartReferences()
    {
        if (resolvingMotherHeart || !Application.isPlaying) return;
        resolvingMotherHeart = true;
        try
        {
            Transform parent = transform.Find("Windows");
            if (parent == null) return;
            OSWindowUnit template = gramophoneWindow != null ? gramophoneWindow : memoryWindow;
            if (motherHeartController == null)
            {
                motherHeartController = OSMotherHeartWindowController.Create(parent, template);
                RegisterMotherGiftWindow(motherHeartController.Window);
            }
            if (motherKeyController == null && firstHeartHealSpecialKey != null && lockHeartDesktopProcess != null)
            {
                var keyImage = firstHeartHealSpecialKey.GetComponent<UnityEngine.UI.Image>();
                motherKeyController = OSChapter04GoldenKeyWindow.Create(parent, template,
                    keyImage != null ? keyImage.sprite : null, (RectTransform)lockHeartDesktopProcess.transform);
                motherKeyController.CanUseKey = () => Chapter04Story?.CanUseMotherKey == true;
                motherKeyController.TryUnlock = () => Chapter04Story?.TryUnlockMotherHeart() == true;
                RegisterMotherGiftWindow(motherKeyController.Window);
            }
        }
        finally { resolvingMotherHeart = false; }
    }

    void RegisterMotherGiftWindow(OSWindowUnit window)
    {
        if (window == null) return;
        if (!windowUnits.Contains(window)) windowUnits.Add(window);
        window.OpenStarted -= HandleWindowOpenStarted;
        window.OpenStarted += HandleWindowOpenStarted;
        window.Closed -= HandleWindowClosed;
        window.Closed += HandleWindowClosed;
        window.LayoutChanged -= HandleWindowLayoutChanged;
        window.LayoutChanged += HandleWindowLayoutChanged;
        window.AlwaysOnTopChanged -= HandleWindowAlwaysOnTopChanged;
        window.AlwaysOnTopChanged += HandleWindowAlwaysOnTopChanged;
    }

    internal void RefreshMotherHeartAccess()
    {
        var data = GameDataManager.Instance;
        var state = data?.OSProcessData.memory.chapter04Story;
        int chapter = data != null ? data.CurrentChapterIndex : -1;
        bool unlocked = IsMotherHeartUnlocked;
        if (ReferenceEquals(state, motherHeartAccessState) && motherHeartAccessUnlocked == unlocked
            && motherHeartAccessChapter == chapter) return;
        motherHeartAccessState = state;
        motherHeartAccessUnlocked = unlocked;
        motherHeartAccessChapter = chapter;
        if (chapter >= 4) SetDesktopProcessLocked(OSDesktopProcessKeys.LockHeart, !unlocked);
        if (unlocked) ReleaseLockHeartIconGlitch();
        else EnsureLockHeartIconGlitch();
        if (!unlocked && motherHeartController?.Window.IsOpen == true)
            motherHeartController.Window.CloseSilently();
    }

    public bool OpenMotherHeartWindow()
    {
        if (!IsMotherHeartUnlocked || !IsOSVisible || IsSystemInputLocked
            || OSSessionPersistence.IsRestoring) return false;
        ResolveChapter04MotherHeartReferences();
        RefreshMotherHeartAccess();
        var window = motherHeartController != null ? motherHeartController.Window : null;
        if (window == null) return false;
        OpenWindow(window, lockHeartDesktopProcess, "???");
        if (window.IsOpen && window.IsMinimized) window.RestoreAndBringToFront();
        return window.IsOpen && !window.IsClosing;
    }
}
