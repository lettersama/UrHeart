using System;
using UnityEngine;

/// <summary>
/// Live's breakdown in front of the mirror (pancake 10-25, second pass). Her red pointer drags her
/// window face to face with the reflection as before; line 02 starts a 15-second focus on
/// her window's centre (Presentation.Closeup), and after her last line eases back
/// out before the usual carry home and offline. Her messages run exactly as they always did.
/// Saves: closeup_open, closeup_closed (the effect keys kept from the first pass, so queues saved
/// with them still play). Portrait cues persist independently of the transient message window.
/// </summary>
public sealed partial class OSChapter03StoryDirector
{
    const float ZoomBreathSeconds = .35f;
    bool livePortraitRestoreStable, livePortraitInstantPending;

    bool CloseupWanted => CurrentPhase == Phase.Mirror && Has("closeup_open") && !Has("closeup_closed");

    bool RunCloseupClose(OSChapter03StoryBeat beat)
    {
        if (!Has("closeup_closed"))
        {
            if (!Has("closeup_open") || liveWindow == null) { State.Add("closeup_closed"); presentation.SetScreenZoom(false, instant: true); return true; }
            if (effectClock < ZoomBreathSeconds) return false;
            State.Add("closeup_closed");
            presentation.SetScreenZoom(false);
        }
        return effectClock >= beat.remaining && !presentation.IsScreenZoomBlinking;
    }

    // Every frame from TickStoryEffects: restores a saved zoom and keeps the frame on her windows.
    void TickCloseup()
    {
        if (presentation == null) return;
        if (!CloseupWanted || liveWindow == null)
        {
            if (presentation.IsScreenZoomed && !presentation.IsScreenZoomBlinking) presentation.SetScreenZoom(false, instant: true);
            return;
        }
        if (presentation.IsScreenZoomBlinking) return;
        presentation.SetScreenZoom(true, liveWindow.WindowRect, instant: !presentation.IsScreenZoomed);
    }

    void RefreshMirrorStage()
    {
        if (presentation == null) return;
        bool liveHasFocus = Has("mirror_live_focus") || Has("live_moved_center") || Has("closeup_open");
        presentation.SetMirrorAttention(!liveHasFocus && !IsMirrorPlaybackPaused && !Has("live_back_home"));
        presentation.SetMirrorStage(IsMirrorPerformanceLocked, IsMirrorPlaybackPaused || IsMirrorPerformanceLocked && liveHasFocus,
            liveWindow, messages?.LinkWindow, settingsWindow);
        presentation.SetMirrorPlaybackPaused(IsMirrorPlaybackPaused, IsWaitingForMirrorResume, ResumeMirrorPlayback);
    }

    // Her nose on the reflection's nose; the mirror's centre when either face cannot be found.
    Vector2 MirrorDockPosition()
    {
        var rect = liveWindow.WindowRect; var parent = rect.parent as RectTransform;
        if (presentation.TryGetMirrorFaceWorld(out Vector3 reflection)
            && os.CharacterWindowController != null && os.CharacterWindowController.TryGetChapter03FaceWorld(out Vector3 face))
        {
            Vector3 delta = parent.InverseTransformVector(reflection - face);
            return rect.anchoredPosition + new Vector2(Mathf.Round(delta.x), Mathf.Round(delta.y));
        }
        return presentation.MirrorRoot != null ? (Vector2)parent.InverseTransformPoint(presentation.MirrorRoot.position) : rect.anchoredPosition;
    }

    void OnBreakdownLine(string id)
    {
        string line = id.Substring(LiveId("live.breakdown.").Length);
        if (line == "02" && !Has("closeup_closed") && State.Add("closeup_open"))
        {
            if (liveWindow != null) presentation.SetScreenZoom(true, liveWindow.WindowRect);
            else State.Add("closeup_closed");
        }
    }

    static string LivePortraitKey(string cue)
    {
        if (string.IsNullOrEmpty(cue)) return string.Empty;
        return cue.StartsWith(EntryPrefix, StringComparison.Ordinal)
            ? cue.Substring(EntryPrefix.Length) : cue.Replace("os.chapter03.", string.Empty).Replace('.', '_');
    }

    static bool IsShortLivePortraitCue(string cue)
    {
        string key = LivePortraitKey(cue);
        return key.StartsWith("live_", StringComparison.Ordinal)
            && !key.StartsWith("live_breakdown_", StringComparison.Ordinal)
            && !key.StartsWith("live_offline_", StringComparison.Ordinal)
            && !key.StartsWith("live_return_", StringComparison.Ordinal)
            && !key.StartsWith("live_letter_", StringComparison.Ordinal);
    }

    static OSCharacterExtendedAnimation LivePortraitAnimation(string cue)
    {
        string key = LivePortraitKey(cue);
        switch (key)
        {
            case "live_opening_voice": case "live_opening_memory":
            case "live_store_knows_question": case "live_paper_husband_letter":
                return OSCharacterExtendedAnimation.Expend02;
            case "live_opening_heartbeat": return OSCharacterExtendedAnimation.Nervous;
            case "live_study_unfinished_page": return OSCharacterExtendedAnimation.MagnifyingGlass00;
            case "live_store_knows_yes": case "mirror_prelude": return OSCharacterExtendedAnimation.Sad;
            case "live_breakdown_01": case "live_breakdown_02": case "live_breakdown_03":
            case "live_breakdown_04_yes": case "live_breakdown_04_no": case "live_breakdown_05":
            case "live_return_09": case "live_return_10":
                return OSCharacterExtendedAnimation.Breakdown00;
            case "live_breakdown_06": case "live_breakdown_07": case "failed_typing":
            case "live_return_06": case "live_return_07": case "live_return_08":
                return OSCharacterExtendedAnimation.Breakdown01;
            case "live_breakdown_08": case "live_breakdown_09": case "reconnect":
            case "live_return_01": case "live_return_02": case "live_return_03":
            case "live_return_04": case "live_return_05":
            case "live_letter_hairclip": case "live_letter_silly":
                return OSCharacterExtendedAnimation.Breakdown02;
            default:
                return key.StartsWith("live_offline_", StringComparison.Ordinal)
                    ? OSCharacterExtendedAnimation.Breakdown01 : OSCharacterExtendedAnimation.None;
        }
    }

    // Only presentation-start callbacks and effects that have actually begun write this cue.
    // A queued next line must never change the portrait while the player is still reading.
    void SetLivePortraitCue(string cue)
    {
        if (State == null) return;
        var animation = LivePortraitAnimation(cue);
        if (animation == OSCharacterExtendedAnimation.None)
        {
            // An ordinary early reply clears the preceding short reaction. Later
            // conversation must not erase the persistent grief established at the mirror.
            if (IsShortLivePortraitCue(State.livePortraitCue)) ClearShortLivePortraitCue();
            return;
        }
        if (IsShortLivePortraitCue(cue) && !IsShortLivePortraitCue(State.livePortraitCue)
            && LivePortraitAnimation(State.livePortraitCue) != OSCharacterExtendedAnimation.None) return;
        if (LivePortraitKey(State.livePortraitCue) != LivePortraitKey(cue))
            livePortraitRestoreStable = livePortraitInstantPending = false;
        State.livePortraitCue = cue;
        RefreshLivePose();
    }

    void RefreshLivePose()
    {
        if (!IsActive || State == null) return;
        var animation = LivePortraitAnimation(State.livePortraitCue);
        if (livePortraitRestoreStable && animation == OSCharacterExtendedAnimation.MagnifyingGlass00)
            animation = OSCharacterExtendedAnimation.MagnifyingGlass01;
        if (animation != OSCharacterExtendedAnimation.None
            && os?.CharacterWindowController?.SetStoryAnimation(this, animation, instant: livePortraitInstantPending) == true)
            livePortraitInstantPending = false;
    }

    void ClearShortLivePortraitCue()
    {
        State.livePortraitCue = null;
        livePortraitRestoreStable = livePortraitInstantPending = false;
        os?.CharacterWindowController?.ReleaseStoryAnimation(this);
    }

    void CompleteLivePortraitCue(string key)
    {
        if (State != null && IsShortLivePortraitCue(State.livePortraitCue)
            && LivePortraitKey(State.livePortraitCue) == LivePortraitKey(key)) ClearShortLivePortraitCue();
    }

    void ReleaseLivePose()
    {
        livePortraitRestoreStable = livePortraitInstantPending = true;
        os?.CharacterWindowController?.ReleaseStoryAnimation(this, instant: true);
    }

    void BeginMirrorPortraitPrelude(bool firstBegin)
    {
        if (LivePortraitAnimation(State.livePortraitCue) != OSCharacterExtendedAnimation.None
            && !IsShortLivePortraitCue(State.livePortraitCue) && LivePortraitKey(State.livePortraitCue) != "mirror_prelude") return;
        State.Add("mirror_portrait_prelude");
        State.livePortraitCue = "mirror_prelude";
        livePortraitRestoreStable = livePortraitInstantPending = !firstBegin;
        RefreshLivePose();
    }

    bool HasActiveShortLivePortraitCue(string cue, bool savedPresentation)
    {
        var state = State;
        if (state == null || state.liveQueue.Count == 0) return false;
        var head = state.liveQueue[0];
        if (LivePortraitKey(head.key) != LivePortraitKey(cue)) return false;
        string id = EntryPrefix + LivePortraitKey(cue);
        bool displayed = messages?.DisplayedEntryId == id;
        if (head.complete) return head.remaining > 0f || displayed && messages.IsReadingHoldActive;
        // A persisted cue proves this unfinished head really appeared before a close/load.
        // Without that cue, only the active UI/presentation record counts, never history.
        return savedPresentation || GameDataManager.Instance?.OSCharacterInteractionState?.presentingGlobalEntryId == id
            || displayed && (messages.IsPresenting || messages.IsWaitingForOption || messages.IsReadingHoldActive);
    }

    // Older saves predate livePortraitCue. Recover once when binding the save, including
    // completed reading holds, which deliberately do not replay presentation-start events.
    void RestoreLivePortraitCue()
    {
        livePortraitRestoreStable = livePortraitInstantPending = true;
        var state = State;
        if (state == null) return;
        if (LivePortraitAnimation(state.livePortraitCue) != OSCharacterExtendedAnimation.None
            && (!IsShortLivePortraitCue(state.livePortraitCue) || HasActiveShortLivePortraitCue(state.livePortraitCue, true))) return;
        state.livePortraitCue = null;
        os?.CharacterWindowController?.ReleaseStoryAnimation(this, instant: true);
        var interaction = GameDataManager.Instance?.OSCharacterInteractionState;
        if (state.liveQueue.Count > 0)
        {
            var live = state.liveQueue[0];
            // begun also means a trigger was accepted into the delayed queue. Only
            // completed text or the global presentation record proves it was shown.
            bool presented = IsShortLivePortraitCue(live.key) ? HasActiveShortLivePortraitCue(live.key, false)
                : live.complete || interaction?.HasConsumedGlobalEntry(LiveId(live.key)) == true;
            if (presented
                && LivePortraitAnimation(live.key) != OSCharacterExtendedAnimation.None)
            { state.livePortraitCue = live.key; return; }
        }
        if (state.memoryQueue.Count > 0 && state.memoryQueue[0].type == "effect"
            && state.memoryQueue[0].key == "failed_typing")
        { state.livePortraitCue = "failed_typing"; return; }
        if (Has("letter_started") || Has("live_returned") || Has("box_open"))
        { state.livePortraitCue = "live.return.10"; return; }
        if (Has("reconnecting"))
        { state.livePortraitCue = "reconnect"; return; }
        // The consumed record covers a save between removal of the last line and the
        // next line's start. It is evidence of past presentation, unlike pending keys.
        if (state.liveOffline)
        {
            foreach (string cue in new[] { "live.offline.east", "live.offline.lab_open", "live.offline.diary13", "live.offline.bed" })
                if (interaction?.HasConsumedGlobalEntry(LiveId(cue)) == true)
                { state.livePortraitCue = cue; return; }
            state.livePortraitCue = "live.breakdown.09";
            return;
        }
        if (Has("reunion_started"))
        {
            for (int i = 10; i >= 1; i--)
            {
                string cue = "live.return." + i.ToString("00");
                if (interaction?.HasConsumedGlobalEntry(LiveId(cue)) == true)
                { state.livePortraitCue = cue; return; }
            }
            state.livePortraitCue = "reconnect";
            return;
        }
        if (Has("live_back_home") || Has("mirror_done") || Has("closeup_closed"))
        { state.livePortraitCue = "live.breakdown.09"; return; }
        for (int i = 9; i >= 1; i--)
        {
            string cue = i == 4 ? (state.knowAnswer == 1 ? "live.breakdown.04_yes" : "live.breakdown.04_no")
                : "live.breakdown." + i.ToString("00");
            if (interaction?.HasConsumedGlobalEntry(LiveId(cue)) == true)
            { state.livePortraitCue = cue; return; }
        }
        // closeup_open is committed by the actual start of line 02. The earlier
        // mirror_breakdown_queued flag only schedules line 01 and is not enough.
        if (Has("closeup_open")) state.livePortraitCue = "live.breakdown.02";
        else if (Has("mirror_portrait_prelude") || CurrentPhase == Phase.Mirror
            && GameDataManager.Instance.CurrentOSMemoryDataIndex >= 9015
            && GameDataManager.Instance.CurrentOSMemoryDataIndex <= 9045)
            state.livePortraitCue = "mirror_prelude";
    }

    void ShakeLivePortrait() => os.CharacterWindowController?.ShakeChapter03Portrait();

    void PulseLiveHairclip() => os.CharacterWindowController?.PulseChapter03Portrait();

    // RestoreExternalPresentation: the game back at full size whatever the story was doing.
    void RestoreLiveAfterCloseup()
    {
        if (presentation != null && presentation.IsScreenZoomed) presentation.SetScreenZoom(false, instant: true);
    }
}
