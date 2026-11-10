using UnityEngine;

public sealed partial class OS_Handle
{
    OSChapter04EntryDirector chapter04Entry;
    int chapter04ProtectionStep = -1;
    public OSChapter04EntryDirector Chapter04Entry => chapter04Entry != null
        ? chapter04Entry : (chapter04Entry = GetComponent<OSChapter04EntryDirector>());
    public bool IsChapter04EntryStoryProtected => Chapter04Entry?.IsEngaged == true;
    public bool IsChapter04EntryMessageProtected => Chapter04Entry?.IsMessageProtected == true;

    void RefreshChapter04EntryProtection()
    {
        int step = Chapter04Entry?.IsEngaged == true ? (int)Chapter04Entry.Step : 0;
        if (step != chapter04ProtectionStep)
        {
            chapter04ProtectionStep = step;
            RefreshEvent01LinkCloseSuppression();
        }
        var installer = toolWindowController?.ProgramLibrary?.Installer;
        var installWindow = installer != null ? installer.GetComponent<OSWindowUnit>() : null;
        bool protectInstall = installer?.IsChapter04Installation == true;
        SetWindowCloseButtonSuppressed(installWindow, protectInstall);
        SetWindowCloseButtonSuppressed(installWindow, protectInstall, OSWindowButton.WindowAction.Minimize);
        SetWindowCloseButtonSuppressed(memoryWindow, Chapter04Entry?.BlocksDismissal(memoryWindow) == true);
        SetWindowCloseButtonSuppressed(memoryWindow, Chapter04Entry?.BlocksDismissal(memoryWindow) == true, OSWindowButton.WindowAction.Minimize);
    }

    public void NotifyChapter04Started() => ChapterStarted?.Invoke(4);
}
