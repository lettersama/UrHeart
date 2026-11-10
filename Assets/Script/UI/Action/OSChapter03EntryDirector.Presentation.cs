using UnityEngine;

public sealed partial class OSChapter03EntryDirector
{
    OSCursorReleaseDirector actor;
    RectTransform parkTarget;
    Vector2 parkPoint;
    int autoPhase;
    float autoClock;
    bool motionStarted, playerCaptured;
    readonly Vector3[] screenCorners = new Vector3[4];

    // Small human beats layered over the step machine; none of them is saved.
    const float UnlockHopSeconds = .4f, UnlockSpeakSeconds = .75f;
    const float LiveThinkSeconds = 1.6f, BeckonIdleSeconds = 4f;
    const float FirstKeyDelay = .25f, EnterDelay = .45f;
    float stepClock, cueClock, typeClock, nextKey;
    int cueStage, pressStage, beckonStage, typedChars;
    bool thinking;
    Vector2 aimPoint, beckonHome;

    static string UpgradeCommand => "tool install " + OSToolProgramIds.MemoryRepair04;

    void ResetCues()
    {
        StopThinking();
        stepClock = cueClock = typeClock = nextKey = 0f;
        cueStage = pressStage = beckonStage = typedChars = 0;
    }

    bool EnsureActor()
    {
        if (actor == null) actor = os?.CursorRelease;
        if (actor == null || (!actor.OwnsScriptActor(this) && !actor.TryAcquireScriptActor(this))) return false;
        actor.ScriptActorSetVisible(this, true);
        actor.ScriptActorSetPaused(this, false);
        return true;
    }

    void ReleaseActor()
    {
        if (actor != null) actor.ReleaseScriptActor(this);
        parkTarget = null;
    }

    bool CapturePlayer()
    {
        var cursor = CursorHandle.Instance;
        if (cursor == null) return false;
        if (!cursor.IsScriptControlledBy(this) && !cursor.AcquireScriptControlWithoutWarp(this)) return false;
        playerCaptured = true;
        return true;
    }

    void ReleasePlayer()
    {
        if (CursorHandle.Instance?.IsScriptControlledBy(this) == true)
            CursorHandle.Instance.ReleaseScriptControlWithoutWarp(this);
        playerCaptured = false;
    }

    void ParkBesideChapter(bool lean = false)
    {
        if (stateWindow != null && stateWindow.IsOpen && !stateWindow.IsMinimized && !stateWindow.IsClosing
            && os.StateCheckWindowController.TryGetChapterRecordTarget(3, out var target))
        {
            // Chapter cards are adjacent horizontally. Use the empty strip below
            // the label so the pointer never lands on the next chapter's artwork.
            var controller = os.StateCheckWindowController;
            if (controller.TryGetChapterRecordLabelTarget(3, out var label))
            {
                Rect bounds = ScreenBounds(label);
                float scale = CanvasScale(label);
                // After the unlock she shifts a step closer, as if reading the new card.
                Vector2 offset = lean ? new Vector2(-8f, 3f) : Vector2.zero;
                ParkAt(target, new Vector2(bounds.center.x - (12f - offset.x) * scale, bounds.yMin - (6f - offset.y) * scale));
            }
            else ParkBeside(target);
        }
        else ParkBeside(stateIcon != null ? stateIcon.transform as RectTransform : null);
    }

    void ParkBeside(RectTransform target)
    {
        if (target == null || !EnsureActor()) return;
        actor.ScriptActorSetPressed(this, false);
        actor.ScriptActorSetTilt(this, 0f);
        Rect bounds = ScreenBounds(target);
        float scale = CanvasScale(target);
        // The pointer's tip is near its upper-left corner. Reserve its full width,
        // not merely the tip, so neither artwork nor option hit areas are covered.
        float clearance = 10f * scale, pointerWidth = 24f * scale;
        Vector2 point = new Vector2(bounds.xMax + clearance, bounds.center.y + 5f * scale);
        if (point.x + pointerWidth > Screen.width - 4f * scale)
            point.x = bounds.xMin - clearance - pointerWidth;
        point.y = Mathf.Clamp(point.y, 24f * scale, Screen.height - 4f * scale);
        ParkAt(target, point);
    }

    void ParkAboveLive()
    {
        var target = liveWindow?.WindowRect;
        if (target == null) return;
        Rect bounds = ScreenBounds(target);
        float scale = CanvasScale(target);
        // Stay away from both the installer controls and the dialogue below Live.
        var point = new Vector2(bounds.center.x - 12f * scale, bounds.yMax + 30f * scale);
        if (point.y > Screen.height - 4f * scale)
            point = new Vector2(bounds.xMin - 34f * scale, bounds.yMax - 4f * scale);
        ParkAt(target, point);
    }

    void ParkAt(RectTransform target, Vector2 point)
    {
        if (target == null || !EnsureActor()) return;
        actor.ScriptActorSetPressed(this, false);
        actor.ScriptActorSetTilt(this, 0f);
        if (parkTarget != target || (point - parkPoint).sqrMagnitude > 4f)
        {
            float seconds = parkTarget == target ? .25f
                : TravelSeconds(Vector2.Distance(actor.CompanionScreenPosition, point) / CanvasScale(target));
            parkTarget = target; parkPoint = point;
            actor.ScriptActorMoveTo(this, point, seconds);
        }
        actor.ScriptActorSetIdleBob(this, true);
    }

    // Fitts-style: short hops are quick, crossing the screen takes longer but
    // never drags. Distance is in canvas units.
    static float TravelSeconds(float distance) =>
        Mathf.Clamp(.26f + .11f * Mathf.Log(1f + Mathf.Max(0f, distance) / 16f, 2f), .3f, .95f);

    // Nobody clicks the exact centre; land a little on the side she came from.
    static Vector2 AimPoint(Rect bounds, Vector2 from)
    {
        Vector2 half = bounds.size * .5f, toward = from - bounds.center;
        return bounds.center + new Vector2(Mathf.Clamp(toward.x, -half.x, half.x),
            Mathf.Clamp(toward.y, -half.y, half.y)) * .22f;
    }

    static float CanvasScale(RectTransform target)
    {
        var canvas = target.GetComponentInParent<Canvas>();
        return canvas != null ? canvas.rootCanvas.scaleFactor : 1f;
    }

    Rect ScreenBounds(RectTransform target)
    {
        target.GetWorldCorners(screenCorners);
        var canvas = target.GetComponentInParent<Canvas>();
        var camera = canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
        Vector2 lo = RectTransformUtility.WorldToScreenPoint(camera, screenCorners[0]);
        Vector2 hi = lo;
        for (int i = 1; i < 4; i++)
        {
            var p = RectTransformUtility.WorldToScreenPoint(camera, screenCorners[i]);
            lo = Vector2.Min(lo, p); hi = Vector2.Max(hi, p);
        }
        return Rect.MinMaxRect(lo.x, lo.y, hi.x, hi.y);
    }

    void NextAutoPhase()
    {
        autoPhase++; autoClock = 0f; motionStarted = false;
    }

    // A reach that slightly overshoots (long moves) or stops short (short ones),
    // a quick correction onto the aim point, a brief hover, then the click(s)
    // before the same UI command the player uses. Desktop icons get two clicks.
    bool MoveAndPress(RectTransform target, float dt, int clicks = 1, Vector2? aim = null)
    {
        if (target == null) return false;
        if (!motionStarted)
        {
            motionStarted = true; autoClock = 0f; pressStage = 0;
            actor.ScriptActorSetPressed(this, false);
            actor.ScriptActorSetIdleBob(this, false);
            Rect bounds = ScreenBounds(target);
            float scale = CanvasScale(target);
            Vector2 from = actor.CompanionScreenPosition;
            aimPoint = aim ?? AimPoint(bounds, from);
            Vector2 delta = aimPoint - from;
            float distance = delta.magnitude / scale;
            float miss = distance > 120f ? Mathf.Min(6f, distance * .03f) : -Mathf.Min(3f, distance * .2f);
            Vector2 reach = distance > 1f ? aimPoint + delta.normalized * miss * scale : aimPoint;
            actor.ScriptActorMoveTo(this, reach, TravelSeconds(distance));
            return false;
        }
        if (actor.ScriptActorIsMoving) return false;
        if (pressStage == 0)
        {
            pressStage = 1;
            if ((actor.CompanionScreenPosition - aimPoint).sqrMagnitude > 1f)
            { actor.ScriptActorMoveTo(this, aimPoint, .14f); return false; }
        }
        autoClock += dt;
        const float Hover = .12f, Press = .1f, Gap = .08f;
        float t = autoClock - Hover, cycle = Press + Gap;
        bool pressed = false;
        for (int i = 0; i < clicks; i++)
            if (t >= i * cycle && t < i * cycle + Press) pressed = true;
        actor.ScriptActorSetPressed(this, pressed);
        if (t < clicks * cycle) return false;
        actor.ScriptActorSetIdleBob(this, true);
        return true;
    }

    /// <summary>
    /// After her Enter the installer runs on its own. She waits by the prompt while
    /// it opens, then beside it watching the bar. Once it finishes she comes back to
    /// Live, who thinks it over (Expend02) before the line. True once she may speak.
    /// </summary>
    bool TickInstallingCue(float dt)
    {
        var installer = library?.Installer != null ? library.Installer.GetComponent<OSWindowUnit>() : null;
        bool watching = library != null && library.IsInstalling && installer != null
            && installer.IsOpen && !installer.IsClosing;
        if (watching && installer.IsOpening) return false;
        if (watching) ParkBeside(installer.WindowRect);
        else ParkAboveLive();
        if (State.chapter03EntryInstallAnswer != 0) { StopThinking(); cueStage = 2; return false; }
        switch (cueStage)
        {
            case 0:
                if (watching || !GameDataManager.Instance.IsMemoryRepair04Installed) return false;
                cueStage = 1; cueClock = 0f;
                thinking = true;
                return false;
            case 1:
                cueClock += dt;
                if (cueClock < LiveThinkSeconds) return false;
                StopThinking(); cueStage = 2;
                return true;
            default:
                return true;
        }
    }

    void StopThinking()
    {
        // The entry animation resolver releases or replaces this intent after
        // the current step settles. Never clear a question that just began.
        thinking = false;
    }

    /// <summary>
    /// Waiting by the memory icon. If the player lingers she leans toward it and
    /// back now and then, a small "this one" instead of another message.
    /// </summary>
    void TickAwaitMemory(float dt)
    {
        var icon = memoryIcon != null ? memoryIcon.transform as RectTransform : null;
        if (icon == null) return;
        if (beckonStage == 0)
        {
            ParkBeside(icon);
            // Not while "不赖..." (or any other line) is still on screen.
            if (actor.ScriptActorIsMoving || !DialogueClear) { cueClock = Mathf.Min(cueClock, 0f); return; }
            cueClock += dt;
            if (cueClock < BeckonIdleSeconds) return;
            beckonHome = parkPoint;
            Vector2 toward = ScreenBounds(icon).center - beckonHome;
            if (toward.sqrMagnitude < 1f) { cueClock = 0f; return; }
            actor.ScriptActorMoveTo(this, beckonHome + toward.normalized * 5f * CanvasScale(icon), .16f);
            beckonStage = 1;
            return;
        }
        if (actor.ScriptActorIsMoving) return;
        if (beckonStage == 1)
        {
            actor.ScriptActorMoveTo(this, beckonHome, .22f);
            beckonStage = 2;
            return;
        }
        // Longer gaps after the first nudge so it never turns into nagging.
        beckonStage = 0; cueClock = -3f;
    }

    void TickAutoInstall(float dt)
    {
        if (library == null || terminal == null || !EnsureActor() || !CapturePlayer()) return;
        if (GameDataManager.Instance.IsMemoryRepair04Installed)
        {
            FinishAutoInstall();
            return;
        }
        switch (autoPhase)
        {
            case 0:
                if (stateWindow == null || !stateWindow.IsOpen)
                { NextAutoPhase(); break; }
                if (stateWindow.IsClosing || stateWindow.IsOpening) break;
                // A beat after "谢谢.." before she reaches for the window.
                if (!motionStarted && autoClock < .35f) { autoClock += dt; break; }
                RectTransform close = stateWindow.transform.Find("TopArea/CloseButton") as RectTransform;
                if (MoveAndPress(close, dt))
                {
                    stateWindow.CloseAnimated();
                    AudioManager.Instance?.PlaySfx("CloseUnit");
                    NextAutoPhase();
                }
                break;
            case 1:
                if (stateWindow?.IsClosing == true) break;
                if (MoveAndPress(toolIcon != null ? toolIcon.ProcessIcon?.rectTransform : null, dt, 2))
                {
                    os.OpenToolWindow();
                    if (toolWindow?.IsOpen == true)
                    {
                        os.NotifyWindowPointerPressed(toolWindow);
                        AudioManager.Instance?.PlaySfx("OpenUnit");
                        NextAutoPhase();
                    }
                }
                break;
            case 2:
                if (toolWindow == null || !toolWindow.IsOpen || toolWindow.IsOpening || !library.CanFillCommand) break;
                os.NotifyWindowPointerPressed(toolWindow);
                if (!library.IsOpen)
                {
                    if (!library.IsUnpacking)
                        terminal.SubmitCommand(OSToolProgramLibrary.UnpackCommand);
                    break;
                }
                library.RefreshPrograms();
                terminal.RevealLatestForScript();
                NextAutoPhase();
                break;
            case 3:
                if (!library.CanFillCommand || terminal.CommandInput == null) break;
                // Let layout and its scroll correction settle before targeting the prompt.
                if (!motionStarted && autoClock < .2f) { autoClock += dt; break; }
                var line = terminal.CommandInput.transform as RectTransform;
                Rect lineBounds = ScreenBounds(line);
                float lineScale = CanvasScale(line);
                // Click near the start of the prompt, where the caret will be.
                Vector2 caret = new Vector2(lineBounds.xMin + Mathf.Min(18f * lineScale, lineBounds.width * .25f), lineBounds.center.y);
                if (MoveAndPress(line, dt, 1, caret))
                {
                    os.NotifyWindowPointerPressed(toolWindow);
                    terminal.FillCommand(string.Empty);
                    terminal.RevealLatestForScript();
                    // Step down off the line so the letters she types stay readable.
                    actor.ScriptActorMoveTo(this, new Vector2(caret.x + 4f * lineScale, lineBounds.yMin - 10f * lineScale), .3f);
                    actor.ScriptActorSetIdleBob(this, true);
                    typedChars = 0; typeClock = 0f; nextKey = FirstKeyDelay;
                    NextAutoPhase();
                }
                break;
            case 4:
                if (!library.CanFillCommand) break;
                string command = UpgradeCommand;
                if (typedChars < command.Length)
                {
                    // Live types it out herself, one key at a time.
                    typeClock += dt;
                    if (typeClock < nextKey) break;
                    typeClock = 0f; typedChars++;
                    if (!os.IsWindowMouseActive(toolWindow)) os.NotifyWindowPointerPressed(toolWindow);
                    terminal.FillCommand(command.Substring(0, typedChars));
                    nextKey = KeyDelay(command[typedChars - 1]);
                    break;
                }
                // Anything typed over her line (the keyboard is not captured): start again.
                if (!HasUpgradeCommand()) { typedChars = 0; typeClock = 0f; nextKey = FirstKeyDelay; break; }
                typeClock += dt;
                if (typeClock < EnterDelay) break;
                // Enter. The small hop is her pressing it.
                os.NotifyWindowPointerPressed(toolWindow);
                actor.ScriptActorHop(this);
                terminal.SubmitCommand(terminal.CommandInput.text);
                if (library.IsInstalling || GameDataManager.Instance.IsMemoryRepair04Installed)
                    FinishAutoInstall();
                else { autoPhase = 3; motionStarted = false; autoClock = 0f; }
                break;
        }
    }

    bool HasUpgradeCommand() => terminal?.CommandInput != null
        && string.Equals(terminal.CommandInput.text.Trim(), UpgradeCommand, System.StringComparison.OrdinalIgnoreCase);

    // Not a machine: an uneven rhythm, with a beat after each word.
    static float KeyDelay(char typed)
    {
        float delay = .055f + Random.value * .05f;
        if (typed == ' ' || typed == '-') delay += .12f;
        return delay;
    }

    void FinishAutoInstall()
    {
        actor?.ScriptActorSetPressed(this, false);
        ReleasePlayer();
        SetStep(EntryStep.Installing);
    }
}
