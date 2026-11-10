using DG.Tweening;
using TMPro;

public sealed partial class OSLinkMessageController
{
    bool chapter04TextPaused;
    OSCharacterInteractionEntryData chapter04DeferredCompletion;
    bool chapter04DeferredWaitsForOption;
    int chapter04DeferredVersion;

    bool CanPresentChapter04Entry(OSCharacterInteractionEntryData entry) => entry == null
        || !OSChapter04StoryDirector.IsOwnEntry(entry.EntryId)
        || OS_Handle.Instance?.Chapter04Story?.CanPresentEntry(entry.EntryId) == true;

    void TickChapter04Text()
    {
        var story = OS_Handle.Instance?.Chapter04Story;
        if (story != null && story.IsActive)
            OSChapter04ChatView.Attach(story, this);
        bool own = OSChapter04StoryDirector.IsOwnEntry(displayedEntryId);
        bool paused = own && story?.IsAvailable != true;
        if (paused != chapter04TextPaused)
        {
            chapter04TextPaused = paused;
            if (paused) { textTween?.Pause(); reopenTween?.Pause(); autoCloseTween?.Pause(); }
            else { textTween?.Play(); reopenTween?.Play(); autoCloseTween?.Play(); }
        }
        // A new tween can be created while the controller is already paused.
        if (paused) { textTween?.Pause(); reopenTween?.Pause(); autoCloseTween?.Pause(); }
        if (chapter04DeferredCompletion == null) return;
        if (chapter04DeferredVersion != presentationVersion || !own)
        { chapter04DeferredCompletion = null; return; }
        if (paused) return;
        var entry = chapter04DeferredCompletion;
        chapter04DeferredCompletion = null;
        HandleTextPlaybackCompleted(entry, chapter04DeferredWaitsForOption, chapter04DeferredVersion);
    }

    bool DeferChapter04Completion(OSCharacterInteractionEntryData entry, bool waitsForOption, int version)
    {
        if (!OSChapter04StoryDirector.IsOwnEntry(entry?.EntryId)
            || OS_Handle.Instance?.Chapter04Story?.IsAvailable == true) return false;
        chapter04DeferredCompletion = entry;
        chapter04DeferredWaitsForOption = waitsForOption;
        chapter04DeferredVersion = version;
        return true;
    }

    bool RestoreCompletedChapter04Text(OSCharacterInteractionEntryData entry, TMP_Text label, bool waitsForOption)
    {
        if (!OSChapter04StoryDirector.IsOwnEntry(entry?.EntryId)
            || OS_Handle.Instance?.Chapter04Story?.HasCompletedCurrentText(entry.EntryId) != true) return false;
        label.maxVisibleCharacters = int.MaxValue;
        activeTextCompleted = true;
        isWaitingForOption = waitsForOption;
        if (waitsForOption)
        {
            SetOptionsVisible(true);
            SetOptionsInteractable(true);
        }
        characterWindowController?.EndLinkMessageTyping();
        windowLayout?.RefreshLayout();
        // Restore and language changes display the saved full line without
        // replaying a typewriter, a completion event, a cue or a resource receipt.
        return true;
    }

    internal bool RestoreChapter04StoryEntry(string id)
    {
        var data = GameDataManager.Instance?.OSCharacterInteractionData;
        if (!isOSVisible || data == null || !data.TryGetGlobalEntry(id, out var entry)
            || !OSChapter04StoryDirector.IsOwnEntry(id) || !CanPresentChapter04Entry(entry)) return false;
        ResolveReferences();
        var save = GetSaveState();
        if (save == null || linkWindow == null) return false;
        save.BeginGlobalPresentation(id, false);
        PresentGlobalEntry(entry, true);
        return displayedEntryId == id && IsPresenting;
    }

    internal void FinishChapter04StoryEntry(string id)
    {
        if (!OSChapter04StoryDirector.IsOwnEntry(id) || displayedEntryId != id) return;
        if (activeEntry?.EntryId == id) CompleteActivePresentation(id, presentationVersion, false);
        StopVisualPresentation(true);
        if (!IsChapter04ChatVisible) CloseLinkWindowWithoutCallback(true);
    }

    internal void ClearChapter04StoryPresentation()
    {
        chapter04DeferredCompletion = null;
        if (!OSChapter04StoryDirector.IsOwnEntry(displayedEntryId)) return;
        InterruptActiveSavePresentation();
        StopVisualPresentation(true);
        CloseLinkWindowWithoutCallback(true);
        chapter04TextPaused = false;
    }

    internal bool IsChapter04ChatVisible => OS_Handle.Instance?.Chapter04Story?.IsActive == true
        && OS_Handle.Instance.Chapter04Story.GetComponent<OSChapter04ChatView>()?.OwnsLayout == true;

    // The chat observes the original typewriter; it never runs a second clock.
    internal TMP_Text Chapter04PlaybackLabel => activeEntry?.PartType == OSCharacterInteractionPartType.TextWithOptions
        ? textWithOptionsLabel : textLabel;
}
