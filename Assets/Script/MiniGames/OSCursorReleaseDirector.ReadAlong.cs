using UnityEngine;

// Read-along for Character's red pointer (chapter two, memory 5410+ after the heart
// talk). While the memory window is open she leaves Live's edge and reads with the
// player: the pointer rests in the memory window's right margin, tip at the newest
// typed line, and slides down as lines appear.
//   - A Live message window covering that spot: she steps just below (or above) it,
//     never over its text. While her question waits she bobs a little.
//   - 5590–5679 (the collapse and the silence after it): she tucks into the window's
//     lower-right corner, pressed and still, until the robot speaks at 5680.
//   - 6520–6599 (the drawer and the silence before the knock): the same tuck, until 6600.
//   - Light comments (5870, 6010, 6710, 7060) get one hop when they appear.
//   - 7040 "对.齐..": once its line finishes typing she tilts her head ~15°, holds,
//     then eases upright. Once per reading of that line.
// Only the rendered actor moves; no story, save or input is touched.
public sealed partial class OSCursorReleaseDirector
{
    const int ReadAlongStart = 5410, ReadTiltNode = 7040;
    const float ReadMarginInset = 20f, ReadHopSeconds = .8f;
    const float ReadTiltDegrees = 15f, ReadTiltIn = .25f, ReadTiltHold = 1f, ReadTiltOut = .8f;
    // Silent stretches: [start, end) memory nodes.
    static readonly int[] ReadSilences = { 5590, 5680, 6520, 6600 };
    static readonly string[] ReadHopEntries = {
        "chapter02_readalong_5870", "chapter02_readalong_6010",
        "chapter02_readalong_6710", "chapter02_readalong_7060" };

    OSWindowUnit readMemoryWindow;
    Vector2 readPosition, readVelocity, readLineAnchor;
    bool readActive, readTucked, readHasLine;
    float readHop = -1f, readTilt = -1f;
    bool readTiltDone;
    string readHopSeen;
    readonly Vector3[] readCorners = new Vector3[4];

    /// <summary>True while the read-along owns the pointer this frame.</summary>
    bool TickReadAlong(float dt)
    {
        var memory = OS_Handle.Instance?.MemoryWindowController;
        bool ready = GameDataManager.Instance?.CurrentChapterIndex == 2
            && State != null && State.chapter02HeartTalkStep == (int)OSCursorHeartTalkDirector.HeartTalkStep.Done
            && memory != null && memory.CurrentData != null && memory.CurrentData.DataIndex >= ReadAlongStart
            && cursorImage != null && visualRoot != null;
        if (ready)
        {
            if (readMemoryWindow == null)
            {
                readMemoryWindow = memory.GetComponent<OSWindowUnit>();
                if (readMemoryWindow == null) readMemoryWindow = memory.GetComponentInParent<OSWindowUnit>(true);
            }
            ready = readMemoryWindow != null && readMemoryWindow.IsOpen && !readMemoryWindow.IsMinimized
                && !readMemoryWindow.IsOpening && readMemoryWindow.gameObject.activeInHierarchy;
        }
        if (!ready) { EndReadAlong(); return false; }

        if (!readActive)
        {
            readActive = true; readPosition = CursorPoint; readVelocity = Vector2.zero; readHasLine = false;
            if (flourish != Flourish.None) EndFlourish();
            if (heartTrail != null) foreach (var dot in heartTrail) dot.gameObject.SetActive(false);
            heartTrailAge = -1f;
        }
        if (dt <= 0f) return true;

        Rect window = WindowBounds(readMemoryWindow.WindowRect);
        int index = memory.CurrentData.DataIndex;
        bool tucked = false;
        for (int i = 0; i < ReadSilences.Length; i += 2)
            tucked |= index >= ReadSilences[i] && index < ReadSilences[i + 1];
        if (tucked != readTucked) { readTucked = tucked; SetPressed(tucked); }

        Vector2 goal;
        float smooth = .35f, speed = 160f;
        if (tucked)
        {
            goal = new Vector2(window.xMax - ReadMarginInset, window.yMin + 34f);
            smooth = .22f;
        }
        else
        {
            if (TryNewestLineY(memory, out float lineY)) { readLineAnchor = new Vector2(0f, lineY); readHasLine = true; }
            float y = readHasLine ? readLineAnchor.y : window.center.y;
            // Tip a few units above the line's middle, pointing up-left at the text.
            y = Mathf.Clamp(y + 5f, window.yMin + 34f, window.yMax - 22f);
            goal = new Vector2(window.xMax - ReadMarginInset, y);
            // A player pointer resting on her spot nudges her down out of the way.
            if (TryIdlePlayerPoint(out Vector2 player) && Vector2.Distance(player, goal) < 14f) goal.y -= 18f;
            goal = AvoidMessage(goal, window);
        }

        readPosition = Vector2.SmoothDamp(readPosition, goal, ref readVelocity, smooth, speed, dt);
        readPosition = ClampToRect(readPosition, IdleScreenBounds());

        Vector2 rendered = readPosition;
        if (!tucked)
        {
            // Breathing sway of a single unit so she reads as present, not pinned.
            rendered.y += Mathf.Round(Mathf.Sin(idleTime * 1.3f) * .6f);
            string shown = messages?.DisplayedEntryId;
            if (!string.IsNullOrEmpty(shown) && shown != readHopSeen && System.Array.IndexOf(ReadHopEntries, shown) >= 0)
            { readHopSeen = shown; readHop = 0f; }
            if (readHop >= 0f)
            {
                readHop += dt;
                rendered.y += Mathf.Round(Mathf.Abs(Mathf.Sin(readHop / ReadHopSeconds * Mathf.PI * 2f)) * 4f);
                if (readHop >= ReadHopSeconds) readHop = -1f;
            }
            // Once per reading of 7040: re-armed only after the reader moves on.
            if (index != ReadTiltNode) readTiltDone = false;
            else if (!readTiltDone && memory.IsCurrentTextComplete) { readTiltDone = true; readTilt = 0f; }
        }
        TickReadTilt(tucked ? -1f : dt);
        idleWritingCursor = true; PlaceCursor(rendered); idleWritingCursor = false;
        return true;
    }

    void EndReadAlong()
    {
        if (!readActive) return;
        readActive = false; readHop = -1f;
        if (readTilt >= 0f) { readTilt = -1f; SetReadTilt(0f); }
        if (readTucked) { readTucked = false; SetPressed(false); }
        // Idle resumes from wherever she is and walks home on its own.
        idleInitialized = false;
    }

    /// <summary>Head tilt about the tip: quick lean, hold, slow return. dt &lt; 0 cancels.</summary>
    void TickReadTilt(float dt)
    {
        if (readTilt < 0f) return;
        if (dt < 0f) { readTilt = -1f; SetReadTilt(0f); return; }
        readTilt += dt;
        float t = readTilt, angle;
        if (t < ReadTiltIn) angle = Mathf.SmoothStep(0f, 1f, t / ReadTiltIn);
        else if (t < ReadTiltIn + ReadTiltHold) angle = 1f;
        else angle = 1f - Mathf.SmoothStep(0f, 1f, (t - ReadTiltIn - ReadTiltHold) / ReadTiltOut);
        if (t >= ReadTiltIn + ReadTiltHold + ReadTiltOut) { readTilt = -1f; angle = 0f; }
        SetReadTilt(angle * ReadTiltDegrees);
    }

    void SetReadTilt(float degrees)
    {
        if (cursorImage != null) cursorImage.rectTransform.localRotation = Quaternion.Euler(0f, 0f, degrees);
    }

    Rect WindowBounds(RectTransform rect)
    {
        rect.GetWorldCorners(readCorners);
        Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity), max = -min;
        foreach (Vector3 corner in readCorners)
        {
            Vector2 p = visualRoot.InverseTransformPoint(corner);
            min = Vector2.Min(min, p); max = Vector2.Max(max, p);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    /// <summary>Middle of the newest line of the line now being typed, in actor space.</summary>
    bool TryNewestLineY(OSMemoryWindowController memory, out float y)
    {
        y = 0f;
        int current = memory.CurrentData.DataIndex;
        var units = memory.TextUnits;
        if (units == null) return false;
        for (int i = units.Count - 1; i >= 0; i--)
        {
            var unit = units[i];
            if (unit == null || unit.DataIndex != current || !unit.gameObject.activeInHierarchy) continue;
            var label = unit.Label;
            if (label == null) return false;
            var info = label.textInfo;
            if (info == null || info.lineCount <= 0 || info.characterCount <= 0) return false;
            // During DOText the text grows; with a visibility limit, follow the last visible glyph.
            int lineIndex = info.lineCount - 1;
            if (label.maxVisibleCharacters < info.characterCount)
            {
                int last = Mathf.Clamp(label.maxVisibleCharacters - 1, 0, info.characterCount - 1);
                lineIndex = Mathf.Clamp(info.characterInfo[last].lineNumber, 0, info.lineCount - 1);
            }
            var line = info.lineInfo[lineIndex];
            Vector3 world = label.rectTransform.TransformPoint(new Vector3(0f, (line.ascender + line.descender) * .5f, 0f));
            y = visualRoot.InverseTransformPoint(world).y;
            return true;
        }
        return false;
    }

    /// <summary>Keeps the pointer off an open Live message: below it, else above it.</summary>
    Vector2 AvoidMessage(Vector2 goal, Rect memoryWindow)
    {
        var window = messages?.LinkWindow;
        if (window == null || !window.IsOpen || window.IsMinimized || !window.gameObject.activeInHierarchy) return goal;
        Rect message = WindowBounds(window.WindowRect);
        Vector2 size = cursorImage.rectTransform.sizeDelta;
        // Pointer pivot is the tip (top-left); the body hangs down and right.
        Rect keepout = Rect.MinMaxRect(message.xMin - size.x - 3f, message.yMin - 3f, message.xMax + 3f, message.yMax + size.y + 3f);
        if (!keepout.Contains(goal))
        {
            if (messages.IsReadAlongQuestionPending) goal.y += Mathf.Round(Mathf.PingPong(idleTime * 2f, 1f));
            return goal;
        }
        float below = keepout.yMin - 1f, above = keepout.yMax + 1f;
        bool belowFits = below - size.y >= memoryWindow.yMin + 6f;
        goal.y = belowFits || above > memoryWindow.yMax - 22f ? below : above;
        if (messages.IsReadAlongQuestionPending) goal.y += Mathf.Round(Mathf.PingPong(idleTime * 2f, 1f));
        return goal;
    }

#if UNITY_EDITOR
    public bool DebugReadAlongActive => readActive;
    public bool DebugReadAlongTucked => readTucked;
    public bool DebugReadAlongTilting => readTilt >= 0f;
    public bool DebugReadAlongTiltDone => readTiltDone;
    public float DebugReadAlongTiltDegrees => cursorImage != null ? cursorImage.rectTransform.localEulerAngles.z : 0f;
    public bool DebugReadAlongHopping => readHop >= 0f;
    public string DebugReadAlongHopSeen => readHopSeen;
#endif
}
