using System;
using DG.Tweening;
using MrTGame.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>The application card Character discovers while the memory is collecting.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(OSWindowUnit))]
public sealed class OSClawMachineAppWindowController : MonoBehaviour
{
    public const string NameLocalizationKey = "ui.os.app.claw_machine.name";

    [SerializeField] OSWindowUnit window;
    [SerializeField] RectTransform body;
    [SerializeField] Image appIconImage;
    [SerializeField] Image nameStickerImage;
    [SerializeField] TMP_Text nameLabel;
    [SerializeField] TMP_Text windowTitle;
    [SerializeField] TMP_Text desktopName;
    [SerializeField] Sprite appIcon;
    [SerializeField] Sprite nameSticker;
    [SerializeField, Min(0f)] float messageGap = 8f;
    [SerializeField, Min(0f)] float screenPadding = 6f;

    readonly Vector3[] corners = new Vector3[4];
    Tween revealTween;
    Vector2 bodyRestPosition;
    bool hasBodyRestPosition;

    public OSWindowUnit Window => window != null ? window : GetComponent<OSWindowUnit>();
    public event Action LaunchRequested;

    // The executable will subscribe here when the claw-machine gameplay is added.
    // The offer card itself is not the application that this icon launches.
    public void RequestLaunch() => LaunchRequested?.Invoke();

    void Awake()
    {
        ResolveReferences();
        ApplyArtwork();
    }

    void OnEnable()
    {
        ResolveReferences();
        ApplyArtwork();
        RefreshLanguage();
        GameEvents.LanguageChanged += HandleLanguageChanged;
        Window.MoveStarted += HandleMoveStarted;
    }

    void OnDisable()
    {
        GameEvents.LanguageChanged -= HandleLanguageChanged;
        Window.MoveStarted -= HandleMoveStarted;
        StopReveal();
    }

    void HandleLanguageChanged(LanguageState language) => RefreshLanguage();
    void HandleMoveStarted(OSWindowUnit source) => StopReveal();

    void ResolveReferences()
    {
        if (window == null) window = GetComponent<OSWindowUnit>();
        if (body != null && !hasBodyRestPosition)
        {
            bodyRestPosition = body.anchoredPosition;
            hasBodyRestPosition = true;
        }
    }

    void ApplyArtwork()
    {
        if (appIconImage != null && appIcon != null)
        {
            appIconImage.sprite = appIcon;
            appIconImage.preserveAspect = true;
        }
        if (nameStickerImage != null && nameSticker != null)
        {
            nameStickerImage.sprite = nameSticker;
            nameStickerImage.preserveAspect = false;
        }
    }

    public void RefreshLanguage()
    {
        string title = OSToolProgramLibrary.Localize(NameLocalizationKey, "抓娃娃机");
        ApplyTitle(nameLabel, title);
        ApplyTitle(windowTitle, title);
        ApplyTitle(desktopName, title);
    }

    static void ApplyTitle(TMP_Text target, string title)
    {
        if (target == null) return;
        target.text = title;
        OSLocalizedFontUtility.ApplyCurrent(target);
    }

    public void PlayReveal(bool animate)
    {
        ResolveReferences();
        StopReveal();
        RefreshLanguage();
        if (!animate || body == null || !gameObject.activeInHierarchy) return;

        // Integer motion keeps the icon's pixel grid intact; the window itself
        // uses the existing OS opening effect and owns the content fade.
        body.anchoredPosition = bodyRestPosition + new Vector2(0f, -5f);
        revealTween = body.DOAnchorPos(bodyRestPosition, 0.28f, true)
            .SetEase(Ease.OutBack, 0.8f).SetUpdate(true)
            .OnComplete(() => revealTween = null);
    }

    public void StopReveal()
    {
        revealTween?.Kill(false);
        revealTween = null;
        if (body != null && hasBodyRestPosition) body.anchoredPosition = bodyRestPosition;
    }

    public void PositionBesideMessage(RectTransform message, RectTransform character, RectTransform screen)
    {
        ResolveReferences();
        RectTransform rect = Window.WindowRect;
        RectTransform parent = rect.parent as RectTransform;
        if (parent == null) return;

        Canvas.ForceUpdateCanvases();
        Rect bounds = screen != null ? RelativeRect(screen, parent) : parent.rect;
        bounds = Rect.MinMaxRect(bounds.xMin + screenPadding, bounds.yMin + screenPadding,
            bounds.xMax - screenPadding, bounds.yMax - screenPadding);
        Vector2 size = Window.RecordedSize;
        Rect anchor = message != null ? RelativeRect(message, parent)
            : character != null ? RelativeRect(character, parent)
            : new Rect(bounds.center, Vector2.zero);
        Rect characterBounds = character != null ? RelativeRect(character, parent) : default;
        float halfWidth = size.x * .5f, halfHeight = size.y * .5f;
        Vector2[] candidates =
        {
            new Vector2(anchor.xMin - messageGap - halfWidth, anchor.center.y),
            new Vector2(anchor.xMax + messageGap + halfWidth, anchor.center.y),
            new Vector2(anchor.center.x, anchor.yMax + messageGap + halfHeight),
            new Vector2(anchor.center.x, anchor.yMin - messageGap - halfHeight),
            new Vector2(anchor.xMin + halfWidth, anchor.yMax + messageGap + halfHeight),
            new Vector2(anchor.xMin + halfWidth, anchor.yMin - messageGap - halfHeight),
            new Vector2(anchor.xMax - halfWidth, anchor.yMax + messageGap + halfHeight),
            new Vector2(anchor.xMax - halfWidth, anchor.yMin - messageGap - halfHeight)
        };
        Vector2 best = bounds.center;
        float bestScore = float.PositiveInfinity;
        for (int i = 0; i < candidates.Length; i++)
        {
            Vector2 center = ClampCenter(candidates[i], size, bounds);
            Rect candidate = new Rect(center - size * .5f, size);
            float score = IntersectionArea(candidate, anchor) * 100f
                + IntersectionArea(candidate, characterBounds) * 20f
                + Vector2.Distance(center, candidates[i]) * 2f
                + Vector2.Distance(center, anchor.center) * .01f + i * .05f;
            if (score >= bestScore) continue;
            bestScore = score;
            best = center;
        }

        // Convert from the common parent-local center to any configured anchors/pivot.
        rect.sizeDelta = size;
        Vector3 desired = new Vector3(best.x + (rect.pivot.x - .5f) * size.x,
            best.y + (rect.pivot.y - .5f) * size.y, 0f);
        rect.localPosition = desired;
        Window.SetOpeningLayout(new Vector2(Mathf.Round(rect.anchoredPosition.x),
            Mathf.Round(rect.anchoredPosition.y)), size);
    }

    Rect RelativeRect(RectTransform source, RectTransform parent)
    {
        source.GetWorldCorners(corners);
        Vector2 min = parent.InverseTransformPoint(corners[0]);
        Vector2 max = min;
        for (int i = 1; i < 4; i++)
        {
            Vector2 point = parent.InverseTransformPoint(corners[i]);
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    static Vector2 ClampCenter(Vector2 center, Vector2 size, Rect bounds)
    {
        center.x = size.x > bounds.width ? bounds.center.x
            : Mathf.Clamp(center.x, bounds.xMin + size.x * .5f, bounds.xMax - size.x * .5f);
        center.y = size.y > bounds.height ? bounds.center.y
            : Mathf.Clamp(center.y, bounds.yMin + size.y * .5f, bounds.yMax - size.y * .5f);
        return center;
    }

    static float IntersectionArea(Rect a, Rect b) =>
        Mathf.Max(0f, Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin))
        * Mathf.Max(0f, Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin));
}
