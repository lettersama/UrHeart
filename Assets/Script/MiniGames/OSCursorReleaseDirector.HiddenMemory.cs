using UnityEngine;

// Chapter 2 finale, hidden memory: the red pointer reads the injected log with the
// player. She waits at the start of the reading line in the dark, then follows the
// tip of each line as it types, a few units after the newest glyph.
//   - Table knocks: she presses with each knock, tapping along.
//   - The paper figure falling: a small flinch downward.
// Only the rendered actor moves; no story, save or input is touched.
public sealed partial class OSCursorReleaseDirector
{
    OSChapter02HiddenMemoryPresentation hiddenMemory;
    Vector2 hiddenPosition, hiddenVelocity;
    bool hiddenActive, hiddenPressed;

    /// <summary>True while the hidden memory owns the pointer this frame.</summary>
    bool TickHiddenMemoryRead(float dt)
    {
        if (hiddenMemory == null) hiddenMemory = OS_Handle.Instance?.GetComponent<OSChapter02HiddenMemoryPresentation>();
        if (hiddenMemory == null || !hiddenMemory.IsPresenting || cursorImage == null || visualRoot == null
            || !hiddenMemory.TryGetReadPoint(out Vector3 world))
        {
            EndHiddenMemoryRead();
            return false;
        }
        if (!hiddenActive)
        {
            hiddenActive = true;
            EndReadAlong();
            hiddenPosition = CursorPoint; hiddenVelocity = Vector2.zero;
        }
        if (dt <= 0f) return true;

        Vector2 goal = visualRoot.InverseTransformPoint(world);
        // Quick enough to keep up with typing, soft enough to read as a hand.
        hiddenPosition = Vector2.SmoothDamp(hiddenPosition, goal, ref hiddenVelocity, .16f, 260f, dt);

        bool pressed = hiddenMemory.KnockPulse > .55f;
        if (pressed != hiddenPressed) { hiddenPressed = pressed; SetPressed(pressed); }
        Vector2 rendered = hiddenPosition;
        if (pressed) rendered.y -= 1f;
        if (hiddenMemory.FallPulse > 0f) rendered.y -= Mathf.Round(hiddenMemory.FallPulse * 3f);
        else rendered.y += Mathf.Round(Mathf.Sin(idleTime * 1.3f) * .6f);
        idleWritingCursor = true; PlaceCursor(rendered); idleWritingCursor = false;
        return true;
    }

    void EndHiddenMemoryRead()
    {
        if (!hiddenActive) return;
        hiddenActive = false;
        if (hiddenPressed) { hiddenPressed = false; SetPressed(false); }
        // Idle (or the memory read-along) resumes from wherever she is.
        idleInitialized = false;
    }
}
