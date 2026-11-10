using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(TextMeshProUGUI), typeof(LayoutElement))]
public sealed class OSToolLibraryFrame : MonoBehaviour
{
    TMP_Text border;
    TMP_Text first;
    TMP_Text last;
    Vector2 lastSize;

    public static OSToolLibraryFrame Create(Transform parent)
    {
        GameObject frame = new GameObject("ProgramLibraryAsciiFrame", typeof(RectTransform));
        frame.layer = parent.gameObject.layer;
        frame.transform.SetParent(parent, false);
        frame.transform.SetAsFirstSibling();
        OSToolLibraryFrame controller = frame.AddComponent<OSToolLibraryFrame>();
        frame.GetComponent<LayoutElement>().ignoreLayout = true;
        return controller;
    }

    void Awake()
    {
        border = GetComponent<TMP_Text>();
        border.raycastTarget = false;
        border.richText = true;
        border.textWrappingMode = TextWrappingModes.NoWrap;
        border.overflowMode = TextOverflowModes.Overflow;
        border.alignment = TextAlignmentOptions.TopLeft;
        border.enableAutoSizing = false;
        border.margin = Vector4.zero;
        border.rectTransform.pivot = new Vector2(0f, 1f);
    }

    void OnEnable() => Canvas.willRenderCanvases += RefreshFrame;
    void OnDisable() => Canvas.willRenderCanvases -= RefreshFrame;

    public void Bind(OSToolLineUnit heading, OSToolLineUnit footer)
    {
        first = heading.Label;
        last = footer.Label;
        lastSize = Vector2.zero;
        gameObject.SetActive(true);
    }

    void RefreshFrame()
    {
        if (first == null || last == null || !first.gameObject.activeInHierarchy || !last.gameObject.activeInHierarchy)
            return;

        RectTransform parent = transform.parent as RectTransform;
        Rect firstRect = first.rectTransform.rect;
        Rect lastRect = last.rectTransform.rect;
        Vector3 topLeft = parent.InverseTransformPoint(first.rectTransform.TransformPoint(new Vector2(firstRect.xMin, firstRect.yMax)));
        Vector3 bottomRight = parent.InverseTransformPoint(last.rectTransform.TransformPoint(new Vector2(lastRect.xMax, lastRect.yMin)));
        Vector2 size = new Vector2(bottomRight.x - topLeft.x, topLeft.y - bottomRight.y);
        if (size.x <= 0f || size.y <= 0f)
            return;

        border.rectTransform.localPosition = topLeft;
        bool styleChanged = border.font != first.font || !Mathf.Approximately(border.fontSize, first.fontSize)
            || border.fontSharedMaterial != first.fontSharedMaterial || border.color != first.color;
        if (!styleChanged && (size - lastSize).sqrMagnitude < 0.01f)
            return;

        lastSize = size;
        border.font = first.font;
        border.fontSharedMaterial = first.fontSharedMaterial;
        border.fontSize = first.fontSize;
        border.color = first.color;
        border.rectTransform.sizeDelta = size;

        // A separate fixed-width character grid keeps the outline aligned across localized, wrapped rows.
        float lineHeight = border.GetPreferredValues("|", Mathf.Infinity, Mathf.Infinity).y;
        int columns = Mathf.Max(6, Mathf.FloorToInt(size.x / (border.fontSize * 0.65f)));
        int rows = Mathf.Max(3, Mathf.CeilToInt((size.y - lineHeight) / (border.fontSize * 0.9f)) + 1);
        float cellWidth = size.x / columns;
        float rowHeight = Mathf.Max(1f, (size.y - lineHeight) / (rows - 1));
        StringBuilder text = new StringBuilder(columns * rows + 100);
        text.Append("<mspace=").Append(cellWidth.ToString("0.###", CultureInfo.InvariantCulture))
            .Append("><line-height=").Append(rowHeight.ToString("0.###", CultureInfo.InvariantCulture)).Append('>');
        AppendCap(text, columns, '.', '.');
        string interior = new string(' ', columns - 2);
        for (int row = 1; row < rows - 1; row++)
        {
            char rail = row % 7 == 3 ? '*' : '|';
            text.Append('\n').Append(rail).Append(interior).Append(rail);
        }
        text.Append('\n');
        AppendCap(text, columns, '\'', '\'');
        text.Append("</line-height></mspace>");
        border.SetText(text.ToString());
        border.ForceMeshUpdate();
    }

    static void AppendCap(StringBuilder text, int columns, char left, char right)
    {
        const string trim = "---*~*---";
        text.Append(left);
        for (int i = 0; i < columns - 2; i++)
            text.Append(trim[i % trim.Length]);
        text.Append(right);
    }
}
