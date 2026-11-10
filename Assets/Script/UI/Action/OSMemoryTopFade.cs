using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// When memory text has scrolled past the top of the mask, the cut line fades
/// out through three dithered black bands instead of being sliced in half.
/// Lives as the last child of MemoryMask; never blocks raycasts.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)] // After the memory window aligns its text pool.
public sealed class OSMemoryTopFade : MonoBehaviour
{
    const float BandHeight = 4f;
    static readonly int[] BandLevels = { 12, 8, 4 };

    RectTransform mask;
    UnityEngine.UI.RectMask2D rectMask;
    RectTransform pool;
    readonly List<TMP_Text> labels = new List<TMP_Text>(10);
    readonly RectTransform[] bands = new RectTransform[BandLevels.Length];

    public bool IsVisible { get; private set; }
    public float FadeHeight { get; private set; }

    public static OSMemoryTopFade Ensure(RectTransform mask, UnityEngine.UI.RectMask2D rectMask, RectTransform pool)
    {
        var existing = mask.GetComponentInChildren<OSMemoryTopFade>(true);
        if (existing != null) { existing.Bind(rectMask, pool); return existing; }

        var node = new GameObject("TopFade", typeof(RectTransform));
        node.layer = mask.gameObject.layer;
        var rect = (RectTransform)node.transform;
        rect.SetParent(mask, false);
        rect.anchorMin = new Vector2(0f, 1f); rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(.5f, 1f);
        rect.sizeDelta = new Vector2(0f, BandHeight * BandLevels.Length);
        var fade = node.AddComponent<OSMemoryTopFade>();
        fade.mask = mask;
        fade.Bind(rectMask, pool);
        for (int i = 0; i < BandLevels.Length; i++)
        {
            var band = new GameObject("Band" + i, typeof(RectTransform));
            band.layer = node.layer;
            var bandRect = (RectTransform)band.transform;
            bandRect.SetParent(rect, false);
            bandRect.anchorMin = new Vector2(0f, 1f); bandRect.anchorMax = Vector2.one;
            bandRect.pivot = new Vector2(.5f, 1f);
            bandRect.sizeDelta = new Vector2(0f, BandHeight);
            bandRect.anchoredPosition = new Vector2(0f, -BandHeight * i);
            OSDitherFill.Create("Dither", bandRect, Color.black).SetLevel(BandLevels[i]);
            fade.bands[i] = bandRect;
            band.SetActive(false);
        }
        return fade;
    }

    void Bind(UnityEngine.UI.RectMask2D newRectMask, RectTransform newPool)
    {
        rectMask = newRectMask;
        pool = newPool;
        if (mask == null) mask = transform.parent as RectTransform;
        for (int i = 0; i < bands.Length && i < transform.childCount; i++)
            bands[i] = transform.GetChild(i) as RectTransform;
    }

    void OnDisable() => SetVisible(false);

    void LateUpdate()
    {
        if (mask == null || rectMask == null || !rectMask.isActiveAndEnabled
            || pool == null || !pool.gameObject.activeInHierarchy)
        {
            SetVisible(false);
            return;
        }
        if (transform.GetSiblingIndex() != mask.childCount - 1)
            transform.SetAsLastSibling();

        Vector4 padding = rectMask.padding;
        float left = Mathf.Max(0f, padding.x);
        float right = Mathf.Max(0f, padding.z);
        float top = Mathf.Max(0f, padding.w);
        Rect visible = Rect.MinMaxRect(mask.rect.xMin + left,
            mask.rect.yMin + Mathf.Max(0f, padding.y), mask.rect.xMax - right, mask.rect.yMax - top);
        float remainingLineHeight = FindCutLineHeight(visible);
        bool clipped = remainingLineHeight > .01f && visible.width > 0f && visible.height > 0f;
        FadeHeight = clipped ? Mathf.Min(BandHeight * BandLevels.Length, Mathf.Min(remainingLineHeight, visible.height)) : 0f;

        var rect = (RectTransform)transform;
        rect.anchoredPosition = new Vector2((left - right) * .5f, -top);
        rect.sizeDelta = new Vector2(-left - right, FadeHeight);
        if (clipped)
        {
            // All three steps end at the cut line's last glyph; a short visible
            // fragment must never darken the next complete line below it.
            float height = FadeHeight / bands.Length;
            for (int i = 0; i < bands.Length; i++)
            {
                if (bands[i] == null) continue;
                bands[i].sizeDelta = new Vector2(0f, height);
                bands[i].anchoredPosition = new Vector2(0f, -height * i);
            }
        }
        SetVisible(clipped);
    }

    float FindCutLineHeight(Rect visible)
    {
        pool.GetComponentsInChildren(false, labels);
        foreach (TMP_Text label in labels)
        {
            if (!label.isActiveAndEnabled || !label.maskable || string.IsNullOrEmpty(label.text)) continue;
            // Typing and a resize can change the mesh after the previous canvas
            // pass. Read current glyphs without changing the text pool layout.
            if (label.havePropertiesChanged || label.textInfo.characterCount == 0)
                label.ForceMeshUpdate();
            TMP_TextInfo info = label.textInfo;
            Matrix4x4 toMask = mask.worldToLocalMatrix * label.transform.localToWorldMatrix;
            int characterLimit = Mathf.Min(info.characterCount, label.maxVisibleCharacters);
            int lineLimit = Mathf.Min(info.lineCount, label.maxVisibleLines);
            for (int lineIndex = 0; lineIndex < lineLimit; lineIndex++)
            {
                TMP_LineInfo line = info.lineInfo[lineIndex];
                float minX = float.PositiveInfinity, minY = float.PositiveInfinity;
                float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
                int end = Mathf.Min(line.lastCharacterIndex + 1, characterLimit);
                for (int c = Mathf.Max(line.firstCharacterIndex, label.firstVisibleCharacter); c < end; c++)
                {
                    TMP_CharacterInfo glyph = info.characterInfo[c];
                    if (!glyph.isVisible || (label.overflowMode == TextOverflowModes.Page
                        && glyph.pageNumber + 1 != label.pageToDisplay)) continue;
                    Encapsulate(toMask.MultiplyPoint3x4(glyph.bottomLeft), ref minX, ref minY, ref maxX, ref maxY);
                    Encapsulate(toMask.MultiplyPoint3x4(glyph.topLeft), ref minX, ref minY, ref maxX, ref maxY);
                    Encapsulate(toMask.MultiplyPoint3x4(glyph.topRight), ref minX, ref minY, ref maxX, ref maxY);
                    Encapsulate(toMask.MultiplyPoint3x4(glyph.bottomRight), ref minX, ref minY, ref maxX, ref maxY);
                }
                // A fully hidden old line or the gap between lines isn't a cut.
                if (maxY > visible.yMax + .01f && minY < visible.yMax - .01f
                    && maxX > visible.xMin && minX < visible.xMax)
                    return visible.yMax - minY;
            }
        }
        return 0f;
    }

    static void Encapsulate(Vector3 point, ref float minX, ref float minY, ref float maxX, ref float maxY)
    {
        minX = Mathf.Min(minX, point.x); minY = Mathf.Min(minY, point.y);
        maxX = Mathf.Max(maxX, point.x); maxY = Mathf.Max(maxY, point.y);
    }

    void SetVisible(bool visible)
    {
        IsVisible = visible;
        if (!visible) FadeHeight = 0f;
        foreach (RectTransform band in bands)
            if (band != null && band.gameObject.activeSelf != visible) band.gameObject.SetActive(visible);
    }
}
