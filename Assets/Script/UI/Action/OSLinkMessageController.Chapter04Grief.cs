using DG.Tweening;
using TMPro;
using UnityEngine;

/// <summary>
/// Round 8: the typewriter's rhythm at the grave (lines 7-16), by the grief
/// tier OSChapter04GriefPresentation holds. Only the timing changes: the same
/// label, the same completion callback, the same final text.
///   Tier 1: ellipses hold longer; now and then a letter waits before it comes.
///   Tier 2: plus one typo per line (a common character, the same one each time
///           for the same line), held, erased, typed right; commas hold a beat.
///   Tier 3: letters squeezed out at uneven intervals; ellipsis dots come one by
///           one, each with a heartbeat.
/// </summary>
public sealed partial class OSLinkMessageController
{
    // Preserve the authored Simplified Chinese slips; other scripts use their
    // own letters so a localized line never flashes an unrelated Chinese glyph.
    const string GriefTypoCharacters = "的了是我不在有这她也";

    /// <summary>The grief tier for a chapter-4 line, 0 for every other line.</summary>
    static int Chapter04GriefTier(OSCharacterInteractionEntryData entry) =>
        entry != null && OSChapter04StoryDirector.IsOwnEntry(entry.EntryId) ? OSChapter04GriefPresentation.TypingTier : 0;

    internal Tween CreateChapter04GriefTween(TMP_Text label, string text, int tier) =>
        CreateGriefTypewriter(label, text, tier, Mathf.Max(1f, charactersPerSecond));

    /// <summary>The grief typewriter on any label (QA drives it on its own label).</summary>
    public static Tween CreateGriefTypewriter(TMP_Text label, string text, int tier, float charactersPerSecond)
    {
        label.text = text; label.maxVisibleCharacters = 0; label.ForceMeshUpdate(true);
        int total = label.textInfo.characterCount;
        float step = 1f / Mathf.Max(1f, charactersPerSecond);
        uint lineHash = GriefHash(label);
        int typoAt = tier >= 2 ? GriefTypoIndex(label, text, lineHash) : -1;
        char original = typoAt >= 0 ? label.textInfo.characterInfo[typoAt].character : '的';
        string typoPool = GriefTypoPool(original);
        int typoPick = (int)((lineHash >> 9) % (uint)typoPool.Length);
        char typo = char.IsUpper(original) ? char.ToUpperInvariant(typoPool[typoPick]) : typoPool[typoPick];
        if (original == typo)
        {
            typoPick = (typoPick + 1) % typoPool.Length;
            typo = char.IsUpper(original) ? char.ToUpperInvariant(typoPool[typoPick]) : typoPool[typoPick];
        }
        var sequence = DOTween.Sequence();
        for (int i = 0; i < total; i++)
        {
            char c = label.textInfo.characterInfo[i].character;
            bool dot = IsGriefDot(c);
            int reveal = i + 1;
            float wait = step;
            // Squeezed out: anywhere from half to three times the usual step, and now and then a stall.
            if (tier >= 3) wait = dot ? .38f : step * (.5f + 2.5f * GriefNoise(lineHash, i))
                + (GriefNoise(lineHash ^ 0x5bd1e995u, i) > .8f ? .22f : 0f);
            else if (tier >= 1 && !dot && char.IsLetter(c) && GriefNoise(lineHash, i) > .9f) wait += .3f;   // a letter that hesitates
            if (i == typoAt)
            {
                // i/reveal count parsed TMP characters. Only the mapped source span
                // may be edited: a color tag before this glyph is not a typed letter.
                var character = label.textInfo.characterInfo[i];
                string wrong = text.Remove(character.index, character.stringLength)
                    .Insert(character.index, typo.ToString());
                sequence.AppendInterval(step);
                sequence.AppendCallback(() => { label.text = wrong; label.maxVisibleCharacters = reveal; });
                sequence.AppendInterval(.42f);
                sequence.AppendCallback(() => { label.text = text; label.maxVisibleCharacters = reveal - 1; });
                sequence.AppendInterval(.2f);
            }
            sequence.AppendInterval(wait);
            sequence.AppendCallback(() => label.maxVisibleCharacters = reveal);
            if (tier >= 3 && dot) sequence.AppendCallback(() => OS_Handle.Instance?.HeartbeatWaveform?.GenerateStoryBeat(.45f));
            bool runEnds = dot && (i + 1 >= total || !IsGriefDot(label.textInfo.characterInfo[i + 1].character));
            if (runEnds) sequence.AppendInterval(tier >= 3 ? .45f : .6f);
            if (tier >= 2 && IsGriefComma(c)) sequence.AppendInterval(.35f);
        }
        sequence.AppendCallback(() => { label.text = text; label.maxVisibleCharacters = int.MaxValue; });
        return sequence;
    }

    static bool IsGriefDot(char c) => c == '.' || c == '…' || c == '⋯';
    static bool IsGriefComma(char c) => c == '，' || c == ',' || c == '、' || c == '；';
    static bool IsHan(char c) => c >= '一' && c <= '鿿';

    static string GriefTypoPool(char c)
    {
        if (IsHan(c))
        {
            var language = GameDataManager.Instance?.CurrentLanguageState ?? LanguageState.zh_CN;
            if (language == LanguageState.zh_TW) return "的了是我不在有這她也";
            if (language == LanguageState.ja) return "私今何心君見日母思言";
            return GriefTypoCharacters;
        }
        if (c >= '\u3040' && c <= '\u309f') return "のがをにはてもだしと";
        if (c >= '\u30a0' && c <= '\u30ff') return "ノガヲニハテモダシト";
        if (c >= '\uac00' && c <= '\ud7af') return "나는이그서고다한에가";
        if (c >= '\u0400' && c <= '\u04ff') return "оеаинтсрвл";
        return "etaoinshrd";
    }

    /// <summary>One parsed letter inside the line (never the first two or last two), or -1.</summary>
    static int GriefTypoIndex(TMP_Text label, string text, uint hash)
    {
        int total = label.textInfo.characterCount;
        int count = 0;
        for (int i = 2; i < total - 2; i++)
            if (IsGriefTypoCandidate(label.textInfo.characterInfo[i], text)) count++;
        if (count == 0) return -1;
        int pick = (int)(hash % (uint)count);
        for (int i = 2; i < total - 2; i++)
            if (IsGriefTypoCandidate(label.textInfo.characterInfo[i], text) && pick-- == 0) return i;
        return -1;
    }

    static bool IsGriefTypoCandidate(TMP_CharacterInfo character, string text)
    {
        if (character.elementType != TMP_TextElementType.Character || !char.IsLetter(character.character)
            || character.index < 0 || character.stringLength <= 0
            || character.index > text.Length - character.stringLength) return false;
        // Exclude tag-generated elements; ordinary glyphs inside color/bold tags
        // still map to their own source characters and remain eligible.
        for (int i = character.index; i < character.index + character.stringLength; i++)
            if (text[i] == '<' || text[i] == '>') return false;
        return true;
    }

    static uint GriefHash(TMP_Text label)
    {
        uint hash = 2166136261u;
        // Styling must not change the chosen typo or the line's timing pattern.
        for (int i = 0; i < label.textInfo.characterCount; i++)
        { hash ^= label.textInfo.characterInfo[i].character; hash *= 16777619u; }
        return hash;
    }

    static float GriefNoise(uint seed, int index)
    {
        uint value = seed ^ (uint)(index * 374761393);
        value = (value ^ (value >> 13)) * 1274126177u;
        return (value ^ (value >> 16)) % 1000 / 1000f;
    }
}
