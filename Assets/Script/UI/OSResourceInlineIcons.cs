using System.Globalization;
using MrTGame.Narrative;
using TMPro;
using UnityEngine;

// Resource badges are real TMP glyphs: they participate in wrapping and measurement.
[DisallowMultipleComponent]
[RequireComponent(typeof(TMP_Text))]
public sealed class OSResourceInlineIcons : MonoBehaviour
{
    const string AssetPath = "OSResourceIcons";
    static TMP_SpriteAsset icons;
    TMP_Text label;

    public static string Format(TMP_Text target, string text, OSMemoryResourceChange change)
        => Format(target, text, change != null ? change.Heart : 0f,
            change != null ? change.Battery : 0f);

    public static string Format(TMP_Text target, string text, float heart = 0f, float energy = 0f)
    {
        int heartDelta = Mathf.RoundToInt(heart);
        int energyDelta = Mathf.RoundToInt(energy);
        if (heartDelta == 0 && energyDelta == 0) return text ?? string.Empty;
        Prepare(target);
        string result = text ?? string.Empty;
        if (heartDelta != 0) result += Badge("heart", heartDelta);
        if (energyDelta != 0) result += Badge("energy", energyDelta);
        return result;
    }

    static string Badge(string name, int delta)
        => "  <nobr><sprite name=\"" + name + "\">\u00a0"
            + delta.ToString("+0;-0;0", CultureInfo.InvariantCulture) + "</nobr>";

    static void Prepare(TMP_Text target)
    {
        if (target == null) return;
        if (icons == null) icons = Resources.Load<TMP_SpriteAsset>(AssetPath);
        if (icons == null)
        {
            Debug.LogError("Missing Resources/OSResourceIcons sprite asset.", target);
            return;
        }
        target.richText = true;
        target.spriteAsset = icons;
        if (target.GetComponent<OSResourceInlineIcons>() == null)
            target.gameObject.AddComponent<OSResourceInlineIcons>();
    }

    void OnEnable()
    {
        label = GetComponent<TMP_Text>();
        label.OnPreRenderText += AlignIcons;
        label.SetVerticesDirty();
    }

    void OnDisable()
    {
        if (label != null) label.OnPreRenderText -= AlignIcons;
    }

    // Use the visible glyphs on each wrapped line, so CJK and Latin fonts share
    // the same visual center without language-specific pixel offsets.
    void AlignIcons(TMP_TextInfo info)
    {
        for (int i = 0; i < info.characterCount; i++)
        {
            TMP_CharacterInfo icon = info.characterInfo[i];
            if (!icon.isVisible || icon.elementType != TMP_TextElementType.Sprite
                || icon.textElement.textAsset != icons) continue;
            TMP_LineInfo line = info.lineInfo[icon.lineNumber];
            float bottom = float.PositiveInfinity, top = float.NegativeInfinity;
            for (int j = line.firstCharacterIndex; j <= line.lastCharacterIndex
                && j < info.characterCount; j++)
            {
                TMP_CharacterInfo character = info.characterInfo[j];
                if (!character.isVisible || character.elementType != TMP_TextElementType.Character)
                    continue;
                bottom = Mathf.Min(bottom, character.bottomLeft.y);
                top = Mathf.Max(top, character.topRight.y);
            }
            if (float.IsInfinity(bottom)) continue;
            Vector3[] vertices = info.meshInfo[icon.materialReferenceIndex].vertices;
            int first = icon.vertexIndex;
            float center = (vertices[first].y + vertices[first + 2].y) * .5f;
            float offset = (bottom + top) * .5f - center;
            for (int v = 0; v < 4; v++) vertices[first + v].y += offset;
        }
    }
}
