using UnityEngine;

public sealed class Event_04 : WorldTipEventBase
{
    const string NextEventId = "Event_05";

    protected override void EventAction()
    {
        MainGameHandle mainGame = MainGameHandle.Instance;
        if (mainGame != null)
        {
            mainGame.SetEventActive(NextEventId, true);
            return;
        }

        Debug.LogWarning(
            $"[Event_04] MainGameHandle is missing; {NextEventId} was not activated.",
            this);
    }
}
