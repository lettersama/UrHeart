using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed partial class OSCursorReleaseDirector
{
    RectTransform visualRoot;
    Image cursorImage, paperImage, continueTip;
    Sprite normalCursor, pressedCursor, apologyPaper, yeahPaper;
    OSWindowButton closeTarget;
    RectTransform titleTarget;
    Vector2 fromPoint, phaseTarget, dragFromOffset, followVelocity;
    bool dragStarted, closeWasClicked, liveMoveInteractionOwned, liveCouldMove, liveCouldResize;
    Image[] confetti;
    Vector2[] confettiOrigins, confettiVelocities;
    Vector2 celebrationOrigin;
    // Apology/Yeah share a 64x72 canvas. Feet end at top-down y54 and
    // the stick runs y55..70: y59 grips its upper part, four pixels below.
    // Use an integer local point to keep the unrotated paper on the pixel grid.
    static readonly Vector2 PaperGrip = new Vector2(0f, 13f);
    static readonly Vector2 HandContact = new Vector2(-2f, 0f);
    sealed class SorryPuff
    {
        public RectTransform root;
        public CanvasGroup group;
        public TMP_Text text;
        public Vector2 origin;
        public float age, direction;
        public bool active;
    }
    readonly SorryPuff[] sorryPuffs = new SorryPuff[3];
    float sorryCooldown = .18f;
    int sorrySerial;
    Vector2 chaseFollowPoint;
    bool chaseFollowInitialized;
    // Sulking route: the free player answers with the same mouse gestures the
    // tests taught. Nod = forgive; shake = she droops, then pleads harder.
    readonly OSCursorShakeRecognizer chaseGestures = new OSCursorShakeRecognizer();
    Vector2 chasePlayerPrevious;
    bool chasePlayerTracked;
    float chaseDroop, chaseGestureIdle;
    int chaseRefusals;
    Image nodHint;
    const float ChaseGestureWarmup = .8f, ChaseDroopSeconds = .9f, NodHintDelay = 2.6f;
    Vector2 idlePosition, idleVelocity, idleOffset, idleHome, idleGoal;
    bool idleInitialized, idleWritingCursor, idleAvoiding, idleReturning;
    float idleNearTime, idleAvoidTime, idleAvoidCooldown;
    int idleRail = -1;
    readonly Vector3[] idleLiveCorners = new Vector3[4];
    const float IdleNearEnter = 11f, IdleNearExit = 22f;
    const float IdleReturnEnter = 64f, IdleReturnExit = 18f;
    const float SorryLifetime = 1.25f;
    static readonly Color Red = new Color32(216, 52, 58, 255);
    Vector2 CursorPoint => cursorImage != null ? cursorImage.rectTransform.anchoredPosition : Vector2.zero;

    void TickStepAudio(float before, float after)
    {
        if (audioStep != Step) return;
        float at;
        float gain = .55f, pitch = 1f;
        if (Step == ReleaseStep.FetchPaper) at = .72f;
        else if (Step == ReleaseStep.FetchYeah) at = .62f;
        else if (Step == ReleaseStep.StowPaper) { at = .68f; pitch = .94f; }
        else if (Step == ReleaseStep.Celebrate) { at = .14f; gain = .45f; pitch = 1.2f; }
        else return;
        if (before < at && after >= at)
            Chapter02Audio.Play("Chapter02_PaperRustle", gain, pitch);
    }

    public void ConfigureArt(Sprite normal, Sprite pressed, Sprite apology, Sprite yeah)
    {
        normalCursor = normal; pressedCursor = pressed; apologyPaper = apology; yeahPaper = yeah;
    }
    void EnsureVisuals()
    {
        if (normalCursor == null) normalCursor = CursorHandle.Instance?.NormalSprite;
        if (pressedCursor == null) pressedCursor = CursorHandle.Instance?.PressedSprite;
        if (apologyPaper == null) apologyPaper = Resources.Load<Sprite>("CursorExperiment/Release/Apology");
        if (yeahPaper == null) yeahPaper = Resources.Load<Sprite>("CursorExperiment/Release/Yeah");
        var parent = live != null ? live.transform.parent as RectTransform : null;
        if (parent == null) return;
        if (visualRoot != null && visualRoot.parent != parent) { DestroyVisuals(); }
        if (visualRoot != null)
        {
            if (cursorImage != null && cursorImage.sprite == null && normalCursor != null) SetPressed(false);
            return;
        }
        var go = new GameObject("CharacterPermanentRedPointer", typeof(RectTransform), typeof(CanvasGroup));
        go.layer = parent.gameObject.layer;
        visualRoot = (RectTransform)go.transform; visualRoot.SetParent(parent, false);
        visualRoot.anchorMin = Vector2.zero; visualRoot.anchorMax = Vector2.one;
        visualRoot.offsetMin = visualRoot.offsetMax = Vector2.zero;
        var cg = go.GetComponent<CanvasGroup>(); cg.interactable = false; cg.blocksRaycasts = false;
        paperImage = NewImage("ApologyPaper", visualRoot);
        paperImage.rectTransform.pivot = new Vector2(.5f, 0f);
        paperImage.gameObject.SetActive(false);
        cursorImage = NewImage("RedPointer", visualRoot);
        cursorImage.color = Red;
        cursorImage.rectTransform.pivot = new Vector2(.05f, .95f);
        SetPressed(false);
        Vector2 point = State.chapter02RedCursorHasPosition ? State.chapter02RedCursorPosition : LiveSidePoint();
        PlaceCursor(ClampPoint(point, 8f));
    }
    static Image NewImage(string name, RectTransform parent)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.layer = parent.gameObject.layer;
        var result = go.GetComponent<Image>(); result.rectTransform.SetParent(parent, false);
        result.rectTransform.anchorMin = result.rectTransform.anchorMax = new Vector2(.5f, .5f);
        result.raycastTarget = false; result.preserveAspect = true; return result;
    }
    void SetPressed(bool pressed)
    {
        if (cursorImage == null) return;
        cursorImage.sprite = pressed && pressedCursor != null ? pressedCursor : normalCursor;
        cursorImage.color = pressed ? new Color32(177, 32, 39, 255) : Red;
        if (cursorImage.sprite != null) cursorImage.rectTransform.sizeDelta = cursorImage.sprite.rect.size * 1.4f;
    }
    void PlaceCursor(Vector2 point)
    {
        if (cursorImage == null) return;
        // Scripted gestures own the actor completely. A later idle tick starts
        // from their final pose, not from stale avoidance/pursuit velocity.
        if (!idleWritingCursor) idleInitialized = false;
        cursorImage.rectTransform.anchoredPosition = new Vector2(Mathf.Round(point.x), Mathf.Round(point.y));
    }
    void SavePointerPosition()
    {
        if (State == null || !State.chapter02RedCursorUnlocked || cursorImage == null) return;
        State.chapter02RedCursorHasPosition = true;
        State.chapter02RedCursorPosition = CursorPoint;
    }
    void LateUpdate()
    {
        if (visualRoot == null || !visualRoot.gameObject.activeSelf) return;
        if (IsEngaged && (Step == ReleaseStep.FetchPaper || Step == ReleaseStep.FetchYeah
            || (Step == ReleaseStep.StowPaper && elapsed >= .68f)))
        {
            // The same opaque Live window that covers the doll also covers the
            // hand reaching for it. There is no fade standing in for occlusion.
            int beforeLive = live.transform.GetSiblingIndex();
            if (visualRoot.GetSiblingIndex() < beforeLive) beforeLive--;
            visualRoot.SetSiblingIndex(Mathf.Max(0, beforeLive));
        }
        else visualRoot.SetAsLastSibling();
    }
    void TickCloseMessage(float dt)
    {
        if (cursorImage == null || messages?.LinkWindow == null) return;
        if (closeTarget == null || closeTarget.LinkedWindow != messages.LinkWindow)
        {
            foreach (var button in messages.LinkWindow.GetComponentsInChildren<OSWindowButton>(true))
                if (button.Action == OSWindowButton.WindowAction.Close && button.LinkedWindow == messages.LinkWindow)
                { closeTarget = button; break; }
        }
        if (closeTarget == null) { elapsed -= dt; return; }
        Vector2 target = LocalPoint((RectTransform)closeTarget.transform, ((RectTransform)closeTarget.transform).rect.center);
        float flight = .62f;
        if (elapsed < flight) PlaceCursor(HumanCurve(fromPoint, target, elapsed / flight, -9f));
        else
        {
            PlaceCursor(target);
            if (EventSystem.current != null) closeTarget.OnPointerEnter(new PointerEventData(EventSystem.current));
            SetPressed(elapsed >= flight + .09f);
            if (elapsed >= flight + .20f && !closeWasClicked)
            {
                closeWasClicked = true; ClearCloseHover();
                messages.LinkWindow.CloseFromScript();
                AudioManager.Instance?.PlaySfx("CloseUnit");
                SetPressed(false); SetStep(ReleaseStep.MoveLive);
            }
        }
    }
    void ClearCloseHover()
    {
        if (closeTarget != null && EventSystem.current != null)
            closeTarget.OnPointerExit(new PointerEventData(EventSystem.current));
        closeTarget = null;
    }
    RectTransform FindTitle()
    {
        if (live == null) return null;
        // IsDragAreaTarget is the window's actual authored hit target. The
        // first matching ancestor is its title bar, not an invented coordinate.
        foreach (var rect in live.GetComponentsInChildren<RectTransform>(true))
            if (live.IsDragAreaTarget(rect.gameObject)) return rect;
        return null;
    }
    Vector2 TitlePoint()
    {
        if (titleTarget == null) titleTarget = FindTitle();
        if (titleTarget == null) return LocalPoint(live.WindowRect, new Vector2(live.WindowRect.rect.center.x, live.WindowRect.rect.yMax - 6));
        return LocalPoint(titleTarget, titleTarget.rect.center - new Vector2(titleTarget.rect.width * .13f, 0f));
    }
    void OwnLiveMotion()
    {
        if (liveMoveInteractionOwned || live == null) return;
        GetComponent<OSCursorExperimentLiveReaction>()?.PausePresentation();
        liveCouldMove = live.AllowsMove; liveCouldResize = live.AllowsResize;
        live.ConfigureInteraction(false, false); liveMoveInteractionOwned = true;
    }
    Vector2 CenterOffset()
    {
        var parent = live.WindowRect.parent as RectTransform;
        Vector2 actualCenter = parent.InverseTransformPoint(live.WindowRect.TransformPoint(live.WindowRect.rect.center));
        // Center the conversation composition: reserve the lower third for the
        // dialog, while Live's horizontal center is the screen's center.
        Vector2 goal = parent.rect.center + new Vector2(0f, Mathf.Round(parent.rect.height * .12f));
        return live.PresentationOffset + goal - actualCenter;
    }
    void KeepLiveCentered()
    {
        if (live == null || !live.IsOpen || live.IsOpening) return;
        OwnLiveMotion(); live.TrySetPresentationOffset(this, CenterOffset());
    }
    void TickMoveLive()
    {
        if (live == null || cursorImage == null) return;
        OwnLiveMotion();
        const float approach = .62f, grip = .14f, drag = 1.10f, settle = .3f;
        if (elapsed < approach)
        {
            PlaceCursor(HumanCurve(fromPoint, TitlePoint(), elapsed / approach, 11f)); return;
        }
        if (!dragStarted)
        {
            dragStarted = true; dragFromOffset = live.PresentationOffset;
            phaseTarget = CenterOffset();
        }
        if (elapsed < approach + grip) { SetPressed(true); PlaceCursor(TitlePoint()); return; }
        float u = Mathf.Clamp01((elapsed - approach - grip) / drag);
        // One mild arc and a small final adjustment resemble an intentional
        // human drag; the cursor remains fixed to the real title-bar grip.
        Vector2 offset = HumanCurve(dragFromOffset, phaseTarget, u, 5f);
        live.TrySetPresentationOffset(this, offset);
        PlaceCursor(TitlePoint());
        if (u >= 1f) SetPressed(false);
        if (elapsed >= approach + grip + drag + settle) SetStep(ReleaseStep.Explain);
    }
    Vector2 LiveSidePoint()
    {
        if (visualRoot == null || live == null) return Vector2.zero;
        if (!live.IsOpen || live.IsMinimized || !live.gameObject.activeInHierarchy)
            return new Vector2(visualRoot.rect.xMax - 18f, visualRoot.rect.yMin + 22f);
        return ClampPoint(LocalPoint(live.WindowRect,
            new Vector2(live.WindowRect.rect.xMax + 15f, live.WindowRect.rect.center.y - 16f)), 8f);
    }
    Vector2 UnderLivePoint()
    {
        return LocalPoint(live.WindowRect, new Vector2(live.WindowRect.rect.center.x,
            live.WindowRect.rect.yMin + 48f));
    }
    void TickIdle(float dt)
    {
        if (cursorImage == null || visualRoot == null) return;
        if (!idleInitialized)
        {
            idlePosition = CursorPoint; idleVelocity = idleOffset = Vector2.zero;
            idleAvoiding = idleReturning = false;
            idleNearTime = idleAvoidTime = idleAvoidCooldown = 0f;
            idleInitialized = true;
        }
        Rect screen = IdleScreenBounds();
        bool hasWindow = TryIdleWindowBounds(out Rect window);
        Rect obstacle = hasWindow ? IdleWindowKeepout(window) : default;
        idleHome = ResolveIdleHome(screen, window, hasWindow);
        // A paused first frame must not consume a reaction or integrate motion.
        if (dt <= 0f) return;
        dt = Mathf.Min(dt, .1f);
        float homeDistance = Vector2.Distance(idlePosition, idleHome);
        if (!idleReturning && homeDistance > IdleReturnEnter) idleReturning = true;
        else if (idleReturning && homeDistance < IdleReturnExit) idleReturning = false;
        idleAvoidCooldown = Mathf.Max(0f, idleAvoidCooldown - dt);
        if (idleReturning)
        {
            idleAvoiding = false; idleNearTime = 0f;
            idleOffset = Vector2.MoveTowards(idleOffset, Vector2.zero, 22f * dt);
        }
        else if (TryIdlePlayerPoint(out Vector2 player))
        {
            float distance = Vector2.Distance(idlePosition, player);
            idleNearTime = distance < IdleNearEnter ? idleNearTime + dt : 0f;
            idleAvoidTime += dt;
            if (idleNearTime >= .1f && idleAvoidCooldown <= 0f && homeDistance < 24f)
            {
                Vector2 away = idlePosition - player;
                if (away.sqrMagnitude < .01f) away = idleRail == 1 ? Vector2.left : Vector2.right;
                Vector2 candidate = idlePosition + away.normalized * 10f;
                // A close player on the outside must not push her into Live.
                // When the normal step is blocked, sidestep along its edge.
                Vector2 safe = ClampIdleTarget(candidate, screen, obstacle, hasWindow);
                if (Vector2.Distance(safe, idlePosition) < 3f && hasWindow)
                {
                    Vector2 tangent = idleRail < 2 ? Vector2.up : Vector2.right;
                    float sign = Vector2.Dot(away, tangent) < -.1f ? -1f : 1f;
                    safe = ClampIdleTarget(idlePosition + tangent * (10f * sign), screen, obstacle, true);
                }
                idleOffset = Vector2.ClampMagnitude(safe - idleHome, 16f);
                idleAvoiding = true; idleAvoidTime = 0f; idleNearTime = 0f; idleAvoidCooldown = .85f;
            }
            if (idleAvoiding && distance > IdleNearExit && idleAvoidTime >= .75f) idleAvoiding = false;
        }
        else idleAvoiding = false;
        if (!idleAvoiding && !idleReturning) idleOffset = Vector2.MoveTowards(idleOffset, Vector2.zero, 5f * dt);
        Vector2 drift = idleReturning ? Vector2.zero : new Vector2(Mathf.Sin(idleTime * .43f), Mathf.Sin(idleTime * .29f)) * .65f;
        idleGoal = ClampIdleTarget(idleHome + (idleReturning ? Vector2.zero : idleOffset + drift), screen, obstacle, hasWindow);
        Vector2 waypoint = hasWindow ? IdleRouteGoal(idlePosition, idleGoal, obstacle, screen) : idleGoal;
        float speed = idleReturning ? 72f : idleAvoiding ? 20f : 30f;
        idlePosition = Vector2.SmoothDamp(idlePosition, waypoint, ref idleVelocity,
            idleReturning ? .48f : idleAvoiding ? .38f : .6f, speed, dt);
        idlePosition = ClampToRect(idlePosition, screen);
        // Round only the rendered image. Feeding the rounded hotspot back into
        // SmoothDamp stalls slow movement and jitters at one-pixel thresholds.
        idleWritingCursor = true; PlaceCursor(ApplyFlourish(idlePosition, dt)); idleWritingCursor = false;
    }
    bool TryIdlePlayerPoint(out Vector2 point)
    {
        point = Vector2.zero;
        if (CursorHandle.Instance == null || visualRoot == null) return false;
        var canvas = visualRoot.GetComponentInParent<Canvas>();
        var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        return RectTransformUtility.ScreenPointToLocalPointInRectangle(visualRoot,
            CursorHandle.Instance.DisplayedScreenPosition, camera, out point);
    }
    Rect IdleScreenBounds()
    {
        Rect bounds = visualRoot.rect;
        Vector2 size = cursorImage.rectTransform.sizeDelta, pivot = cursorImage.rectTransform.pivot;
        return Rect.MinMaxRect(bounds.xMin + size.x * pivot.x + 2f,
            bounds.yMin + size.y * pivot.y + 2f, bounds.xMax - size.x * (1f - pivot.x) - 2f,
            bounds.yMax - size.y * (1f - pivot.y) - 2f);
    }
    bool TryIdleWindowBounds(out Rect bounds)
    {
        bounds = default;
        if (live == null || !live.IsOpen || live.IsMinimized || !live.gameObject.activeInHierarchy) return false;
        live.WindowRect.GetWorldCorners(idleLiveCorners);
        Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity), max = -min;
        foreach (Vector3 corner in idleLiveCorners)
        {
            Vector2 point = visualRoot.InverseTransformPoint(corner);
            min = Vector2.Min(min, point); max = Vector2.Max(max, point);
        }
        bounds = Rect.MinMaxRect(min.x, min.y, max.x, max.y); return true;
    }
    Rect IdleWindowKeepout(Rect window)
    {
        Vector2 size = cursorImage.rectTransform.sizeDelta, pivot = cursorImage.rectTransform.pivot;
        return Rect.MinMaxRect(window.xMin - size.x * (1f - pivot.x) - 4f,
            window.yMin - size.y * (1f - pivot.y) - 4f,
            window.xMax + size.x * pivot.x + 4f, window.yMax + size.y * pivot.y + 4f);
    }
    Vector2 ResolveIdleHome(Rect screen, Rect window, bool hasWindow)
    {
        if (!hasWindow)
        {
            idleRail = -1;
            return ClampToRect(new Vector2(screen.xMax - 8f, screen.yMin + 8f), screen);
        }
        Rect keepout = IdleWindowKeepout(window);
        // Keep a chosen edge until it really loses its safe space. This is
        // deliberate hysteresis: dragging Live back a pixel cannot flip sides.
        if (idleRail < 0) idleRail = 0;
        bool chosenFits = IdleRailFits(idleRail, keepout, screen);
        float chosenDistance = Vector2.Distance(IdleRailPoint(idleRail, window, keepout, screen), idlePosition);
        if (!chosenFits || chosenDistance > IdleReturnEnter)
        {
            float nearest = float.PositiveInfinity; int nextRail = -1;
            for (int side = 0; side < 4; side++)
            {
                if (!IdleRailFits(side, keepout, screen)) continue;
                float distance = Vector2.Distance(IdleRailPoint(side, window, keepout, screen), idlePosition);
                if (distance < nearest) { nearest = distance; nextRail = side; }
            }
            if (nextRail >= 0 && (!chosenFits || nearest < chosenDistance - 24f)) idleRail = nextRail;
            else if (nextRail < 0)
            {
                // A maximized Live leaves no exterior strip. Park in its
                // header margin, away from the character/dialogue body.
                idleRail = -1;
                return ClampToRect(new Vector2(window.xMin + 10f, window.yMax - 4f), screen);
            }
        }
        return IdleRailPoint(idleRail, window, keepout, screen);
    }
    static bool IdleRailFits(int side, Rect keepout, Rect screen)
    {
        if (side == 0) return keepout.xMax + 5f <= screen.xMax;
        if (side == 1) return keepout.xMin - 5f >= screen.xMin;
        if (side == 2) return keepout.yMax + 5f <= screen.yMax;
        return keepout.yMin - 5f >= screen.yMin;
    }
    static Vector2 IdleRailPoint(int side, Rect window, Rect keepout, Rect screen)
    {
        Vector2 point = new Vector2(window.center.x, window.center.y - 16f);
        if (side == 0) point.x = keepout.xMax + 10f;
        else if (side == 1) point.x = keepout.xMin - 10f;
        else if (side == 2) point.y = keepout.yMax + 10f;
        else point.y = keepout.yMin - 10f;
        return ClampToRect(point, screen);
    }
    Vector2 ClampIdleTarget(Vector2 point, Rect screen, Rect keepout, bool hasWindow)
    {
        point = ClampToRect(point, screen);
        if (!hasWindow) return point;
        if (idleRail < 0)
        {
            // With no exterior space, a sideways gesture stays in the header.
            point.y = idleHome.y; return ClampToRect(point, screen);
        }
        if (idleRail == 0) point.x = Mathf.Max(point.x, keepout.xMax);
        else if (idleRail == 1) point.x = Mathf.Min(point.x, keepout.xMin);
        else if (idleRail == 2) point.y = Mathf.Max(point.y, keepout.yMax);
        else point.y = Mathf.Min(point.y, keepout.yMin);
        return ClampToRect(point, screen);
    }
    Vector2 IdleRouteGoal(Vector2 from, Vector2 goal, Rect obstacle, Rect screen)
    {
        if (idleRail < 0 || !IdleSegmentCrosses(from, goal, obstacle)) return goal;
        if (from.x > obstacle.xMin + .05f && from.x < obstacle.xMax - .05f
            && from.y > obstacle.yMin + .05f && from.y < obstacle.yMax - .05f)
        {
            // Live may be dragged over the companion. Take its nearest free
            // edge first; never teleport to the distant home on the other side.
            Vector2 nearest = goal; float distance = float.PositiveInfinity; int exitSide = idleRail;
            for (int side = 0; side < 4; side++)
            {
                if (!IdleRailFits(side, obstacle, screen)) continue;
                Vector2 exit = from;
                if (side == 0) exit.x = obstacle.xMax;
                else if (side == 1) exit.x = obstacle.xMin;
                else if (side == 2) exit.y = obstacle.yMax;
                else exit.y = obstacle.yMin;
                float d = (exit - from).sqrMagnitude;
                if (d < distance) { nearest = exit; distance = d; exitSide = side; }
            }
            // Once Live has moved over her, stay by this nearest edge rather
            // than crossing back to an obsolete home on the opposite side.
            idleRail = exitSide; idleOffset = Vector2.zero;
            return nearest;
        }
        // Usually home is on the same side and the direct segment is clear.
        // After a window move, use only the current outside corner as a short
        // waypoint. No route list or general screen pathfinding is needed.
        if (idleRail < 2)
        {
            if (from.y >= obstacle.yMax - .1f) return new Vector2(goal.x, obstacle.yMax);
            if (from.y <= obstacle.yMin + .1f) return new Vector2(goal.x, obstacle.yMin);
            bool topFits = obstacle.yMax <= screen.yMax, bottomFits = obstacle.yMin >= screen.yMin;
            if (!topFits && !bottomFits) return from;
            float y = topFits && (!bottomFits || obstacle.yMax - from.y < from.y - obstacle.yMin)
                ? obstacle.yMax : obstacle.yMin;
            return new Vector2(from.x < obstacle.center.x ? obstacle.xMin : obstacle.xMax, y);
        }
        if (from.x <= obstacle.xMin + .1f) return new Vector2(obstacle.xMin, goal.y);
        if (from.x >= obstacle.xMax - .1f) return new Vector2(obstacle.xMax, goal.y);
        bool leftFits = obstacle.xMin >= screen.xMin, rightFits = obstacle.xMax <= screen.xMax;
        if (!leftFits && !rightFits) return from;
        float x = leftFits && (!rightFits || from.x - obstacle.xMin < obstacle.xMax - from.x)
            ? obstacle.xMin : obstacle.xMax;
        return new Vector2(x, from.y > obstacle.center.y ? obstacle.yMax : obstacle.yMin);
    }
    static bool IdleSegmentCrosses(Vector2 from, Vector2 to, Rect rectangle)
    {
        // The boundary itself is a safe route; test only the strict interior.
        Rect r = Rect.MinMaxRect(rectangle.xMin + .05f, rectangle.yMin + .05f,
            rectangle.xMax - .05f, rectangle.yMax - .05f);
        Vector2 delta = to - from; float enter = 0f, leave = 1f;
        for (int axis = 0; axis < 2; axis++)
        {
            float a = from[axis], d = delta[axis], min = axis == 0 ? r.xMin : r.yMin, max = axis == 0 ? r.xMax : r.yMax;
            if (Mathf.Abs(d) < .0001f) { if (a < min || a > max) return false; continue; }
            float t0 = (min - a) / d, t1 = (max - a) / d;
            if (t0 > t1) { float temp = t0; t0 = t1; t1 = temp; }
            enter = Mathf.Max(enter, t0); leave = Mathf.Min(leave, t1);
            if (enter > leave) return false;
        }
        return enter <= leave;
    }
    static Vector2 ClampToRect(Vector2 point, Rect bounds) => new Vector2(
        Mathf.Clamp(point.x, bounds.xMin, bounds.xMax), Mathf.Clamp(point.y, bounds.yMin, bounds.yMax));
    void TickAcceptanceReaction(float dt)
    {
        const float pause = .2f, nod = .42f;
        // Tick first restores the neutral centered pose. Add this one offset
        // afterwards so centering never cancels or accumulates the small nod.
        float u = Mathf.Clamp01((elapsed - pause) / nod);
        float dip = Mathf.Sin(Mathf.PI * u) * 4f;
        if (live != null) live.TrySetPresentationOffset(this, CenterOffset() + Vector2.down * dip);
        TickIdle(dt);
        if (elapsed >= pause + nod)
        {
            KeepLiveCentered();
            SetStep(ReleaseStep.Promise);
        }
    }
    Vector2 ClampPaperGrip(Vector2 point)
    {
        if (visualRoot == null) return point;
        Rect bounds = visualRoot.rect;
        return new Vector2(Mathf.Clamp(point.x, bounds.xMin + 36f, bounds.xMax - 36f),
            Mathf.Clamp(point.y, bounds.yMin + 18f, bounds.yMax - 78f));
    }
    Vector2 PaperBesideLivePoint()
    {
        // Give the 64px paper its own breathing room beside Live, rather than
        // parking its hand at the closer, empty-pointer idle position.
        return ClampPaperGrip(LocalPoint(live.WindowRect,
            new Vector2(live.WindowRect.rect.xMax + 45f, live.WindowRect.rect.center.y - 16f)));
    }
    Vector2 PaperBelowLivePoint()
    {
        // At this point the complete paper is below the window. Changing its
        // sibling order here cannot make any already-visible pixels disappear.
        return LocalPoint(live.WindowRect, new Vector2(live.WindowRect.rect.center.x,
            live.WindowRect.rect.yMin - 65f));
    }
    void SetPaper(bool happy)
    {
        if (happy) HideSorryPuffs();
        if (paperImage == null) return;
        paperImage.sprite = happy ? yeahPaper : apologyPaper;
        if (paperImage.sprite != null) paperImage.rectTransform.sizeDelta = paperImage.sprite.rect.size;
        paperImage.rectTransform.localScale = Vector3.one;
    }
    void SetPaperVisible(bool shown)
    {
        if (paperImage != null) paperImage.gameObject.SetActive(shown && paperImage.sprite != null);
    }
    void PlacePaperAtPointer(float angle = 0f)
    {
        if (paperImage == null) return;
        Quaternion rotation = Quaternion.Euler(0f, 0f, angle);
        // Rotate about the held section of the stick, rather than about the
        // canvas bottom. Compensating translation keeps it attached to the hand.
        paperImage.rectTransform.localRotation = rotation;
        paperImage.rectTransform.anchoredPosition = CursorPoint + HandContact
            - (Vector2)(rotation * (Vector3)PaperGrip);
    }
    void TickFetchPaper()
    {
        const float homeFlight = .72f, reachHold = .3f, drawOut = .68f;
        Vector2 hidden = UnderLivePoint();
        if (elapsed < homeFlight)
        {
            PlaceCursor(HumanCurve(fromPoint, hidden, elapsed / homeFlight, -9f)); return;
        }
        SetPressed(true); SetPaper(false); SetPaperVisible(true);
        if (elapsed < homeFlight + reachHold) PlaceCursor(hidden);
        else
        {
            Vector2 outPoint = LocalPoint(live.WindowRect, new Vector2(live.WindowRect.rect.xMin - 31f, live.WindowRect.rect.yMin - 9f));
            PlaceCursor(HumanCurve(hidden, outPoint, (elapsed - homeFlight - reachHold) / drawOut, -13f));
        }
        PlacePaperAtPointer();
        if (elapsed >= homeFlight + reachHold + drawOut)
        {
            followVelocity = Vector2.zero; chaseFollowInitialized = false;
            SetStep(ReleaseStep.Chase); SetPressed(true);
        }
    }
    void TickFetchYeah()
    {
        const float homeFlight = .62f, reachHold = .16f, peek = .34f,
            peekHold = .16f, drawBelow = .35f, lift = .66f, settle = .18f;
        Vector2 hidden = UnderLivePoint();
        if (elapsed < homeFlight)
        {
            PlaceCursor(HumanCurve(fromPoint, hidden, elapsed / homeFlight, -8f)); return;
        }
        SetPressed(true); SetPaper(true); SetPaperVisible(true);
        Vector2 peeking = LocalPoint(live.WindowRect, new Vector2(live.WindowRect.rect.center.x,
            live.WindowRect.rect.yMin - 12f));
        Vector2 target = PaperBesideLivePoint();
        Vector2 overshoot = ClampPaperGrip(target + new Vector2(3f, 3f));
        Vector2 below = PaperBelowLivePoint();
        float t = elapsed - homeFlight;
        if (t < reachHold) PlaceCursor(hidden);
        else if (t < reachHold + peek)
            PlaceCursor(HumanCurve(hidden, peeking, (t - reachHold) / peek, -3f));
        else if (t < reachHold + peek + peekHold) PlaceCursor(peeking);
        else if (t < reachHold + peek + peekHold + drawBelow)
            PlaceCursor(HumanCurve(peeking, below, (t - reachHold - peek - peekHold) / drawBelow, -3f));
        else if (t < reachHold + peek + peekHold + drawBelow + lift)
        {
            // Pull the whole paper clear of the lower edge before lifting it.
            // Round the outside corner so it cannot sink back through Live.
            float liftTime = t - reachHold - peek - peekHold - drawBelow;
            Vector2 corner = new Vector2(overshoot.x, below.y);
            if (liftTime < .28f) PlaceCursor(HumanCurve(below, corner, liftTime / .28f, -5f));
            else PlaceCursor(HumanCurve(corner, overshoot, (liftTime - .28f) / (lift - .28f), -6f));
        }
        else PlaceCursor(HumanCurve(overshoot, target,
            (t - reachHold - peek - peekHold - drawBelow - lift) / settle, 1f));
        PlacePaperAtPointer();
        if (elapsed >= homeFlight + reachHold + peek + peekHold + drawBelow + lift + settle)
        {
            PlaceCursor(target); PlacePaperAtPointer();
            SetStep(ReleaseStep.Celebrate);
        }
    }
    void TickStowPaper()
    {
        const float approach = .68f, tuck = .42f, returnHand = .36f;
        Vector2 below = PaperBelowLivePoint(), hidden = UnderLivePoint();
        SetPressed(elapsed < approach + tuck);
        if (elapsed < approach)
            PlaceCursor(HumanCurve(fromPoint, below, elapsed / approach, 13f));
        else if (elapsed < approach + tuck)
            PlaceCursor(HumanCurve(below, hidden, (elapsed - approach) / tuck, -4f));
        else
        {
            SetPaperVisible(false);
            PlaceCursor(HumanCurve(hidden, LiveSidePoint(), (elapsed - approach - tuck) / returnHand, 7f));
        }
        PlacePaperAtPointer();
        if (elapsed >= approach + tuck + returnHand)
        {
            SetPaperVisible(false); followVelocity = Vector2.zero;
            SetStep(ReleaseStep.ConversationHold);
        }
    }
    void TickChase(float dt)
    {
        if (CursorHandle.Instance == null || visualRoot == null) return;
        var canvas = visualRoot.GetComponentInParent<Canvas>();
        var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(visualRoot,
            CursorHandle.Instance.DisplayedScreenPosition, camera, out Vector2 player);
        if (TickChaseGesture(dt, player)) return;
        float side = player.x > 0f ? -1f : 1f;
        Vector2 goal = player + new Vector2(side * 38f, -37f);
        // Keep the whole paper in view, including its later celebratory hop,
        // even when the free player pointer touches a screen edge.
        goal = ClampPaperGrip(goal);
        // Delay and a capped pursuit speed let the player pull away. There is
        // no teleporting, cursor capture, collision, or button targeting.
        // Each refusal makes her follow a little more eagerly.
        if (!chaseFollowInitialized) { chaseFollowPoint = CursorPoint; chaseFollowInitialized = true; }
        float eagerness = Mathf.Min(chaseRefusals, 3);
        Vector2 next = chaseDroop > 0f
            ? chaseFollowPoint
            : Vector2.SmoothDamp(chaseFollowPoint, goal, ref followVelocity, .24f - eagerness * .03f, 132f + eagerness * 22f, dt);
        chaseFollowPoint = next;
        float near = 1f - Mathf.Clamp01(Vector2.Distance(next, goal) / 25f);
        // A refused puppet sags and tilts away, then lifts back up to plead again.
        float droop = Mathf.Clamp01(chaseDroop / ChaseDroopSeconds);
        float sag = Mathf.Sin(droop * Mathf.PI) * 5f;
        // The hand and paper bob together. Only the angle pivots around the
        // fixed grip; the stick can never slide through the red pointer.
        PlaceCursor(next + Vector2.up * Mathf.Round(Mathf.Sin(idleTime * 4.2f) * 2f * near * (1f - droop) - sag));
        SetPressed(true);
        float wobble = Mathf.Sin(idleTime * (5.6f + eagerness * .8f)) * 5f * near * (1f - droop);
        PlacePaperAtPointer(wobble - 16f * Mathf.Sin(droop * Mathf.PI * .5f));
        if (chaseDroop > 0f) { chaseDroop = Mathf.Max(0f, chaseDroop - dt); HideSorryPuffs(); }
        else TickSorryPuffs(dt, player, near);
        RenderNodHint(dt);
    }
    /// <returns>True when this frame forgave her and the step already changed.</returns>
    bool TickChaseGesture(float dt, Vector2 player)
    {
        if (!chasePlayerTracked) { chasePlayerPrevious = player; chasePlayerTracked = true; chaseGestures.Reset(); }
        Vector2 delta = player - chasePlayerPrevious;
        chasePlayerPrevious = player;
        // Ignore the motion of leaving the "ignore" option itself.
        if (elapsed < ChaseGestureWarmup) { chaseGestures.Reset(); return false; }
        int answer = chaseGestures.Push(delta, dt);
        chaseGestureIdle = answer == 0 ? chaseGestureIdle + dt : 0f;
        if (answer == 2) { ForgiveByGesture(); return true; }
        if (answer == 1 && chaseDroop <= 0f)
        {
            chaseRefusals++; chaseDroop = ChaseDroopSeconds; followVelocity = Vector2.zero;
        }
        return false;
    }
    void RenderNodHint(float dt)
    {
        bool show = Step == ReleaseStep.Chase && elapsed >= NodHintDelay && chaseDroop <= 0f
            && elapsed < ForgiveFallbackSeconds && paperImage != null && paperImage.gameObject.activeSelf;
        if (show && nodHint == null) nodHint = MakeNodHint();
        if (nodHint == null) return;
        nodHint.gameObject.SetActive(show);
        if (!show) return;
        // A tiny up/down arrow beside the puppet's head: the gesture, not a sentence.
        Vector2 head = CursorPoint + HandContact + new Vector2(18f, 56f);
        float bob = Mathf.Repeat(elapsed * 2.4f, 1f) < .5f ? 1f : -1f;
        nodHint.rectTransform.anchoredPosition = new Vector2(Mathf.Round(head.x), Mathf.Round(head.y + bob));
        nodHint.transform.SetAsLastSibling();
    }
    Image MakeNodHint()
    {
        // 5x11 one-bit glyph: up chevron, stem, down chevron.
        string[] rows = { "..I..", ".III.", "I.I.I", "..I..", "..I..", ".....", "..I..", "..I..", "I.I.I", ".III.", "..I.." };
        var texture = new Texture2D(5, rows.Length, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < rows.Length; y++)
            for (int x = 0; x < 5; x++)
                texture.SetPixel(x, rows.Length - 1 - y, rows[y][x] == 'I' ? Color.white : Color.clear);
        texture.Apply(false, true);
        var image = NewImage("NodGestureHint", visualRoot);
        image.sprite = Sprite.Create(texture, new Rect(0, 0, 5, rows.Length), new Vector2(.5f, .5f), 1f);
        image.rectTransform.sizeDelta = new Vector2(5, rows.Length);
        image.color = new Color32(0x91, 0x8a, 0x85, 0xff);
        return image;
    }
    // ---- +5 heart feedback: the only other red besides her pointer.
    RectTransform heartPop;
    TMP_Text heartPopText;
    float heartPopAge = -1f;
    Vector2 heartPopOrigin;
    const float HeartPopSeconds = 1.5f;

    void SpawnHeartPop(Vector2 point)
    {
        if (visualRoot == null) return;
        if (heartPop == null)
        {
            var go = new GameObject("HeartRewardPop", typeof(RectTransform));
            go.layer = visualRoot.gameObject.layer;
            heartPop = (RectTransform)go.transform; heartPop.SetParent(visualRoot, false);
            heartPop.anchorMin = heartPop.anchorMax = new Vector2(.5f, .5f); heartPop.sizeDelta = new Vector2(24f, 8f);
            string[] rows = { ".II.II.", "IIIIIII", "IIIIIII", ".IIIII.", "..III..", "...I..." };
            var texture = new Texture2D(7, 6, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            for (int y = 0; y < 6; y++)
                for (int x = 0; x < 7; x++)
                    texture.SetPixel(x, 5 - y, rows[y][x] == 'I' ? Color.white : Color.clear);
            texture.Apply(false, true);
            var heart = NewImage("Heart", heartPop);
            heart.sprite = Sprite.Create(texture, new Rect(0, 0, 7, 6), new Vector2(.5f, .5f), 1f);
            heart.rectTransform.sizeDelta = new Vector2(7f, 6f);
            heart.rectTransform.anchoredPosition = new Vector2(-6f, 0f);
            heart.color = Red;
            var textGo = new GameObject("Amount", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textGo.layer = go.layer;
            heartPopText = textGo.GetComponent<TextMeshProUGUI>();
            heartPopText.rectTransform.SetParent(heartPop, false);
            heartPopText.rectTransform.anchoredPosition = new Vector2(6f, 0f);
            heartPopText.rectTransform.sizeDelta = new Vector2(16f, 8f);
            heartPopText.raycastTarget = false; heartPopText.richText = false; heartPopText.text = "+5";
            heartPopText.alignment = TextAlignmentOptions.MidlineLeft; heartPopText.enableAutoSizing = false;
            heartPopText.textWrappingMode = TextWrappingModes.NoWrap; heartPopText.overflowMode = TextOverflowModes.Overflow;
            heartPopText.color = Red; heartPopText.margin = Vector4.zero;
        }
        var source = messages?.DisplayedTextLabel;
        if (source != null && source.font != null)
        {
            heartPopText.font = source.font; heartPopText.fontSharedMaterial = source.fontSharedMaterial;
            heartPopText.fontSize = Mathf.Clamp(source.fontSize * .9f, 6f, 8f);
        }
        heartPopOrigin = point; heartPopAge = 0f;
        heartPop.gameObject.SetActive(true);
        heartPop.anchoredPosition = new Vector2(Mathf.Round(point.x), Mathf.Round(point.y));
        heartPop.SetAsLastSibling();
        // This entry is called only by the actual accept/forgive reward, not
        // the save migration or the renderer that rebuilds an existing pop.
        Chapter02Audio.Play("Chapter02_ChoiceConfirm", .65f);
    }

    void TickHeartPop(float dt)
    {
        if (heartPop == null || heartPopAge < 0f) return;
        heartPopAge += dt;
        if (heartPopAge >= HeartPopSeconds) { heartPopAge = -1f; heartPop.gameObject.SetActive(false); return; }
        // Pop up quickly, hang, then blink out in two steps instead of fading.
        float rise = 1f - Mathf.Pow(1f - Mathf.Clamp01(heartPopAge / .45f), 3f);
        heartPop.anchoredPosition = new Vector2(Mathf.Round(heartPopOrigin.x), Mathf.Round(heartPopOrigin.y + 12f * rise));
        bool blinkOff = heartPopAge > 1.1f && Mathf.FloorToInt((heartPopAge - 1.1f) / .1f) % 2 == 0;
        heartPop.gameObject.SetActive(!blinkOff);
    }

    void ResetChaseGesture()
    {
        chaseGestures.Reset(); chasePlayerTracked = false;
        chaseDroop = chaseGestureIdle = 0f; chaseRefusals = 0;
        if (nodHint != null) nodHint.gameObject.SetActive(false);
    }
    void TickSorryPuffs(float dt, Vector2 player, float near)
    {
        for (int i = 0; i < sorryPuffs.Length; i++)
        {
            var puff = sorryPuffs[i];
            if (puff == null || !puff.active) continue;
            puff.age += dt;
            if (puff.age >= SorryLifetime)
            { puff.active = false; puff.root.gameObject.SetActive(false); continue; }
            float t = puff.age / SorryLifetime;
            Vector2 position = puff.origin + new Vector2(puff.direction * 3f * t, 11f * t);
            position = new Vector2(Mathf.Round(position.x), Mathf.Round(position.y));
            // Player movement or a newly opened button must not leave an old
            // word hovering across the pointer/choice hit target.
            if (!SorryPositionClear(position, player))
            { puff.active = false; puff.root.gameObject.SetActive(false); continue; }
            puff.root.anchoredPosition = position;
            puff.group.alpha = Mathf.Clamp01(puff.age / .09f)
                * (1f - Mathf.Clamp01((puff.age - .38f) / (SorryLifetime - .38f)));
        }
        bool closeEnough = near >= .45f && Vector2.Distance(CursorPoint, player) <= 88f;
        if (!closeEnough) { sorryCooldown = Mathf.Max(sorryCooldown, .18f); return; }
        sorryCooldown -= dt;
        if (sorryCooldown > 0f) return;
        sorryCooldown = Mathf.Max(.5f, .84f - Mathf.Min(chaseRefusals, 3) * .1f) + (sorrySerial % 3) * .13f;
        SpawnSorryPuff(player);
    }
    void SpawnSorryPuff(Vector2 player)
    {
        var source = messages?.DisplayedTextLabel;
        if (source == null || source.font == null || visualRoot == null) return;
        int slot = -1;
        for (int i = 0; i < sorryPuffs.Length; i++)
            if (sorryPuffs[i] == null || !sorryPuffs[i].active) { slot = i; break; }
        if (slot < 0) return;
        float outward = CursorPoint.x <= player.x ? -1f : 1f;
        Vector2 origin = CursorPoint + new Vector2(outward * 37f, 49f);
        Rect bounds = visualRoot.rect;
        origin.x = Mathf.Clamp(origin.x, bounds.xMin + 24f, bounds.xMax - 24f);
        origin.y = Mathf.Clamp(origin.y, bounds.yMin + 12f, bounds.yMax - 25f);
        if (!SorryPositionClear(origin, player)) return;
        var puff = sorryPuffs[slot];
        if (puff == null)
        {
            var go = new GameObject("ApologySorryPuff_" + slot, typeof(RectTransform), typeof(CanvasGroup));
            go.layer = visualRoot.gameObject.layer;
            var rt = (RectTransform)go.transform; rt.SetParent(visualRoot, false);
            rt.anchorMin = rt.anchorMax = new Vector2(.5f, .5f); rt.sizeDelta = new Vector2(42f, 16f);
            var group = go.GetComponent<CanvasGroup>(); group.interactable = false; group.blocksRaycasts = false;
            var textGo = new GameObject("Sorry", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textGo.layer = go.layer;
            var text = textGo.GetComponent<TextMeshProUGUI>();
            text.rectTransform.SetParent(rt, false);
            text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(5f, 0f); text.rectTransform.offsetMax = new Vector2(-5f, 0f);
            text.raycastTarget = false; text.text = "sorry"; text.richText = false;
            text.alignment = TextAlignmentOptions.Center; text.enableAutoSizing = false;
            text.textWrappingMode = TextWrappingModes.NoWrap; text.overflowMode = TextOverflowModes.Overflow;
            text.color = new Color32(0x91, 0x8a, 0x85, 0xff); text.margin = Vector4.zero;
            for (int mark = 0; mark < 3; mark++)
            {
                var line = NewImage("SoftEmphasis_" + mark, rt);
                line.color = new Color32(0x70, 0x6b, 0x67, 0xff);
                line.rectTransform.sizeDelta = new Vector2(mark == 1 ? 3f : 2f, 1f);
                line.rectTransform.anchoredPosition = new Vector2(mark == 2 ? 18f : -18f, mark == 1 ? 4f : -2f);
                line.rectTransform.localRotation = Quaternion.Euler(0f, 0f, mark == 1 ? -45f : mark == 2 ? 45f : 0f);
            }
            puff = new SorryPuff { root = rt, group = group, text = text };
            sorryPuffs[slot] = puff;
        }
        // Reuse the game's existing TMP font/material after language changes;
        // this fixed lowercase effect word is deliberately not dialogue text.
        puff.text.font = source.font; puff.text.fontSharedMaterial = source.fontSharedMaterial;
        puff.text.fontSize = Mathf.Clamp(source.fontSize * .9f, 6f, 8f);
        // Fixed effect word (not dialogue): pleading grows with each refusal.
        puff.text.text = chaseRefusals <= 0 ? "sorry" : chaseRefusals == 1 ? "sorry!" : "sorry!!";
        puff.age = 0f; puff.origin = origin; puff.direction = outward; puff.active = true;
        puff.root.anchoredPosition = origin; puff.group.alpha = 0f; puff.root.gameObject.SetActive(true);
        cursorImage.transform.SetAsLastSibling();
        sorrySerial++;
    }
    bool SorryPositionClear(Vector2 point, Vector2 player)
    {
        Rect word = new Rect(point - new Vector2(22f, 9f), new Vector2(44f, 18f));
        if (word.Overlaps(new Rect(player - Vector2.one * 11f, Vector2.one * 22f))) return false;
        if (messages != null && messages.TryGetActiveOptionTarget("forgive", out RectTransform option) && option != null)
        {
            Vector2 a = LocalPoint(option, option.rect.min), b = LocalPoint(option, option.rect.max);
            Rect button = Rect.MinMaxRect(Mathf.Min(a.x, b.x) - 4f, Mathf.Min(a.y, b.y) - 4f,
                Mathf.Max(a.x, b.x) + 4f, Mathf.Max(a.y, b.y) + 4f);
            if (word.Overlaps(button)) return false;
        }
        return true;
    }
    void HideSorryPuffs()
    {
        for (int i = 0; i < sorryPuffs.Length; i++)
        {
            var puff = sorryPuffs[i]; if (puff == null) continue;
            puff.active = false;
            if (puff.root != null) puff.root.gameObject.SetActive(false);
        }
        sorryCooldown = .18f;
    }
    void SpawnConfetti()
    {
        ClearConfetti(); celebrationOrigin = CursorPoint;
        if (visualRoot == null) return;
        int count = State?.chapter02CursorReleaseChoice == 1 ? 12 : 20;
        confetti = new Image[count]; confettiOrigins = new Vector2[count]; confettiVelocities = new Vector2[count];
        Color32[] colors = { new Color32(173, 119, 110, 255), new Color32(179, 160, 89, 255),
            new Color32(110, 151, 145, 255), new Color32(162, 144, 167, 255), new Color32(184, 180, 162, 255) };
        for (int i = 0; i < count; i++)
        {
            var piece = NewImage("ApologyConfetti_" + i, visualRoot);
            piece.rectTransform.sizeDelta = i % 3 == 0 ? new Vector2(1f, 3f) : new Vector2(2f, 1f);
            piece.color = colors[i % colors.Length];
            float angle = Mathf.Lerp(22f, 158f, (i + .35f) / count) * Mathf.Deg2Rad;
            confettiOrigins[i] = celebrationOrigin + new Vector2(-2f, 50f);
            confettiVelocities[i] = new Vector2(Mathf.Cos(angle) * (35f + i % 4 * 7f), Mathf.Sin(angle) * (40f + i % 5 * 6f));
            confetti[i] = piece;
        }
        cursorImage.transform.SetAsLastSibling();
    }
    void TickCelebrate()
    {
        EnsureMessage(ThanksSuffix, false);
        float u = Mathf.Clamp01(elapsed / .56f);
        float jump = 4f * u * (1f - u) * 12f;
        PlaceCursor(celebrationOrigin + Vector2.up * Mathf.Round(jump)); SetPressed(true);
        PlacePaperAtPointer();
        if (confetti != null)
            for (int i = 0; i < confetti.Length; i++)
            {
                var piece = confetti[i]; if (piece == null) continue;
                Vector2 p = confettiOrigins[i] + confettiVelocities[i] * elapsed + Vector2.down * (65f * elapsed * elapsed);
                p.x += Mathf.Sin(elapsed * 7f + i) * 2f;
                piece.rectTransform.anchoredPosition = new Vector2(Mathf.Round(p.x), Mathf.Round(p.y));
                piece.rectTransform.localRotation = Quaternion.Euler(0, 0, Mathf.Round((elapsed * (i % 2 == 0 ? 300f : -240f) + i * 17f) / 45f) * 45f);
                Color c = piece.color; c.a = 1f - Mathf.Clamp01((elapsed - 1.1f) / .4f); piece.color = c;
            }
        if (elapsed >= 1.5f) { ClearConfetti(); SetStep(ReleaseStep.Thanks); }
    }
    void ClearConfetti()
    {
        if (confetti != null) foreach (var p in confetti) if (p != null) Destroy(p.gameObject);
        confetti = null; confettiOrigins = confettiVelocities = null;
    }
    void RenderContinueTip()
    {
        bool needed = OwnMessageDisplayed && ((Step == ReleaseStep.Apology && !AssistedEscape) || Step == ReleaseStep.Thanks
            || Step == ReleaseStep.Promise);
        if (!needed) { HideContinueTip(); return; }
        if (continueTip == null)
        {
            var parent = messages.WindowLayout.transform.Find("MessageArea") as RectTransform;
            if (parent == null) return;
            continueTip = NewImage("CursorReleaseContinueTip", parent);
            var rect = continueTip.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0f); rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = new Vector2(10f, 7f);
            var source = OS_Handle.Instance?.MemoryWindowController?.transform.Find("TextArea/ContinueTip")?.GetComponent<Image>();
            if (source != null)
            {
                continueTip.sprite = source.sprite;
                // The memory hint may currently be faded out. Reuse its RGB,
                // not the transient alpha, for this independently shown hint.
                Color tipColor = source.color; tipColor.a = 1f; continueTip.color = tipColor;
            }
        }
        messages.WindowLayout.SetTextFooterHeight(15f);
        continueTip.gameObject.SetActive(messageTyped && Step != ReleaseStep.CloseMessage);
        continueTip.rectTransform.anchoredPosition = new Vector2(0f, 6f + Mathf.Round(Mathf.Sin(idleTime * 4.4f)));
    }
    void HideContinueTip()
    {
        if (continueTip != null) continueTip.gameObject.SetActive(false);
        if (messages != null && messages.DisplayedEntryId != null && messages.DisplayedEntryId.StartsWith(Prefix, System.StringComparison.Ordinal))
            messages.WindowLayout?.SetTextFooterHeight(0f);
    }
    void SetVisualsVisible(bool visible)
    {
        if (visualRoot != null) visualRoot.gameObject.SetActive(visible);
    }
    void DestroyVisuals()
    {
        ClearConfetti();
        for (int i = 0; i < sorryPuffs.Length; i++)
        {
            if (sorryPuffs[i] != null && sorryPuffs[i].root != null) Destroy(sorryPuffs[i].root.gameObject);
            sorryPuffs[i] = null;
        }
        sorryCooldown = .18f; sorrySerial = 0;
        if (nodHint != null) { if (nodHint.sprite != null) { Destroy(nodHint.sprite.texture); Destroy(nodHint.sprite); } Destroy(nodHint.gameObject); }
        if (heartPop != null) Destroy(heartPop.gameObject);
        DestroyFlourish();
        heartPop = null; heartPopText = null; heartPopAge = -1f;
        nodHint = null; ResetChaseGesture();
        chaseFollowInitialized = false; followVelocity = Vector2.zero;
        idleInitialized = idleWritingCursor = idleAvoiding = idleReturning = false;
        idlePosition = idleVelocity = idleOffset = idleHome = idleGoal = Vector2.zero;
        idleNearTime = idleAvoidTime = idleAvoidCooldown = 0f; idleRail = -1;
        if (visualRoot != null) Destroy(visualRoot.gameObject);
        if (continueTip != null) Destroy(continueTip.gameObject);
        visualRoot = null; cursorImage = paperImage = continueTip = null;
    }
    Vector2 LocalPoint(RectTransform source, Vector2 point) => visualRoot.InverseTransformPoint(source.TransformPoint(point));
    Vector2 ClampPoint(Vector2 point, float margin)
    {
        if (visualRoot == null) return point;
        Rect r = visualRoot.rect;
        return new Vector2(Mathf.Clamp(point.x, r.xMin + margin, r.xMax - margin), Mathf.Clamp(point.y, r.yMin + margin, r.yMax - margin));
    }
    static Vector2 HumanCurve(Vector2 from, Vector2 to, float progress, float bend)
    {
        float t = Mathf.Clamp01(progress);
        float u = t * t * t * (10f + t * (-15f + 6f * t));
        Vector2 normal = new Vector2(-(to - from).y, (to - from).x).normalized;
        // Minimum-jerk acceleration/deceleration plus a single asymmetrical arc.
        return Vector2.Lerp(from, to, u) + normal * (Mathf.Sin(Mathf.PI * u) * bend * (1f - .3f * u));
    }
#if UNITY_EDITOR
    public Vector2 DebugRedPointerPosition => CursorPoint;
    public Vector2 DebugIdlePosition => idlePosition;
    public Vector2 DebugIdleHome => idleHome;
    public Vector2 DebugIdleGoal => idleGoal;
    public bool DebugIdleAvoiding => idleAvoiding;
    public bool DebugIdleReturning => idleReturning;
    public int DebugIdleRail => idleRail;
    public Rect DebugIdleScreenBounds => visualRoot != null && cursorImage != null ? IdleScreenBounds() : default;
    public Rect DebugIdleWindowKeepout => visualRoot != null && cursorImage != null && TryIdleWindowBounds(out Rect bounds)
        ? IdleWindowKeepout(bounds) : default;
    public Image DebugPaperImage => paperImage;
    public RectTransform DebugVisualRoot => visualRoot;
    public Vector2 DebugPaperBesideLivePoint => PaperBesideLivePoint();
    public Vector2 DebugPaperBelowLivePoint => PaperBelowLivePoint();
    public Vector2 DebugPaperGripPosition => paperImage != null && visualRoot != null
        ? (Vector2)visualRoot.InverseTransformPoint(paperImage.rectTransform.TransformPoint(PaperGrip)) : Vector2.zero;
    public int DebugSorryPuffCount
    {
        get { int count = 0; foreach (var puff in sorryPuffs) if (puff != null && puff.active) count++; return count; }
    }
#endif
}
