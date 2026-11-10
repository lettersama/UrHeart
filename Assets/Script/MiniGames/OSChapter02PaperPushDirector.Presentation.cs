using UnityEngine;
using UnityEngine.UI;

// Choreography only. The other partial owns dialogue, checkpoints and match state.
// This file never drives Update or the physical/virtual player cursor.
public sealed partial class OSChapter02PaperPushDirector
{
    RematchStep choreoStep = (RematchStep)(-1);
    int choreoPhase;
    float choreoTime, choreoFolderWait, choreoMatchTime;
    bool choreoNewPlayed, choreoFolderRevealed;
    RectTransform choreoSeal, choreoFold;
    Quaternion choreoSealRotation;
    Vector2 choreoSealThrowFrom, choreoSealThrowTo;
    bool choreoLiveOwned, choreoLiveCouldMove, choreoLiveCouldResize;
    Vector2 choreoLiveFrom, choreoLiveTo;
    bool choreoBenchPlaced;
    Vector2 choreoBenchPoint;
    bool choreoBracing;
    float choreoBraceReach;
    Vector2 choreoBraceFrom;

    public bool UnsealIconReady { get; private set; }

    void CloseBackgroundWindowsForRematch(bool keepMatchWindow)
    {
        if (os == null) return;
        foreach (var candidate in os.GetComponentsInChildren<OSWindowUnit>(true))
        {
            if (!candidate.IsOpen
                || liveWindow != null && candidate.transform.IsChildOf(liveWindow.transform)
                || candidate == messages?.LinkWindow
                || settingsWindow != null && candidate.transform.IsChildOf(settingsWindow.transform)
                || keepMatchWindow && window != null && candidate.transform.IsChildOf(window.transform))
                continue;
            // This is staging, not a player dismissal that should trigger dialogue.
            candidate.CloseSilently();
        }
        if (keepMatchWindow && window != null && window.IsOpen)
            window.RestoreAndBringToFront();
    }

    bool BeginChoreography()
    {
        if (actor == null || !actor.OwnsScriptActor(this)) return false;
        if (Step != RematchStep.Unsealing && Step != RematchStep.Wrap) return false;
        if (choreoStep == Step) return true;
        ResetChoreography();
        choreoStep = Step;
        actor.ScriptActorSetVisible(this, true);
        if (Step == RematchStep.Unsealing)
        {
            if (chapter01Story?.DesktopIcon?.ProcessIcon == null || chapter01Story.SealSticker == null)
            { choreoStep = (RematchStep)(-1); return false; }
            chapter01Story.SetChapter02SealVisible(true);
            choreoFolderRevealed = true;
            choreoFolderWait = os != null && os.RevealPaperPushForSeal() ? .35f : 0f;
        }
        else
        {
            if (liveWindow == null) { choreoStep = (RematchStep)(-1); return false; }
            if (window != null && window.IsOpen)
            {
                window.RestoreAndBringToFront();
                actor.ScriptActorMoveTo(this, ChoreoClosePoint(), .5f);
            }
            else choreoPhase = 2;
        }
        return true;
    }

    void TickChoreography(float dt)
    {
        if (actor == null || !actor.OwnsScriptActor(this) || choreoStep != Step) return;
        dt = Mathf.Clamp(dt, 0f, .05f);
        if (dt <= 0f) return;
        choreoTime += dt;
        if (choreoStep == RematchStep.Unsealing) TickUnsealing();
        else if (choreoStep == RematchStep.Wrap) TickWrap();
    }

    void NextChoreoPhase() { choreoPhase++; choreoTime = 0f; }

    void TickUnsealing()
    {
        var icon = chapter01Story?.DesktopIcon;
        if (icon?.ProcessIcon == null || chapter01Story.SealSticker == null) return;
        if (UnsealIconReady && !choreoNewPlayed && !icon.IsLocked)
        {
            // The regular desktop NEW implementation supplies its two hops.
            // Wait for the director's playable gate to clear the real icon lock.
            icon.SetNewState(true); choreoNewPlayed = true;
        }
        switch (choreoPhase)
        {
            case 0:
                if (choreoTime < choreoFolderWait || !icon.gameObject.activeInHierarchy) return;
                actor.ScriptActorMoveTo(this, ChoreoSealApproachPoint(), .9f);
                NextChoreoPhase(); break;
            case 1:
                if (choreoTime < .9f) return;
                NextChoreoPhase(); break;
            case 2:
                // Four deliberate one-unit side steps: a small guilty hesitation.
                float side = Mathf.FloorToInt(choreoTime / .125f) % 2 == 0 ? 1f : -1f;
                actor.ScriptActorSetPosition(this, ChoreoSealApproachPoint() + Vector2.right * (side * ChoreoUnit()));
                if (choreoTime < .5f) return;
                if (!CreateChoreoSeal()) return;
                actor.ScriptActorSetPosition(this, ChoreoSealGripPoint());
                actor.ScriptActorSetPressed(this, true);
                NextChoreoPhase(); break;
            case 3:
                if (choreoTime < .1f) return;
                choreoFold.gameObject.SetActive(true);
                AudioManager.Instance?.PlaySfx("HidePaper", .65f);
                NextChoreoPhase(); break;
            case 4:
                if (choreoTime < .2f) return;
                // Hinge about the bottom-left corner; no blended smear frames.
                ChoreoSetPivot(choreoSeal, Vector2.zero);
                choreoSeal.rotation = choreoSealRotation * Quaternion.Euler(0f, 0f, -20f);
                actor.ScriptActorSetPosition(this, ChoreoSealGripPoint());
                AudioManager.Instance?.PlaySfx("HidePaper", .7f);
                NextChoreoPhase(); break;
            case 5:
                if (choreoTime < .2f) return;
                actor.ScriptActorCarry(this, choreoSeal, new Vector2(choreoSeal.rect.xMax, choreoSeal.rect.yMax));
                actor.ScriptActorMoveTo(this, actor.CompanionScreenPosition + new Vector2(14f, 3f) * ChoreoUnit(), .2f);
                AudioManager.Instance?.PlaySfx("HidePaper", .7f);
                NextChoreoPhase(); break;
            case 6:
                if (choreoTime < .2f) return;
                actor.ScriptActorDrop(this); actor.ScriptActorSetPressed(this, false);
                ChoreoSetPivot(choreoSeal, new Vector2(.5f, .5f));
                choreoSealThrowFrom = ChoreoScreenPoint(choreoSeal, Vector2.zero);
                choreoSealThrowTo = new Vector2(choreoSealThrowFrom.x + 42f * ChoreoUnit(), -32f * ChoreoUnit());
                actor.ScriptActorMoveTo(this, actor.CompanionScreenPosition + new Vector2(6f, -4f) * ChoreoUnit(), .12f);
                NextChoreoPhase(); break;
            case 7:
                float u = Mathf.Clamp01(choreoTime / .5f);
                ChoreoPlaceScreen(choreoSeal, Vector2.Lerp(choreoSealThrowFrom, choreoSealThrowTo, u * u));
                choreoSeal.rotation = Quaternion.Euler(0f, 0f, 90f * Mathf.Min(3, 1 + Mathf.FloorToInt(u * 3f)));
                if (choreoTime < .5f) return;
                DestroyChoreoSeal();
                UnsealIconReady = true;
                actor.ScriptActorMoveTo(this, ChoreoIconPoint(), .3f);
                NextChoreoPhase(); break;
            case 8:
                if (choreoTime < .3f || icon.IsLocked) return;
                actor.ScriptActorSetPosition(this, ChoreoIconPoint());
                actor.ScriptActorSetPressed(this, true);
                NextChoreoPhase(); break;
            case 9:
                actor.ScriptActorSetPosition(this, ChoreoIconPoint());
                if (choreoTime < .08f) return;
                actor.ScriptActorSetPressed(this, false);
                NextChoreoPhase(); break;
            case 10:
                actor.ScriptActorSetPosition(this, ChoreoIconPoint());
                if (choreoTime < .12f) return;
                actor.ScriptActorSetPressed(this, true);
                NextChoreoPhase(); break;
            case 11:
                actor.ScriptActorSetPosition(this, ChoreoIconPoint());
                if (choreoTime < .08f) return;
                actor.ScriptActorSetPressed(this, false);
                NextChoreoPhase(); break;
            case 12:
                if (choreoTime < .08f || os == null || !os.OpenPaperPushWindow()) return;
                os.PlayInteractionFeedback();
                RestoreChoreoFolder();
                NextChoreoPhase(); break;
            case 13:
                if (window == null || !window.IsOpen || window.IsOpening || window.IsClosing || game?.CharacterRect == null) return;
                actor.ScriptActorMoveTo(this, ChoreoCharacterPoint(false), .65f);
                NextChoreoPhase(); break;
            case 14:
                if (choreoTime < .85f) return;
                SetStep(RematchStep.Match1Title); break;
        }
    }

    void TickWrap()
    {
        switch (choreoPhase)
        {
            case 0:
                if (choreoTime < .5f) return;
                actor.ScriptActorSetPosition(this, ChoreoClosePoint());
                actor.ScriptActorSetPressed(this, true);
                NextChoreoPhase(); break;
            case 1:
                if (choreoTime < .13f) return;
                window?.CloseAnimated(.25f);
                AudioManager.Instance?.PlaySfx("CloseUnit");
                actor.ScriptActorSetPressed(this, false);
                NextChoreoPhase(); break;
            case 2:
                if (choreoTime < .2f || !EnsureChoreoLive()) return;
                OwnChoreoLiveMotion();
                actor.ScriptActorMoveTo(this, ChoreoLiveGripPoint(), .5f);
                NextChoreoPhase(); break;
            case 3:
                if (choreoTime < .5f) return;
                actor.ScriptActorSetPosition(this, ChoreoLiveGripPoint());
                actor.ScriptActorSetPressed(this, true);
                choreoLiveFrom = liveWindow.PresentationOffset;
                choreoLiveTo = ChoreoCenteredLiveOffset();
                NextChoreoPhase(); break;
            case 4:
                if (choreoTime < .12f) return;
                BeginHeartTalkAudio();
                NextChoreoPhase(); break;
            case 5:
                float u = Mathf.SmoothStep(0f, 1f, choreoTime / 1.1f);
                Vector2 offset = Vector2.Lerp(choreoLiveFrom, choreoLiveTo, u) + Vector2.up * (Mathf.Sin(u * Mathf.PI) * 3f);
                if (!liveWindow.TrySetPresentationOffset(this, offset)) return;
                actor.ScriptActorSetPosition(this, ChoreoLiveGripPoint());
                if (choreoTime < 1.1f) return;
                actor.ScriptActorSetPressed(this, false);
                PlaceChoreoBenchAnchor();
                actor.ScriptActorMoveTo(this, choreoBenchPoint, .65f);
                NextChoreoPhase(); break;
            case 6:
                if (choreoTime < .8f) return;
                SetStep(RematchStep.HeartTalkHold); break;
        }
    }

    void TickMatchActor(float dt)
    {
        if (actor == null || !actor.OwnsScriptActor(this) || game == null || window == null) return;
        if (!window.IsOpen || window.IsMinimized || !window.gameObject.activeInHierarchy)
        { actor.ScriptActorSetVisible(this, false); return; }
        choreoMatchTime += Mathf.Clamp(dt, 0f, .05f);
        actor.ScriptActorSetVisible(this, true);
        bool bracing = game.IsChapter02Bracing;
        if (bracing && !choreoBracing)
        {
            choreoBraceFrom = actor.CompanionScreenPosition;
            choreoBraceReach = 0f; choreoBracing = true;
        }
        if (!bracing) choreoBracing = false;
        Vector2 point = ChoreoCharacterPoint(bracing);
        if (bracing)
        {
            choreoBraceReach += Mathf.Clamp(dt, 0f, .05f);
            point = Vector2.Lerp(choreoBraceFrom, point, Mathf.SmoothStep(0f, 1f, choreoBraceReach / .4f));
            actor.ScriptActorSetPressed(this, choreoBraceReach >= .4f);
            actor.ScriptActorSetTilt(this, window.WindowRect != null ? Mathf.DeltaAngle(0f, window.WindowRect.eulerAngles.z) : 0f);
        }
        else
        {
            actor.ScriptActorSetPressed(this, false); actor.ScriptActorSetTilt(this, 0f);
            point.y += Mathf.Round(Mathf.Sin(choreoMatchTime * 1.3f) * .6f) * ChoreoUnit();
        }
        actor.ScriptActorSetPosition(this, point);
    }

    void TickBenchHold(float dt)
    {
        if (actor == null || !actor.OwnsScriptActor(this) || !EnsureChoreoLive()) return;
        OwnChoreoLiveMotion();
        liveWindow.TrySetPresentationOffset(this, ChoreoCenteredLiveOffset());
        if (TickStateCheckLead(dt)) return;
        if (!choreoBenchPlaced) PlaceChoreoBenchAnchor();
        // The final talk's quiet lines (the grave, her forgotten face, a silent
        // answer) stop the bob; it resumes from the low step afterwards.
        bool quiet = os?.Chapter02FinalTalk?.IsQuietBeat == true;
        if (quiet) choreoMatchTime = 0f;
        else choreoMatchTime += Mathf.Clamp(dt, 0f, .05f);
        actor.ScriptActorSetVisible(this, true);
        actor.ScriptActorSetPressed(this, false); actor.ScriptActorSetTilt(this, 0f);
        actor.ScriptActorSetPosition(this, choreoBenchPoint + Vector2.up
            * (Mathf.FloorToInt(choreoMatchTime / .65f) % 2) * ChoreoUnit());
    }

    void PlaceChoreoBenchAnchor()
    {
        float unit = ChoreoUnit();
        Vector2 player = CursorHandle.Instance != null ? CursorHandle.Instance.DisplayedScreenPosition
            : new Vector2(Screen.width * .5f, Screen.height * .35f);
        float side = player.x + 30f * unit < Screen.width ? 1f : -1f;
        choreoBenchPoint = player + Vector2.right * (side * 16f * unit);
        choreoBenchPoint.x = Mathf.Clamp(choreoBenchPoint.x, 8f * unit, Screen.width - 20f * unit);
        choreoBenchPoint.y = Mathf.Clamp(choreoBenchPoint.y, 24f * unit, Screen.height - 8f * unit);
        choreoBenchPlaced = true;
    }

    bool CreateChoreoSeal()
    {
        RectTransform source = chapter01Story?.SealSticker;
        var parent = liveWindow != null ? liveWindow.WindowRect.parent as RectTransform : chapter01Story?.PresentationLayer;
        if (source == null || parent == null) return false;
        // The authored seal is a small UI hierarchy (paper, rules, localized
        // text), not a single sprite. Keep that actual artwork and localization.
        var copy = Instantiate(source.gameObject, parent, false);
        copy.name = "Chapter02RematchTornSeal";
        choreoSeal = (RectTransform)copy.transform;
        choreoSeal.anchorMin = choreoSeal.anchorMax = new Vector2(.5f, .5f);
        choreoSeal.sizeDelta = source.rect.size; choreoSeal.pivot = source.pivot;
        choreoSeal.position = source.position; choreoSeal.rotation = source.rotation;
        Vector3 scale = source.lossyScale, parentScale = parent.lossyScale;
        choreoSeal.localScale = new Vector3(scale.x / parentScale.x, scale.y / parentScale.y, 1f);
        choreoSealRotation = choreoSeal.rotation;
        var group = copy.GetComponent<CanvasGroup>();
        if (group == null) group = copy.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false; group.interactable = false;
        foreach (var graphic in copy.GetComponentsInChildren<Graphic>(true))
        {
            graphic.raycastTarget = false;
            Color ink = graphic.color.grayscale >= .5f ? (Color)new Color32(0x87, 0x82, 0x7e, 255) : Color.black;
            ink.a = graphic.color.a; graphic.color = ink;
        }
        var fold = new GameObject("FoldedUpperRightCorner", typeof(RectTransform), typeof(CanvasRenderer), typeof(OSPaperPushSealFoldGraphic));
        fold.layer = copy.layer;
        choreoFold = (RectTransform)fold.transform; choreoFold.SetParent(choreoSeal, false);
        choreoFold.anchorMin = Vector2.zero; choreoFold.anchorMax = Vector2.one;
        choreoFold.offsetMin = choreoFold.offsetMax = Vector2.zero;
        fold.GetComponent<OSPaperPushSealFoldGraphic>().raycastTarget = false;
        fold.SetActive(false); copy.SetActive(true);
        chapter01Story.SetChapter02SealVisible(false);
        return true;
    }

    void DestroyChoreoSeal()
    {
        actor?.ScriptActorDrop(this);
        if (choreoSeal != null) Destroy(choreoSeal.gameObject);
        choreoSeal = choreoFold = null;
    }

    void RestoreChoreoFolder()
    {
        if (!choreoFolderRevealed) return;
        os?.RestorePaperPushSealFolder(); choreoFolderRevealed = false;
    }

    void ResetChoreography()
    {
        DestroyChoreoSeal(); RestoreChoreoFolder();
        if (choreoNewPlayed) chapter01Story?.DesktopIcon?.SetNewState(false, false);
        if (choreoLiveOwned && liveWindow != null)
        {
            liveWindow.ClearPresentationOffset(this);
            liveWindow.ConfigureInteraction(choreoLiveCouldMove, choreoLiveCouldResize);
        }
        choreoLiveOwned = false; choreoBenchPlaced = false; choreoLeading = false;
        choreoStep = (RematchStep)(-1); choreoPhase = 0;
        choreoTime = choreoFolderWait = choreoMatchTime = 0f;
        choreoNewPlayed = false; UnsealIconReady = false;
        choreoBracing = false;
        if (actor != null && actor.OwnsScriptActor(this))
        {
            actor.ScriptActorSetPosition(this, actor.CompanionScreenPosition);
            actor.ScriptActorSetPressed(this, false); actor.ScriptActorSetTilt(this, 0f);
            actor.ScriptActorSetVisible(this, true);
        }
        if (GameDataManager.Instance?.CurrentChapterIndex == 2)
            chapter01Story?.SetChapter02SealVisible(Step < RematchStep.Match1Title);
    }

    bool EnsureChoreoLive()
    {
        if (liveWindow == null) return false;
        if (!liveWindow.IsOpen || liveWindow.IsMinimized || !liveWindow.gameObject.activeInHierarchy)
        { os?.OpenCharacterWindowForReconnect(.15f); return false; }
        return !liveWindow.IsOpening && !liveWindow.IsClosing;
    }

    void OwnChoreoLiveMotion()
    {
        if (choreoLiveOwned || liveWindow == null) return;
        choreoLiveCouldMove = liveWindow.AllowsMove; choreoLiveCouldResize = liveWindow.AllowsResize;
        liveWindow.ConfigureInteraction(false, false); choreoLiveOwned = true;
    }

    Vector2 ChoreoCenteredLiveOffset()
    {
        var rect = liveWindow.WindowRect;
        var parent = rect.parent as RectTransform;
        if (parent == null) return liveWindow.PresentationOffset;
        Vector2 center = parent.InverseTransformPoint(rect.TransformPoint(rect.rect.center));
        // Match the post-experiment heart-to-heart: Live above centre leaves
        // the lower third for the shared bottom-dialogue layout. Wrap drags
        // directly here, so entering the final talk never snaps the portrait.
        Vector2 goal = parent.rect.center + new Vector2(0f, Mathf.Round(parent.rect.height * .12f));
        return liveWindow.PresentationOffset + goal - center;
    }

    Vector2 ChoreoLiveGripPoint()
    {
        var rect = liveWindow.WindowRect;
        foreach (var candidate in liveWindow.GetComponentsInChildren<RectTransform>(true))
            if (liveWindow.IsDragAreaTarget(candidate.gameObject))
                return ChoreoScreenPoint(candidate, candidate.rect.center - new Vector2(candidate.rect.width * .13f, 0f));
        return ChoreoScreenPoint(rect, new Vector2(rect.rect.xMin + 42f, rect.rect.yMax - 6f));
    }

    Vector2 ChoreoClosePoint()
    {
        var close = chapter01Story?.CloseButton;
        if (close != null) return ChoreoScreenPoint(close, close.rect.center);
        var rect = window != null ? window.WindowRect : null;
        return rect != null ? ChoreoScreenPoint(rect, new Vector2(rect.rect.xMax - 7f, rect.rect.yMax - 6f))
            : actor.CompanionScreenPosition;
    }

    Vector2 ChoreoIconPoint()
    {
        var rect = chapter01Story.DesktopIcon.ProcessIcon.rectTransform;
        return ChoreoScreenPoint(rect, rect.rect.center);
    }

    Vector2 ChoreoSealApproachPoint()
    {
        var rect = chapter01Story.SealSticker;
        return ChoreoScreenPoint(rect, new Vector2(rect.rect.xMax - 1f, rect.rect.yMax + 3f));
    }

    Vector2 ChoreoSealGripPoint() => ChoreoScreenPoint(choreoSeal, new Vector2(choreoSeal.rect.xMax, choreoSeal.rect.yMax));

    Vector2 ChoreoCharacterPoint(bool brace)
    {
        var rect = game?.CharacterRect;
        if (rect == null) return ChoreoWindowRightPerch();
        // Her paper faces left; its back is the right half of the 70px canvas.
        // The pointer tip presses into that back while the whole hand sits behind.
        Vector2 point = brace ? new Vector2(rect.rect.center.x + rect.rect.width * .18f, rect.rect.yMin + rect.rect.height * .53f)
            : new Vector2(rect.rect.center.x + rect.rect.width * .32f, rect.rect.yMax + 4f);
        return ChoreoScreenPoint(rect, point);
    }

    Vector2 ChoreoWindowRightPerch()
    {
        var rect = window != null ? window.WindowRect : null;
        return rect != null ? ChoreoScreenPoint(rect, new Vector2(rect.rect.xMax - 25f, rect.rect.yMax - 5f))
            : actor.CompanionScreenPosition;
    }

    float ChoreoUnit()
    {
        var rect = liveWindow != null ? liveWindow.WindowRect : chapter01Story?.PresentationLayer;
        return rect != null ? Vector2.Distance(ChoreoScreenPoint(rect, Vector2.zero), ChoreoScreenPoint(rect, Vector2.right))
            : Mathf.Max(1f, Screen.height / 360f);
    }

    static Camera ChoreoCamera(RectTransform rect)
    {
        var canvas = rect.GetComponentInParent<Canvas>();
        return canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
    }

    static Vector2 ChoreoScreenPoint(RectTransform rect, Vector2 local)
        => RectTransformUtility.WorldToScreenPoint(ChoreoCamera(rect), rect.TransformPoint(local));

    static void ChoreoPlaceScreen(RectTransform rect, Vector2 screen)
    {
        if (rect == null || !(rect.parent is RectTransform parent)) return;
        if (RectTransformUtility.ScreenPointToWorldPointInRectangle(parent, screen, ChoreoCamera(parent), out Vector3 world))
            rect.position = world;
    }

    static void ChoreoSetPivot(RectTransform rect, Vector2 pivot)
    {
        Vector3 center = rect.TransformPoint(rect.rect.center);
        rect.pivot = pivot;
        rect.position += center - rect.TransformPoint(rect.rect.center);
    }

    static Rect ChoreoScreenBounds(RectTransform rect)
    {
        var corners = new Vector3[4]; rect.GetWorldCorners(corners);
        Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity), max = -min;
        var camera = ChoreoCamera(rect);
        foreach (Vector3 corner in corners)
        {
            Vector2 p = RectTransformUtility.WorldToScreenPoint(camera, corner);
            min = Vector2.Min(min, p); max = Vector2.Max(max, p);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }
}
