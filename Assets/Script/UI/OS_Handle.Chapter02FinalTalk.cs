using UnityEngine;

public sealed partial class OS_Handle
{
    OSChapter02FinalTalkDirector chapter02FinalTalk;
    OSChapter02DemoEnding chapter02DemoEnding;
    bool chapter02FinalTalkProtected;
    public OSChapter02FinalTalkDirector Chapter02FinalTalk => chapter02FinalTalk != null
        ? chapter02FinalTalk : (chapter02FinalTalk = GetComponent<OSChapter02FinalTalkDirector>());
    public OSChapter02DemoEnding Chapter02DemoEnding => chapter02DemoEnding != null
        ? chapter02DemoEnding : (chapter02DemoEnding = GetComponent<OSChapter02DemoEnding>());
    public bool IsChapter02FinalTalkStoryProtected => Chapter02FinalTalk?.IsStoryProtected == true;
    public bool IsChapter02FinalTalkMessageProtected => Chapter02FinalTalk?.IsMessageProtected == true;

    void RefreshChapter02FinalTalkProtection()
    {
        bool protect = IsChapter02FinalTalkStoryProtected;
        if (protect == chapter02FinalTalkProtected) return;
        chapter02FinalTalkProtected = protect;
        RefreshEvent01LinkCloseSuppression();
    }
}
