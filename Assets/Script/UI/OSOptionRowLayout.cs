using TMPro;
using UnityEngine;

// Existing option parents lay out fixed-height rows; let translated rows grow
// before their parent measures them, while preserving their authored minimum.
[DisallowMultipleComponent]
public sealed class OSOptionRowLayout : MonoBehaviour
{
    TMP_Text label;
    RectTransform row;
    float minimumHeight;
    float measuredWidth = -1f;
    float measuredFontSize;
    string measuredText;
    TMP_FontAsset measuredFont;
    Vector4 measuredMargin;

    public static void Bind(Component owner, TMP_Text text)
    {
        if (owner == null || text == null) return;
        var layout = owner.GetComponent<OSOptionRowLayout>();
        if (layout == null) layout = owner.gameObject.AddComponent<OSOptionRowLayout>();
        layout.label = text;
        if (layout.row == null)
        {
            layout.row = owner.transform as RectTransform;
            layout.minimumHeight = layout.row != null ? layout.row.rect.height : 14f;
        }
        layout.measuredWidth = -1f;
        layout.Refresh();
    }

    void LateUpdate() => Refresh();

    void Refresh()
    {
        if (label == null || row == null || !label.isActiveAndEnabled) return;
        float width = label.rectTransform.rect.width;
        if (width <= 0f || (Mathf.Approximately(measuredWidth, width)
            && measuredText == label.text && measuredFont == label.font
            && Mathf.Approximately(measuredFontSize, label.fontSize)
            && measuredMargin == label.margin)) return;
        measuredWidth = width;
        measuredText = label.text;
        measuredFont = label.font;
        measuredFontSize = label.fontSize;
        measuredMargin = label.margin;
        // Measure the actual label after parsing. TMP's string/width overload can
        // undercount a wrapped <nobr> sprite badge after its width calculation.
        label.ForceMeshUpdate();
        float height = Mathf.Max(minimumHeight, Mathf.Ceil(label.preferredHeight) + 4f);
        if (Mathf.Abs(row.rect.height - height) < .01f) return;
        row.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        if (row.parent is RectTransform parent)
            UnityEngine.UI.LayoutRebuilder.MarkLayoutForRebuild(parent);
    }
}
