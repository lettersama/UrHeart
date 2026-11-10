using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// A message that leaves by itself (auto-close, or a timed hand-off to the next
// line) shows a 1px line along its bottom edge that retracts as its time runs
// out. Messages waiting for a click keep their blinking continue tip instead,
// so the two kinds read differently at a glance. Presentation only.
public sealed partial class OSLinkMessageController
{
    const float WaitLineMinimumSeconds = 1.5f;
    const float WaitLineInset = 3f;
    RectTransform waitLineRoot;
    Image waitLineTrack;
    Image waitLineFill;
    float waitLineFillWidth = -1f;

    string storyWaitEntryId;
    float storyWaitRemaining01;

    // Story-owned waits share the existing text-only progress line. A negative
    // value clears only the requesting entry, so it cannot hide another timer.
    public void SetStoryWaitLineProgress(string entryId, float remaining01)
    {
        if (remaining01 < 0f && storyWaitEntryId != entryId) return;
        storyWaitEntryId = remaining01 < 0f ? null : entryId;
        storyWaitRemaining01 = Mathf.Clamp01(remaining01);
        TickWaitLine();
    }

    void TickWaitLine()
    {
        float remaining01 = -1f;
        // Both timers only start once the text has finished typing.
        if (textPart != null && textPart.activeInHierarchy && hasDisplayedMessage)
        {
            Tween timer = autoCloseTween != null && autoCloseTween.IsActive() ? autoCloseTween : null;
            float duration = timer != null ? timer.Duration(false) : 0f;
            if (timer != null)
            {
                if (duration >= WaitLineMinimumSeconds)
                    remaining01 = 1f - Mathf.Clamp01(timer.Elapsed(false) / duration);
            }
            else if ((activeTextCompleted || !IsPresenting) && !string.IsNullOrEmpty(storyWaitEntryId) && displayedEntryId == storyWaitEntryId)
                remaining01 = storyWaitRemaining01;
            else if (activeEntry != null && !activeEntry.LoopTrailingDots
                && activeTextCompleted && activeEntry.NextEntryDelay >= WaitLineMinimumSeconds)
            {
                var state = GetSaveState();
                if (state != null && state.nextEntryCountdownKey == OptionCountdownKey(displayedSequenceId, displayedEntryId))
                    remaining01 = Mathf.Clamp01(state.nextEntryCountdownRemaining / activeEntry.NextEntryDelay);
            }
        }

        if (remaining01 < 0f)
        {
            if (waitLineRoot != null && waitLineRoot.gameObject.activeSelf)
                waitLineRoot.gameObject.SetActive(false);
            return;
        }
        if (!EnsureWaitLine()) return;
        if (!waitLineRoot.gameObject.activeSelf) waitLineRoot.gameObject.SetActive(true);

        Color ink = textLabel != null ? textLabel.color : (Color)new Color32(0x87, 0x82, 0x7e, 0xff);
        if (waitLineFill.color != ink) waitLineFill.color = ink;
        Color trackInk = ink;
        trackInk.a *= .25f;
        if (waitLineTrack.color != trackInk) waitLineTrack.color = trackInk;

        // Left edge fixed, right edge retracts in whole logical pixels.
        float width = Mathf.Round(Mathf.Max(0f, waitLineRoot.rect.width) * remaining01);
        if (width != waitLineFillWidth)
        {
            waitLineFillWidth = width;
            waitLineFill.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        }
        waitLineFill.enabled = width > 0f;
    }

    bool EnsureWaitLine()
    {
        if (waitLineRoot != null) return true;
        if (textPart == null) return false;
        var parent = (RectTransform)textPart.transform;

        var node = new GameObject("WaitLine", typeof(RectTransform), typeof(LayoutElement));
        node.layer = parent.gameObject.layer;
        node.GetComponent<LayoutElement>().ignoreLayout = true;
        waitLineRoot = (RectTransform)node.transform;
        waitLineRoot.SetParent(parent, false);
        waitLineRoot.anchorMin = new Vector2(0f, 0f);
        waitLineRoot.anchorMax = new Vector2(1f, 0f);
        waitLineRoot.pivot = new Vector2(.5f, 0f);
        waitLineRoot.offsetMin = new Vector2(WaitLineInset, 1f);
        waitLineRoot.offsetMax = new Vector2(-WaitLineInset, 2f);

        waitLineTrack = CreateOptionCountdownLine("Track", waitLineRoot);
        var track = waitLineTrack.rectTransform;
        track.anchorMin = Vector2.zero; track.anchorMax = Vector2.one;
        track.offsetMin = track.offsetMax = Vector2.zero;

        waitLineFill = CreateOptionCountdownLine("Remaining", waitLineRoot);
        var fill = waitLineFill.rectTransform;
        fill.anchorMin = Vector2.zero; fill.anchorMax = new Vector2(0f, 1f);
        fill.pivot = new Vector2(0f, .5f);
        fill.anchoredPosition = Vector2.zero;
        fill.sizeDelta = Vector2.zero;
        waitLineFill.enabled = false;
        waitLineFillWidth = -1f;
        node.SetActive(false);
        return true;
    }
}
