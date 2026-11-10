using UnityEngine;

public sealed class Event_03 : WorldTipEventBase
{
    public override bool CanCompleteWithoutDialogue => true;

    protected override void EventAction()
    {
        ReopenEvent();

        if (AdditionHandle.Instance != null)
            AdditionHandle.Instance.InitPaper();
        else
            Debug.LogWarning("[Event_03] AdditionHandle is missing.", this);
    }
}
