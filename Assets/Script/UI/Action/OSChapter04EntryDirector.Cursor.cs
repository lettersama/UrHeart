using UnityEngine;

public sealed partial class OSChapter04EntryDirector
{
    OSCursorReleaseDirector actor;
    RectTransform parkTarget;
    Vector2 parkPoint, pressPoint, beckonHome;
    readonly Vector3[] cursorCorners = new Vector3[4];
    float pressClock, beckonClock;
    int pressStage, beckonStage;
    bool pressStarted;

    bool EnsureActor()
    {
        if (actor == null) actor = os?.CursorRelease;
        if (actor == null || !actor.OwnsScriptActor(this) && !actor.TryAcquireScriptActor(this)) return false;
        actor.ScriptActorSetVisible(this, true);
        actor.ScriptActorSetPaused(this, false);
        return true;
    }

    void ReleaseActor()
    {
        if (actor != null) actor.ReleaseScriptActor(this);
        parkTarget = null;
    }

    void ResetCursorCues()
    {
        parkTarget = null; pressStarted = false;
        pressClock = beckonClock = 0f;
        pressStage = beckonStage = 0;
    }

    void ParkBesideChapter(bool lean = false)
    {
        if (stateWindow != null && stateWindow.IsOpen && !stateWindow.IsMinimized && !stateWindow.IsClosing
            && os.StateCheckWindowController.TryGetChapterRecordTarget(4, out var target))
        {
            if (os.StateCheckWindowController.TryGetChapterRecordLabelTarget(4, out var label))
            {
                Rect bounds = ScreenBounds(label);
                float scale = CanvasScale(label);
                Vector2 offset = lean ? new Vector2(-8f, 3f) : Vector2.zero;
                ParkAt(target, new Vector2(bounds.center.x - (12f - offset.x) * scale, bounds.yMin - (6f - offset.y) * scale));
            }
            else ParkBeside(target);
        }
        else ParkBeside(stateIcon != null ? stateIcon.transform as RectTransform : null);
    }

    void ParkBeside(RectTransform target)
    {
        if (target == null) return;
        Rect bounds = ScreenBounds(target);
        float scale = CanvasScale(target);
        Vector2 point = new Vector2(bounds.xMax + 10f * scale, bounds.center.y + 5f * scale);
        if (point.x + 24f * scale > Screen.width - 4f * scale) point.x = bounds.xMin - 34f * scale;
        point.y = Mathf.Clamp(point.y, 24f * scale, Screen.height - 4f * scale);
        ParkAt(target, point);
    }

    void ParkAboveLive()
    {
        var target = liveWindow?.WindowRect;
        if (target == null) return;
        Rect bounds = ScreenBounds(target);
        float scale = CanvasScale(target);
        Vector2 point = new Vector2(bounds.center.x - 12f * scale, bounds.yMax + 30f * scale);
        if (point.y > Screen.height - 4f * scale) point = new Vector2(bounds.xMin - 34f * scale, bounds.yMax - 4f * scale);
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

    void TickOpenState(float dt)
    {
        if (!EnsureActor() || stateIcon == null) return;
        var target = stateIcon.ProcessIcon?.rectTransform;
        if (!MoveAndPress(target, dt, 2)) return;
        os.OpenStateCheck(GameDataManager.Instance.CurrentStateCheckProgress);
        if (stateWindow == null || !stateWindow.IsOpen) return;
        os.NotifyWindowPointerPressed(stateWindow);
        AudioManager.Instance?.PlaySfx("OpenUnit");
        SetStep(EntryStep.AwaitUnlock);
        ParkBesideChapter();
    }

    bool MoveAndPress(RectTransform target, float dt, int clicks)
    {
        if (target == null) return false;
        if (!pressStarted)
        {
            pressStarted = true; pressStage = 0; pressClock = 0f;
            actor.ScriptActorSetPressed(this, false);
            actor.ScriptActorSetIdleBob(this, false);
            Rect bounds = ScreenBounds(target);
            Vector2 from = actor.CompanionScreenPosition, half = bounds.size * .5f;
            Vector2 toward = from - bounds.center;
            pressPoint = bounds.center + new Vector2(Mathf.Clamp(toward.x, -half.x, half.x),
                Mathf.Clamp(toward.y, -half.y, half.y)) * .22f;
            float distance = Vector2.Distance(from, pressPoint) / CanvasScale(target);
            actor.ScriptActorMoveTo(this, pressPoint, TravelSeconds(distance));
            return false;
        }
        if (actor.ScriptActorIsMoving) return false;
        if (pressStage == 0) { pressStage = 1; pressClock = 0f; }
        pressClock += dt;
        const float hover = .12f, press = .1f, gap = .08f;
        float t = pressClock - hover, cycle = press + gap;
        bool pressed = false;
        for (int i = 0; i < clicks; i++) if (t >= i * cycle && t < i * cycle + press) pressed = true;
        actor.ScriptActorSetPressed(this, pressed);
        if (t < clicks * cycle) return false;
        actor.ScriptActorSetIdleBob(this, true);
        return true;
    }

    void TickAwaitTV(float dt)
    {
        var icon = os.TVDesktopIcon != null ? os.TVDesktopIcon.transform as RectTransform : null;
        if (icon == null || !EnsureActor()) return;
        if (beckonStage == 0)
        {
            ParkBeside(icon);
            if (actor.ScriptActorIsMoving || !DialogueClear) { beckonClock = Mathf.Min(beckonClock, 0f); return; }
            beckonClock += dt;
            if (beckonClock < 4f) return;
            beckonHome = parkPoint;
            Vector2 toward = ScreenBounds(icon).center - beckonHome;
            if (toward.sqrMagnitude < 1f) { beckonClock = 0f; return; }
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
        beckonStage = 0; beckonClock = -3f;
    }

    static float TravelSeconds(float distance) =>
        Mathf.Clamp(.26f + .11f * Mathf.Log(1f + Mathf.Max(0f, distance) / 16f, 2f), .3f, .95f);

    static float CanvasScale(RectTransform target)
    {
        var canvas = target.GetComponentInParent<Canvas>();
        return canvas != null ? canvas.rootCanvas.scaleFactor : 1f;
    }

    Rect ScreenBounds(RectTransform target)
    {
        target.GetWorldCorners(cursorCorners);
        var canvas = target.GetComponentInParent<Canvas>();
        var camera = canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
        Vector2 lo = RectTransformUtility.WorldToScreenPoint(camera, cursorCorners[0]), hi = lo;
        for (int i = 1; i < 4; i++)
        {
            Vector2 p = RectTransformUtility.WorldToScreenPoint(camera, cursorCorners[i]);
            lo = Vector2.Min(lo, p); hi = Vector2.Max(hi, p);
        }
        return Rect.MinMaxRect(lo.x, lo.y, hi.x, hi.y);
    }
}
