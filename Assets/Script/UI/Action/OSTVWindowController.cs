using System;
using DG.Tweening;
using MrTGame.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(OSWindowUnit))]
public sealed class OSTVWindowController : MonoBehaviour
{
    [SerializeField] RectTransform screen;
    [SerializeField] RectTransform leftCurtain;
    [SerializeField] RectTransform rightCurtain;
    [SerializeField] TMP_Text title;
    [SerializeField, Min(.01f)] float curtainDuration = 1.4f;
    Tween curtainTween;
    float curtainProgress;
    bool targetOpen;
    OSWindowUnit window;

    public bool IsCurtainOpen => targetOpen && curtainProgress >= .999f;
    public bool AreCurtainsClosed => !targetOpen && curtainProgress <= .001f;
    public float CurtainProgress => curtainProgress;
    public RectTransform Screen => screen;
    public event Action CurtainOpened;

    void OnEnable()
    {
        window = GetComponent<OSWindowUnit>();
        window.AlwaysOnTopToggled += HandlePinToggled;
        GameEvents.LanguageChanged += HandleLanguageChanged;
        RefreshTitle();
        RestoreSavedPresentation();
    }

    void OnDisable()
    {
        if (window != null) window.AlwaysOnTopToggled -= HandlePinToggled;
        GameEvents.LanguageChanged -= HandleLanguageChanged;
        curtainTween?.Kill(false);
        curtainTween = null;
        curtainProgress = targetOpen ? 1f : 0f;
        ApplyCurtainPose();
    }

    void OnRectTransformDimensionsChange() => ApplyCurtainPose();
    void HandleLanguageChanged(LanguageState language) => RefreshTitle();

    void LateUpdate()
    {
        if (window == null || !window.IsOpen || OSSessionPersistence.IsRestoring) return;
        // Generic layout restoration can reset the pin stack. Reapply the TV's
        // saved preference, just as interactive Memory restores its own pin.
        bool pinned = GameDataManager.Instance?.OSProcessData.memory.chapter04TVPinned ?? true;
        if (window.IsAlwaysOnTop != pinned) window.SetAlwaysOnTop(pinned);
    }

    void HandlePinToggled(OSWindowUnit source, bool pinned)
    {
        if (source == window && GameDataManager.Instance != null)
            GameDataManager.Instance.OSProcessData.memory.chapter04TVPinned = pinned;
    }

    void RefreshTitle()
    {
        if (title == null) return;
        OSLocalizedFontUtility.ApplyCurrent(title);
        title.text = OSToolProgramLibrary.Localize("ui.os.tv.title", "TV");
    }

    public void ResetPresentation() => ApplyCurtainState(false, true);

    public void RestoreSavedPresentation()
    {
        GameDataManager data = GameDataManager.Instance;
        bool canResume = data != null && data.IsTVInstalled && data.CurrentChapterIndex == 4
            && data.OSProcessData.memory.chapter04EntryStep == (int)OSChapter04EntryDirector.EntryStep.Complete;
        ApplyCurtainState(canResume && data.OSProcessData.memory.chapter04TVCurtainOpen, true, false);
    }

    // The first chapter-4 line calls this when its text actually starts playing.
    public void NotifyOpeningLineStarted() => SetCurtainOpen(true);

    public void SetCurtainOpen(bool open, bool immediate = false)
    {
        GameDataManager data = GameDataManager.Instance;
        if (data != null && data.IsTVInstalled && data.CurrentChapterIndex == 4
            && data.OSProcessData.memory.chapter04EntryStep == (int)OSChapter04EntryDirector.EntryStep.Complete)
            data.OSProcessData.memory.chapter04TVCurtainOpen = open;
        ApplyCurtainState(open, immediate);
    }

    void ApplyCurtainState(bool open, bool immediate, bool notify = true)
    {
        bool changed = targetOpen != open;
        targetOpen = open;
        curtainTween?.Kill(false);
        curtainTween = null;
        if (immediate || !isActiveAndEnabled)
        {
            curtainProgress = open ? 1f : 0f;
            ApplyCurtainPose();
            if (notify && changed && open) CurtainOpened?.Invoke();
            return;
        }
        curtainTween = DOTween.To(() => curtainProgress, value =>
        {
            curtainProgress = value;
            ApplyCurtainPose();
        }, open ? 1f : 0f, curtainDuration).SetEase(Ease.InOutSine).SetUpdate(true)
            .OnComplete(() =>
            {
                curtainTween = null;
                if (notify && changed && open) CurtainOpened?.Invoke();
            });
    }

    void ApplyCurtainPose()
    {
        if (screen == null || leftCurtain == null || rightCurtain == null) return;
        float offset = screen.rect.width * .5f * curtainProgress;
        leftCurtain.anchoredPosition = new Vector2(-offset, 0f);
        rightCurtain.anchoredPosition = new Vector2(offset, 0f);
    }

    public static OSTVWindowController Create(Transform parent, OSWindowUnit frameTemplate)
    {
        var root = new GameObject("WindowUnit_TV", typeof(RectTransform));
        root.SetActive(false);
        root.layer = parent.gameObject.layer;
        var rect = (RectTransform)root.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
        rect.pivot = new Vector2(.5f, .5f);
        rect.sizeDelta = new Vector2(320f, 224f);
        Image border = root.AddComponent<Image>();
        Image source = frameTemplate != null
            ? frameTemplate.transform.Find("Bk")?.GetComponent<Image>() : null;
        if (source == null && frameTemplate != null) source = frameTemplate.GetComponent<Image>();
        border.color = source != null ? source.color : new Color32(53, 51, 49, 255);
        if (source != null)
        {
            border.sprite = source.sprite;
            border.type = source.type;
            border.material = source.material;
            border.pixelsPerUnitMultiplier = source.pixelsPerUnitMultiplier;
        }
        border.raycastTarget = true;
        Image top = AddImage("DragArea", rect, Color.clear, true);
        SetAnchors(top.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(1, -15), new Vector2(-1, -1));
        TMP_Text fontTemplate = frameTemplate != null
            ? frameTemplate.transform.Find("TopName")?.GetComponent<TMP_Text>() : null;
        if (fontTemplate == null && frameTemplate != null) fontTemplate = frameTemplate.GetComponentInChildren<TMP_Text>(true);
        TMP_Text label = AddText("Title", top.transform, "TV", fontTemplate);
        SetAnchors(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(4, 0), new Vector2(-45, 0));

        Image body = AddImage("DetailsArea", rect, new Color32(15, 14, 16, 255));
        SetAnchors(body.rectTransform, Vector2.zero, Vector2.one, new Vector2(3, 3), new Vector2(-3, -18));
        body.gameObject.AddComponent<CanvasGroup>();
        body.gameObject.AddComponent<RectMask2D>();
        Image left = AddCurtain("LeftCurtain", body.transform, false);
        Image right = AddCurtain("RightCurtain", body.transform, true);
        Image valance = AddImage("Valance", body.transform, new Color32(59, 27, 34, 255));
        SetAnchors(valance.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(0, -9), Vector2.zero);

        OSWindowUnit window = root.AddComponent<OSWindowUnit>();
        window.SetOpeningLayout(new Vector2(145f, 12f), new Vector2(320f, 224f));
        window.ConfigureInteraction(true, true);
        AddWindowButton(frameTemplate, top.transform, window, OSWindowButton.WindowAction.ToggleAlwaysOnTop, -29f);
        AddWindowButton(frameTemplate, top.transform, window, OSWindowButton.WindowAction.Minimize, -15f);
        AddWindowButton(frameTemplate, top.transform, window, OSWindowButton.WindowAction.Close, -1f);
        OSTVWindowController controller = root.AddComponent<OSTVWindowController>();
        controller.screen = body.rectTransform;
        controller.leftCurtain = left.rectTransform;
        controller.rightCurtain = right.rectTransform;
        controller.title = label;
        controller.ResetPresentation();
        // Chapter 4 draws its paper theatre over the screen and hides the curtains itself.
        OSChapter04TheaterView.Attach(controller);
        controller.RefreshTitle();
        window.PrepareRuntimeClosed();
        return controller;
    }

    static Image AddCurtain(string name, Transform parent, bool right)
    {
        Image panel = AddImage(name, parent, new Color32(96, 45, 54, 255));
        SetAnchors(panel.rectTransform, new Vector2(right ? .5f : 0f, 0f),
            new Vector2(right ? 1f : .5f, 1f), Vector2.zero, Vector2.zero);
        for (int i = 0; i < 7; i++)
        {
            Image shadow = AddImage("Pleat" + i, panel.transform, new Color32(64, 29, 37, 255));
            SetAnchors(shadow.rectTransform, new Vector2(i / 7f, 0f),
                new Vector2((i + .32f) / 7f, 1f), Vector2.zero, Vector2.zero);
            Image light = AddImage("Fold" + i, panel.transform, new Color32(111, 54, 63, 255));
            SetAnchors(light.rectTransform, new Vector2((i + .36f) / 7f, 0f),
                new Vector2((i + .46f) / 7f, 1f), Vector2.zero, Vector2.zero);
        }
        return panel;
    }

    static Image AddImage(string name, Transform parent, Color color, bool raycast = false)
    {
        var child = new GameObject(name, typeof(RectTransform), typeof(Image));
        child.layer = parent.gameObject.layer;
        child.transform.SetParent(parent, false);
        Image image = child.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = raycast;
        return image;
    }

    static TMP_Text AddText(string name, Transform parent, string text, TMP_Text template)
    {
        var child = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        child.layer = parent.gameObject.layer;
        child.transform.SetParent(parent, false);
        var label = child.GetComponent<TextMeshProUGUI>();
        if (template != null) label.font = template.font;
        label.text = text;
        label.fontSize = template != null ? Mathf.Clamp(template.fontSize, 8f, 11f) : 10f;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.color = template != null ? template.color : new Color32(159, 155, 151, 255);
        label.raycastTarget = false;
        label.enableWordWrapping = false;
        return label;
    }

    static void AddWindowButton(OSWindowUnit template, Transform parent, OSWindowUnit window,
        OSWindowButton.WindowAction action, float right)
    {
        if (template == null) return;
        OSWindowButton source = Array.Find(template.GetComponentsInChildren<OSWindowButton>(true),
            candidate => candidate.LinkedWindow == template && candidate.Action == action);
        if (source == null)
        {
            Debug.LogError("[TV] Window template is missing its shared " + action + " button.");
            return;
        }
        GameObject clone = Instantiate(source.gameObject, parent, false);
        clone.name = source.name;
        clone.SetActive(true);
        var rect = (RectTransform)clone.transform;
        SetAnchors(rect, Vector2.one, Vector2.one,
            new Vector2(right - 14, -14), new Vector2(right, 0));
        clone.GetComponent<OSWindowButton>().BindToWindow(window);
    }

    static void SetAnchors(RectTransform rect, Vector2 min, Vector2 max, Vector2 insetMin, Vector2 insetMax)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = insetMin;
        rect.offsetMax = insetMax;
    }
}
