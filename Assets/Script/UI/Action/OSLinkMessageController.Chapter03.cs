using System;
using DG.Tweening;
using TMPro;
using UnityEngine;

public sealed partial class OSLinkMessageController
{
    bool chapter03TextPaused;

    public bool HasChapter03EntryPending(string id)
    {
        var state = GetSaveState();
        return state != null && (state.pendingGlobalEntryId == id || state.presentingGlobalEntryId == id);
    }

    // The chapter queue persists the remaining visible reading time. Restoring
    // already-read text must not replay completion callbacks or consume it again.
    internal bool RestoreChapter03ReadingHold(string id, float remaining)
    {
        var data = GameDataManager.Instance?.OSCharacterInteractionData;
        if (!isOSVisible || remaining <= 0f || !OSChapter03StoryDirector.IsOwnEntry(id)
            || data == null || !data.TryGetGlobalEntry(id, out var entry)
            || entry.PartType != OSCharacterInteractionPartType.Text || entry.AutoCloseDelay <= 0f
            || !AllowsCursorExperimentEntry(entry) || !CanUseGlobalChapter(entry)) return false;
        ResolveReferences();
        if (linkWindow == null || textPart == null || textLabel == null) return false;
        int version = BeginNewPresentation();
        displayedSequenceId = GlobalPresentationSequenceId;
        displayedEntryId = id;
        displayedEntryIsGlobal = true;
        hasDisplayedMessage = true;
        ApplyLocalizedFonts();
        linkWindow.OpenStandalone();
        SetOnlyPartActive(textPart);
        string text = ResolveLocalizedText(entry);
        textLabel.text = text;
        textLabel.maxVisibleCharacters = int.MaxValue;
        windowLayout?.SetMeasurementText(textLabel, text);
        windowLayout?.RefreshLayout();
        ScheduleAutoClose(entry, version, Mathf.Max(0f, entry.AutoCloseDelay - remaining));
        TickWaitLine();
        return true;
    }

    void TickChapter03Text()
    {
        var director = OS_Handle.Instance?.Chapter03Story;
        bool paused = director?.IsActive == true && OSChapter03StoryDirector.IsOwnEntry(displayedEntryId) && !director.IsAvailable;
        if (paused != chapter03TextPaused)
        {
            chapter03TextPaused = paused;
            if (!paused)
            { textTween?.Play(); autoCloseTween?.Play(); reopenTween?.Play(); }
        }
        // Changing language or restoring a minimized Live window can replace
        // a tween while the story is already paused. The replacement inherits
        // that pause too; the cached flag alone does not describe its state.
        if (paused)
        { textTween?.Pause(); autoCloseTween?.Pause(); reopenTween?.Pause(); }
    }

    Tween CreateChapter03TextTween(TMP_Text label, string text, OSCharacterInteractionEntryData entry)
    {
        label.text = text; label.maxVisibleCharacters = 0; label.ForceMeshUpdate(true);
        int total = label.textInfo.characterCount;
        float speed = entry.EntryId == OSChapter03StoryDirector.LiveId("live.breakdown.08") ? 11f
            : entry.EntryId.Contains("live_breakdown") ? 24f : charactersPerSecond;
        var sequence = DOTween.Sequence(); int start = 0;
        for (int i = 0; i < total; i++)
        {
            char c = label.textInfo.characterInfo[i].character;
            bool dots = c == '.' && i + 1 < total && label.textInfo.characterInfo[i + 1].character == '.';
            if (!dots && c != '…' && c != '⋯') continue;
            int end = i + 1;
            while (end < total && (label.textInfo.characterInfo[end].character == '.' || label.textInfo.characterInfo[end].character == '…'
                || label.textInfo.characterInfo[end].character == '⋯')) end++;
            int limit = end;
            sequence.Append(DOTween.To(() => label.maxVisibleCharacters, v => label.maxVisibleCharacters = v,
                limit, Mathf.Max(.02f, (end - start) / Mathf.Max(1f, speed))).SetEase(Ease.Linear));
            sequence.AppendInterval(Mathf.Min(.75f, .2f + (end - i) * .12f));
            start = end; i = end - 1;
        }
        if (start < total)
            sequence.Append(DOTween.To(() => label.maxVisibleCharacters, v => label.maxVisibleCharacters = v,
                total, Mathf.Max(.02f, (total - start) / Mathf.Max(1f, speed))).SetEase(Ease.Linear));
        sequence.AppendCallback(() => label.maxVisibleCharacters = int.MaxValue);
        return sequence;
    }
}
