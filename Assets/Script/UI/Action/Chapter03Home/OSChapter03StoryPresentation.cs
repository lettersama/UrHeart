using System;
using System.Collections.Generic;
using MrTGame.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Chapter 3 transient UI only. The story owner supplies localized text,
/// commits saves, owns the red cursor, and pauses this component during settings/loading.</summary>
[DisallowMultipleComponent]
public sealed partial class OSChapter03StoryPresentation : MonoBehaviour
{
    enum ModalKind { None, Paper, Keypad, Art, Letter }
    static readonly Color Ink = new Color32(0xDC, 0xD8, 0xD4, 255);
    static readonly Color Dim = new Color32(0x87, 0x82, 0x7E, 255);
    const int ModalOrder = 1190; // Below the existing night mask (1200) and red pointer (1202).
    const string ArtResourcePath = "Chapter03Story/Art/";
    OS_Handle os;
    RectTransform windowParent;
    Canvas rootCanvas;
    TMP_FontAsset font;
    OSChapter02NightfallPresentation night;
    readonly List<RectTransform> registeredGlows = new List<RectTransform>();
    readonly List<GameObject> ownedRoots = new List<GameObject>();
    ModalKind modalKind;
    RectTransform modalRoot, modalFrame, paperBodyRect;
    CanvasGroup modalGroup, paperBodyGroup;
    OSDitherFill modalShade;
    TMP_Text paperBody, paperPageLabel, keypadFeedback;
    Button previousPageButton, nextPageButton, closeButton;
    Action modalClosed;
    Action<string> keypadSubmitted;
    string keypadValue = string.Empty;
    Image modalArt;
    string modalArtId;
    bool artDismissible;
    int modalOpenedFrame, paperPage, paperPages = 1;
    float modalAge, modalOpacity = 1f;
    bool paused;

    public bool Paused
    {
        get => paused;
        set
        {
            if (paused == value) return;
            paused = value;
            RefreshLetterPressVisuals();
            RefreshInput();
            RefreshMirrorInputShield();
            RefreshMirrorPlaybackPresentation();
            RefreshResourceDrainVisibility();
        }
    }
    public bool IsModalOpen => modalRoot != null && modalRoot.gameObject.activeSelf;
    public bool IsAnimating => letterEntering || letterOpening || letterCollecting || reasoningRoot != null || thoughtRoot != null;
    public RectTransform PaperRoot => modalKind == ModalKind.Paper || modalKind == ModalKind.Letter ? modalFrame : null;
    public RectTransform MirrorRoot => mirrorRoot;
    public Image ArtImage => modalKind == ModalKind.Art ? modalArt : null;
    public string ArtId => modalKind == ModalKind.Art ? modalArtId : null;
    public bool IsArtAutoClosing => modalKind == ModalKind.Art && !artDismissible;
    public bool IsLetterUnfolded => modalKind == ModalKind.Letter && letterUnfolded;
    public int LetterPage => modalKind == ModalKind.Letter ? paperPage : 0;
    public int LetterPageCount => modalKind == ModalKind.Letter ? paperPages : 0;
    public int LetterVisibleLineCount => LetterLineIndices().Count;
    public RectTransform LetterEnvelope => letterEnvelope;
    /// <summary>Where Live's red pointer presses the envelope, beside the seal.</summary>
    public RectTransform LetterPartnerPoint => letterPartnerPoint;
    // Older QA names from the fold-open letter.
    public RectTransform LetterLeftGrip => letterEnvelope;
    public RectTransform LetterRightGrip => letterPartnerPoint;

    public void Initialize(OS_Handle owner, RectTransform parent = null, TMP_FontAsset textFont = null)
    {
        if (os != null && os != owner) ResetPresentation();
        os = owner;
        windowParent = parent != null ? parent : os != null ? os.transform.Find("Windows") as RectTransform : null;
        if (windowParent == null) windowParent = owner != null ? owner.transform as RectTransform : transform as RectTransform;
        var canvas = windowParent != null ? windowParent.GetComponentInParent<Canvas>() : null;
        rootCanvas = canvas != null ? canvas.rootCanvas : null;
        if (windowParent == null || rootCanvas == null)
            throw new InvalidOperationException("Chapter 3 presentation requires the OS window RectTransform beneath a Canvas.");
        font = textFont;
        if (font == null && os?.MemoryWindowController != null)
            foreach (var text in os.MemoryWindowController.TextUnits)
                if (text != null && text.Label != null && text.Label.font != null) { font = text.Label.font; break; }
        if (font == null) font = TMP_Settings.defaultFontAsset;
        night = os != null ? os.GetComponent<OSChapter02NightfallPresentation>() : null;
    }

    void OnEnable() => GameEvents.LanguageChanged += OnLanguageChanged;
    void OnDisable() { GameEvents.LanguageChanged -= OnLanguageChanged; ResetPresentation(); }
    void OnDestroy() { ResetPresentation(); ReleasePixelImageMaterial(); ReleasePaperTextures(); }
    void OnLanguageChanged(LanguageState language)
    {
        foreach (var node in ownedRoots)
            if (node != null) foreach (var label in node.GetComponentsInChildren<TMP_Text>(true))
            {
                OSLocalizedFontUtility.ApplyCurrent(label);
                label.GetComponent<OSChapter03PixelText>()?.RefreshMaterial();
            }
        RefreshModalTypography();
        RebuildPagination(); // Strings remain the story owner's responsibility.
    }

    void Update()
    {
        if (windowParent == null) return;
        float dt = paused ? 0f : Mathf.Min(Time.unscaledDeltaTime, .1f);
        modalAge += dt;
        TickLetter(dt);
        TickKeypad(dt);
        TickEffects(dt);
        TickTransientNote(dt);
        TickResourceDrains(dt);
    }

    bool CanInteract => IsModalOpen && !paused && isActiveAndEnabled && Time.frameCount > modalOpenedFrame + 1
        && modalAge >= .18f && !letterEntering && !letterOpening && !letterCollecting;

    void RequireInitialized()
    {
        if (windowParent == null) Initialize(OS_Handle.Instance);
    }

    public void ShowPaper(string title, string body, Action closed, string style = "diary")
    {
        ValidatePaperStyle(style);
        OpenModal(ModalKind.Paper, title, new Vector2(PaperWidth(style), PaperMaxHeight), closed);
        BuildPaperBody(title, body, style);
    }

    public bool TryDismissPaperFromEscape()
    {
        if (!isActiveAndEnabled || paused || !IsModalOpen || !modalRoot.gameObject.activeInHierarchy
            || (modalKind != ModalKind.Paper && (modalKind != ModalKind.Letter || !letterUnfolded)))
            return false;

        // Use the X callbacks so collection and story completion stay identical.
        // Consume the key even during the brief opening input guard.
        if (modalKind == ModalKind.Letter) CloseLetterFromInput();
        else DismissFromInput();
        return true;
    }

    /// <summary>Refresh translated content without reopening a modal or resetting its progress.</summary>
    public void RefreshModalText(string title, string bodyOrHint = null)
    {
        if (!IsModalOpen || modalFrame == null) return;
        if (modalTitle != null && title != null) modalTitle.text = title;
        RefreshModalTypography();
        if (bodyOrHint == null && (modalKind == ModalKind.Paper || modalKind == ModalKind.Letter)) RebuildPagination();
        if (bodyOrHint == null) return;
        if (modalKind == ModalKind.Paper || modalKind == ModalKind.Letter)
        {
            if (paperBody == null) return;
            paperBody.text = OSPalette.TintRichText(bodyOrHint);
            // Pagination may change with the language; retain the nearest valid page.
            // Fold, entrance, collection and callbacks belong to the existing modal.
            RebuildPagination();
        }
        else if (modalKind == ModalKind.Keypad && keypadFeedback != null)
        {
            keypadFeedback.text = bodyOrHint;
            FitModalText(keypadFeedback, 8, 11);
            // A language change is not a rejected password: keep entered digits.
        }
    }

    // pancake 10-26: the metal plate (screws, broken rules, dot clusters) read as clutter.
    // Now one frame, one recessed display and one key grid, spaced on a 10 px rhythm with
    // equal side and bottom margins; the digits use the same pixel figures as the keys.
    const float KeyPitchX = 62f, KeyPitchY = 37f, KeySlotPitch = 22f;
    static readonly Vector2 KeySize = new Vector2(54f, 30f), KeypadSize = new Vector2(228f, 260f),
        KeyDisplaySize = new Vector2(120f, 32f);

    public void ShowKeypad(string title, string hint, Action<string> submitted, Action closed = null)
    {
        OpenModal(ModalKind.Keypad, title, KeypadSize, closed);
        keypadSubmitted = submitted; keypadValue = string.Empty; keypadShake = 0f;
        float divider = KeypadSize.y * .5f - 20f;
        var display = PixelImage(modalFrame, "Display", ModalInk, new Vector2(0f, divider - 10f - KeyDisplaySize.y * .5f), KeyDisplaySize);
        PixelImage(display.rectTransform, "Interior", Color.black, Vector2.zero, KeyDisplaySize - Vector2.one * 2f);
        keypadDisplay = display.rectTransform; keypadDisplayHome = keypadDisplay.anchoredPosition;
        keypadSlots = Rect("Slots", keypadDisplay, Vector2.zero, KeyDisplaySize);
        keypadFeedback = ModalText(modalFrame, "Feedback", hint, new Vector2(0f, divider - 10f - KeyDisplaySize.y - 6f - 8f),
            new Vector2(196f, 16f), 11, ModalInk);
        keypadFeedback.textWrappingMode = TextWrappingModes.NoWrap;
        FitModalText(keypadFeedback, 8, 11);
        // Bottom row sits as far from the frame's bottom edge as the grid does from its sides.
        float margin = (KeypadSize.x - KeySize.x * 3f - (KeyPitchX - KeySize.x) * 2f) * .5f;
        float bottomRow = -KeypadSize.y * .5f + margin + KeySize.y * .5f;
        for (int i = 1; i <= 9; i++)
        {
            string digit = i.ToString(); int cell = i - 1;
            PixelButton(modalFrame, "Digit" + digit, digit, new Vector2((cell % 3 - 1) * KeyPitchX, bottomRow + (3 - cell / 3) * KeyPitchY),
                KeySize, () => KeypadPress(digit));
        }
        var backspace = PixelButton(modalFrame, "Backspace", "", new Vector2(-KeyPitchX, bottomRow), KeySize, () => {
            if (!CanInteract || keypadValue.Length == 0) return;
            keypadValue = keypadValue.Substring(0, keypadValue.Length - 1); RefreshKeypadDigits();
            os?.Chapter03Story?.PlayKeypadPressSfx();
        });
        DrawKeypadActionIcon(PixelContent(backspace), true);
        PixelButton(modalFrame, "Digit0", "0", new Vector2(0f, bottomRow), KeySize, () => KeypadPress("0"));
        var submit = PixelButton(modalFrame, "Submit", "", new Vector2(KeyPitchX, bottomRow), KeySize, () => {
            if (CanInteract && keypadValue.Length == 4) keypadSubmitted?.Invoke(keypadValue);
        });
        DrawKeypadActionIcon(PixelContent(submit), false);
        RefreshKeypadDigits();
    }

    void DrawKeypadActionIcon(RectTransform parent, bool backspace)
    {
        DrawPixelGlyph(parent, "ActionPixel", backspace
            ? new[] { "...##########", "..#.........#", ".#..#...#...#", "#....#.#....#", ".#....#.....#", "..#..#.#....#", "...##########" }
            : new[] { "..........##", "..........##", "...#......##", "..#.......##", ".###########", "..#.........", "...#........" });
    }

    public void SetKeypadFeedback(string text)
    {
        if (modalKind != ModalKind.Keypad) return;
        keypadFeedback.text = text ?? string.Empty;
        FitModalText(keypadFeedback, 8, 11);
        keypadValue = string.Empty; RefreshKeypadDigits();
        keypadShake = KeypadShakeSeconds; // a wrong code shudders the display, no extra text
    }
    void KeypadPress(string digit)
    {
        if (!CanInteract || modalKind != ModalKind.Keypad || keypadValue.Length >= 4) return;
        keypadValue += digit; RefreshKeypadDigits();
        os?.Chapter03Story?.PlayKeypadPressSfx();
    }
    // Four slots: an underline each, the entered figure above it at three times the key scale's
    // pixel. The next slot to fill has the longer underline, so the display reads as waiting.
    void RefreshKeypadDigits()
    {
        if (keypadSlots == null) return;
        for (int i = keypadSlots.childCount - 1; i >= 0; i--)
        {
            var child = keypadSlots.GetChild(i).gameObject;
            child.SetActive(false); Destroy(child);
        }
        for (int i = 0; i < 4; i++)
        {
            float x = (i - 1.5f) * KeySlotPitch;
            bool next = i == keypadValue.Length;
            PixelImage(keypadSlots, "Slot", ModalInk, new Vector2(x, -10.5f), new Vector2(next ? 15f : 11f, 1f));
            if (i >= keypadValue.Length) continue;
            var cell = Rect("Figure", keypadSlots, new Vector2(x, 2f), new Vector2(15f, 15f));
            DrawDigit(cell, keypadValue[i] - '0', 3);
        }
    }

    const float KeypadShakeSeconds = .32f;
    float keypadShake;
    RectTransform keypadDisplay, keypadSlots;
    Vector2 keypadDisplayHome;

    void TickKeypad(float dt)
    {
        if (keypadDisplay == null || keypadShake <= 0f) return;
        keypadShake = Mathf.Max(0f, keypadShake - dt);
        float t = 1f - keypadShake / KeypadShakeSeconds;
        float offset = Mathf.Round(Mathf.Sin(t * Mathf.PI * 6f) * 3f * (1f - t));
        keypadDisplay.anchoredPosition = keypadDisplayHome + new Vector2(offset, 0f);
    }

    /// <summary>The id is stable and the title is localized. Source pixel resolution
    /// is independent of the fixed 216 by 162 UI viewport.</summary>
    public void ShowArt(string id, string title, Action closed = null, bool dismissible = true)
    {
        var sprite = RequireArtSprite(id);
        OpenModal(ModalKind.Art, title, new Vector2(252f, 214f), closed);
        modalArtId = id; artDismissible = dismissible;
        modalArt = Image(modalFrame, "ArtImage", Color.white, Vector2.zero, new Vector2(216f, 162f));
        modalArt.sprite = sprite; modalArt.type = UnityEngine.UI.Image.Type.Simple;
        modalArt.preserveAspect = true; modalArt.raycastTarget = false;
        closeButton.gameObject.SetActive(dismissible);
    }

    static Sprite RequireArtSprite(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Chapter 3 art requires a stable resource id.", nameof(id));
        var sprite = Resources.Load<Sprite>(ArtResourcePath + id);
        if (sprite == null)
            throw new InvalidOperationException("Missing Chapter 3 art Sprite: Assets/Resources/" + ArtResourcePath + id
                + ".png. Import the authored image as a Single Sprite; no placeholder is used.");
        return sprite;
    }

    void OpenModal(ModalKind kind, string title, Vector2 size, Action closed)
    {
        RequireInitialized(); HideModal(false);
        modalKind = kind; modalClosed = closed; modalAge = 0f; modalOpacity = 1f; modalOpenedFrame = Time.frameCount;
        modalRoot = Root("Chapter03Modal", windowParent, true);
        var canvas = modalRoot.gameObject.AddComponent<Canvas>();
        canvas.overrideSorting = true; canvas.sortingOrder = ModalOrder; canvas.worldCamera = rootCanvas.worldCamera;
        modalRoot.gameObject.AddComponent<GraphicRaycaster>();
        modalGroup = modalRoot.gameObject.AddComponent<CanvasGroup>();
        var shade = modalShade = OSDitherFill.Create("Outside", modalRoot, Color.black);
        shade.Image.material = RequirePixelImageMaterial();
        shade.SetLevel(11); shade.Image.raycastTarget = true;
        shade.gameObject.AddComponent<OSChapter03PresentationHit>().Clicked = e => DismissFromInput();
        Vector2 available = windowParent.rect.size - new Vector2(14f, 14f);
        size = Vector2.Min(size, new Vector2(Mathf.Max(170f, available.x), Mathf.Max(180f, available.y)));
        if (kind == ModalKind.Paper || kind == ModalKind.Letter)
        {
            // Paper is a bare sheet with its own title and close; see the Paper partial.
            modalFrame = Rect("PaperFrame", modalRoot, Vector2.zero, size);
        }
        else
        {
            modalFrame = ModalFrame(modalRoot, "PaperFrame", title, size, Vector2.zero);
            modalTitle = modalFrame.Find("Title")?.GetComponent<TMP_Text>();
            closeButton = PixelButton(modalFrame, "Close", "", new Vector2(size.x * .5f - 12f, size.y * .5f - 10f),
                new Vector2(20f, 18f), DismissFromInput);
            DrawClose(PixelContent(closeButton));
        }
        Glow(modalFrame);
        RefreshInput();
    }

    void DismissFromInput()
    {
        // The letter uses CloseLetterFromInput for its X/Escape, never the shade.
        if (!CanInteract || modalKind == ModalKind.Letter) return;
        if (modalKind == ModalKind.Art && !artDismissible) return;
        HideModal(true);
    }

    void RebuildPagination()
    {
        if (paperBody == null) return;
        // Re-cut the sheet to the text; a letter keeps its size while it folds.
        LayoutPaper();
    }
    void TurnPage(int direction)
    {
        if (paperBody == null || modalKind == ModalKind.Letter && !letterUnfolded) return;
        paperPage = Mathf.Clamp(paperPage + direction, 0, paperPages - 1); ApplyPage();
    }
    void ApplyPage()
    {
        if (paperBody == null) return;
        paperBody.pageToDisplay = paperPage + 1; paperBody.ForceMeshUpdate(true);
        bool paged = paperPages > 1;
        if (paperPageLabel != null)
        {
            paperPageLabel.text = paged ? (paperPage + 1) + " / " + paperPages : string.Empty;
            paperPageLabel.gameObject.SetActive(paged);
        }
        // Arrows that lead nowhere are hidden rather than greyed out.
        if (previousPageButton != null)
        {
            previousPageButton.interactable = paperPage > 0;
            previousPageButton.gameObject.SetActive(paged && paperPage > 0);
        }
        if (nextPageButton != null)
        {
            nextPageButton.interactable = paperPage + 1 < paperPages;
            nextPageButton.gameObject.SetActive(paged && paperPage + 1 < paperPages);
        }
        PaintPaper();
    }

    public void HideModal(bool notify = false)
    {
        var callback = notify ? modalClosed : null;
        modalClosed = null; keypadSubmitted = null; modalKind = ModalKind.None;
        ResetLetter(); Unglow(modalFrame); DestroyRoot(modalRoot);
        modalRoot = modalFrame = paperBodyRect = paperTitleTab = paperTitleRect = null; modalGroup = paperBodyGroup = null; modalShade = null;
        paperBody = paperPageLabel = keypadFeedback = modalTitle = null;
        keypadDisplay = keypadSlots = null; keypadShake = 0f;
        paperSheet = null; paperRules.Clear();
        if (paperTexture != null) Destroy(paperTexture);
        paperTexture = null;
        previousPageButton = nextPageButton = closeButton = null; paperPage = 0; paperPages = 1;
        modalArt = null; modalArtId = null; artDismissible = false;
        callback?.Invoke();
    }

    void RefreshInput()
    {
        if (modalGroup != null)
        {
            modalGroup.interactable = !paused;
            modalGroup.blocksRaycasts = !paused;
            // Settings uses the regular window stack, beneath this modal canvas.
            // Retain the page/animation state while allowing settings to be seen.
            modalGroup.alpha = paused ? 0f : modalOpacity;
        }
    }

    public void ResetPresentation()
    {
        ResetMirrorStage(); HideModal(false); ResetEffects(); ClearTransientNote();
        foreach (var rect in registeredGlows) if (rect != null) night?.RemoveGlow(rect);
        registeredGlows.Clear();
        foreach (var node in ownedRoots) if (node != null) { node.SetActive(false); Destroy(node); }
        ownedRoots.Clear();
    }

    RectTransform Root(string name, RectTransform parent, bool stretch = false)
    {
        var rect = Rect(name, parent, Vector2.zero, Vector2.zero);
        ownedRoots.Add(rect.gameObject); if (stretch) Stretch(rect); return rect;
    }
    void DestroyRoot(RectTransform rect)
    {
        if (rect == null) return;
        Unglow(rect); ownedRoots.Remove(rect.gameObject); rect.gameObject.SetActive(false); Destroy(rect.gameObject);
    }
    void Glow(RectTransform rect)
    {
        if (rect == null || registeredGlows.Contains(rect)) return;
        night?.AddGlowRect(rect); registeredGlows.Add(rect);
    }
    void Unglow(RectTransform rect)
    {
        if (rect == null) return;
        night?.RemoveGlow(rect); registeredGlows.Remove(rect);
    }
    static RectTransform Rect(string name, RectTransform parent, Vector2 position, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform)); go.layer = parent.gameObject.layer;
        var rect = (RectTransform)go.transform; rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = position; rect.sizeDelta = size; return rect;
    }
    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
    static Image Image(RectTransform parent, string name, Color color, Vector2 position, Vector2 size)
    {
        var image = Rect(name, parent, position, size).gameObject.AddComponent<Image>();
        image.color = color; image.raycastTarget = false; return image;
    }
    TMP_Text Text(RectTransform parent, string name, string value, Vector2 position, Vector2 size, float em, Color color)
    {
        var label = Rect(name, parent, position, size).gameObject.AddComponent<TextMeshProUGUI>();
        label.font = font; label.fontSize = em; label.color = color; label.text = value ?? string.Empty;
        label.alignment = TextAlignmentOptions.Center; label.textWrappingMode = TextWrappingModes.Normal;
        label.overflowMode = TextOverflowModes.Truncate; label.raycastTarget = false;
        OSLocalizedFontUtility.ApplyCurrent(label); return label;
    }
    RectTransform Frame(RectTransform parent, string name, string title, Vector2 size, Vector2 position)
    {
        var frame = Image(parent, name, Dim, position, size); frame.raycastTarget = true;
        Image(frame.rectTransform, "Interior", Color.black, Vector2.zero, size - Vector2.one * 2f);
        var label = Text(frame.rectTransform, "Title", title, new Vector2(-8f, size.y * .5f - 10f),
            new Vector2(size.x - 34f, 17f), 11f, Ink);
        label.alignment = TextAlignmentOptions.Left;
        Image(frame.rectTransform, "Divider", Dim, new Vector2(0f, size.y * .5f - 20f), new Vector2(size.x - 2f, 1f));
        return frame.rectTransform;
    }
    Button Button(RectTransform parent, string name, string caption, Vector2 position, Vector2 size, Action clicked)
    {
        var face = Image(parent, name, new Color32(0x3A, 0x38, 0x35, 255), position, size); face.raycastTarget = true;
        var button = face.gameObject.AddComponent<Button>(); button.targetGraphic = face;
        var colors = button.colors; colors.normalColor = Color.white; colors.highlightedColor = new Color(1.35f, 1.35f, 1.35f);
        colors.pressedColor = new Color(.6f, .6f, .6f); colors.fadeDuration = .05f; button.colors = colors;
        var label = Text(face.rectTransform, "Label", caption, Vector2.zero, size, 14f, Ink);
        label.enableAutoSizing = true; label.fontSizeMin = 8f; label.fontSizeMax = 14f;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        button.onClick.AddListener(() => clicked?.Invoke()); return button;
    }
    Camera EventCamera => rootCanvas != null && rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? rootCanvas.worldCamera : null;
}
