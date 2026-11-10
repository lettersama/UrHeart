using UnityEngine;

public sealed class Event_00 : WorldTipEventBase
{
    const string NextEventId = "Event_01";
    const int LowHeartDialogueIndex = 10;

    public override void OnDialogueLineEntered(
        int dialogueIndex,
        EventDialogueData dialogue)
    {
        if (dialogueIndex == LowHeartDialogueIndex)
        {
            MainGameHandle.Instance?.SetNpcLowHeart(true);
            MainGameHandle.Instance?.PlayNpcCollapseShake();
        }
    }

    protected override void EventAction()
    {
        MainGameHandle mainGame = MainGameHandle.Instance;
        if (mainGame != null)
        {
            mainGame.SetEventActive(NextEventId, true);
            return;
        }

        Debug.LogWarning(
            $"[Event_00] MainGameHandle is missing; {NextEventId} was not activated.",
            this);
    }
}
