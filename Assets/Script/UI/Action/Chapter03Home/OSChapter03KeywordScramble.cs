using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Presentation-only anxiety text. The localized source and history remain complete sentences.
/// Keywords stay readable and red; every other character is replaced by one exactly as wide
/// and of the same kind (CJK for CJK, letters for letters), so the line never reflows while it
/// churns. Punctuation and kana TMP keeps off a line start never change: they decide where it wraps.
/// </summary>
public sealed class OSChapter03KeywordScramble
{
    const float Interval = .11f;
    static readonly string KeywordOpen = "<color=" + OSPalette.RedHex + ">";
    const string KeywordClose = "</color>";

    string clean;
    char[] buffer;
    readonly List<int> unstable = new List<int>();
    readonly List<List<char>> choices = new List<List<char>>();
    uint random;
    int node;
    float nextTick;
    public bool Active => buffer != null;
    public string Text => buffer == null ? clean : new string(buffer);

    /// <param name="measure">Advance of one character in the label's font, size and style.</param>
    public string Prepare(string text, int dataIndex, Func<char, float> measure)
    {
        Clear(); clean = text;
        if (string.IsNullOrEmpty(text) || !text.Contains("<k>")) return text;
        node = dataIndex; random = unchecked((uint)dataIndex * 1664525u + 1013904223u);
        var builder = new StringBuilder(); bool keyword = false;
        for (int i = 0; i < text.Length;)
        {
            if (string.CompareOrdinal(text, i, "<k>", 0, 3) == 0)
            { keyword = true; builder.Append(KeywordOpen); i += 3; continue; }
            if (string.CompareOrdinal(text, i, "</k>", 0, 4) == 0)
            { keyword = false; builder.Append(KeywordClose); i += 4; continue; }
            if (text[i] == '<')
            {
                int end = text.IndexOf('>', i);
                if (end >= 0) { builder.Append(text, i, end - i + 1); i = end + 1; continue; }
            }
            if (!keyword && Kind(text[i]) != Fixed) unstable.Add(builder.Length);
            builder.Append(text[i++]);
        }
        clean = builder.ToString(); buffer = clean.ToCharArray();
        BuildChoices(measure);
        nextTick = 0f;
        Tick(); return Text;
    }

    // Candidates are the line's own characters plus a per-language pool, grouped by advance and kind.
    void BuildChoices(Func<char, float> measure)
    {
        advance.Clear(); groups.Clear(); this.measure = measure;
        foreach (char c in ReplacementPool()) Offer(c);
        foreach (int index in unstable) Offer(clean[index]);
        choices.Clear();
        foreach (int index in unstable)
            choices.Add(groups.TryGetValue(Group(clean[index]), out var list) ? list : null);
        this.measure = null;
    }

    static string ReplacementPool()
    {
        var language = GameDataManager.Instance?.CurrentLanguageState ?? LanguageState.zh_CN;
        return language == LanguageState.zh_CN ? "主人房门雨水窗鞋影字壳心步地灰回音未知"
            : language == LanguageState.zh_TW ? "主人房門雨水窗鞋影字殼心步地灰回音未知"
            : language == LanguageState.ja ? "あかさたなはまやらわみずのひとへつふる雨家心"
            : language == LanguageState.ko ? "가나다라마바사아자차카타파하주인마음비집문"
            : language == LanguageState.ru ? "абвгдежзийклмнопрстуфхцчшщыэюя"
            : "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
    }

    readonly Dictionary<char, int> advance = new Dictionary<char, int>();
    readonly Dictionary<int, List<char>> groups = new Dictionary<int, List<char>>();
    Func<char, float> measure;
    int Width(char c)
    {
        if (!advance.TryGetValue(c, out int w))
            advance[c] = w = Mathf.RoundToInt((measure != null ? measure(c) : 0f) * 100f);
        return w;
    }
    int Group(char c) => Width(c) * 4 + Kind(c);
    void Offer(char c)
    {
        if (Kind(c) == Fixed || Width(c) <= 0) return;
        int key = Group(c);
        if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<char>();
        if (!list.Contains(c)) list.Add(c);
    }

    const int Fixed = 0, Wide = 1, Narrow = 2;
    // Small kana and marks TMP will not start a line with; swapping one in or out moves the wrap.
    const string NoLineStart = "ぁぃぅぇぉっゃゅょゎゕゖァィゥェォッャュョヮヵヶーヽヾゝゞ々〻・";
    static int Kind(char c)
    {
        // Some punctuation and marks live inside the kana Unicode blocks too.
        if (char.IsWhiteSpace(c) || char.IsSurrogate(c) || char.IsPunctuation(c)
            || char.IsSymbol(c) || NoLineStart.IndexOf(c) >= 0) return Fixed;
        if ((c >= '\u3040' && c <= '\u30FF') || (c >= '\u3400' && c <= '\u4DBF') || (c >= '\u4E00' && c <= '\u9FFF')
            || (c >= '\uF900' && c <= '\uFAFF') || (c >= '\u1100' && c <= '\u11FF') || (c >= '\u3130' && c <= '\u318F')
            || (c >= '\uAC00' && c <= '\uD7AF')) return Wide;
        return char.IsLetterOrDigit(c) ? Narrow : Fixed;
    }

    public void Tick()
    {
        if (!Active || Time.unscaledTime < nextTick) return;
        nextTick = Time.unscaledTime + Interval;
        var story = GameDataManager.Instance?.OSProcessData.memory.chapter03Story;
        float amount = story?.Has("yard_returned") == true ? 0f : node == OSChapter03StoryContent.Instance.Node("memory.yard.center.scolding") ? .32f
            : node == OSChapter03StoryContent.Instance.Node("memory.yard.center.rain") ? .72f : 1f;
        for (int i = 0; i < unstable.Count; i++)
        {
            int index = unstable[i];
            var list = choices[i];
            random = unchecked(random * 1664525u + 1013904223u);
            buffer[index] = list != null && list.Count > 1 && (random & 1023) / 1024f < amount
                ? list[(int)((random >> 10) % (uint)list.Count)] : clean[index];
        }
    }
    public void Clear() { clean = null; buffer = null; unstable.Clear(); choices.Clear(); }
}
