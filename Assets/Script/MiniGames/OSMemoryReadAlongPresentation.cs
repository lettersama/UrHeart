using MrTGame.Narrative;
using UnityEngine;

/// <summary>
/// Chapter-two read-along presentation (memory 5410+, after the heart talk). The red
/// pointer's reading position lives in <see cref="OSCursorReleaseDirector"/>; this
/// component adds the rest:
///   #4 (5690)  while "……我这里，为什么跳得这么快？" waits for an answer the heart panel
///              beats faster; after the answer it eases back over a few seconds.
///   5970       "我的手就开始狂抖了": the memory window trembles by one pixel for ~1.6s.
///   #8 (7200)  "……被缝起来的心。": when the line appears the heart panel gives one
///              strong beat. Pace and resources are untouched; the cat version has none.
/// Presentation only: never writes story, save or resources. Freezes with focus loss,
/// hidden OS, settings, session flow or the test console.
/// </summary>
[DisallowMultipleComponent]
public sealed class OSMemoryReadAlongPresentation : MonoBehaviour
{
    const int TrembleNode = 5970;
    const string HeartbeatEntry = "chapter02_readalong_5690";
    const string StitchedHeartEntry = "chapter02_readalong_7200";
    const float FastBeat = .52f, EaseSeconds = 3f, TrembleSeconds = 1.6f;

    OSMemoryWindowController memory;
    OSWindowUnit memoryWindow, settings;
    OSLinkMessageController messages;
    bool hasFocus = true, heartbeatOwned, stitchedBeatPlayed;
    float normalBeat = -1f, easeTime = -1f, tremble = -1f;
    int shownIndex = -1;

    void OnEnable() => hasFocus = Application.isFocused;
    void OnDisable() { Unbind(); ClearAll(); }
    void OnApplicationFocus(bool focused) => hasFocus = focused;

    void Bind()
    {
        var os = OS_Handle.Instance;
        var next = os?.MemoryWindowController;
        if (next != memory)
        {
            if (memory != null) memory.DataShown -= OnDataShown;
            memory = next; memoryWindow = null;
            if (memory != null) memory.DataShown += OnDataShown;
        }
        if (memory != null && memoryWindow == null)
        {
            memoryWindow = memory.GetComponent<OSWindowUnit>();
            if (memoryWindow == null) memoryWindow = memory.GetComponentInParent<OSWindowUnit>(true);
        }
        if (messages == null) messages = os?.LinkMessageController;
        if (settings == null && os != null) settings = os.transform.Find("Windows/WindowUnit_Option")?.GetComponent<OSWindowUnit>();
    }

    void Unbind()
    {
        if (memory != null) memory.DataShown -= OnDataShown;
        memory = null; memoryWindow = null;
    }

    void OnDataShown(OSMemoryReadData data)
    {
        if (data == null || GameDataManager.Instance?.CurrentChapterIndex != 2) return;
        if (data.DataIndex == shownIndex) return;
        shownIndex = data.DataIndex;
        if (shownIndex == TrembleNode) tremble = 0f;
    }

    bool InReadAlong => GameDataManager.Instance?.CurrentChapterIndex == 2
        && GameDataManager.Instance.OSProcessData?.memory?.chapter02HeartTalkStep == (int)OSCursorHeartTalkDirector.HeartTalkStep.Done
        && memory?.CurrentData != null && memory.CurrentData.DataIndex >= 5410;

    bool Available
    {
        get
        {
            var os = OS_Handle.Instance;
            return hasFocus && os != null && os.IsOSVisible && !OSSessionPersistence.IsRestoring
                && GameSessionFlow.Instance?.IsBusy != true && !GameTestConsole.BlocksGameplayInput
                && !(settings != null && settings.IsOpen && !settings.IsMinimized);
        }
    }

    void Update()
    {
        Bind();
        bool reading = InReadAlong;
        if (!reading) { ClearAll(); return; }
        if (!Available) return;   // freeze in place; nothing advances
        float dt = Mathf.Min(Time.unscaledDeltaTime, .05f);
        TickHeartbeat(dt);
        TickStitchedHeart();
        TickTremble(dt);
    }

    // ---------------------------------------------------------------- heartbeat
    void TickHeartbeat(float dt)
    {
        var wave = OS_Handle.Instance?.HeartbeatWaveform;
        if (wave == null) return;
        bool asking = messages != null && messages.DisplayedEntryId == HeartbeatEntry && messages.IsReadAlongQuestionPending;
        if (asking)
        {
            if (!heartbeatOwned) { normalBeat = wave.NaturalBeatInterval; heartbeatOwned = true; }
            easeTime = -1f;
            wave.SetNaturalBeatInterval(FastBeat);
            return;
        }
        if (!heartbeatOwned) return;
        // Answered (or the message left): ease back to the resting pace.
        if (easeTime < 0f) easeTime = 0f;
        easeTime += dt;
        float u = Mathf.Clamp01(easeTime / EaseSeconds);
        wave.SetNaturalBeatInterval(Mathf.Lerp(FastBeat, normalBeat, u * u * (3f - 2f * u)));
        if (u >= 1f) ReleaseHeartbeat();
    }

    void ReleaseHeartbeat()
    {
        if (heartbeatOwned && normalBeat > 0f) OS_Handle.Instance?.HeartbeatWaveform?.SetNaturalBeatInterval(normalBeat);
        heartbeatOwned = false; easeTime = -1f;
    }

    // ---------------------------------------------------------------- 7200 one strong beat
    void TickStitchedHeart()
    {
        bool shown = messages != null && messages.DisplayedEntryId == StitchedHeartEntry;
        if (!shown || stitchedBeatPlayed) return;
        var wave = OS_Handle.Instance?.HeartbeatWaveform;
        if (wave == null) return;
        // An active (highlighted) beat is the existing one-off larger pulse.
        wave.GenerateActiveBeat();
        stitchedBeatPlayed = true;
    }

    // ---------------------------------------------------------------- 5970 tremble
    void TickTremble(float dt)
    {
        if (tremble < 0f || memoryWindow == null) return;
        tremble += dt;
        if (tremble > TrembleSeconds) { memoryWindow.ClearPresentationOffset(this); tremble = -1f; return; }
        // Fine, uneven shiver: one-unit sideways twitches on most beats, rest between.
        int beat = Mathf.FloorToInt(tremble / .05f);
        float h = Hash(beat);
        Vector2 offset = h < .65f ? new Vector2(Hash(beat * 7 + 3) < .5f ? -1f : 1f, Hash(beat * 11 + 5) < .2f ? -1f : 0f) : Vector2.zero;
        if (!memoryWindow.TrySetPresentationOffset(this, offset)) tremble = -1f;
    }

    static float Hash(int n)
    {
        unchecked { uint h = (uint)n * 2654435761u; h ^= h >> 15; h *= 0x2c1b3c6du; h ^= h >> 12; return (h & 0xffff) / 65535f; }
    }

    void ClearAll()
    {
        ReleaseHeartbeat();
        if (tremble >= 0f && memoryWindow != null) memoryWindow.ClearPresentationOffset(this);
        tremble = -1f;
    }

#if UNITY_EDITOR
    public bool DebugHeartbeatFast => heartbeatOwned && easeTime < 0f;
    public bool DebugTrembling => tremble >= 0f;
    public bool DebugStitchedBeatPlayed => stitchedBeatPlayed;
#endif
}
