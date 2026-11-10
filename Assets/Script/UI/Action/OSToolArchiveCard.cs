using TMPro;
using UnityEngine;

/// <summary>The first-use archive occupies one row of the existing terminal.</summary>
public sealed class OSToolArchiveCard : MonoBehaviour
{
    static readonly Color Ink = new Color32(135, 130, 126, 255);
    TMP_Text filename;
    TMP_Text description;
    TMP_Text status;
    TMP_Text percentage;
    RectTransform panel;
    RectTransform bar;
    RectTransform fill;
    RectTransform bottomBorder;
    RectTransform leftBorder;
    RectTransform rightBorder;
    UnityEngine.UI.LayoutElement rowLayout;
    UnityEngine.UI.Image hitArea;
    bool unpacking;
    int percent = -1;

    /// <summary>Clicking anywhere on the waiting card fills in the unpack command.</summary>
    public UnityEngine.UI.Graphic HitArea => hitArea;
    public UnityEngine.UI.Graphic[] Inks => new UnityEngine.UI.Graphic[] { filename, description, status };
    public bool IsWaiting => !unpacking;

    public static OSToolArchiveCard Create(RectTransform parent, TMP_Text template)
    {
        var row = new GameObject("MemoryArchive", typeof(RectTransform), typeof(UnityEngine.UI.LayoutElement));
        row.layer = parent.gameObject.layer;
        row.transform.SetParent(parent, false);
        var layout = row.GetComponent<UnityEngine.UI.LayoutElement>();
        layout.minHeight = layout.preferredHeight = 76f;
        layout.flexibleHeight = 0f;
        var card = row.AddComponent<OSToolArchiveCard>();
        card.Build(template);
        return card;
    }

    void Build(TMP_Text template)
    {
        rowLayout = GetComponent<UnityEngine.UI.LayoutElement>();
        panel = Rect("ArchiveCard", transform, 0, 6, 250, 64);
        hitArea = Box("HitArea", panel, 0, 0, 250, 64, Color.clear).GetComponent<UnityEngine.UI.Image>();
        Box("Top", panel, 0, 0, 250, 1, Ink);
        bottomBorder = Box("Bottom", panel, 0, 63, 250, 1, Ink);
        leftBorder = Box("Left", panel, 0, 1, 1, 62, Ink);
        rightBorder = Box("Right", panel, 249, 1, 1, 62, Ink);

        // A folded file and zipper, drawn on the same integer pixel grid as the UI.
        var icon = Rect("ZipIcon", panel, 10, 10, 23, 28);
        Box("FileLeft", icon, 0, 0, 1, 28, Ink);
        Box("FileTop", icon, 1, 0, 15, 1, Ink);
        Box("FileBottom", icon, 1, 27, 22, 1, Ink);
        Box("FileRight", icon, 22, 7, 1, 20, Ink);
        Box("FoldLeft", icon, 15, 0, 1, 8, Ink);
        Box("FoldBottom", icon, 16, 7, 6, 1, Ink);
        for (int i = 0; i < 7; i++)
            Box("FoldStep" + i, icon, 16 + i, i, 1, 1, Ink);
        for (int i = 0; i < 7; i++)
            Box("ZipTooth" + i, icon, 8 + i % 2, 3 + i * 2, 2, 1, Ink);
        Box("ZipPullTop", icon, 8, 18, 4, 1, Ink);
        Box("ZipPullLeft", icon, 8, 19, 1, 4, Ink);
        Box("ZipPullRight", icon, 11, 19, 1, 4, Ink);
        Box("ZipPullBottom", icon, 8, 23, 4, 1, Ink);

        filename = Label("Filename", template, 43, 8, 100, 15, 10);
        filename.text = "memory.zip";
        description = Label("Description", template, 43, 24, 197, 12, 8);
        status = Label("Status", template, 143, 8, 97, 15, 8);
        status.alignment = TextAlignmentOptions.MidlineRight;
        percentage = Label("Percentage", template, 212, 43, 28, 15, 8);
        percentage.alignment = TextAlignmentOptions.Right;
        bar = Rect("ProgressBar", panel, 10, 55, 196, 3);
        Box("Track", bar, 0, 0, 196, 3, new Color(Ink.r, Ink.g, Ink.b, 0.22f));
        fill = Box("Fill", bar, 0, 0, 0, 3, Ink);
        RefreshLanguage();
    }

    public void Present(bool active, float progress)
    {
        int next = Mathf.Clamp(Mathf.FloorToInt(progress * 100f), 0, 100);
        bool textChanged = unpacking != active || (next == 100) != (percent == 100);
        unpacking = active;
        float height = active ? 64f : 48f;
        panel.sizeDelta = new Vector2(250f, height);
        hitArea.rectTransform.sizeDelta = new Vector2(250f, height);
        bottomBorder.anchoredPosition = new Vector2(0f, 1f - height);
        leftBorder.sizeDelta = rightBorder.sizeDelta = new Vector2(1f, height - 2f);
        rowLayout.minHeight = rowLayout.preferredHeight = height + 12f;
        bar.gameObject.SetActive(active);
        percentage.gameObject.SetActive(active);
        if (percent != next)
        {
            percent = next;
            percentage.SetText(percent + "%");
            fill.sizeDelta = new Vector2(Mathf.Floor(196f * next / 100f), 3f);
        }
        if (textChanged)
            RefreshLanguage();
    }

    public void RefreshLanguage()
    {
        description.text = OSToolProgramLibrary.Localize("ui.os.tool.archive.description", "Memory program archive");
        string state = !unpacking ? "waiting" : percent >= 100 ? "complete" : "unpacking";
        string fallback = !unpacking ? "Waiting to unpack" : percent >= 100 ? "Unpacked" : "Unpacking";
        status.text = OSToolProgramLibrary.Localize("ui.os.tool.archive." + state, fallback);
        OSLocalizedFontUtility.ApplyCurrent(filename);
        OSLocalizedFontUtility.ApplyCurrent(description);
        OSLocalizedFontUtility.ApplyCurrent(status);
        OSLocalizedFontUtility.ApplyCurrent(percentage);
    }

    TMP_Text Label(string name, TMP_Text template, float x, float y, float w, float h, float size)
    {
        RectTransform rect = Rect(name, panel, x, y, w, h);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = template.font;
        text.fontSharedMaterial = template.fontSharedMaterial;
        text.fontSize = size;
        text.color = Ink;
        text.raycastTarget = false;
        text.richText = false;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        return text;
    }

    static RectTransform Rect(string name, Transform parent, float x, float y, float w, float h)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(w, h);
        return rect;
    }

    static RectTransform Box(string name, Transform parent, float x, float y, float w, float h, Color color)
    {
        RectTransform rect = Rect(name, parent, x, y, w, h);
        var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
        image.color = color;
        image.raycastTarget = false;
        return rect;
    }
}
