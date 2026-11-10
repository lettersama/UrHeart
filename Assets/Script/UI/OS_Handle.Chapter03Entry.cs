using UnityEngine;

public sealed partial class OS_Handle
{
    OSChapter03EntryDirector chapter03Entry;
    bool chapter03EntryProtected;
    public OSChapter03EntryDirector Chapter03Entry => chapter03Entry != null
        ? chapter03Entry : (chapter03Entry = GetComponent<OSChapter03EntryDirector>());
    public bool IsChapter03EntryStoryProtected => Chapter03Entry?.IsEngaged == true;
    public bool IsChapter03EntryMessageProtected => Chapter03Entry?.IsMessageProtected == true;

    void RefreshChapter03EntryProtection()
    {
        bool protect = IsChapter03EntryStoryProtected;
        if (protect == chapter03EntryProtected) return;
        chapter03EntryProtected = protect;
        RefreshEvent01LinkCloseSuppression();
    }
}
