using UnityEngine;

public sealed partial class OSChapter04StoryDirector
{
    const string MotherGiftLine = EntryPrefix + "39";
    const string MotherHeartLine = EntryPrefix + "40";
    bool motherGiftRestorePending = true;
    int motherGiftMotion;
    float motherGiftMotionTime;
    readonly Vector2 motherKeyPosition = new Vector2(65f, -5f);
    OSCursorReleaseDirector motherGiftActor;

    public OSChapter04MotherGiftStage MotherGiftStage => State?.motherGiftStage ?? OSChapter04MotherGiftStage.None;
    public bool CanUseMotherKey => IsAvailable && ReferenceEquals(observed, State)
        && State.currentEntryId == MotherGiftLine && State.currentTextCompleted
        && State.currentCueCommitted && State.currentResourcesCommitted
        && MotherGiftStage == OSChapter04MotherGiftStage.KeyOffered && motherGiftMotion == 0;

    bool MotherGiftAllowsContinue => State != null
        && (State.currentEntryId != MotherGiftLine || MotherGiftStage >= OSChapter04MotherGiftStage.Seen)
        && MotherGiftStage != OSChapter04MotherGiftStage.CloseRequested;

    // A real open is the gate. Giving the key, queuing a window, or clicking the
    // old continue triangle cannot skip the player's discovery.
    public bool TryUnlockMotherHeart()
    {
        if (!CanUseMotherKey || os?.MotherHeartController == null) return false;
        State.motherGiftStage = OSChapter04MotherGiftStage.Unlocked;
        os.RefreshMotherHeartAccess();
        os.MotherKeyController?.SetInputEnabled(false);
        os.MotherKeyController?.Window.CloseSilently();
        os.OpenMotherHeartWindow();
        GuardCurrentGesture();
        ProgressChanged?.Invoke();
        return true;
    }

    void TickMotherGift()
    {
        if (!IsActive || State == null)
        { ResetMotherGiftPresentation(); return; }
        if (OSSessionPersistence.IsRestoring)
        { SetMotherGiftPaused(true); return; }
        os.ResolveChapter04MotherHeartReferences();
        bool restoring = motherGiftRestorePending;
        if (restoring)
        {
            RestoreMotherGiftProgress();
        }
        os.RefreshMotherHeartAccess();
        SetMotherGiftPaused(!IsAvailable);
        if (!IsAvailable) return;

        var key = os.MotherKeyController;
        var heart = os.MotherHeartController;
        if (key == null || heart == null) return;
        motherGiftRestorePending = false;
        if (State.currentEntryId == MotherGiftLine && State.currentTextCompleted
            && State.currentCueCommitted && State.currentResourcesCommitted)
        {
            if (MotherGiftStage == OSChapter04MotherGiftStage.None)
            {
                State.motherGiftStage = OSChapter04MotherGiftStage.KeyOffered;
                ShowMotherKey(!restoring);
                ProgressChanged?.Invoke();
            }
            else if (MotherGiftStage == OSChapter04MotherGiftStage.KeyOffered && !key.Window.IsOpen && !key.IsDragging)
                ShowMotherKey(false);
            if (MotherGiftStage == OSChapter04MotherGiftStage.Unlocked || MotherGiftStage == OSChapter04MotherGiftStage.Seen)
            {
                if (!heart.Window.IsOpen || heart.Window.IsMinimized) os.OpenMotherHeartWindow();
                if (Ready(heart.Window) && MotherGiftStage == OSChapter04MotherGiftStage.Unlocked)
                {
                    State.motherGiftStage = OSChapter04MotherGiftStage.Seen;
                    ProgressChanged?.Invoke();
                }
            }
            if (MotherGiftStage == OSChapter04MotherGiftStage.Seen && Ready(heart.Window)) TryContinue();
        }
        else if (State.currentEntryId == MotherHeartLine && restoring
            && MotherGiftStage == OSChapter04MotherGiftStage.Seen)
        {
            os.OpenMotherHeartWindow();
            heart.Window.CompleteOpeningFromScript();
        }

        if (MotherGiftStage == OSChapter04MotherGiftStage.CloseRequested && motherGiftMotion == 0)
            BeginMotherHeartClose(restoring);
        TickMotherGiftMotion();
        key.SetInputEnabled(CanUseMotherKey);
    }

    void ShowMotherKey(bool animate)
    {
        var key = os.MotherKeyController;
        key.SetInputEnabled(false);
        key.Window.SetOpeningLayout(motherKeyPosition, new Vector2(110f, 62f));
        key.Window.OpenStandalone();
        key.Window.CompleteOpeningFromScript();
        key.Window.ConfigureInteraction(false, false);
        if (!animate || !AcquireMotherGiftActor())
        { key.Window.ConfigureInteraction(true, false); return; }

        // The red companion drags the title bar; the player's real pointer stays free.
        Vector2 destination = MotherGiftScreenPoint(key.TitleGrip);
        var rect = key.Window.WindowRect;
        Vector2 grip = rect.InverseTransformPoint(key.TitleGrip.position);
        rect.anchoredPosition = new Vector2(360f, motherKeyPosition.y);
        motherGiftActor.ScriptActorSetPosition(this, MotherGiftScreenPoint(key.TitleGrip));
        motherGiftActor.ScriptActorSetPressed(this, true);
        motherGiftActor.ScriptActorCarry(this, rect, grip);
        motherGiftActor.ScriptActorMoveTo(this, destination, .85f);
        motherGiftMotion = 1;
        motherGiftMotionTime = 0f;
    }

    bool TryBeginMotherHeartClose()
    {
        if (State.currentEntryId != MotherHeartLine || MotherGiftStage == OSChapter04MotherGiftStage.Closed)
            return false;
        State.motherGiftStage = OSChapter04MotherGiftStage.CloseRequested;
        BeginMotherHeartClose(false);
        ProgressChanged?.Invoke();
        return true;
    }

    void BeginMotherHeartClose(bool restoring)
    {
        var heart = os?.MotherHeartController?.Window;
        if (heart == null || !heart.IsOpen || restoring)
        {
            heart?.CloseSilently();
            FinishMotherHeartClose();
            return;
        }
        if (heart.IsMinimized) heart.RestoreAndBringToFront();
        var close = System.Array.Find(heart.GetComponentsInChildren<OSWindowButton>(true),
            b => b.LinkedWindow == heart && b.Action == OSWindowButton.WindowAction.Close);
        if (close == null || !AcquireMotherGiftActor())
        { heart.Close(); motherGiftMotion = 3; return; }
        motherGiftActor.ScriptActorMoveTo(this, MotherGiftScreenPoint((RectTransform)close.transform), .4f);
        motherGiftMotion = 2;
        motherGiftMotionTime = 0f;
    }

    void TickMotherGiftMotion()
    {
        if (motherGiftMotion == 0) return;
        bool moving = motherGiftActor != null && motherGiftActor.OwnsScriptActor(this)
            && motherGiftActor.ScriptActorIsMoving;
        if (moving) return;
        if (motherGiftMotion == 1)
        {
            ReleaseMotherGiftActor();
            os.MotherKeyController.Window.WindowRect.anchoredPosition = motherKeyPosition;
            os.MotherKeyController.Window.ConfigureInteraction(true, false);
            motherGiftMotion = 0;
        }
        else if (motherGiftMotion == 2)
        {
            motherGiftActor?.ScriptActorSetPressed(this, true);
            motherGiftMotionTime += Mathf.Min(Time.unscaledDeltaTime, .1f);
            if (motherGiftMotionTime < .14f) return;
            motherGiftActor?.ScriptActorSetPressed(this, false);
            os.MotherHeartController.Window.Close();
            motherGiftMotion = 3;
        }
        else if (os.MotherHeartController?.Window.IsOpen != true && os.MotherHeartController?.Window.IsClosing != true)
            FinishMotherHeartClose();
    }

    void FinishMotherHeartClose()
    {
        ReleaseMotherGiftActor();
        motherGiftMotion = 0;
        if (State == null || MotherGiftStage != OSChapter04MotherGiftStage.CloseRequested) return;
        State.motherGiftStage = OSChapter04MotherGiftStage.Closed;
        if (State.currentEntryId == MotherHeartLine)
        {
            string next = Content.Find(MotherHeartLine)?.nextEntryId;
            if (!string.IsNullOrEmpty(next))
            { AdvanceTo(next); messages?.FinishChapter04StoryEntry(MotherHeartLine); }
        }
        ProgressChanged?.Invoke();
    }

    bool AcquireMotherGiftActor()
    {
        motherGiftActor = os?.CursorRelease;
        return motherGiftActor != null && motherGiftActor.TryAcquireScriptActor(this);
    }

    void ReleaseMotherGiftActor()
    {
        if (motherGiftActor != null && motherGiftActor.OwnsScriptActor(this))
        {
            motherGiftActor.ScriptActorDrop(this);
            motherGiftActor.ScriptActorSetPressed(this, false);
            motherGiftActor.ReleaseScriptActor(this);
        }
        motherGiftActor = null;
    }

    static Vector2 MotherGiftScreenPoint(RectTransform rect)
    {
        var canvas = rect.GetComponentInParent<Canvas>();
        Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        return RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(rect.rect.center));
    }

    void SetMotherGiftPaused(bool paused)
    {
        motherGiftActor?.ScriptActorSetPaused(this, paused);
        os?.MotherHeartController?.SetPaused(paused);
        if (paused) os?.MotherKeyController?.SetInputEnabled(false);
    }

    void RestoreMotherGiftProgress()
    {
        if (State == null || MotherGiftStage != OSChapter04MotherGiftStage.None || !State.started) return;
        // Old versions used manual Continue for 39/40. Keep their later position,
        // and silently make the already-seen gift available from the desktop.
        if (State.currentEntryId == MotherHeartLine)
            State.motherGiftStage = OSChapter04MotherGiftStage.Seen;
        else if (IsAfterMotherGift(State.currentEntryId) || State.completed
            || State.finalTreatmentStage != OSChapter04FinalTreatmentStage.None)
            State.motherGiftStage = OSChapter04MotherGiftStage.Closed;
        else if (State.history != null)
            foreach (var record in State.history)
                if (record != null && IsAfterMotherGift(record.entryId))
                { State.motherGiftStage = OSChapter04MotherGiftStage.Closed; break; }
    }

    static bool IsAfterMotherGift(string id) => id == EntryPrefix + "40a" || id == EntryPrefix + "41"
        || id == EntryPrefix + "42" || id == EntryPrefix + "43" || id == EntryPrefix + "44"
        || id == EntryPrefix + "45" || id == EntryPrefix + "46" || id == EntryPrefix + "47"
        || id == EntryPrefix + "48" || id == EntryPrefix + "49" || id == EntryPrefix + "50"
        || id == EntryPrefix + "50a" || id == EntryPrefix + "50b"
        || id == EntryPrefix + "51" || id == EntryPrefix + "52" || id == EntryPrefix + "53"
        || id != null && id.StartsWith(EntryPrefix + "grave_", System.StringComparison.Ordinal);

    void ResetMotherGiftPresentation()
    {
        ReleaseMotherGiftActor();
        motherGiftMotion = 0;
        motherGiftMotionTime = 0f;
        motherGiftRestorePending = true;
        os?.MotherKeyController?.SetInputEnabled(false);
        os?.MotherKeyController?.Window.CloseSilently();
        // Session restore can already have opened/rebound this desktop app
        // before Tick observes the replacement save and resets its old owner.
        // Preserve that window (including minimization) while the persistence
        // layer owns restoration; Closed means the gift scene ended, not that
        // the player must lose a later desktop opening of the same app.
        bool sessionRestoresHeart = OSSessionPersistence.IsRestoring && IsActive && State != null
            && MotherGiftStage >= OSChapter04MotherGiftStage.Unlocked
            && MotherGiftStage != OSChapter04MotherGiftStage.CloseRequested;
        if (!sessionRestoresHeart) os?.MotherHeartController?.Window.CloseSilently();
    }
}
