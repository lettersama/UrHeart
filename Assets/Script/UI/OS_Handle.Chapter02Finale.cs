using UnityEngine;

public sealed partial class OS_Handle
{
    OSChapter02FinaleDirector chapter02Finale;
    bool chapter02FinaleProtectionActive;

    public OSChapter02FinaleDirector Chapter02Finale => chapter02Finale != null
        ? chapter02Finale : (chapter02Finale = GetComponent<OSChapter02FinaleDirector>());
    public bool IsChapter02FinaleStoryProtected => Chapter02Finale?.IsStoryProtected == true;
    public bool IsChapter02FinaleMessageProtected => Chapter02Finale?.IsMessageProtected == true;
    public bool IsChapter02FinaleHiddenTextActive => Chapter02Finale?.IsHiddenTextActive == true;

    void ResolveChapter02FinaleReferences()
    {
        if (!Application.isPlaying) return;
        if (GetComponent<OSChapter02HiddenMemoryPresentation>() == null)
            gameObject.AddComponent<OSChapter02HiddenMemoryPresentation>();
        if (Chapter02Finale == null)
            chapter02Finale = gameObject.AddComponent<OSChapter02FinaleDirector>();
        if (Chapter02PaperPush == null)
            chapter02PaperPush = gameObject.AddComponent<OSChapter02PaperPushDirector>();
        if (Chapter02FinalTalk == null)
            chapter02FinalTalk = gameObject.AddComponent<OSChapter02FinalTalkDirector>();
        if (Chapter03Entry == null)
            chapter03Entry = gameObject.AddComponent<OSChapter03EntryDirector>();
    }

    void RefreshChapter02FinaleProtection()
    {
        bool protect = IsChapter02FinaleStoryProtected;
        if (chapter02FinaleProtectionActive == protect) return;
        chapter02FinaleProtectionActive = protect;
        RefreshEvent01LinkCloseSuppression();
    }
}
