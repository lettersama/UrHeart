using UnityEngine;

public sealed partial class OSClawMachineGameWindowController
{
    const int CursorBaitPrizeIndex = 1;
    const float CursorTurnSeconds = .16f, CursorLungeSeconds = .22f;
    const float CursorCloseSeconds = .16f, CursorReturnSeconds = .575f;
    bool cursorTrapDescent, trapHasFocus = true;
    bool struggleAudioArmed = true;
    Vector2 trapStartGrip, trapTargetGrip, trapGrip, trapFrozenScreen, struggleOffset;
    float trapAngle, trapTargetAngle, struggleEnergy, struggleClock;
    int motionSiblingBeforeTrap = -1;

    public bool IsCursorTrapControlling => CursorHandle.Instance != null
        && CursorHandle.Instance.IsScriptControlledBy(this);
    public bool IsCursorStruggling => IsCursorTrapControlling && struggleEnergy > .08f;
    bool IsCursorTrapPhase => phase >= ClawPhase.CursorTurning;
    RectTransform TrapMotion => claw != null ? claw.parent as RectTransform : null;
    Camera TrapCamera
    {
        get
        {
            var canvas = claw != null ? claw.GetComponentInParent<Canvas>() : null;
            return canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        }
    }

    bool ShouldTrapAtCurrentPosition()
    {
        var p = Progress;
        if (p == null || !p.chapter02ClawFirstPrizeTaken
            || (!p.chapter02ClawNextRequestShown && OS_Handle.Instance.ClawMachineStory?.IsSecondRequestReady != true)
            || (!p.chapter02ClawSecondTargetRevealed && !p.chapter02ClawNextRequestShown)
            || hintedPrize != CursorBaitPrizeIndex
            || secondTargetOutline == null || !secondTargetOutline.gameObject.activeInHierarchy
            || targetArrow == null || !targetArrow.gameObject.activeInHierarchy
            || p.chapter02ClawOutletPrize != 0 || p.chapter02ClawCursorCaptured
            || OS_Handle.Instance.ClawMachineStory?.IsPickupInProgress == true
            || prizes.Length <= CursorBaitPrizeIndex || prizes[CursorBaitPrizeIndex] == null) return false;
        float center = prizeHomes[CursorBaitPrizeIndex].x
            + prizes[CursorBaitPrizeIndex].rectTransform.rect.width * .5f;
        return Mathf.Abs(headX - center) <= 18f;
    }

    bool BeginCursorTrap()
    {
        // The descent is armed only by a fresh click after the second request
        // and its visible robot cue. Recheck before taking pointer ownership.
        if (!ShouldTrapAtCurrentPosition()) return false;
        var cursor = CursorHandle.Instance;
        if (cursor == null || TrapMotion == null || !cursor.AcquireScriptControlWithoutWarp(this)) return false;
        // Freeze only when the claw notices the pointer, after the ordinary descent.
        // Subsequent raw input can never drag this target away from the approaching claw.
        trapFrozenScreen = cursor.DisplayedScreenPosition;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(TrapMotion, trapFrozenScreen, TrapCamera, out trapTargetGrip);
        trapStartGrip = new Vector2(headX, -RestGripY - drop);
        trapGrip = trapStartGrip;
        Vector2 towards = trapTargetGrip - trapStartGrip;
        trapAngle = 0f;
        trapTargetAngle = Mathf.Clamp(Mathf.Atan2(towards.x, -towards.y) * Mathf.Rad2Deg, -85f, 85f);
        cursorTrapDescent = false;
        PrepareCursorTrapPresentation();
        cursor.SetScriptPressed(this, false);
        Chapter02Audio.Play("Chapter02_ClawTurn", .6f, .8f);
        SetPhase(ClawPhase.CursorTurning);
        RefreshButtons();
        ApplyTrapPose();
        return true;
    }

    void PrepareCursorTrapPresentation()
    {
        Window.BringToFront();
        var motion = TrapMotion;
        if (motion != null && motionSiblingBeforeTrap < 0)
        {
            motionSiblingBeforeTrap = motion.GetSiblingIndex();
            motion.SetAsLastSibling();
        }
        HidePrizeTooltip();
    }

    // Runs before the ordinary OS input guard: this sequence owns that guard itself.
    bool TickCursorTrap(float dt, bool visible)
    {
        bool available = visible && trapHasFocus && GameDataManager.Instance.CurrentChapterIndex == 2
            && !OS_Handle.Instance.IsSystemInputLockedWithoutClawTrap && !OSSessionPersistence.IsRestoring;
        if (!available)
        {
            if (IsCursorTrapControlling || cursorTrapDescent) SuspendCursorTrap();
            return IsCursorTrapPhase;
        }
        if (!IsCursorTrapPhase) return false;
        var cursor = CursorHandle.Instance;
        if (cursor == null) return true;
        if (!cursor.IsScriptControlledBy(this))
        {
            if (!cursor.AcquireScriptControlWithoutWarp(this)) return true;
            PrepareCursorTrapPresentation();
            // Reopening a checkpoint never replays a lunge toward an unrelated physical position.
            SetPhase(ClawPhase.CursorSuspended);
            trapGrip = new Vector2(headX, -RestGripY);
            trapAngle = 0f;
            cursor.SetScriptCaptured(this, true);
            SetFingers(true);
            DiscardMachineShell(false);
        }
        phaseTime += dt;
        switch (phase)
        {
            case ClawPhase.CursorTurning:
                trapAngle = Mathf.Lerp(0f, trapTargetAngle, Mathf.SmoothStep(0f, 1f, phaseTime / CursorTurnSeconds));
                if (phaseTime >= CursorTurnSeconds) SetPhase(ClawPhase.CursorLunging);
                break;
            case ClawPhase.CursorLunging:
                float lunge = Mathf.Clamp01(phaseTime / CursorLungeSeconds);
                trapGrip = Vector2.Lerp(trapStartGrip, trapTargetGrip, 1f - Mathf.Pow(1f - lunge, 3f));
                if (lunge >= 1f)
                {
                    OSChapter02ActivityMusic.NotifyCursorCaptured();
                    Chapter02Audio.Play("Chapter02_CursorCapture", .86f);
                    SetFingers(true);
                    cursor.SetScriptCaptured(this, true);
                    Progress.chapter02ClawCursorCaptured = true;
                    Progress.chapter02ClawCursorHeadX = headX;
                    RefreshTargetOutline();
                    DiscardMachineShell(true);
                    SetPhase(ClawPhase.CursorClosing);
                }
                break;
            case ClawPhase.CursorClosing:
                if (phaseTime >= CursorCloseSeconds) SetPhase(ClawPhase.CursorReturning);
                break;
            case ClawPhase.CursorReturning:
                float retract = Mathf.SmoothStep(0f, 1f, phaseTime / CursorReturnSeconds);
                trapGrip = Vector2.Lerp(trapTargetGrip, new Vector2(headX, -RestGripY), retract);
                trapAngle = Mathf.Lerp(trapTargetAngle, 0f, retract);
                drop = Mathf.Max(0f, -trapGrip.y - RestGripY);
                if (phaseTime >= CursorReturnSeconds) { drop = 0f; SetPhase(ClawPhase.CursorSuspended); }
                break;
        }
        TickDiscardedMachineShell(dt);
        if (phase >= ClawPhase.CursorClosing) TickCursorStruggle(cursor, dt);
        ApplyTrapPose();
        PresentCapturedCursor();
        RefreshButtons();
        return true;
    }

    void TickCursorStruggle(CursorHandle cursor, float dt)
    {
        Vector2 input = cursor.RawMouseDelta;
        struggleEnergy = Mathf.MoveTowards(struggleEnergy, 0f, dt * 3f);
        if (input.sqrMagnitude > .1f) struggleEnergy = Mathf.Clamp01(struggleEnergy + input.magnitude * .06f);
        if (struggleEnergy < .06f) struggleAudioArmed = true;
        if (struggleAudioArmed && struggleEnergy > .32f)
        {
            Chapter02Audio.Play("Chapter02_ClawGrip", .3f, .76f, .5f);
            struggleAudioArmed = false;
        }
        struggleClock += dt;
        Vector2 wanted = Vector2.ClampMagnitude(input * .12f, 1.5f);
        struggleOffset = Vector2.Lerp(struggleOffset, wanted, 1f - Mathf.Exp(-22f * dt));
        // The two small poses read as pulling against the grip, never free cursor travel.
        bool pulling = struggleEnergy > .08f;
        cursor.SetScriptPressed(this, pulling && Mathf.Sin(struggleClock * 32f) > 0f);
        cursor.SetScriptRotation(this, pulling ? Mathf.Sin(struggleClock * 27f) * 9f * struggleEnergy : 0f);
    }

    void ApplyTrapPose()
    {
        if (claw == null || cable == null || carriage == null) return;
        Quaternion rotation = Quaternion.Euler(0f, 0f, trapAngle);
        // Rotate about the gap between the fingers rather than the sprite's corner.
        claw.localRotation = rotation;
        claw.anchoredPosition = trapGrip - (Vector2)(rotation * new Vector3(13f, -16f));
        if (machineShellDiscarded)
        {
            // The exposed trap keeps a compact control handle; its fake cabinet
            // and rail fall away. The connector stays only three pixels long.
            carriage.localRotation = rotation;
            carriage.anchoredPosition = trapGrip + (Vector2)(rotation * new Vector3(-10f, 29f));
            cable.localRotation = rotation;
            cable.anchoredPosition = trapGrip + (Vector2)(rotation * new Vector3(-1.5f, 19f));
            cable.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, RestCableLength);
            return;
        }
        carriage.anchoredPosition = new Vector2(Mathf.Round(headX - 20f), 0f);
        Vector2 start = new Vector2(headX, -19f);
        Vector2 hub = trapGrip + (Vector2)(rotation * new Vector3(0f, 12f));
        Vector2 line = hub - start;
        float angle = Mathf.Atan2(line.x, -line.y) * Mathf.Rad2Deg;
        cable.localRotation = Quaternion.Euler(0f, 0f, angle);
        cable.anchoredPosition = start - (Vector2)(cable.localRotation * new Vector3(1.5f, 0f));
        cable.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(1f, line.magnitude));
    }

    void PresentCapturedCursor()
    {
        var cursor = CursorHandle.Instance;
        if (cursor == null || !cursor.IsScriptControlledBy(this) || TrapMotion == null) return;
        Vector2 screen = phase < ClawPhase.CursorClosing ? trapFrozenScreen
            : RectTransformUtility.WorldToScreenPoint(TrapCamera, TrapMotion.TransformPoint(trapGrip + struggleOffset));
        cursor.SetScriptScreenPosition(this, screen);
    }

    void RestoreCursorBait(OSMemoryProcessSaveData p)
    {
        if (p == null) return;
        // Earlier prototype saves allowed this toy into the outlet. Restore the bait;
        // do not consume the story target or trap such saves behind a missing prize.
        p.chapter02ClawCapturedMask &= ~(1 << CursorBaitPrizeIndex);
        if (p.chapter02ClawOutletPrize == CursorBaitPrizeIndex + 1) p.chapter02ClawOutletPrize = 0;
    }

    void RestoreCursorTrapCheckpoint()
    {
        if (Progress?.chapter02ClawCursorCaptured != true) return;
        float savedX = Progress.chapter02ClawCursorHeadX;
        headX = float.IsNaN(savedX) || float.IsInfinity(savedX) ? HomeX : Mathf.Clamp(savedX, MinX, MaxX);
        drop = 0f;
        trapGrip = new Vector2(headX, -RestGripY);
        trapAngle = 0f;
        SetFingers(true);
        SetPhase(ClawPhase.CursorSuspended);
        ApplyClawPosition();
        DiscardMachineShell(false);
        ApplyTrapPose();
    }

    public void SuspendCursorTrap()
    {
        motorAudio?.Stop();
        CursorHandle.Instance?.ReleaseScriptControlWithoutWarp(this);
        bool wasTrap = IsCursorTrapPhase || cursorTrapDescent;
        cursorTrapDescent = false;
        struggleOffset = Vector2.zero;
        struggleEnergy = struggleClock = 0f;
        struggleAudioArmed = true;
        if (claw != null) claw.localRotation = Quaternion.identity;
        if (cable != null) cable.localRotation = Quaternion.identity;
        // Unity forbids sibling changes during the parent's OnDisable. Hidden
        // presentation is rebuilt by RestoreGameplay when it next opens.
        if (gameObject.activeInHierarchy && motionSiblingBeforeTrap >= 0 && TrapMotion != null)
            TrapMotion.SetSiblingIndex(motionSiblingBeforeTrap);
        motionSiblingBeforeTrap = -1;
        if (!wasTrap) return;
        drop = 0f;
        if (Progress?.chapter02ClawCursorCaptured == true) RestoreCursorTrapCheckpoint();
        else { SetFingers(false); SetPhase(ClawPhase.Sweeping); ApplyClawPosition(); }
        RefreshButtons();
    }

    public bool TryDismissCursorTrap()
    {
        if (!IsCursorTrapControlling) return false;
        SuspendCursorTrap();
        Window.Minimize();
        return true;
    }

    void OnApplicationFocus(bool focused)
    {
        trapHasFocus = focused;
        if (!focused) SuspendCursorTrap();
    }

    // Destruction must only release ownership; rebuilding a captured pose here
    // can touch children that Unity has already destroyed during a scene unload.
    void OnDestroy()
    {
        motorAudio?.Dispose();
        motorAudio = null;
        CursorHandle.Instance?.ReleaseScriptControlWithoutWarp(this);
        StopOutletReveal();
    }
}
