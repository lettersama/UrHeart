using System.Collections.Generic;
using MrTGame.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Round 11i: level text in the game's own TMP font (the Live chat's font and
/// material): the green lines after 「妈妈」, Live's lines, the reply buttons and
/// the postcard hint. The atlas-drawn letters at these sizes read blurry; the
/// flying perspective words stay drawn on the layer.
///
/// The labels sit on a canvas just above the pixel layer, at whole OS pixels.
/// Above them a second 640 x 360 point texture, the mask, lays Bayer black over
/// them where the layer's dithering must reach them too: the sinking black, the
/// words' fade-out, the hint's breathing.
/// </summary>
public sealed partial class OSChapter04FinalHealPresentation
{
    const int TextOrder = SortingOrder + 2, MaskOrder = SortingOrder + 4;
    RectTransform textRoot, maskRoot;
    Texture2D maskTexture;
    readonly Color32[] mask = new Color32[Width * Height];
    bool maskUsed, maskWasUsed;
    TMP_Text titleLabel, lineLabel, hintLabel;
    readonly TMP_Text[] optionLabels = new TMP_Text[3];
    readonly Dictionary<Answer, TMP_Text> answerLabels = new Dictionary<Answer, TMP_Text>();
    readonly List<Answer> staleAnswerLabels = new List<Answer>();

    void BuildTextLayers(Transform parent)
    {
        textRoot = Layer("Chapter04FinalHealText", parent, TextOrder);
        maskRoot = Layer("Chapter04FinalHealMask", parent, MaskOrder);
        var raw = maskRoot.gameObject.AddComponent<RawImage>();
        raw.raycastTarget = false;
        maskTexture = new Texture2D(Width, Height, TextureFormat.RGBA32, false)
        { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave, name = "Chapter04FinalHealMask" };
        maskTexture.SetPixels32(mask); maskTexture.Apply(false);
        raw.texture = maskTexture;
        titleLabel = Label("Title", 9f, Light, false);
        lineLabel = Label("Line", 11f, Light, true);
        for (int i = 0; i < optionLabels.Length; i++) optionLabels[i] = Label("Option" + i, 10f, Light, true);
        hintLabel = Label("PostcardHint", 9f, Light, false);
        hintLabel.alignment = TextAlignmentOptions.Top;
        foreach (var label in optionLabels) label.alignment = TextAlignmentOptions.Top;
        HideTexts();
    }

    /// <summary>A full-screen 640 x 360 child of the OS canvas, on its own sorting order.</summary>
    RectTransform Layer(string name, Transform parent, int order)
    {
        var node = new GameObject(name, typeof(RectTransform));
        node.layer = parent.gameObject.layer;
        var rect = (RectTransform)node.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = Vector2.zero; rect.sizeDelta = new Vector2(Width, Height);
        var canvas = node.AddComponent<Canvas>();
        canvas.overrideSorting = true; canvas.sortingOrder = order;
        return rect;
    }

    TMP_Text Label(string name, float size, Color32 color, bool wrap)
    {
        var node = new GameObject(name, typeof(RectTransform));
        node.layer = textRoot.gameObject.layer;
        var rect = (RectTransform)node.transform;
        rect.SetParent(textRoot, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        var text = node.AddComponent<TextMeshProUGUI>();
        MatchChatFont(text);
        text.fontSize = size; text.color = color; text.raycastTarget = false;
        text.enableWordWrapping = wrap; text.overflowMode = TextOverflowModes.Overflow;
        text.margin = Vector4.zero;
        return text;
    }

    /// <summary>
    /// Round 11k: a label takes the font and material the Live chat shows now
    /// (the game swaps it per language, e.g. to the Korean font). Done at creation,
    /// on every language change, and checked each frame.
    /// </summary>
    static bool MatchChatFont(TMP_Text label)
    {
        var template = OS_Handle.Instance?.LinkMessageController?.DisplayedTextLabel;
        if (label == null || template == null || template.font == null) return false;
        bool changed = false;
        if (label.font != template.font) { label.font = template.font; changed = true; }
        if (template.fontSharedMaterial != null && label.fontSharedMaterial != template.fontSharedMaterial)
        { label.fontSharedMaterial = template.fontSharedMaterial; changed = true; }
        return changed;
    }

    void SyncLabelFonts()
    {
        MatchChatFont(titleLabel); MatchChatFont(lineLabel); MatchChatFont(hintLabel);
        foreach (var label in optionLabels) MatchChatFont(label);
        bool answersChanged = false;
        foreach (var label in answerLabels.Values) answersChanged |= MatchChatFont(label);
        if (answersChanged) ReflowAnswers();
    }

    void OnEnable() => GameEvents.LanguageChanged += OnLanguageChangedForLabels;
    void OnDisable() => GameEvents.LanguageChanged -= OnLanguageChangedForLabels;
    void OnLanguageChangedForLabels(LanguageState language) { SyncLabelFonts(); lastFrame = -1; }

    /// <summary>Place a label's box at whole OS pixels (top-left origin).</summary>
    static void PlaceLabel(TMP_Text label, int left, int top, int width, int height)
    {
        var rect = label.rectTransform;
        rect.anchoredPosition = new Vector2(left, -top);
        rect.sizeDelta = new Vector2(width, height);
    }

    static void Show(TMP_Text label, bool on) { if (label != null && label.gameObject.activeSelf != on) label.gameObject.SetActive(on); }

    void HideTexts()
    {
        Show(titleLabel, false); Show(lineLabel, false); Show(hintLabel, false);
        foreach (var label in optionLabels) Show(label, false);
    }

    /// <summary>How tall the text runs in a box this wide (OS pixels).</summary>
    static int LabelHeight(TMP_Text label, string text, int width) =>
        Mathf.CeilToInt(label.GetPreferredValues(text ?? string.Empty, width, 0f).y);

    static int LabelWidth(TMP_Text label, string text) =>
        Mathf.CeilToInt(label.GetPreferredValues(text ?? string.Empty, 4000f, 0f).x);

    // ---- the green lines

    TMP_Text AnswerLabel(Answer answer)
    {
        if (!answerLabels.TryGetValue(answer, out var label))
        {
            label = Label("Answer_" + answer.id, answer.type == TypeHeavy ? 20f : 14f, Green, false);
            label.alignment = TextAlignmentOptions.Top;
            if (answer.type == TypeHeavy) label.fontStyle = FontStyles.Bold;
            label.text = answer.text;
            answerLabels[answer] = label;
            Show(label, false);
        }
        MatchChatFont(label);
        return label;
    }

    void MeasureAnswer(Answer answer)
    {
        // Placement and rendering must use the same TMP font, size and boldness.
        // Atlas line-unit estimates can be narrower than the actual Latin/Cyrillic label.
        var label = AnswerLabel(answer);
        int available = Width - 8;
        int natural = LabelWidth(label, answer.text);
        label.enableWordWrapping = natural > available;
        answer.layoutWidth = Mathf.Min(natural, available) + 4;
        answer.layoutHeight = Mathf.Max(answer.Height + 8,
            LabelHeight(label, answer.text, answer.layoutWidth - 4) + 4);
    }

    void SyncAnswerLabels(float t, bool hidden)
    {
        if (textRoot == null) return;
        staleAnswerLabels.Clear();
        foreach (var pair in answerLabels)
            if (!answers.Contains(pair.Key))
            {
                Destroy(pair.Value.gameObject);
                staleAnswerLabels.Add(pair.Key);
            }
        foreach (var key in staleAnswerLabels) answerLabels.Remove(key);
        staleAnswerLabels.Clear();
        foreach (var answer in answers)
        {
            if (!answer.shown) continue;
            var label = AnswerLabel(answer);
            if (answer.layoutWidth == 0) MeasureAnswer(answer);
            PlaceLabel(label, answer.cx - answer.layoutWidth / 2, answer.top, answer.layoutWidth, answer.layoutHeight);
            label.maxVisibleCharacters = VisibleLetters(answer, t);
            float fade = answer.fadeAt < 0f ? 1f : Mathf.Clamp01(1f - (t - answer.fadeAt) / AnswerFadeSeconds);
            // fading (FadeAnswers) goes in whole steps, never a smooth ramp
            var color = (Color)Green; color.a = Mathf.Ceil(fade * 4f) / 4f; label.color = color;
            Show(label, !hidden);
        }
    }

    void ClearAnswerLabels()
    {
        foreach (var label in answerLabels.Values) if (label != null) Destroy(label.gameObject);
        answerLabels.Clear();
    }

    // ---- the mask over the labels

    void MaskDither(int left, int top, int right, int bottom, int blackBelow)
    {
        if (blackBelow <= 0) return;
        maskUsed = true;
        left = Mathf.Max(0, left); top = Mathf.Max(0, top); right = Mathf.Min(Width - 1, right); bottom = Mathf.Min(Height - 1, bottom);
        for (int y = top; y <= bottom; y++)
            for (int x = left; x <= right; x++)
                if (Bayer[(y & 3) * 4 + (x & 3)] < blackBelow) mask[(Height - 1 - y) * Width + x] = Black;
    }

    void BeginMask() { if (maskWasUsed) System.Array.Clear(mask, 0, mask.Length); maskUsed = false; }

    void SetTextLayersActive(bool on)
    {
        if (textRoot != null) textRoot.gameObject.SetActive(on);
        if (maskRoot != null) maskRoot.gameObject.SetActive(on);
    }

    void DestroyTextLayers()
    {
        if (textRoot != null) Destroy(textRoot.gameObject);
        if (maskRoot != null) Destroy(maskRoot.gameObject);
        if (maskTexture != null) Destroy(maskTexture);
    }

    void EndMask()
    {
        if (maskTexture == null || (!maskUsed && !maskWasUsed)) return;
        maskTexture.SetPixels32(mask);
        maskTexture.Apply(false);
        maskWasUsed = maskUsed;
    }
}
