using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(TMP_Text))]
public sealed class OSLocalizedRedaction : MonoBehaviour
{
    const string MarkerOpenTag = "<garble>";
    
    const string TransparentOpenTag = "<color=#00000000>";
    const string TransparentCloseTag = "</color>";
    const string MarkerCloseTag = "</garble>";
    const string RedactionShaderName = "UI/OS/TextRedaction";

    static readonly int SeedId = Shader.PropertyToID("_Seed");
    static readonly int RectSizeId = Shader.PropertyToID("_RectSize");

    [SerializeField] TMP_Text targetText;
    [SerializeField] Material redactionMaterialTemplate;
    [SerializeField] Vector2 padding = new Vector2(1.5f, 0.6f);
    [SerializeField, Range(0.5f, 1.2f)] float verticalCoverage = 0.92f;
    [SerializeField] float seed = 17.3f;

    struct TextRange
    {
        public int Start;
        public int End;

        public TextRange(int start, int end)
        {
            Start = start;
            End = end;
        }
    }

    struct LineBounds
    {
        public int Line;
        public Rect Rect;

        public LineBounds(int line, Rect rect)
        {
            Line = line;
            Rect = rect;
        }
    }

    readonly List<TextRange> markedRanges = new List<TextRange>();
    readonly List<LineBounds> lineBounds = new List<LineBounds>();
    readonly List<Image> overlays = new List<Image>();
    readonly List<Material> overlayMaterials = new List<Material>();

    bool hasMarkedText;
    bool rebuildPending;
    bool warnedMissingMaterial;

    public TMP_Text TargetText => targetText;
    public Material RedactionMaterialTemplate => redactionMaterialTemplate;

    void Reset()
    {
        ResolveReferences();
    }

    void OnValidate()
    {
        ResolveReferences();
        padding.x = Mathf.Max(0f, padding.x);
        padding.y = Mathf.Max(0f, padding.y);
        verticalCoverage = Mathf.Clamp(verticalCoverage, 0.5f, 1.2f);
    }

    void OnEnable()
    {
        if (hasMarkedText)
            rebuildPending = true;
    }

    void OnDisable()
    {
        SetOverlayCount(0);
    }

    void LateUpdate()
    {
        if (!rebuildPending)
            return;

        rebuildPending = false;
        RebuildOverlays();
    }

    void OnRectTransformDimensionsChange()
    {
        if (Application.isPlaying && hasMarkedText)
            rebuildPending = true;
    }

    void OnDestroy()
    {
        DestroyOverlayResources();
    }

    public void Initialize()
    {
        ResolveReferences();
    }

    public void ApplyMarkedText(string markedText)
    {
        ResolveReferences();
        markedRanges.Clear();
        if (targetText == null)
        {
            SetOverlayCount(0);
            return;
        }

        if (!EnsureMaterialAvailable())
        {
            hasMarkedText = false;
            rebuildPending = false;
            targetText.text = RemoveMarkers(markedText);
            SetOverlayCount(0);
            return;
        }

        string displayText = ParseMarkedText(markedText, markedRanges);
        hasMarkedText = markedRanges.Count > 0;
        targetText.text = displayText;
        rebuildPending = hasMarkedText;
        RebuildOverlays();
    }

    public void ClearOverlay()
    {
        hasMarkedText = false;
        rebuildPending = false;
        markedRanges.Clear();
        SetOverlayCount(0);
    }

    public static string RemoveMarkers(string source)
    {
        if (string.IsNullOrEmpty(source))
            return source ?? string.Empty;

        return source
            .Replace(MarkerOpenTag, string.Empty)
            .Replace(MarkerCloseTag, string.Empty);
    }

    void RebuildOverlays()
    {
        if (!hasMarkedText || targetText == null)
        {
            SetOverlayCount(0);
            return;
        }

        targetText.ForceMeshUpdate(true, true);
        TMP_TextInfo textInfo = targetText.textInfo;
        lineBounds.Clear();
        if (textInfo == null || textInfo.characterCount == 0)
        {
            SetOverlayCount(0);
            return;
        }

        for (int i = 0; i < textInfo.characterCount; i++)
        {
            TMP_CharacterInfo character = textInfo.characterInfo[i];
            if (character.character == '\n' || character.character == '\r')
                continue;

            int characterStart = character.index;
            int characterEnd = characterStart
                + Mathf.Max(1, character.stringLength);
            if (!IntersectsMarkedRange(characterStart, characterEnd))
                continue;

            float left = Mathf.Min(character.origin, character.xAdvance);
            float right = Mathf.Max(character.origin, character.xAdvance);
            float bottom = character.descender;
            float top = character.ascender;
            if (!AreFinite(left, right, bottom, top))
                continue;

            if (right - left <= 0.001f && character.isVisible)
            {
                left = Mathf.Min(character.bottomLeft.x, character.topLeft.x);
                right = Mathf.Max(character.bottomRight.x, character.topRight.x);
            }

            if (right - left <= 0.001f || top - bottom <= 0.001f)
                continue;

            AddCharacterBounds(
                character.lineNumber,
                Rect.MinMaxRect(left, bottom, right, top));
        }

        lineBounds.Sort((left, right) => left.Line.CompareTo(right.Line));
        if (lineBounds.Count == 0 || !EnsureMaterialAvailable())
        {
            SetOverlayCount(0);
            return;
        }

        SetOverlayCount(lineBounds.Count);
        Vector2 anchor = targetText.rectTransform.pivot;
        for (int i = 0; i < lineBounds.Count; i++)
        {
            Rect source = lineBounds[i].Rect;
            float height = Mathf.Max(
                0.5f,
                source.height * verticalCoverage + padding.y * 2f);
            Rect paintRect = Rect.MinMaxRect(
                source.xMin - padding.x,
                source.center.y - height * 0.5f,
                source.xMax + padding.x,
                source.center.y + height * 0.5f);

            Image overlay = overlays[i];
            RectTransform overlayRect = overlay.rectTransform;
            overlayRect.anchorMin = anchor;
            overlayRect.anchorMax = anchor;
            overlayRect.pivot = new Vector2(0.5f, 0.5f);
            overlayRect.anchoredPosition = paintRect.center;
            overlayRect.sizeDelta = paintRect.size;
            overlayRect.localRotation = Quaternion.identity;
            overlayRect.localScale = Vector3.one;
            Material overlayMaterial = overlayMaterials[i];
            if (overlayMaterial != null)
            {
                overlayMaterial.SetVector(
                    RectSizeId,
                    new Vector4(
                        paintRect.width,
                        paintRect.height,
                        0f,
                        0f));
            }
            overlay.transform.SetAsLastSibling();
        }
    }

    bool IntersectsMarkedRange(int start, int end)
    {
        for (int i = 0; i < markedRanges.Count; i++)
        {
            TextRange range = markedRanges[i];
            if (start < range.End && end > range.Start)
                return true;
        }

        return false;
    }

    void AddCharacterBounds(int line, Rect characterRect)
    {
        for (int i = 0; i < lineBounds.Count; i++)
        {
            if (lineBounds[i].Line != line)
                continue;

            Rect current = lineBounds[i].Rect;
            lineBounds[i] = new LineBounds(
                line,
                Rect.MinMaxRect(
                    Mathf.Min(current.xMin, characterRect.xMin),
                    Mathf.Min(current.yMin, characterRect.yMin),
                    Mathf.Max(current.xMax, characterRect.xMax),
                    Mathf.Max(current.yMax, characterRect.yMax)));
            return;
        }

        lineBounds.Add(new LineBounds(line, characterRect));
    }

    void SetOverlayCount(int count)
    {
        count = Mathf.Max(0, count);
        while (overlays.Count < count)
            CreateOverlay(overlays.Count);

        for (int i = 0; i < overlays.Count; i++)
        {
            if (overlays[i] != null)
                overlays[i].gameObject.SetActive(i < count);
        }
    }

    void CreateOverlay(int index)
    {
        var overlayObject = new GameObject(
            "Localized Redaction " + (index + 1),
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        overlayObject.layer = gameObject.layer;
        overlayObject.hideFlags = HideFlags.HideAndDontSave;
        overlayObject.transform.SetParent(targetText.rectTransform, false);

        Image image = overlayObject.GetComponent<Image>();
        image.color = Color.white;
        image.raycastTarget = false;
        image.maskable = true;

        Material material = CreateRuntimeMaterial(index);
        image.material = material;
        overlays.Add(image);
        overlayMaterials.Add(material);
    }

    Material CreateRuntimeMaterial(int index)
    {
        Material material = null;
        if (redactionMaterialTemplate != null)
        {
            material = new Material(redactionMaterialTemplate);
        }
        else
        {
            Shader shader = Shader.Find(RedactionShaderName);
            if (shader != null)
                material = new Material(shader);
        }

        if (material == null)
            return null;

        material.name = "Localized Redaction " + (index + 1) + " (Runtime)";
        material.hideFlags = HideFlags.HideAndDontSave;
        material.SetFloat(SeedId, seed + index * 7.37f);
        return material;
    }

    bool EnsureMaterialAvailable()
    {
        for (int i = 0; i < overlays.Count; i++)
        {
            if (overlays[i] != null && overlayMaterials[i] == null)
            {
                Material material = CreateRuntimeMaterial(i);
                overlayMaterials[i] = material;
                overlays[i].material = material;
            }
        }

        bool available = redactionMaterialTemplate != null
            || Shader.Find(RedactionShaderName) != null;
        if (!available && !warnedMissingMaterial)
        {
            warnedMissingMaterial = true;
            Debug.LogWarning(
                "[OSLocalizedRedaction] Redaction material is missing.",
                this);
        }
        else if (available)
        {
            warnedMissingMaterial = false;
        }

        return available;
    }

    void ResolveReferences()
    {
        if (targetText == null)
            targetText = GetComponent<TMP_Text>();
    }

    void DestroyOverlayResources()
    {
        for (int i = 0; i < overlayMaterials.Count; i++)
        {
            Material material = overlayMaterials[i];
            if (material == null)
                continue;

            if (Application.isPlaying)
                Destroy(material);
            else
                DestroyImmediate(material);
        }

        for (int i = 0; i < overlays.Count; i++)
        {
            Image overlay = overlays[i];
            if (overlay == null)
                continue;

            if (Application.isPlaying)
                Destroy(overlay.gameObject);
            else
                DestroyImmediate(overlay.gameObject);
        }

        overlayMaterials.Clear();
        overlays.Clear();
    }

    static string ParseMarkedText(
        string source,
        List<TextRange> ranges)
    {
        if (string.IsNullOrEmpty(source))
            return source ?? string.Empty;

        int cursor = 0;
        var result = new StringBuilder(source.Length);
        while (cursor < source.Length)
        {
            int openIndex = source.IndexOf(
                MarkerOpenTag,
                cursor,
                StringComparison.Ordinal);
            if (openIndex < 0)
            {
                result.Append(source, cursor, source.Length - cursor);
                break;
            }

            result.Append(source, cursor, openIndex - cursor);
            int contentStart = openIndex + MarkerOpenTag.Length;
            int closeIndex = source.IndexOf(
                MarkerCloseTag,
                contentStart,
                StringComparison.Ordinal);
            result.Append(TransparentOpenTag);
            int markedStart = result.Length;
            if (closeIndex < 0)
            {
                result.Append(
                    source,
                    contentStart,
                    source.Length - contentStart);
                if (result.Length > markedStart)
                    ranges.Add(new TextRange(markedStart, result.Length));
                result.Append(TransparentCloseTag);
                break;
            }

            result.Append(source, contentStart, closeIndex - contentStart);
            if (result.Length > markedStart)
                ranges.Add(new TextRange(markedStart, result.Length));
            result.Append(TransparentCloseTag);
            cursor = closeIndex + MarkerCloseTag.Length;
        }

        return result.ToString();
    }

    static bool AreFinite(
        float first,
        float second,
        float third,
        float fourth)
    {
        return IsFinite(first)
            && IsFinite(second)
            && IsFinite(third)
            && IsFinite(fourth);
    }

    static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
