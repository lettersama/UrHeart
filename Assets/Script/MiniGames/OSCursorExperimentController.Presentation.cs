using MrTGame.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class OSCursorExperimentController
{
    [Header("Optional two-state art")]
    [SerializeField] Sprite cageLatchOpenSprite;
    [SerializeField] Sprite[] collisionFrames = new Sprite[0];

    [Header("Cage side-hinge frames (closed, then five opening states)")]
    [SerializeField] Sprite[] cageDoorFrames = new Sprite[6];

    readonly RectTransform[] stages = new RectTransform[3];
    RectTransform intro, contact, cageFront, cageDoor, cageLatch, contactPanel;
    RectTransform dynamicRoot, transferRoot;
    UnityEngine.UI.Image pointerImage, weightImage, transferHandle, transferHub, transferLeft, transferRight;
    UnityEngine.UI.Image contactFlash;
    readonly UnityEngine.UI.Image[] chainPixels = new UnityEngine.UI.Image[24];
    Sprite normalPointer, hackedPointer, smallWeight, closedLatch, originalContact, closedCageDoor;
    // visiblePointer is the normal sprite's top-left; maze pointer/contactPoint
    // are its centre. Keep cage/claw art in its authored top-left coordinates.
    Vector2 weightPoint, visiblePointer, cageDoorHome, cageLatchHome;
    bool presentationReady;
    int currentShot = -2;
    TMP_Text cageGestureHint;
    bool showCageGestureHint;
    public bool ShowCageGestureHint
    {
        get => showCageGestureHint;
        set
        {
            showCageGestureHint = value;
            if (cageGestureHint != null) cageGestureHint.gameObject.SetActive(value && IsPointerFreeInCage);
        }
    }
    static readonly Color Ink = new Color32(0x87, 0x82, 0x7e, 0xff);

    void InitializePresentation()
    {
        if (presentationReady) return;
        if (rootRect == null) rootRect = transform as RectTransform;
        for (int i = 0; i < 3; i++) stages[i] = transform.Find("Stage" + (i + 1)) as RectTransform;
        intro = transform.Find("IntroCage") as RectTransform;
        contact = transform.Find("CollisionCloseup") as RectTransform;
        if (intro == null || contact == null || stages[0] == null || stages[1] == null || stages[2] == null) return;
        normalPointer = SpriteAt(stages[0], "Pointer");
        hackedPointer = SpriteAt(stages[2], "Pointer");
        smallWeight = SpriteAt(stages[0], "SmallBall");
        cageFront = intro.Find("CageFront") as RectTransform;
        cageDoor = intro.Find("CageDoor") as RectTransform;
        closedCageDoor = SpriteAt(intro, "CageDoor");
        cageLatch = intro.Find("Latch") as RectTransform;
        if (cageFront != null && cageLatch != null)
        {
            // The new gate has an authored handle; the old floating latch belongs
            // only to the earlier small-cage preview.
            cageLatch.gameObject.SetActive(false);
            cageLatch = null;
        }
        cageDoorHome = cageDoor != null ? new Vector2(cageDoor.anchoredPosition.x, -cageDoor.anchoredPosition.y) : Vector2.zero;
        cageLatchHome = cageLatch != null ? new Vector2(cageLatch.anchoredPosition.x, -cageLatch.anchoredPosition.y) : Vector2.zero;
        closedLatch = SpriteAt(intro, "Latch");
        contactPanel = contact.Find("ContactPanel") as RectTransform;
        originalContact = SpriteAt(contact, "ContactPanel");
        foreach (var stage in stages)
        {
            HideChild(stage, "Pointer"); HideChild(stage, "Chain");
            HideChild(stage, "SmallBall"); HideChild(stage, "HeavyBall");
            HideChild(stage, "Inversion");
        }
        HideChild(intro, "Pointer");
        InitializeCageGestureHint();

        dynamicRoot = MakeRect("RuntimePointerAndWeight", rootRect, new Vector2(256, 208));
        for (int i = 0; i < chainPixels.Length; i++)
        {
            chainPixels[i] = MakeImage("Link" + i.ToString("00"), dynamicRoot, null);
            chainPixels[i].color = Ink;
            chainPixels[i].rectTransform.sizeDelta = Vector2.one;
        }
        weightImage = MakeImage("Weight", dynamicRoot, smallWeight);
        pointerImage = MakeImage("VirtualPointer", dynamicRoot, normalPointer);
        InitializeContactEffects();
        InitializeGravityPresentation();
        transferRoot = MakeRect("TransferClaw", rootRect, Vector2.one);
        transferHandle = MakeImage("ControlHandle", transferRoot, transferHandleSprite);
        var cable = MakeImage("Cable", transferRoot, null);
        cable.color = Ink; cable.rectTransform.sizeDelta = new Vector2(3, 3);
        Place(cable.rectTransform, new Vector2(-1, -19));
        transferHub = MakeImage("Hub", transferRoot, transferHubSprite);
        transferLeft = MakeImage("LeftFinger", transferRoot, transferLeftFingerSprite);
        transferRight = MakeImage("RightFinger", transferRoot, transferRightFingerSprite);
        Place(transferHandle.rectTransform, new Vector2(-10, -29));
        Place(transferHub.rectTransform, new Vector2(-4, -16));
        SetTransferGrip(true);
        contactFlash = MakeImage("ContactFlash", rootRect, null);
        contactFlash.rectTransform.anchorMin = Vector2.zero;
        contactFlash.rectTransform.anchorMax = Vector2.one;
        contactFlash.rectTransform.offsetMin = contactFlash.rectTransform.offsetMax = Vector2.zero;
        contactFlash.color = new Color(Ink.r, Ink.g, Ink.b, 0f);
        contactFlash.gameObject.SetActive(false);
        dynamicRoot.gameObject.SetActive(false); transferRoot.gameObject.SetActive(false);
        presentationReady = true;
        RefreshLanguage();
    }

    void LanguageChanged(LanguageState language) => RefreshLanguage();
    public void RefreshLanguage()
    {
        LabelAt(transform, "Title", "ui.os.cursor_experiment.title", "观察");
        for (int i = 0; i < stages.Length; i++)
        {
            if (stages[i] == null) continue;
            bool completed = phase == ExperimentPhase.Completed && i == 2;
            LabelAt(stages[i], "RuleLabel", completed ? "ui.os.cursor_experiment.complete" : i == 2
                ? "ui.os.cursor_experiment.inverted" : "ui.os.cursor_experiment.avoid_walls",
                completed ? "测试完成" : i == 2 ? "方向反转 · 避开墙壁" : "避开墙壁");
        }
        if (intro != null) LabelAt(intro, "ObservingLabel", "ui.os.cursor_experiment.observing", "观察中…");
        RefreshCageGestureHint();
        if (contact != null) LabelAt(contact, "RepositioningLabel", "ui.os.cursor_experiment.repositioning", "重新定位…");
    }

    static void LabelAt(Transform parent, string name, string key, string fallback)
    {
        var label = parent != null ? parent.Find(name)?.GetComponent<TMP_Text>() : null;
        if (label == null) return;
        OSLocalizedFontUtility.ApplyCurrent(label);
        OSLocalizedText.SetRuntime(label, key, fallback);
    }

    void InitializeCageGestureHint()
    {
        var rect = MakeRect("GestureHint", intro, new Vector2(224, 26));
        Place(rect, new Vector2(16, 136));
        cageGestureHint = rect.gameObject.AddComponent<TextMeshProUGUI>();
        cageGestureHint.raycastTarget = false;
        cageGestureHint.fontSize = 7f;
        cageGestureHint.enableAutoSizing = false;
        cageGestureHint.textWrappingMode = TextWrappingModes.Normal;
        cageGestureHint.overflowMode = TextOverflowModes.Overflow;
        cageGestureHint.alignment = TextAlignmentOptions.Top;
        cageGestureHint.color = new Color32(0x59, 0x56, 0x53, 0xff);
        cageGestureHint.margin = Vector4.zero;
        cageGestureHint.gameObject.SetActive(false);
    }

    void RefreshCageGestureHint()
    {
        if (cageGestureHint == null) return;
        var source = intro.Find("ObservingLabel")?.GetComponent<TMP_Text>();
        if (source != null)
        {
            cageGestureHint.font = source.font;
            cageGestureHint.fontSharedMaterial = source.fontSharedMaterial;
            cageGestureHint.fontStyle = source.fontStyle;
            cageGestureHint.fontWeight = source.fontWeight;
        }
        OSLocalizedText.SetRuntime(cageGestureHint, "character.cursor_test.gesture_hint", "左右晃动鼠标反对，上下晃动鼠标同意");
        OSLocalizedFontUtility.ApplyCurrent(cageGestureHint);
    }

    void RenderPresentation()
    {
        if (!presentationReady || !begun) return;
        if (firstTestTransitionActive)
        {
            RenderFirstTestTransition();
            return;
        }
        int shot = phase == ExperimentPhase.Intake ? 3
            : phase == ExperimentPhase.Recapture && phaseTime >= .62f ? 3 : Mathf.Clamp(round - 1, 0, 2);
        SetShot(shot);
        if (cageGestureHint != null) cageGestureHint.gameObject.SetActive(showCageGestureHint && IsPointerFreeInCage);
        RenderGravityPresentation();
        RenderContactEffects();
        bool transfer = phase == ExperimentPhase.Intake || phase == ExperimentPhase.Recapture;
        transferRoot.gameObject.SetActive(transfer);
        dynamicRoot.gameObject.SetActive(phase != ExperimentPhase.Completed || completionPending);
        contactFlash.gameObject.SetActive(phase == ExperimentPhase.Collision && phaseTime < .25f);
        if (phase == ExperimentPhase.Completed)
        {
            HideWeight();
            if (completionPending)
            {
                visiblePointer = MazePointerTopLeft(pointer);
                SetPointerSprite(normalPointer);
                PlacePointer(visiblePointer);
                PresentVirtualPointer();
            }
            return;
        }

        if (phase == ExperimentPhase.Collision)
        {
            RenderContactFeedback();
            return;
        }

        if (phase == ExperimentPhase.Intake) RenderIntake();
        else if (phase == ExperimentPhase.Recapture) RenderRecapture();
        else
        {
            visiblePointer = MazePointerTopLeft(pointer);
            SetPointerSprite(round == 3 ? hackedPointer : normalPointer);
            PlacePointer(visiblePointer);
            RenderWeight();
            // The starting pointer remains still while the new rule can be read.
            if (phase == ExperimentPhase.RoundIntro)
            {
                float pulse = phaseTime < .55f ? .75f + .25f * Mathf.Sin(phaseTime * 12f) : 1f;
                pointerImage.color = new Color(1f, 1f, 1f, pulse);
            }
            else pointerImage.color = Color.white;
        }
        PresentVirtualPointer();
    }

    void RenderContactFeedback()
    {
        // The maze and travelled chain remain in place. Only the displayed
        // pointer recoils; its interaction hotspot stays at first contact.
        int beat = Mathf.FloorToInt(phaseTime / .045f) & 1;
        float recoil = phaseTime < .18f ? (beat == 0 ? 1f : .35f)
            : Mathf.Lerp(.35f, 0f, Mathf.Clamp01((phaseTime - .18f) / .1f));
        visiblePointer = MazePointerTopLeft(contactPoint) + contactRecoil * recoil;
        SetPointerSprite(round == 3 ? hackedPointer : normalPointer);
        PlacePointer(visiblePointer);
        pointerImage.color = new Color(1f, 1f, 1f, phaseTime < .18f && beat == 1 ? .45f : 1f);
        RenderWeight();

        // One restrained flash of the authored ink colour, confined to this
        // window. No other OS app, Live view, or physical pointer is moved.
        float alpha = phaseTime < .07f ? .22f : .22f * (1f - Mathf.Clamp01((phaseTime - .07f) / .18f));
        contactFlash.color = new Color(Ink.r, Ink.g, Ink.b, alpha);
        PresentVirtualPointer();
    }

    void RenderIntake()
    {
        HideWeight();
        if (IsInteriorTransportActive)
        {
            RenderInteriorTransport();
            return;
        }
        float t = Mathf.Clamp01(phaseTime / 1.25f);
        Vector2 carriedPosition = CageFrontEntryPosition(t);
        visiblePointer = IsPointerFreeInCage ? cagePointer : carriedPosition;
        SetCagePointerDepth(IsPointerFreeInCage || t >= .88f);
        SetPointerSprite(normalPointer); PlacePointer(visiblePointer);
        // Once released, the claw retreats along its own fixed track. A player
        // moving around the cage cannot pull the departing mechanism sideways.
        Vector2 grip = phaseTime > 1.55f ? CageFrontRetreatPosition(phaseTime - 1.55f) : carriedPosition;
        Place(transferRoot, grip);
        transferRoot.gameObject.SetActive(phaseTime < 2.25f);
        SetTransferGrip(phaseTime < 1.45f);
        SetCageDoor(CageDoorOpenness(phaseTime, 1.55f));
        if (round == 1 && showCageStoryWeight && IsPointerFreeInCage) RenderCageStoryWeight();
    }

    void RenderCageStoryWeight()
    {
        RenderShortStoryTether(cagePointer, GetCageStoryWeightCenter());
    }

    Vector2 GetCageStoryWeightCenter()
    {
        return TetherBallForPresentation(cagePointer);
    }

    void RenderShortStoryTether(Vector2 cursorPosition, Vector2 ball)
    {
        weightImage.sprite = smallWeight;
        NativeSize(weightImage);
        weightImage.gameObject.SetActive(true);
        Vector2 half = weightImage.rectTransform.sizeDelta * .5f;
        Vector2 attachment = cursorPosition + new Vector2(2, 2);
        Place(weightImage.rectTransform, ball - half);
        for (int i = 0; i < chainPixels.Length; i++)
        {
            float distance = (i + 1) * 1.5f;
            bool active = distance < TetherLength - 3f;
            chainPixels[i].gameObject.SetActive(active);
            if (active) Place(chainPixels[i].rectTransform, TetherRopePoint(distance, attachment, ball));
        }
    }

    void RenderRecapture()
    {
        HideWeight();
        SetPointerSprite(round == 3 ? hackedPointer : normalPointer);
        float approach = Mathf.Clamp01(phaseTime / .45f);
        if (phaseTime < .62f)
        {
            visiblePointer = transferOrigin;
            Place(transferRoot, transferOrigin + new Vector2(0, -48f * (1f - Ease(approach))));
            SetTransferGrip(phaseTime > .4f);
        }
        else
        {
            float t = Mathf.Clamp01((phaseTime - .62f) / 1.03f);
            Vector2 carriedPosition = CageFrontEntryPosition(t);
            visiblePointer = IsPointerFreeInCage ? cagePointer : carriedPosition;
            SetCagePointerDepth(IsPointerFreeInCage || t >= .88f);
            Place(transferRoot, phaseTime > 1.75f ? CageFrontRetreatPosition(phaseTime - 1.75f) : carriedPosition);
            transferRoot.gameObject.SetActive(phaseTime < 2.45f);
            SetTransferGrip(phaseTime < 1.65f);
            SetCageDoor(CageDoorOpenness(phaseTime - .62f, 1.75f - .62f));
        }
        PlacePointer(visiblePointer);
        if (round == 1 && showCageStoryWeight && IsPointerFreeInCage) RenderCageStoryWeight();
    }

    Vector2 CageFrontEntryPosition(float progress)
    {
        // The roof is solid. Approach below the front doorway in the foreground,
        // then insert through that opening instead of dropping through the roof.
        Vector2 threshold = CagePoint + new Vector2(0f, 24f);
        if (progress < .68f) return Vector2.Lerp(transferOrigin, threshold, Ease(progress / .68f));
        return Vector2.Lerp(threshold, CagePoint, Ease((progress - .68f) / .32f));
    }

    static Vector2 CageFrontRetreatPosition(float seconds)
    {
        // The released pointer stays in the cage; only the foreground mechanism
        // withdraws from the doorway and rises past the front of the solid roof.
        Vector2 threshold = CagePoint + new Vector2(0f, 24f);
        if (seconds < .23f) return Vector2.Lerp(CagePoint, threshold, Ease(seconds / .23f));
        return Vector2.Lerp(threshold, CagePoint + new Vector2(0f, -138f), Ease((seconds - .23f) / .47f));
    }

    static float CageDoorOpenness(float seconds, float retreatStarts)
    {
        if (seconds < .18f) return Ease(seconds / .18f);
        return 1f - Ease((seconds - retreatStarts - .28f) / .42f);
    }

    void SetCagePointerDepth(bool inside)
    {
        if (dynamicRoot == null || intro == null || rootRect == null) return;
        RectTransform target = inside ? intro : rootRect;
        if (dynamicRoot.parent != target)
        {
            dynamicRoot.SetParent(target, false);
            Place(dynamicRoot, Vector2.zero);
        }
        if (inside)
        {
            dynamicRoot.SetAsLastSibling();
            RectTransform front = cageFront != null ? cageFront : cageDoor;
            if (front != null) dynamicRoot.SetSiblingIndex(front.GetSiblingIndex());
        }
        else dynamicRoot.SetAsLastSibling();
        // Fingers always stay in front of the cage while inserting/retracting.
        if (transferRoot != null) transferRoot.SetAsLastSibling();
    }

    void RenderWeight()
    {
        if (round != 1) { HideWeight(); return; }
        RenderShortStoryTether(visiblePointer, TetherBallForPresentation(visiblePointer));
    }

    void HideWeight()
    {
        weightImage.gameObject.SetActive(false);
        foreach (var link in chainPixels) link.gameObject.SetActive(false);
    }

    void SetCageDoor(float openness)
    {
        openness = Mathf.Clamp01(openness);
        if (cageDoor != null)
        {
            // The shared 72x48 canvas keeps the left hinge and authored lock
            // aligned. Open states are drawn perspectives, never a lifted door.
            Place(cageDoor, cageDoorHome);
            var doorImage = cageDoor.GetComponent<UnityEngine.UI.Image>();
            if (doorImage != null)
            {
                int frame = Mathf.Clamp(Mathf.RoundToInt(openness * 5f), 0, 5);
                Sprite sprite = cageDoorFrames != null && cageDoorFrames.Length == 6 ? cageDoorFrames[frame] : null;
                doorImage.sprite = sprite != null ? sprite : closedCageDoor;
                NativeSize(doorImage);
            }
        }
        if (cageLatch != null)
        {
            Place(cageLatch, cageLatchHome);
            var latch = cageLatch.GetComponent<UnityEngine.UI.Image>();
            if (latch != null) latch.sprite = closedLatch;
        }
    }

    void SetTransferGrip(bool closed)
    {
        if (transferLeft != null) Place(transferLeft.rectTransform, new Vector2(closed ? -8 : -13, closed ? -10 : -11));
        if (transferRight != null) Place(transferRight.rectTransform, new Vector2(closed ? -2 : 3, closed ? -10 : -11));
    }

    void SetShot(int shot)
    {
        if (!presentationReady && shot != -1) return;
        if (currentShot == shot) return;
        currentShot = shot;
        for (int i = 0; i < stages.Length; i++) if (stages[i] != null) stages[i].gameObject.SetActive(shot == i);
        if (intro != null) intro.gameObject.SetActive(shot == 3);
        if (contact != null) contact.gameObject.SetActive(shot == 4);
        if (shot < 0 || dynamicRoot == null) return;
        RectTransform parent = shot < 3 ? stages[shot] : shot == 3 ? intro : rootRect;
        dynamicRoot.SetParent(parent, false);
        Place(dynamicRoot, Vector2.zero);
        // The cage front really masks the pointer once it is deposited inside.
        if (shot == 3 && (cageFront != null || cageDoor != null))
        {
            // Resume may already have this parent. Move out of the way before
            // reading the front's index so repeated restores preserve layering.
            dynamicRoot.SetAsLastSibling();
            dynamicRoot.SetSiblingIndex((cageFront != null ? cageFront : cageDoor).GetSiblingIndex());
        }
        else dynamicRoot.SetAsLastSibling();
        if (shot == 4 && contact != null && stages[Mathf.Clamp(round - 1, 0, 2)] != null)
        {
            CopySprite(stages[round - 1], contact, "Progress");
            CopySprite(stages[round - 1], contact, "RuleIcon");
        }
    }

    void SetPointerSprite(Sprite sprite)
    {
        pointerImage.sprite = sprite; pointerImage.color = Color.white; NativeSize(pointerImage);
    }

    Vector2 NormalPointerHalfSize => normalPointer != null
        ? normalPointer.rect.size * .5f : Vector2.one * (CagePointerSize * .5f);

    Vector2 MazePointerTopLeft(Vector2 centre) => MazeOffset + centre - NormalPointerHalfSize;

    void PlacePointer(Vector2 normalTopLeft)
    {
        // Snap the shared 9px frame first, then centre the larger hacked sprite.
        // Rounding each sprite independently would shift its centre by one pixel.
        Vector2 snapped = new Vector2(Mathf.Round(normalTopLeft.x), Mathf.Round(normalTopLeft.y));
        Vector2 topLeft = snapped + NormalPointerHalfSize - pointerImage.rectTransform.sizeDelta * .5f;
        pointerImage.rectTransform.anchoredPosition = new Vector2(topLeft.x, -topLeft.y);
    }

    void PresentVirtualPointer()
    {
        var cursor = CursorHandle.Instance;
        if (cursor == null || !cursor.IsScriptControlledBy(this)) return;
        Vector2 point = phase == ExperimentPhase.Collision ? MazeOffset + contactPoint : visiblePointer + NormalPointerHalfSize;
        cursor.SetScriptScreenPosition(this, NativeToScreen(point));
    }

    Camera UICamera
    {
        get
        {
            var canvas = rootRect != null ? rootRect.GetComponentInParent<Canvas>() : null;
            return canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        }
    }
    Vector2 ScreenToNative(Vector2 screen)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(rootRect, screen, UICamera, out Vector2 local);
        return new Vector2(local.x - rootRect.rect.xMin, rootRect.rect.yMax - local.y);
    }
    Vector2 NativeToScreen(Vector2 native)
    {
        Vector2 local = new Vector2(rootRect.rect.xMin + native.x, rootRect.rect.yMax - native.y);
        return RectTransformUtility.WorldToScreenPoint(UICamera, rootRect.TransformPoint(local));
    }
    Vector2 RawDeltaToNative(Vector2 delta)
    {
        Vector2 origin = NativeToScreen(Vector2.zero);
        return ScreenToNative(origin + delta) - ScreenToNative(origin);
    }
    static float Ease(float t) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
    static void Place(RectTransform rt, Vector2 topLeft)
    { rt.anchoredPosition = new Vector2(Mathf.Round(topLeft.x), -Mathf.Round(topLeft.y)); }
    static Sprite SpriteAt(Transform parent, string name) => parent.Find(name)?.GetComponent<UnityEngine.UI.Image>()?.sprite;
    static void HideChild(Transform parent, string name) { var child = parent.Find(name); if (child != null) child.gameObject.SetActive(false); }
    static void CopySprite(Transform source, Transform target, string name)
    { var image = target.Find(name)?.GetComponent<UnityEngine.UI.Image>(); if (image != null) image.sprite = SpriteAt(source, name); }
    static void NativeSize(UnityEngine.UI.Image image)
    { if (image.sprite != null) image.rectTransform.sizeDelta = image.sprite.rect.size; }
    static RectTransform MakeRect(string name, Transform parent, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform)); go.layer = parent.gameObject.layer;
        var rect = (RectTransform)go.transform; rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.sizeDelta = size; rect.anchoredPosition = Vector2.zero;
        return rect;
    }
    static UnityEngine.UI.Image MakeImage(string name, Transform parent, Sprite sprite)
    {
        var rect = MakeRect(name, parent, sprite != null ? sprite.rect.size : Vector2.one);
        var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>(); image.sprite = sprite;
        image.raycastTarget = false;
        return image;
    }
}
