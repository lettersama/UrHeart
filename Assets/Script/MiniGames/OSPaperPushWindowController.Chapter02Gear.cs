using UnityEngine;

public sealed partial class OSPaperPushWindowController
{
    OSPaperPushTiltGear chapter02Gear;
    bool chapter02GearRevealed;
    public bool IsChapter02GearRevealed => matchChapter == 2 && matchStage == 2 && chapter02GearRevealed;
    public OSPaperPushTiltGear Chapter02Gear => chapter02Gear;

    // Also handles an older save resumed in the previous version's hint line.
    public void RevealChapter02Gear()
    {
        if (matchChapter != 2 || matchStage != 2) return;
        chapter02GearRevealed = true;
        if (IsChapter02GameActive && phase != MatchPhase.Sliding)
        { StopAllCoroutines();BeginChapter02Sliding(); }
        TickChapter02Gear(phase == MatchPhase.Sliding && CanSimulate);
    }

    void TickChapter02Gear(bool canDrag)
    {
        bool show = IsChapter02GearRevealed && IsChapter02GameActive
            && window != null && window.IsOpen && !window.IsMinimized && window.gameObject.activeInHierarchy;
        if (show && chapter02Gear == null) chapter02Gear = OSPaperPushTiltGear.Create(window, commentLabel);
        if (chapter02Gear != null) chapter02Gear.Tick(show, canDrag);
    }
}
