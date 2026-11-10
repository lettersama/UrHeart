using UnityEngine;

public sealed partial class OS_Handle
{
    OSChapter02PaperPushDirector chapter02PaperPush;
    bool chapter02PaperPushProtected, chapter02PaperPushAnimating;
    public OSChapter02PaperPushDirector Chapter02PaperPush => chapter02PaperPush != null
        ? chapter02PaperPush : (chapter02PaperPush = GetComponent<OSChapter02PaperPushDirector>());
    public bool IsChapter02PaperPushStoryProtected => Chapter02PaperPush?.IsStoryProtected == true;
    public bool IsChapter02PaperPushMessageProtected => Chapter02PaperPush?.IsMessageProtected == true;
    void RefreshChapter02PaperPushProtection()
    {
        bool protect = IsChapter02PaperPushStoryProtected;
        bool animating = Chapter02PaperPush?.IsBlockingInput == true;
        if (protect == chapter02PaperPushProtected && animating == chapter02PaperPushAnimating) return;
        chapter02PaperPushProtected = protect; chapter02PaperPushAnimating = animating;
        RefreshEvent01LinkCloseSuppression();
    }
}
