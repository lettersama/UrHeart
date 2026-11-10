using UnityEngine;

public sealed partial class OSChapter03StoryDirector
{
    // Author-tuned to 75% of the previous approach speed. This is independent
    // of the first message's .4-second delay.
    const float MirrorPauseTravelSeconds = .62f / .75f, MirrorPauseHoverSeconds = .09f, MirrorPausePressSeconds = .11f;
    float mirrorPauseStageStarted;
    Vector2 mirrorPausePoint;

    public bool IsMirrorPlaybackPaused => IsMirrorSequenceActive
        && Has("mirror_playback_paused") && !Has("mirror_playback_resumed");
    public bool IsWaitingForMirrorResume => IsMirrorPlaybackPaused && Has("live_back_home")
        && State.liveOffline && State.memoryQueue.Count > 0
        && State.memoryQueue[0].type == "effect" && State.memoryQueue[0].key == "wait_mirror_resume";

    void BeginMirrorPause(OSChapter03StoryBeat beat)
    {
        State.Add("mirror_pause_started");
        // The ordinary Live queue starts on its own clock while she is still
        // moving toward the reflection. It never sets the cursor's deadline.
        if (State.Add("mirror_breakdown_queued"))
            WithInteractionSource(beat.interactionSource, () => Live("live.breakdown.01", .4f));
        var target = presentation?.MirrorRoot?.Find("Frame/MirrorFace") as RectTransform;
        if (target == null) target = presentation?.MirrorRoot;
        mirrorPausePoint = target != null ? ScreenPoint(target) : new Vector2(Screen.width * .5f, Screen.height * .5f);
        if (EnsureActor())
        {
            actor.ScriptActorSetPressed(this, false);
            actor.ScriptActorMoveTo(this, mirrorPausePoint, MirrorPauseTravelSeconds);
        }
    }

    bool TickMirrorPause()
    {
        if (effectStage == 0)
        {
            if (actor != null && actor.ScriptActorIsMoving) return false;
            mirrorPauseStageStarted = effectClock;
            effectStage = 1;
        }
        if (effectStage == 1 && effectClock - mirrorPauseStageStarted >= MirrorPauseHoverSeconds)
        {
            if (EnsureActor())
            {
                actor.ScriptActorSetPressed(this, true);
            }
            State.Add("mirror_playback_paused");
            os.PlayInteractionFeedback();
            RefreshMirrorStage();
            mirrorPauseStageStarted = effectClock;
            effectStage = 2;
        }
        if (effectStage == 2 && effectClock - mirrorPauseStageStarted >= MirrorPausePressSeconds)
        {
            if (EnsureActor())
            {
                actor.ScriptActorSetPressed(this, false);
                if (liveWindow != null) actor.ScriptActorMoveTo(this, TitlePoint(liveWindow.WindowRect), MirrorPauseTravelSeconds);
            }
            effectStage = 3;
        }
        // Do not force either flight to finish from the director's faster clock.
        // Carry begins only after the actor has actually reached Live's title.
        return effectStage == 3 && (actor == null || !actor.ScriptActorIsMoving);
    }

    void ResumeMirrorPlayback()
    {
        if (!IsAvailable || !IsWaitingForMirrorResume) return;
        State.Add("mirror_playback_resumed");
        os.PlayInteractionFeedback();
        RefreshMirrorStage();
    }
}
