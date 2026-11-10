using UnityEngine;

public sealed partial class OSChapter04StoryDirector
{
    OSWindowUnit conversationTVWindow;
    bool ownsConversationTVLayout;

    // The 640x360 desktop fits the expanded Live frame and TV side by side.
    // This is a chapter composition, so ordinary player layouts stay in saves.
    void EnsureConversationTVLayout()
    {
        var tv = os?.TVWindow;
        if (tv == null || conversationTVWindow == tv) return;
        ReleaseConversationTVLayout();
        conversationTVWindow = tv;
        if (tv.HasTemporaryOpeningLayout) return;
        tv.CompleteOpeningFromScript();
        tv.SetTemporaryOpeningLayout(new Vector2(200f, 120f),
            new Vector2(191f, 43f), new Vector2(250f, 158f));
        ownsConversationTVLayout = true;
    }

    void ReleaseConversationTVLayout()
    {
        if (ownsConversationTVLayout && conversationTVWindow != null)
            conversationTVWindow.ClearTemporaryOpeningLayout();
        conversationTVWindow = null;
        ownsConversationTVLayout = false;
    }
}
