using UnityEngine;

public sealed partial class OSLinkMessageController
{
    const float OptionCountdownItemHeight = 4f;
    RectTransform optionCountdownVisualRoot;
    UnityEngine.UI.Image optionCountdownTrack;
    UnityEngine.UI.Image optionCountdownFill;
    float optionCountdownFillWidth = -1f;

    // The caller owns timing and visibility. This only draws the remaining time
    // below the options, without rebuilding the message layout on every tick.
    public void SetOptionCountdownProgress(float remaining01, bool visible)
    {
        if (!visible)
        {
            if (optionCountdownVisualRoot != null && optionCountdownVisualRoot.gameObject.activeSelf)
            {
                optionCountdownVisualRoot.gameObject.SetActive(false);
                windowLayout?.RefreshLayout();
            }
            return;
        }

        if (!EnsureOptionCountdownVisual()) return;
        bool layoutChanged = !optionCountdownVisualRoot.gameObject.activeSelf;
        if (layoutChanged) optionCountdownVisualRoot.gameObject.SetActive(true);
        if (optionCountdownVisualRoot.GetSiblingIndex() != buttonArea.childCount - 1)
        {
            optionCountdownVisualRoot.SetAsLastSibling();
            layoutChanged = true;
        }
        if (layoutChanged) windowLayout?.RefreshLayout();

        Color ink = textWithOptionsLabel != null ? textWithOptionsLabel.color
            : textLabel != null ? textLabel.color : (Color)new Color32(0x87, 0x82, 0x7e, 0xff);
        if (optionCountdownFill.color != ink) optionCountdownFill.color = ink;
        Color trackInk = ink;
        trackInk.a *= .25f;
        if (optionCountdownTrack.color != trackInk) optionCountdownTrack.color = trackInk;

        // Keep the left edge fixed and retract the right edge in logical pixels.
        float progress = float.IsNaN(remaining01) ? 0f : Mathf.Clamp01(remaining01);
        float width = Mathf.Round(Mathf.Max(0f, optionCountdownTrack.rectTransform.rect.width) * progress);
        if (width != optionCountdownFillWidth)
        {
            optionCountdownFillWidth = width;
            optionCountdownFill.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        }
        optionCountdownFill.enabled = width > 0f;
    }

    bool EnsureOptionCountdownVisual()
    {
        if (optionCountdownVisualRoot != null) return true;
        if (buttonArea == null) return false;

        var item = new GameObject("OptionCountdown", typeof(RectTransform), typeof(UnityEngine.UI.LayoutElement));
        item.layer = buttonArea.gameObject.layer;
        item.SetActive(false);
        optionCountdownVisualRoot = (RectTransform)item.transform;
        optionCountdownVisualRoot.SetParent(buttonArea, false);
        optionCountdownVisualRoot.anchorMin = new Vector2(0f, .5f);
        optionCountdownVisualRoot.anchorMax = new Vector2(1f, .5f);
        optionCountdownVisualRoot.pivot = new Vector2(.5f, .5f);
        optionCountdownVisualRoot.sizeDelta = new Vector2(0f, OptionCountdownItemHeight);
        var layout = item.GetComponent<UnityEngine.UI.LayoutElement>();
        layout.minHeight = layout.preferredHeight = OptionCountdownItemHeight;
        layout.flexibleHeight = 0f;

        optionCountdownTrack = CreateOptionCountdownLine("Track", optionCountdownVisualRoot);
        var trackRect = optionCountdownTrack.rectTransform;
        trackRect.anchorMin = new Vector2(0f, 0f);
        trackRect.anchorMax = new Vector2(1f, 0f);
        trackRect.pivot = Vector2.zero;
        trackRect.anchoredPosition = new Vector2(0f, 1f);
        trackRect.sizeDelta = new Vector2(0f, 1f);

        optionCountdownFill = CreateOptionCountdownLine("Remaining", trackRect);
        var fillRect = optionCountdownFill.rectTransform;
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = new Vector2(0f, 1f);
        fillRect.pivot = new Vector2(0f, .5f);
        fillRect.anchoredPosition = Vector2.zero;
        fillRect.sizeDelta = Vector2.zero;
        optionCountdownFill.enabled = false;
        optionCountdownFillWidth = -1f;
        return true;
    }

    static UnityEngine.UI.Image CreateOptionCountdownLine(string name, RectTransform parent)
    {
        var node = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image));
        node.layer = parent.gameObject.layer;
        node.transform.SetParent(parent, false);
        var line = node.GetComponent<UnityEngine.UI.Image>();
        line.raycastTarget = false;
        line.type = UnityEngine.UI.Image.Type.Simple;
        return line;
    }
}
