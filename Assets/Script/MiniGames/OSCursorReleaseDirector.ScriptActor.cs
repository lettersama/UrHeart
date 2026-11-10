using UnityEngine;

// A lease on the existing red pointer, never on the player's cursor or input.
// Screen coordinates match OSWindowUnit/RectTransformUtility. All animation uses
// this director's paused clock; scripts may also place the actor directly to keep
// contact with a moving paper figure or a title bar.
public sealed partial class OSCursorReleaseDirector
{
    UnityEngine.Object scriptActorOwner;
    Vector2 scriptActorPosition, scriptActorFrom, scriptActorTo, scriptActorControl;
    float scriptActorMoveTime, scriptActorMoveDuration, scriptActorHop = -1f;
    bool scriptActorMoving, scriptActorPaused, scriptActorVisible = true, scriptActorSkip;
    bool scriptActorPressed;
    float scriptActorTilt;
    // Opt-in standing pose: a slow one-or-two pixel rise and fall while parked.
    bool scriptActorBob;
    float scriptActorBobTime;
    const float ScriptActorBobSeconds = 1.6f, ScriptActorBobPixels = 1.5f;
    RectTransform scriptActorCarried;
    Vector2 scriptActorGrip;
    const float ScriptActorHopSeconds = .4f;

    bool ScriptActorAmbientAvailable => hasFocus && OS_Handle.Instance?.IsOSVisible == true
        && !OSSessionPersistence.IsRestoring && GameSessionFlow.Instance?.IsBusy != true
        && !GameTestConsole.BlocksGameplayInput
        && !(settings != null && settings.IsOpen && !settings.IsMinimized && settings.gameObject.activeInHierarchy)
        && OS_Handle.Instance?.IsSystemInputLockedWithoutChapter02Nightfall != true;

    public bool ScriptActorIsMoving => scriptActorOwner != null && scriptActorMoving;
    public bool ScriptActorCanAdvance => scriptActorOwner != null && isActiveAndEnabled
        && !scriptActorPaused && ScriptActorAmbientAvailable;

    public bool OwnsScriptActor(UnityEngine.Object owner) => owner != null
        && ReferenceEquals(scriptActorOwner, owner);

    /// <summary>
    /// Acquire the already-unlocked companion in her current pose. A different
    /// owner, the apology, or the earlier heart talk cannot be interrupted.
    /// Re-acquiring with the same owner is harmless and preserves its animation.
    /// </summary>
    public bool TryAcquireScriptActor(UnityEngine.Object owner)
    {
        if (owner == null || !isActiveAndEnabled) return false;
        Resolve(); ObserveSave();
        if (OwnsScriptActor(owner)) return true;
        if (scriptActorOwner != null || State?.chapter02RedCursorUnlocked != true
            || OS_Handle.Instance?.CursorHeartTalk?.IsEngaged == true
            || (IsEngaged && Step != ReleaseStep.ConversationHold)) return false;
        EnsureVisuals();
        if (cursorImage == null || visualRoot == null) return false;

        // Withdraw read-along offsets before taking a new pose; do not destroy or
        // otherwise alter either memory presentation that supplied them.
        EndHiddenMemoryRead(); EndReadAlong(); EndFlourish();
        if (heartTrail != null) foreach (var dot in heartTrail)
            if (dot != null) dot.gameObject.SetActive(false);
        heartTrailAge = -1f;
        scriptActorOwner = owner;
        scriptActorPosition = CursorPoint;
        scriptActorMoving = scriptActorPaused = scriptActorPressed = false;
        scriptActorVisible = true; scriptActorSkip = true;
        scriptActorCarried = null;
        scriptActorHop = -1f; scriptActorTilt = 0f;
        SetPaperVisible(false); HideContinueTip();
        SetPressed(false); SetReadTilt(0f);
        SetVisualsVisible(ScriptActorAmbientAvailable);
        return true;
    }

    /// <summary>
    /// Stop all motion and release at the last rendered position. Carried objects
    /// remain where they were dropped and stay owned by their creator.
    /// </summary>
    public void ReleaseScriptActor(UnityEngine.Object owner)
    {
        if (!OwnsScriptActor(owner)) return;
        if (ReferenceEquals(observed, State)) SavePointerPosition();
        ResetScriptActor();
    }

    /// <summary>Quadratic Bezier with a slight bow, in actual screen coordinates.</summary>
    public bool ScriptActorMoveTo(UnityEngine.Object owner, Vector2 screenPoint, float seconds)
    {
        if (!OwnsScriptActor(owner) || !TryScriptActorLocal(screenPoint, out Vector2 local)
            || !FiniteScriptActorValue(seconds)) return false;
        scriptActorFrom = scriptActorPosition; scriptActorTo = local;
        Vector2 delta = local - scriptActorFrom;
        Vector2 bend = new Vector2(-delta.y, delta.x).normalized * Mathf.Min(10f, delta.magnitude * .16f);
        scriptActorControl = (scriptActorFrom + local) * .5f + bend;
        scriptActorMoveTime = 0f; scriptActorMoveDuration = Mathf.Max(0f, seconds);
        scriptActorMoving = scriptActorMoveDuration > 0f;
        if (!scriptActorMoving) { scriptActorPosition = local; RenderScriptActor(); }
        return true;
    }

    /// <summary>
    /// Set the tip immediately and cancel any flight. Deliberately not clamped:
    /// a scripted fall can cross the screen edge before the owner hides her.
    /// </summary>
    public bool ScriptActorSetPosition(UnityEngine.Object owner, Vector2 screenPoint)
    {
        if (!OwnsScriptActor(owner) || !TryScriptActorLocal(screenPoint, out Vector2 local)) return false;
        scriptActorMoving = false; scriptActorPosition = local;
        RenderScriptActor(); return true;
    }

    public void ScriptActorSetPressed(UnityEngine.Object owner, bool pressed)
    {
        if (!OwnsScriptActor(owner)) return;
        scriptActorPressed = pressed; SetPressed(pressed);
    }

    public void ScriptActorSetTilt(UnityEngine.Object owner, float degrees)
    {
        if (!OwnsScriptActor(owner) || !FiniteScriptActorValue(degrees)) return;
        scriptActorTilt = degrees; SetReadTilt(degrees);
        PlaceScriptActorCarry();
    }

    /// <summary>A single four-canvas-unit hop, with the same pixel snapping as read-along.</summary>
    public void ScriptActorHop(UnityEngine.Object owner)
    {
        if (!OwnsScriptActor(owner)) return;
        scriptActorHop = 0f;
    }

    /// <summary>
    /// Follow the tip with this point in the object's own local space. The object
    /// is neither reparented nor destroyed; use a presentation-layer copy for a
    /// seal that should appear above other windows. Carry(null) drops it.
    /// </summary>
    public void ScriptActorCarry(UnityEngine.Object owner, RectTransform item, Vector2 localGrip = default)
    {
        if (!OwnsScriptActor(owner)) return;
        scriptActorCarried = item; scriptActorGrip = localGrip;
        PlaceScriptActorCarry();
    }

    public void ScriptActorDrop(UnityEngine.Object owner)
    {
        if (OwnsScriptActor(owner)) scriptActorCarried = null;
    }

    /// <summary>
    /// Breathe gently up and down whenever she is not flying or pressing, like
    /// her ambient idle. Each rest starts from the parked point, so nothing jumps.
    /// </summary>
    public void ScriptActorSetIdleBob(UnityEngine.Object owner, bool enabled)
    {
        if (!OwnsScriptActor(owner) || scriptActorBob == enabled) return;
        scriptActorBob = enabled; scriptActorBobTime = 0f;
    }

    /// <summary>Freeze animation time while retaining the lease and current pose.</summary>
    public void ScriptActorSetPaused(UnityEngine.Object owner, bool paused)
    {
        if (!OwnsScriptActor(owner) || scriptActorPaused == paused) return;
        scriptActorPaused = paused; scriptActorSkip = true;
    }

    public void ScriptActorSetVisible(UnityEngine.Object owner, bool visible)
    {
        if (!OwnsScriptActor(owner)) return;
        scriptActorVisible = visible;
        SetVisualsVisible(visible && ScriptActorAmbientAvailable);
    }

    bool TickScriptActor()
    {
        if (ReferenceEquals(scriptActorOwner, null)) return false;
        if (scriptActorOwner == null
            || (scriptActorOwner is Behaviour behaviour && !behaviour.isActiveAndEnabled))
        { ResetScriptActor(); return false; }
        if (!ScriptActorAmbientAvailable)
        { SetVisualsVisible(false); scriptActorSkip = true; return true; }
        EnsureVisuals();
        if (cursorImage == null || visualRoot == null) return true;
        SetVisualsVisible(scriptActorVisible); SetPaperVisible(false);
        float dt = scriptActorPaused || scriptActorSkip ? 0f : Mathf.Min(Time.unscaledDeltaTime, .05f);
        scriptActorSkip = false;
        AdvanceScriptActor(dt);
        SavePointerPosition();
        return true;
    }

    void AdvanceScriptActor(float dt)
    {
        if (scriptActorMoving && dt > 0f)
        {
            scriptActorMoveTime = Mathf.Min(scriptActorMoveDuration, scriptActorMoveTime + dt);
            float u = Mathf.SmoothStep(0f, 1f, scriptActorMoveTime / scriptActorMoveDuration), v = 1f - u;
            scriptActorPosition = v * v * scriptActorFrom + 2f * v * u * scriptActorControl + u * u * scriptActorTo;
            if (scriptActorMoveTime >= scriptActorMoveDuration)
            { scriptActorMoving = false; scriptActorPosition = scriptActorTo; }
        }
        if (!scriptActorBob || scriptActorMoving || scriptActorPressed) scriptActorBobTime = 0f;
        else scriptActorBobTime += dt;
        if (scriptActorHop >= 0f && dt > 0f)
        {
            scriptActorHop += dt;
            if (scriptActorHop >= ScriptActorHopSeconds) scriptActorHop = -1f;
        }
        RenderScriptActor();
    }

    void RenderScriptActor()
    {
        Vector2 shown = scriptActorPosition;
        if (scriptActorHop >= 0f)
            shown.y += Mathf.Round(Mathf.Sin(scriptActorHop / ScriptActorHopSeconds * Mathf.PI) * 4f);
        if (scriptActorBobTime > 0f)
            shown.y += Mathf.Round(Mathf.Sin(scriptActorBobTime / ScriptActorBobSeconds * Mathf.PI * 2f) * ScriptActorBobPixels);
        PlaceCursor(shown); SetPressed(scriptActorPressed); SetReadTilt(scriptActorTilt);
        PlaceScriptActorCarry();
    }

    void PlaceScriptActorCarry()
    {
        if (scriptActorCarried == null || cursorImage == null) return;
        scriptActorCarried.position += cursorImage.rectTransform.position - scriptActorCarried.TransformPoint(scriptActorGrip);
    }

    bool TryScriptActorLocal(Vector2 screenPoint, out Vector2 local)
    {
        local = default;
        if (visualRoot == null || !FiniteScriptActorValue(screenPoint.x) || !FiniteScriptActorValue(screenPoint.y)) return false;
        var canvas = visualRoot.GetComponentInParent<Canvas>();
        var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        return RectTransformUtility.ScreenPointToLocalPointInRectangle(visualRoot, screenPoint, camera, out local);
    }

    static bool FiniteScriptActorValue(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    void ResetScriptActor()
    {
        if (ReferenceEquals(scriptActorOwner, null)) return;
        scriptActorOwner = null; scriptActorCarried = null;
        scriptActorMoving = scriptActorPaused = scriptActorPressed = false;
        scriptActorVisible = true; scriptActorHop = -1f; scriptActorTilt = 0f;
        scriptActorBob = false; scriptActorBobTime = 0f;
        SetPressed(false); SetReadTilt(0f);
        idleInitialized = false; followVelocity = Vector2.zero; skipAdvance = true;
    }
}
