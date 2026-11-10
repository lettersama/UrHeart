using UnityEngine;

public sealed partial class OSCursorReleaseDirector
{
    bool heartTalkOwnsActor;
    /// <summary>Frontend offset from centre while Live is held (the loading spinner takes the middle).</summary>
    public Vector2 HeartTalkLiveShift { get; set; }
    public void SuspendForHeartTalk() { Resolve(); HoldForHeartTalk(); }

    void HoldForHeartTalk()
    {
        if (!heartTalkOwnsActor)
        {
            heartTalkOwnsActor = true;
            CloseOwnMessage();
            messages?.WindowLayout?.ClearTemporaryBottomDialogue(this);
            DestroyFlourish();
            idleInitialized = false;
        }
        bool visible = AmbientAvailable && live != null && live.IsOpen && !live.IsMinimized
            && live.gameObject.activeInHierarchy;
        if (!visible) { SetVisualsVisible(false); skipAdvance = true; return; }
        EnsureVisuals(); SetVisualsVisible(true); SetPaperVisible(false); HideContinueTip();
        if (OS_Handle.Instance.CursorHeartTalk.Step < OSCursorHeartTalkDirector.HeartTalkStep.Return
            && live.IsOpen && !live.IsOpening)
        {
            OwnLiveMotion(); live.TrySetPresentationOffset(this, CenterOffset() + HeartTalkLiveShift);
        }
        float dt = skipAdvance ? 0f : Mathf.Min(Time.unscaledDeltaTime, .05f);
        skipAdvance = false;
        TickHeartPop(dt);
        SavePointerPosition();
        // The presentation owns cursor motion until Done; no idle integration,
        // avoidance, flourish or player click is performed by this director.
    }

    public void ReleaseHeartTalkLayout()
    {
        Resolve();
        heartTalkOwnsActor = false; HeartTalkLiveShift = Vector2.zero;
        live?.ClearPresentationOffset(this);
        messages?.WindowLayout?.ClearTemporaryBottomDialogue(this);
        if (liveMoveInteractionOwned && live != null) live.ConfigureInteraction(liveCouldMove, liveCouldResize);
        liveMoveInteractionOwned = false;
        idleInitialized = false; skipAdvance = true;
    }

    // Frontend bench choreography can move the existing noninteractive actor
    // without taking control of the player's physical or virtual cursor.
    public Vector2 CompanionScreenPosition
    {
        get
        {
            if (visualRoot == null) return Vector2.zero;
            var canvas = visualRoot.GetComponentInParent<Canvas>();
            var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            return RectTransformUtility.WorldToScreenPoint(camera, visualRoot.TransformPoint(CursorPoint));
        }
    }
    public void SetHeartTalkCompanionPressed(bool pressed)
    {
        if (OS_Handle.Instance?.CursorHeartTalk?.IsEngaged != true) return;
        Resolve(); EnsureVisuals(); SetPressed(pressed);
    }
    public void SetHeartTalkCompanionPosition(Vector2 screenPosition)
    {
        if (OS_Handle.Instance?.CursorHeartTalk?.IsEngaged != true) return;
        Resolve(); EnsureVisuals();
        if (visualRoot == null) return;
        var canvas = visualRoot.GetComponentInParent<Canvas>();
        var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(visualRoot, screenPosition, camera, out Vector2 local))
            PlaceCursor(ClampPoint(local, 8f));
    }
}
