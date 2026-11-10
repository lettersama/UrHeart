using System;
using UnityEngine;

/// <summary>
/// The OS has one bright red: the red pointer's. Hearts, warnings and the memory
/// text's highlighted words all use it. Darker reds (the "/ 100" after a value,
/// secondary numbers, a failed heal) are a separate, deliberate dim tier.
/// </summary>
public static class OSPalette
{
    public static readonly Color32 Red = new Color32(0xD8, 0x34, 0x3A, 0xFF);
    public const string RedHex = "#D8343A";

    const string NamedRedTag = "<color=red>";
    const string RedTag = "<color=" + RedHex + ">";

    /// <summary>Authored text uses TMP's pure red; draw it in the OS red instead.</summary>
    public static string TintRichText(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf(NamedRedTag, StringComparison.OrdinalIgnoreCase) < 0)
            return text;
        return text.Replace(NamedRedTag, RedTag, StringComparison.OrdinalIgnoreCase);
    }
}
