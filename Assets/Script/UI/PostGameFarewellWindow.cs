using System;
using System.Linq;
using MrTGame.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>The next-launch Live message and its complete, locally available letter.</summary>
public sealed class PostGameFarewellWindow : MonoBehaviour
{
    // Above the OS windows, below the player's foreground cursor (1500).
    public const int SortingOrder = 1400;
    public static PostGameFarewellWindow Current { get; private set; }
    public UnityEngine.UI.ScrollRect Scroll { get; private set; }
    public UnityEngine.UI.Button CloseButton { get; private set; }
    public UnityEngine.UI.Button AttachmentButton { get; private set; }
    public bool IsAttachmentExpanded { get; private set; }
    public TMP_Text BodyLabel { get; private set; }
    public TMP_Text LetterBeforeCatLabel { get; private set; }
    public TMP_Text CatLabel { get; private set; }
    public TMP_Text LetterAfterCatLabel { get; private set; }
    public TMP_Text AttachmentLabel { get; private set; }
    readonly Color ink = new Color32(203, 198, 190, 255);
    TMP_FontAsset baseFont;
    Material baseMaterial;
    TMP_Text closeLabel;
    RectTransform content, letterContent;
    GameObject attachmentPlusStroke;
    GameObject previousSelection;
    Action closed;
    bool closing;

    public static PostGameFarewellWindow Show(Transform parent, Action onClosed = null, Transform fontSource = null)
    {
        if (Current != null) return Current;
        if (parent == null || !PostGameFarewellContent.IsReady()) return null;
        GameObject node = null;
        try
        {
            node = new GameObject("PostGameFarewellWindow", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasGroup), typeof(UnityEngine.UI.GraphicRaycaster));
            node.layer = parent.gameObject.layer;
            node.transform.SetParent(parent, false);
            var window = node.AddComponent<PostGameFarewellWindow>();
            window.closed = onClosed;
            window.Build(fontSource != null ? fontSource : parent);
            Current = window;
            return window;
        }
        catch (Exception error)
        {
            if (node != null) Destroy(node);
            Debug.LogWarning("[Farewell] Could not open the letter window: " + error.Message);
            return null;
        }
    }

    void Build(Transform parent)
    {
        var template = parent.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(label => label.font != null);
        var authored = parent.GetComponentsInChildren<OSLocalizedFontSwitcher>(true)
            .FirstOrDefault(switcher => switcher.SimplifiedChineseFont != null);
        baseFont = authored != null ? authored.SimplifiedChineseFont
            : template != null ? OSLocalizedFontUtility.GetAuthoringFont(template) : TMP_Settings.defaultFontAsset;
        baseMaterial = authored != null ? baseFont.material
            : template != null ? OSLocalizedFontUtility.GetAuthoringMaterial(template) : baseFont.material;
        var canvas = GetComponent<Canvas>();
        canvas.overrideSorting = true; canvas.sortingOrder = SortingOrder; canvas.pixelPerfect = true;
        GetComponent<CanvasGroup>().ignoreParentGroups = true;
        Stretch((RectTransform)transform, Vector2.zero, Vector2.zero);
        Image((RectTransform)transform, new Color(0f, 0f, 0f, .82f), true);

        var border = Rect("Border", transform);
        border.anchorMin = border.anchorMax = border.pivot = new Vector2(.5f, .5f);
        border.sizeDelta = new Vector2(544f, 316f);
        Image(border, ink, true);
        var panel = Rect("Panel", border); Stretch(panel, Vector2.one, -Vector2.one);
        Image(panel, Color.black, true);
        var heading = Text("Live", panel, 14f); heading.text = PostGameFarewellContent.WindowTitle;
        Top(heading.rectTransform, 14f, 7f, 470f, 23f);

        var scrollRoot = Rect("ScrollView", panel); Top(scrollRoot, 14f, 43f, 515f, 224f);
        Scroll = scrollRoot.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
        Scroll.horizontal = false; Scroll.vertical = true;
        Scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
        Scroll.inertia = false; Scroll.scrollSensitivity = 22f;
        var viewport = Rect("Viewport", scrollRoot); Stretch(viewport, Vector2.zero, new Vector2(-12f, 0f));
        Image(viewport, Color.black, true);
        viewport.gameObject.AddComponent<UnityEngine.UI.Mask>().showMaskGraphic = false;
        content = Rect("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f); content.anchorMax = Vector2.one;
        content.pivot = new Vector2(.5f, 1f); content.sizeDelta = Vector2.zero;
        var layout = content.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
        layout.spacing = 8f; layout.padding = new RectOffset(2, 7, 0, 8);
        var fitter = content.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
        fitter.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
        Scroll.viewport = viewport; Scroll.content = content;

        BodyLabel = Text("Message", content, 11f);
        var divider = Rect("AttachmentRule", content); Image(divider, ink);
        var dividerSize = divider.gameObject.AddComponent<UnityEngine.UI.LayoutElement>(); dividerSize.preferredHeight = 1f;
        var attachmentRow = Rect("AttachmentButton", content);
        var attachmentImage = Image(attachmentRow, Color.white, true);
        AttachmentButton = attachmentRow.gameObject.AddComponent<UnityEngine.UI.Button>();
        AttachmentButton.targetGraphic = attachmentImage;
        AttachmentButton.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
        var attachmentColors = AttachmentButton.colors;
        attachmentColors.normalColor = new Color32(35, 33, 30, 255);
        attachmentColors.highlightedColor = attachmentColors.selectedColor = new Color32(61, 57, 51, 255);
        attachmentColors.pressedColor = new Color32(77, 72, 64, 255);
        AttachmentButton.colors = attachmentColors;
        AttachmentButton.onClick.AddListener(ToggleAttachment);
        attachmentRow.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().minHeight = 28f;
        var rowLayout = attachmentRow.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        rowLayout.padding = new RectOffset(8, 8, 6, 6); rowLayout.spacing = 6f;
        rowLayout.childAlignment = TextAnchor.MiddleLeft;
        rowLayout.childControlWidth = rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = rowLayout.childForceExpandHeight = false;
        var icon = Rect("DisclosureIcon", attachmentRow);
        var iconLayout = icon.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
        iconLayout.minWidth = iconLayout.preferredWidth = 10f;
        iconLayout.minHeight = iconLayout.preferredHeight = 10f;
        var minus = Rect("Minus", icon); Top(minus, 2f, 4.5f, 6f, 1f); Image(minus, ink);
        var plus = Rect("Plus", icon); Top(plus, 4.5f, 2f, 1f, 6f); Image(plus, ink);
        attachmentPlusStroke = plus.gameObject;
        AttachmentLabel = Text("Attachment", attachmentRow, 12f);
        AttachmentLabel.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = 1f;

        letterContent = Rect("LetterContent", content);
        var letterLayout = letterContent.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
        letterLayout.childControlWidth = letterLayout.childControlHeight = true;
        letterLayout.childForceExpandWidth = true; letterLayout.childForceExpandHeight = false;
        letterLayout.spacing = 8f;
        LetterBeforeCatLabel = Text("LetterBeforeCat", letterContent, 11f);
        CatLabel = Text("WinkingCat", letterContent, 12f);
        CatLabel.richText = true;
        LetterAfterCatLabel = Text("LetterAfterCat", letterContent, 11f);
        letterContent.gameObject.SetActive(false);

        var rail = Rect("Scrollbar", scrollRoot);
        rail.anchorMin = new Vector2(1f, 0f); rail.anchorMax = Vector2.one;
        rail.pivot = new Vector2(1f, .5f); rail.sizeDelta = new Vector2(5f, 0f);
        Image(rail, new Color32(55, 52, 49, 255), true);
        var thumb = Rect("Handle", rail); Stretch(thumb, Vector2.zero, Vector2.zero);
        var thumbImage = Image(thumb, ink, true);
        var scrollbar = rail.gameObject.AddComponent<UnityEngine.UI.Scrollbar>();
        scrollbar.direction = UnityEngine.UI.Scrollbar.Direction.BottomToTop;
        scrollbar.handleRect = thumb; scrollbar.targetGraphic = thumbImage;
        scrollbar.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
        Scroll.verticalScrollbar = scrollbar;

        var buttonRect = Rect("Close", panel); Top(buttonRect, 386f, 286f, 142f, 21f);
        var buttonImage = Image(buttonRect, ink, true);
        CloseButton = buttonRect.gameObject.AddComponent<UnityEngine.UI.Button>();
        CloseButton.targetGraphic = buttonImage;
        CloseButton.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
        CloseButton.onClick.AddListener(Close);
        closeLabel = Text("Text", buttonRect, 11f);
        Stretch(closeLabel.rectTransform, new Vector2(4f, 1f), new Vector2(-4f, -1f));
        closeLabel.color = Color.black; closeLabel.alignment = TextAlignmentOptions.Center;
        previousSelection = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        RefreshLanguage();
        GameEvents.LanguageChanged += LanguageChanged;
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(CloseButton.gameObject);
    }

    void LanguageChanged(LanguageState language) => RefreshLanguage();

    public void ToggleAttachment()
    {
        if (closing || letterContent == null) return;
        float offset = Mathf.Max(0f, content.anchoredPosition.y);
        IsAttachmentExpanded = !IsAttachmentExpanded;
        letterContent.gameObject.SetActive(IsAttachmentExpanded);
        attachmentPlusStroke.SetActive(!IsAttachmentExpanded);
        Canvas.ForceUpdateCanvases();
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        float hiddenHeight = Mathf.Max(0f, content.rect.height - Scroll.viewport.rect.height);
        if (IsAttachmentExpanded)
        {
            // Bring the attachment header and opening lines into view instead
            // of expanding an entire letter below the bottom of the viewport.
            var headerBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(content, AttachmentButton.transform);
            offset = Mathf.Max(0f, content.rect.yMax - headerBounds.max.y);
        }
        Scroll.StopMovement();
        Scroll.verticalNormalizedPosition = hiddenHeight > 0f ? 1f - Mathf.Clamp01(offset / hiddenHeight) : 1f;
    }

    public void RefreshLanguage()
    {
        string letter = PostGameFarewellContent.Letter;
        int cat = letter.IndexOf(PostGameFarewellContent.Cat, StringComparison.Ordinal);
        if (cat < 0) throw new InvalidOperationException("The authored ASCII cat is missing.");
        BodyLabel.text = PostGameFarewellContent.ReturnMessage;
        AttachmentLabel.text = PostGameFarewellContent.Text("ui.attachment") + "  " + PostGameFarewellContent.FileName;
        // The exact letter stays available in Content.Letter. Only this rendered
        // ASCII span receives fixed character advances; exported text has no tags.
        LetterBeforeCatLabel.text = letter.Substring(0, cat);
        CatLabel.text = "<mspace=7>" + PostGameFarewellContent.Cat + "</mspace>";
        LetterAfterCatLabel.text = letter.Substring(cat + PostGameFarewellContent.Cat.Length);
        closeLabel.text = PostGameFarewellContent.Text("ui.close");
        foreach (var label in GetComponentsInChildren<TMP_Text>(true)) OSLocalizedFontUtility.ApplyCurrent(label);
        Canvas.ForceUpdateCanvases();
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        Scroll.StopMovement(); Scroll.verticalNormalizedPosition = 1f;
    }

    void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null || Scroll == null) return;
        float distance = Mathf.Max(1f, content.rect.height - Scroll.viewport.rect.height);
        if (keyboard.pageDownKey.wasPressedThisFrame)
            Scroll.verticalNormalizedPosition = Mathf.Clamp01(Scroll.verticalNormalizedPosition - Scroll.viewport.rect.height * .85f / distance);
        if (keyboard.pageUpKey.wasPressedThisFrame)
            Scroll.verticalNormalizedPosition = Mathf.Clamp01(Scroll.verticalNormalizedPosition + Scroll.viewport.rect.height * .85f / distance);
    }

    public void Close()
    {
        if (closing) return;
        closing = true;
        try { closed?.Invoke(); }
        finally { Destroy(gameObject); }
    }

    void OnDestroy()
    {
        GameEvents.LanguageChanged -= LanguageChanged;
        if (Current == this) Current = null;
        if (EventSystem.current != null && previousSelection != null && previousSelection.activeInHierarchy)
            EventSystem.current.SetSelectedGameObject(previousSelection);
    }

    TMP_Text Text(string name, Transform parent, float size)
    {
        var rect = Rect(name, parent);
        var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.font = baseFont; label.fontSharedMaterial = baseMaterial;
        label.fontSize = size; label.color = ink; label.raycastTarget = false;
        label.textWrappingMode = TextWrappingModes.Normal; label.overflowMode = TextOverflowModes.Overflow;
        label.richText = false; label.paragraphSpacing = 3f;
        return label;
    }

    static RectTransform Rect(string name, Transform parent)
    {
        var node = new GameObject(name, typeof(RectTransform)); node.layer = parent.gameObject.layer;
        var rect = (RectTransform)node.transform; rect.SetParent(parent, false); return rect;
    }
    static UnityEngine.UI.Image Image(RectTransform rect, Color color, bool raycast = false)
    {
        var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>(); image.color = color; image.raycastTarget = raycast; return image;
    }
    static void Stretch(RectTransform rect, Vector2 min, Vector2 max)
    { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = min; rect.offsetMax = max; }
    static void Top(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f); rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height);
    }
}
